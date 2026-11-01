using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Foundation;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using SimPhi.Core;
using SimPhi.Game;

namespace SimPhi.Render
{
    public enum Phase { Stop, Play, Pause }

    /// <summary>背景图（含懒计算的模糊版），对应 index.ts 的 ImageStore + background。</summary>
    public sealed class Background
    {
        private readonly Res _res;
        public CanvasDevice Device;          // 由 host 注入，用于懒计算模糊
        public CanvasBitmap Base;
        public CanvasBitmap BlurImage;
        public bool IsBlur;
        public Background(Res res) { _res = res; }
        public void SetBase(CanvasBitmap img) { Base = img; BlurImage = null; }
        public CanvasBitmap GetImage() { return Base ?? _res.NoImageWhite; }
        public CanvasBitmap GetImageBlur()
        {
            if (Base == null) return _res.NoImageWhite;
            if (IsBlur)
            {
                if (BlurImage == null && Device != null) BlurImage = ImageTools.ImgBlur(Device, Base);
                return BlurImage ?? Base;
            }
            return Base;
        }
    }

    /// <summary>
    /// 照抄 index.ts 的绘制主循环与判定。原版 mainLoop 先画前景画布(ctxfg)再合成到主画布(ctx)；
    /// Win2D 里前景是独立 CanvasRenderTarget，必须先画完前景、关掉它的 session，再在主 session 里采样它，
    /// 故拆成 DrawForeground(画前景) + DrawMain(画主画布并贴前景)。
    /// </summary>
    public sealed class GameRenderer
    {
        public readonly Renderer app;
        public readonly Stat stat;
        public readonly HitManager hitManager;
        public readonly FrameTimer frameTimer = new FrameTimer();
        public readonly Timer timeIn = new Timer();
        public readonly Timer timeOut = new Timer();
        public readonly Timer timeEnd = new Timer();
        public readonly Res res;
        public readonly NoteRender noteRender;
        public readonly Settings settings;
        public readonly Info info;
        public readonly Background background;
        public IAudioController musicController;
        public IAudioController soundController;
        public IAudioSystem audio;
        public string Version = "1.5.9.2";

        // 生命周期状态（host 通过 Emitter 驱动 play/pause/stop）
        public Phase Emitter = Phase.Stop;
        public bool Awawa = false;        // 倒放
        public double PressTime = 0;
        public string Status2Text = "";

        // 插件 hook（未移植插件时为空）
        public readonly List<Action<double>> Now = new List<Action<double>>();
        public readonly List<Action> After = new List<Action>();
        public readonly List<Action> AfterAll = new List<Action>();
        public Action<Ctx> CustomForeDraw;
        public Action<Ctx> CustomBackDraw;
        public Action<Ctx, double, double> Filter;

        // index.ts 模块级时间/标志
        private double nowTimeMS, curTime, curTimeMS, timeBgm, timeChart;
        public double duration0;
        private bool isInEnd, isOutStart, isOutEnd, isOutOver;
        private StatData tempStat;
        private double _resultAt = -1;      // 替代 setTimeout(…,1000)
        public object bgMusicHack;

        // tmps
        private CanvasBitmap tmpBgImage;
        public CanvasBitmap BgVideoFrame;   // 视频帧（暂未接视频解码，可为 null）
        private double tmpProgress;
        private string tmpName, tmpArtist, tmpIllustrator, tmpCharter, tmpLevel, tmpCombo, tmpCombo2;
        private bool tmpShowStat;

        // 当前帧前景上下文（供 hit 列表回调使用）
        private Ctx _fg;

        // hit 特效列表
        private readonly HitEvents<HitImage> hitImageList;
        private readonly HitEvents<HitWord> hitWordList;
        private readonly HitEvents<HitFeedback> hitFeedbackList;

        // judge
        private readonly List<JudgeEvent> judgeList = new List<JudgeEvent>();

        // 结算页缓存
        private CanvasGeometry _btn1, _btn2;

        // note 皮肤缓存（避免每 note 每帧 Dictionary<string> 查表）
        private bool _skinsCached;
        private ScaledNote _tap, _tapHL, _tapBad, _drag, _dragHL, _hHead, _hHeadHL, _hold, _holdHL, _hEnd, _flick, _flickHL;
        private void CacheSkins()
        {
            var n = noteRender.note;
            n.TryGetValue("Tap", out _tap); n.TryGetValue("TapHL", out _tapHL); n.TryGetValue("TapBad", out _tapBad);
            n.TryGetValue("Drag", out _drag); n.TryGetValue("DragHL", out _dragHL);
            n.TryGetValue("HoldHead", out _hHead); n.TryGetValue("HoldHeadHL", out _hHeadHL);
            n.TryGetValue("Hold", out _hold); n.TryGetValue("HoldHL", out _holdHL); n.TryGetValue("HoldEnd", out _hEnd);
            n.TryGetValue("Flick", out _flick); n.TryGetValue("FlickHL", out _flickHL);
            _skinsCached = _tap != null;
        }

        public GameRenderer(Renderer app, Stat stat, HitManager hitManager, Res res, NoteRender noteRender, Settings settings, Info info)
        {
            this.app = app;
            this.stat = stat;
            this.hitManager = hitManager;
            this.res = res;
            this.noteRender = noteRender;
            this.settings = settings;
            this.info = info;
            background = new Background(res);
            HitImage.Provider = noteRender;

            hitImageList = new HitEvents<HitImage>(
                i => nowTimeMS >= i.time + i.duration,
                i =>
                {
                    double tick = (nowTimeMS - i.time) / i.duration;
                    _fg.GlobalAlpha = 1;
                    _fg.SetTransform(app.noteScaleRatio * 6, 0, 0, app.noteScaleRatio * 6, i.offsetX, i.offsetY);
                    // 照抄 JS: effects[floor(tick*len)] ?? effects[len-1]——越界(含创建当帧 tick<0)回退到最后一帧
                    if (i.effects.Length == 0) return;
                    int raw = (int)Math.Floor(tick * i.effects.Length);
                    var fx = (raw >= 0 && raw < i.effects.Length) ? i.effects[raw] : i.effects[i.effects.Length - 1];
                    fx.Full(_fg);
                    _fg.SetFillStyle(i.color);
                    _fg.GlobalAlpha = 1 - tick;
                    double r3 = 30 * (((0.2078 * tick - 1.6524) * tick + 1.6399) * tick + 0.4988);
                    foreach (var j in i.direction)
                    {
                        double ds = j[0] * (9 * tick / (8 * tick + 1));
                        _fg.FillRect(ds * Math.Cos(j[1]) - r3 / 2, ds * Math.Sin(j[1]) - r3 / 2, r3, r3);
                    }
                });

            hitWordList = new HitEvents<HitWord>(
                i => nowTimeMS >= i.time + i.duration,
                i =>
                {
                    double tick = (nowTimeMS - i.time) / i.duration;
                    _fg.SetTransform(1, 0, 0, 1, i.offsetX, i.offsetY);
                    _fg.SetFont("bold " + (app.noteScaleRatio * (256 + 128 * (((0.2078 * tick - 1.6524) * tick + 1.6399) * tick + 0.4988))).ToString("R", CultureInfo.InvariantCulture) + "px Custom,Noto Sans SC");
                    _fg.TextAlign = TextAlign.Center;
                    _fg.SetFillStyle(i.color);
                    _fg.GlobalAlpha = 1 - tick;
                    _fg.FillText(i.text, 0, -app.noteScaleRatio * 128);
                });

            hitFeedbackList = new HitEvents<HitFeedback>(
                i => i.time++ > 0,
                i =>
                {
                    _fg.GlobalAlpha = 0.85;
                    _fg.SetTransform(1, 0, 0, 1, i.offsetX, i.offsetY);
                    _fg.SetFillStyle(i.color);
                    _fg.FillCircle(0, 0, app.lineScale * 0.5);
                });
        }

        private bool EmEq(Phase p) { return Emitter == p; }

        /// <summary>loadLineData 的默认分支：把每条判定线贴图设为内置判定线（含 MP/FC 着色）。</summary>
        public void SetupDefaultLineImages()
        {
            foreach (var line in app.lines)
            {
                line.imageW = 6220.8;
                line.imageH = 7.68;
                line.imageL = new object[] { res.JudgeLine, res.JudgeLineMP, null, res.JudgeLineFC };
                line.imageS = 1;
                line.imageA = 1;
                line.imageD = false;
                line.imageC = true;
                line.imageU = true;
            }
        }

        public void ClearHitEffects()
        {
            hitImageList.Clear();
            hitWordList.Clear();
            hitFeedbackList.Clear();
        }

        public void ResetPlayState()
        {
            isInEnd = false;
            isOutStart = false;
            isOutEnd = false;
            isOutOver = false;
            timeBgm = 0;
            tempStat = null;
            _resultAt = -1;
            curTime = 0;
            curTimeMS = 0;
        }

        public bool IsOutOver { get { return isOutOver; } }
        public double TimeBgm { get { return timeBgm; } set { timeBgm = value; } }

        // ============ mainLoop（前景部分）============
        public void DrawForeground(Ctx ctxfg)
        {
            _fg = ctxfg;
            frameTimer.AddTick();
            nowTimeMS = Clock.Now();

            if (timeOut.Second < 0.67)
            {
                LoopNoCanvas();
                foreach (var cb in Now) cb(timeBgm * app.speed);
                LoopCanvas(ctxfg);
            }
            else if (!isOutOver)
            {
                isOutOver = true;
                audio.Stop();
                ctxfg.GlobalCompositeOperation = CompositeOp.SourceOver;
                ctxfg.ResetTransform();
                ctxfg.GlobalAlpha = 1;
                var bgImageBlur = background.GetImageBlur();
                DrawImageFit(ctxfg, bgImageBlur, app.CanvasFgWidth, app.CanvasFgHeight, 1);
                ctxfg.SetFillStyle("#000");
                ctxfg.GlobalAlpha = app.brightness;
                ctxfg.FillRect(0, 0, app.CanvasFgWidth, app.CanvasFgHeight);
                _resultAt = Clock.Now() + 1000;   // setTimeout(…,1000)
            }
            // setTimeout 回调
            if (_resultAt >= 0 && Clock.Now() >= _resultAt && isOutOver && tempStat == null)
            {
                _resultAt = -1;
                int difficulty = info.DifficultyIndex;
                musicController.Play(res.LevelOverAudio(difficulty < 0 ? 2 : difficulty), AudioOptions.Make(loop: true));
                timeEnd.Reset();
                timeEnd.Play();
                stat.level = info.LevelNumber;
                tempStat = stat.getData(app.playMode == 1, settings.Speed);
            }
            if (tempStat != null) AtDraw3(ctxfg, tempStat);
        }

        // ============ mainLoop（主画布部分）============
        public void DrawMain(Ctx ctx, CanvasBitmap fg)
        {
            double lineScale = app.lineScale;
            ctx.GlobalAlpha = 1;
            var bgImageBlur = background.GetImageBlur();
            DrawImageFit(ctx, bgImageBlur, app.CanvasWidth, app.CanvasHeight, 1.1);
            ctx.SetFillStyle("#000");
            ctx.GlobalAlpha = 0.4;
            ctx.FillRect(0, 0, app.CanvasWidth, app.CanvasHeight);
            ctx.GlobalAlpha = 1;
            ctx.DrawImage(fg, (app.CanvasWidth - app.CanvasFgWidth) / 2.0, 0);
            foreach (var cb in AfterAll) cb();
            // 版权字（difference 合成）
            ctx.GlobalCompositeOperation = CompositeOp.Difference;
            ctx.SetFont(lineScale * 0.4, "Custom,Noto Sans SC");
            ctx.SetFillStyle("#fff");
            ctx.GlobalAlpha = 0.8;
            ctx.TextAlign = TextAlign.Right;
            ctx.FillText("Phigros Simulator v" + Version + " - Code by lchzh3473",
                (app.CanvasWidth + app.CanvasFgWidth) / 2.0 - lineScale * 0.1, app.CanvasHeight - lineScale * 0.1);
            ctx.GlobalCompositeOperation = CompositeOp.SourceOver;
        }

        // ============ loopNoCanvas ============
        private void LoopNoCanvas()
        {
            if (app.chart == null) throw new InvalidOperationException("Not initialized: Chart");
            background.IsBlur = settings.ImageBlur;
            if (!isInEnd && timeIn.Second >= 3 && EmEq(Phase.Play))
            {
                isInEnd = true;
                PlayBgm(app.bgMusic as IAudioBuffer);
            }
            if (EmEq(Phase.Play) && isInEnd && !isOutStart) timeBgm = curTime + (nowTimeMS - curTimeMS) / 1e3;
            if (timeBgm < 0) timeBgm = 0;
            if (timeBgm >= duration0) isOutStart = true;
            if (settings.ShowTransition && isOutStart && !isOutEnd) { isOutEnd = true; timeOut.Play(); }
            double off = app.chart.offset + settings.Offset / 1e3;
            if (double.IsNaN(off)) off = 0;
            timeChart = Math.Max(timeBgm - off / app.speed, 0);
            app.UpdateByTime(timeChart);
            hitFeedbackList.Update();
            hitImageList.Update();
            hitWordList.Update();
            foreach (var evt in hitManager.list)
            {
                if (evt.type == HitType.Keyboard) continue;
                if (!evt.isTapped) hitFeedbackList.Add(HitFeedback.tap(evt.offsetX, evt.offsetY));
                else if (evt.isMoving) hitFeedbackList.Add(HitFeedback.move(evt.offsetX, evt.offsetY));
                else if (evt.isActive) hitFeedbackList.Add(HitFeedback.hold(evt.offsetX, evt.offsetY));
            }
            if (isInEnd)
            {
                double judgeWidth = app.CanvasFgWidth * 0.118125;
                JudgeAddEvent(app.notes, timeChart);
                JudgeExecute(app.drags, timeChart, judgeWidth);
                JudgeExecute(app.flicks, timeChart, judgeWidth);
                JudgeExecute(app.tapholds, timeChart, judgeWidth);
            }
            hitManager.Update();
            tmpBgImage = background.GetImageBlur();
            tmpProgress = (Awawa ? duration0 - timeBgm : timeBgm) / duration0;
            tmpName = info.NameOrPlaceholder;
            tmpArtist = info.Artist;
            tmpIllustrator = "Illustration designed by " + info.IllustratorOrPlaceholder;
            tmpCharter = "Level designed by " + info.CharterOrPlaceholder;
            tmpLevel = info.LevelText;
            if (stat.combo > 2)
            {
                tmpCombo = stat.combo.ToString(CultureInfo.InvariantCulture);
                tmpCombo2 = app.playMode == 1 ? "Autoplay" : "combo";
            }
            else { tmpCombo = ""; tmpCombo2 = ""; }
            tmpShowStat = true;
            CustomForeDraw = null;
            CustomBackDraw = null;
        }

        // ============ loopCanvas ============
        private void LoopCanvas(Ctx ctxfg)
        {
            double lineScale = app.lineScale, wlen = app.wlen, hlen = app.hlen;
            CanvasBitmap bgImage = tmpBgImage;
            ctxfg.ClearRect(0, 0, app.CanvasFgWidth, app.CanvasFgHeight);
            ctxfg.GlobalAlpha = 1;
            DrawImageFit(ctxfg, bgImage, app.CanvasFgWidth, app.CanvasFgHeight, 1);
            if (isInEnd && BgVideoFrame != null && !Awawa)
                DrawImageFit(ctxfg, BgVideoFrame, app.CanvasFgWidth, app.CanvasFgHeight, 1);
            if (timeIn.Second >= 2.5 && stat.lineStatus == 0) DrawLine(ctxfg, 0, lineScale); // 绘制判定线(背景后0)
            ctxfg.ResetTransform();
            ctxfg.SetFillStyle("#000");
            ctxfg.GlobalAlpha = app.brightness;
            ctxfg.FillRect(0, 0, app.CanvasFgWidth, app.CanvasFgHeight);
            if (timeIn.Second >= 2.5 && CustomBackDraw != null) CustomBackDraw(ctxfg);
            if (timeIn.Second >= 2.5) DrawLine(ctxfg, stat.lineStatus != 0 ? 2 : 1, lineScale);
            ctxfg.ResetTransform();
            if (timeIn.Second >= 3 && timeOut.Second == 0)
            {
                DrawNotes(ctxfg);
                if (settings.ShowPoint)
                {
                    ctxfg.SetFont(lineScale, "Custom,Noto Sans SC");
                    ctxfg.TextAlign = TextAlign.Center;
                    foreach (var line in app.linesReversed)
                    {
                        ctxfg.SetTransform(line.cosr, line.sinr, -line.sinr, line.cosr, line.offsetX, line.offsetY);
                        ctxfg.GlobalAlpha = 1;
                        ctxfg.SetFillStyle("violet");
                        ctxfg.FillRect(-lineScale * 0.2, -lineScale * 0.2, lineScale * 0.4, lineScale * 0.4);
                        ctxfg.SetFillStyle("yellow");
                        ctxfg.GlobalAlpha = (line.alpha + 0.5) / 1.5;
                        ctxfg.FillText(line.lineId.ToString(CultureInfo.InvariantCulture), 0, -lineScale * 0.3);
                    }
                    foreach (var note in app.notesReversed)
                    {
                        if (!note.visible) continue;
                        ctxfg.SetTransform(note.cosr, note.sinr, -note.sinr, note.cosr, note.offsetX, note.offsetY);
                        ctxfg.GlobalAlpha = 1;
                        ctxfg.SetFillStyle("lime");
                        ctxfg.FillRect(-lineScale * 0.2, -lineScale * 0.2, lineScale * 0.4, lineScale * 0.4);
                        ctxfg.SetFillStyle("cyan");
                        ctxfg.GlobalAlpha = note.seconds > timeChart ? 1 : 0.5;
                        ctxfg.FillText(note.name, 0, -lineScale * 0.3);
                    }
                }
            }
            hitImageList.Animate();
            if (settings.ShowCE2) hitWordList.Animate();
            ctxfg.GlobalAlpha = 1;
            // 进度条
            ctxfg.SetTransform(app.CanvasFgWidth / 1920.0, 0, 0, app.CanvasFgWidth / 1920.0, 0,
                lineScale * (timeIn.Second < 0.67 ? EaseOutSine(timeIn.Second * 1.5) - 1 : -EaseOutSine(timeOut.Second * 1.5)) * 1.75);
            ctxfg.DrawImage(res.ProgressBar, tmpProgress * 1920 - 1920, 0);
            ctxfg.ResetTransform();
            foreach (var cb in After) cb();
            ctxfg.SetFillStyle("#fff");
            // 开头过渡动画
            if (timeIn.Second < 3)
            {
                if (timeIn.Second < 0.67) ctxfg.GlobalAlpha = EaseOutSine(timeIn.Second * 1.5);
                else if (timeIn.Second >= 2.5) ctxfg.GlobalAlpha = EaseOutSine(6 - timeIn.Second * 2);
                ctxfg.TextAlign = TextAlign.Center;
                FillTextNode(ctxfg, tmpName, wlen, hlen * 0.75, lineScale * 1.1, app.CanvasFgWidth - lineScale * 1.5);
                FillTextNode(ctxfg, tmpArtist, wlen, hlen * 0.75 + lineScale * 1.25, lineScale * 0.55, app.CanvasFgWidth - lineScale * 1.5);
                FillTextNode(ctxfg, tmpIllustrator, wlen, hlen * 1.25 + lineScale * 0.55, lineScale * 0.55, app.CanvasFgWidth - lineScale * 1.5);
                FillTextNode(ctxfg, tmpCharter, wlen, hlen * 1.25 + lineScale * 1.4, lineScale * 0.55, app.CanvasFgWidth - lineScale * 1.5);
                ctxfg.GlobalAlpha = 1;
                ctxfg.SetTransform(1, 0, 0, 1, wlen, hlen);
                double imgW = lineScale * 48 * (timeIn.Second < 0.67 ? EaseInSine(timeIn.Second * 1.5) : 1);
                double imgH = lineScale * 0.15;
                if (timeIn.Second >= 2.5) ctxfg.GlobalAlpha = EaseOutSine(6 - timeIn.Second * 2);
                ctxfg.DrawImage(settings.LineColor ? res.JudgeLineMP : res.JudgeLine, -imgW / 2, -imgH / 2, imgW, imgH);
            }
            // 分数与 combo
            ctxfg.GlobalAlpha = 1;
            ctxfg.SetTransform(1, 0, 0, 1, 0, lineScale * (timeIn.Second < 0.67 ? EaseOutSine(timeIn.Second * 1.5) - 1 : -EaseOutSine(timeOut.Second * 1.5)) * 1.75);
            if (tmpShowStat)
            {
                ctxfg.SetFont(lineScale * 0.95, "Custom,Noto Sans SC");
                ctxfg.TextAlign = TextAlign.Right;
                ctxfg.FillText(stat.scoreStr, app.CanvasFgWidth - lineScale * 0.65, lineScale * 1.375);
                if (settings.ShowAcc)
                {
                    ctxfg.SetFont(lineScale * 0.66, "Custom,Noto Sans SC");
                    ctxfg.FillText(stat.accStr, app.CanvasFgWidth - lineScale * 0.65, lineScale * 2.05);
                }
            }
            ctxfg.TextAlign = TextAlign.Center;
            ctxfg.SetFont(lineScale * 1.32, "Custom,Noto Sans SC");
            ctxfg.FillText(tmpCombo, wlen, lineScale * 1.375);
            ctxfg.GlobalAlpha = timeIn.Second < 0.67 ? EaseOutSine(timeIn.Second * 1.5) : 1 - EaseOutSine(timeOut.Second * 1.5);
            ctxfg.SetFont(lineScale * 0.66, "Custom,Noto Sans SC");
            ctxfg.FillText(tmpCombo2, wlen, lineScale * 2.05);
            // 曲名与等级
            ctxfg.GlobalAlpha = 1;
            ctxfg.SetTransform(1, 0, 0, 1, 0, lineScale * (timeIn.Second < 0.67 ? 1 - EaseOutSine(timeIn.Second * 1.5) : EaseOutSine(timeOut.Second * 1.5)) * 1.75);
            ctxfg.TextAlign = TextAlign.Right;
            FillTextNode(ctxfg, tmpLevel, app.CanvasFgWidth - lineScale * 0.75, app.CanvasFgHeight - lineScale * 0.66, lineScale * 0.63, wlen - lineScale);
            ctxfg.TextAlign = TextAlign.Left;
            FillTextNode(ctxfg, tmpName, lineScale * 0.65, app.CanvasFgHeight - lineScale * 0.66, lineScale * 0.63, wlen - lineScale);
            ctxfg.ResetTransform();
            // 时间/帧率/打击数
            ctxfg.SetFillStyle("#fff");
            ctxfg.GlobalAlpha = timeIn.Second < 0.67 ? EaseOutSine(timeIn.Second * 1.5) : 1 - EaseOutSine(timeOut.Second * 1.5);
            ctxfg.SetFont(lineScale * 0.4, "Custom,Noto Sans SC");
            ctxfg.TextAlign = TextAlign.Left;
            ctxfg.FillText(Time2Str(Awawa ? duration0 - timeBgm : timeBgm) + "/" + Time2Str(duration0) + Status2Text, lineScale * 0.05, lineScale * 0.6);
            ctxfg.FillText(stat.format, app.CanvasFgWidth - lineScale * 4.35, lineScale * 0.6);
            ctxfg.TextAlign = TextAlign.Right;
            ctxfg.FillText(frameTimer.FpsStr, app.CanvasFgWidth - lineScale * 0.05, lineScale * 0.6);
            if (tmpShowStat && settings.ShowStat)
            {
                ctxfg.TextAlign = TextAlign.Right;
                int[] order = { stat.noteRank[6], stat.noteRank[7], stat.noteRank[5], stat.noteRank[4], stat.noteRank[1], stat.noteRank[3], stat.noteRank[2] };
                string[] comboColor = { "#fe7b93", "#0ac3ff", "lime", "#f0ed69", "lime", "#0ac3ff", "#999" };
                for (int idx = 0; idx < order.Length; idx++)
                {
                    ctxfg.SetFillStyle(comboColor[idx]);
                    ctxfg.FillText(order[idx].ToString(CultureInfo.InvariantCulture), app.CanvasFgWidth - lineScale * 0.05, app.CanvasFgHeight / 2.0 + lineScale * (idx - 2.8) * 0.5);
                }
                ctxfg.SetFillStyle("#fff");
                ctxfg.TextAlign = TextAlign.Left;
                ctxfg.FillText("DSP:  " + stat.curDispStr, lineScale * 0.05, app.CanvasFgHeight / 2.0 - lineScale * 0.15);
                ctxfg.FillText("AVG:  " + stat.avgDispStr, lineScale * 0.05, app.CanvasFgHeight / 2.0 + lineScale * 0.35);
                ctxfg.TextAlign = TextAlign.Center;
                string[] comboColor2 = { "#fff", "#0ac3ff", "#f0ed69", "#a0e9fd", "#fe4365" };
                for (int idx = 0; idx < stat.combos.Length; idx++)
                {
                    ctxfg.SetFillStyle(comboColor2[idx]);
                    ctxfg.FillText(stat.combos[idx].ToString(CultureInfo.InvariantCulture), lineScale * (idx + 0.55) * 1.1, app.CanvasFgHeight - lineScale * 0.1);
                }
            }
            if (timeIn.Second >= 2.5 && timeIn.Second < 3) ctxfg.GlobalAlpha = 1 - EaseOutSine(6 - timeIn.Second * 2);
            else ctxfg.GlobalAlpha = 1 - EaseOutSine(timeOut.Second * 1.5);
            if (timeIn.Second >= 2.5 && CustomForeDraw != null) CustomForeDraw(ctxfg);
            if (timeIn.Second >= 2.5 && Filter != null) Filter(ctxfg, timeBgm, nowTimeMS / 1e3);
            if (settings.Feedback) hitFeedbackList.Animate();
            ctxfg.ResetTransform();
        }

        // ============ drawLine ============
        // bool: 0 默认, 1 非, 2 恒成立
        private void DrawLine(Ctx ctxfg, int boolv, double lineScale)
        {
            double tw = 1 - EaseOutSine(timeOut.Second * 1.5);
            foreach (var line in app.linesReversed)
            {
                if (((boolv ^ (line.imageD ? 1 : 0)) != 0) && timeOut.Second < 0.67)
                {
                    ctxfg.GlobalAlpha = line.alpha;
                    ctxfg.SetTransform(line.cosr * tw, line.sinr, -line.sinr * tw, line.cosr, app.wlen + (line.offsetX - app.wlen) * tw, line.offsetY);
                    double imgS = (line.imageU ? lineScale * 18.75 : app.CanvasFgHeight) * line.imageS / 1080;
                    double imgW = imgS * line.imageW * line.imageA;
                    double imgH = imgS * line.imageH;
                    int idx = line.imageC && settings.LineColor ? stat.lineStatus : 0;
                    var img = line.imageL[idx] as CanvasBitmap;
                    if (img != null) ctxfg.DrawImage(img, -imgW / 2, -imgH / 2, imgW, imgH);
                }
            }
            ctxfg.GlobalAlpha = 1;
        }

        private double FillTextNode(Ctx ctxfg, string text, double x, double y, double size, double maxWidth)
        {
            ctxfg.SetFont(size, "Custom,Noto Sans SC");
            double dx = ctxfg.MeasureText(text);
            if (dx > maxWidth) ctxfg.SetFont(size / dx * maxWidth, "Custom,Noto Sans SC");
            ctxfg.FillText(text, x, y);
            return dx;
        }

        // ============ atDraw3（结算页）============
        private void AtDraw3(Ctx ctxfg, StatData statData)
        {
            ctxfg.ResetTransform();
            ctxfg.ClearRect(0, 0, app.CanvasFgWidth, app.CanvasFgHeight);
            ctxfg.GlobalAlpha = 1;
            var bgImageBlur = background.GetImageBlur();
            DrawImageFit(ctxfg, bgImageBlur, app.CanvasFgWidth, app.CanvasFgHeight, 1);
            ctxfg.SetFillStyle("#000");
            ctxfg.GlobalAlpha = app.brightness;
            ctxfg.FillRect(0, 0, app.CanvasFgWidth, app.CanvasFgHeight);
            ctxfg.GlobalCompositeOperation = CompositeOp.DestinationOut;
            ctxfg.GlobalAlpha = 1;
            double k = 3.7320508075688776; // tan75°
            ctxfg.SetTransform(app.CanvasFgWidth - app.CanvasFgHeight / k, 0, -app.CanvasFgHeight / k, app.CanvasFgHeight, app.CanvasFgHeight / k, 0);
            ctxfg.FillRect(0, 0, 1, EaseOutCubic(Clip((timeEnd.Second - 0.13) * 0.94)));
            ctxfg.ResetTransform();
            ctxfg.GlobalCompositeOperation = CompositeOp.DestinationOver;
            double skew = (app.CanvasFgWidth - app.CanvasFgHeight / k) / (16 - 9 / k);
            ctxfg.SetTransform(skew / 120, 0, 0, skew / 120, app.wlen - skew * 8, app.hlen - skew * 4.5);
            ctxfg.DrawImage(res.LevelOver4, 183, 42, 1184, 228);
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 0.27) / 0.83);
            ctxfg.DrawImage(res.LevelOver1, 102, 378);
            ctxfg.GlobalCompositeOperation = CompositeOp.SourceOver;
            ctxfg.GlobalAlpha = 1;
            ctxfg.DrawImage(res.LevelOver5, 700 * EaseOutCubic(Clip(timeEnd.Second * 1.25)) - 369, 91, 20, 80);
            // 曲名与等级
            ctxfg.SetFillStyle("#fff");
            ctxfg.TextAlign = TextAlign.Left;
            FillTextNode(ctxfg, info.NameOrPlaceholder, 700 * EaseOutCubic(Clip(timeEnd.Second * 1.25)) - 320, 160, 80, 1500);
            double textWidth = FillTextNode(ctxfg, info.LevelText, 700 * EaseOutCubic(Clip(timeEnd.Second * 1.25)) - 317, 212, 30, 750);
            ctxfg.SetFont(30, "Custom,Noto Sans SC");
            // Rank 图标
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 1.87) * 3.75);
            double outerSize = 293 + Clip((timeEnd.Second - 1.87) * 3.75) * 100;
            double innerSize = 410 - Clip((timeEnd.Second - 1.87) * 2.14) * 164;
            ctxfg.DrawImage(res.LevelOver3, 661 - outerSize / 2, 545 - outerSize / 2, outerSize, outerSize);
            ctxfg.DrawImage(res.Ranks[stat.rankStatus], 661 - innerSize / 2, 545 - innerSize / 2, innerSize, innerSize);
            // 数据
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 0.87) * 2.5);
            ctxfg.SetFillStyle(statData.newBestColor);
            ctxfg.FillText(statData.newBestStr, 898, 433);
            ctxfg.SetFillStyle("#fff");
            ctxfg.TextAlign = TextAlign.Center;
            ctxfg.FillText(statData.scoreBest, 1180, 433);
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 1.87) * 2.5);
            ctxfg.TextAlign = TextAlign.Right;
            ctxfg.FillText(statData.scoreDelta, 1414, 433);
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 0.95) * 1.5);
            ctxfg.TextAlign = TextAlign.Left;
            ctxfg.FillText(stat.accStr, 352, 550);
            ctxfg.FillText(stat.maxcombo.ToString(CultureInfo.InvariantCulture), 1528, 550);
            ctxfg.SetFillStyle(statData.textAboveColor);
            ctxfg.FillText(app.speed == 1 ? "" : statData.textAboveStr.Replace("{SPEED}", app.speed.ToString("F2", CultureInfo.InvariantCulture)), 383 + Math.Min(textWidth, 750), 212);
            ctxfg.SetFillStyle(statData.textBelowColor);
            ctxfg.FillText(statData.textBelowStr, 1355, 595);
            ctxfg.SetFillStyle("#fff");
            ctxfg.TextAlign = TextAlign.Center;
            ctxfg.SetFont(86, "Custom,Noto Sans SC");
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 1.12) * 2.0);
            ctxfg.FillText(stat.scoreStr, 1075, 569);
            ctxfg.SetFont(26, "Custom,Noto Sans SC");
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 0.87) * 2.5);
            ctxfg.FillText(stat.perfect.ToString(CultureInfo.InvariantCulture), 891, 650);
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 1.07) * 2.5);
            ctxfg.FillText(stat.good.ToString(CultureInfo.InvariantCulture), 1043, 650);
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 1.27) * 2.5);
            ctxfg.FillText(stat.noteRank[6].ToString(CultureInfo.InvariantCulture), 1196, 650);
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 1.47) * 2.5);
            ctxfg.FillText(stat.noteRank[2].ToString(CultureInfo.InvariantCulture), 1349, 650);
            ctxfg.SetFont(22, "Custom,Noto Sans SC");
            double transition = Clip((PressTime > 0 ? timeEnd.Second - PressTime : 0.2 - timeEnd.Second - PressTime) * 5.0);
            var dev = ctxfg.Session.Device;
            if (_btn1 == null) _btn1 = SvgPath.Build(dev, "M841,718s-10,0-10,10v80s0,10,10,10h100s10,0,10-10v-80s0-10-10-10h-40l-10-20-10,20h-40z");
            if (_btn2 == null) _btn2 = SvgPath.Build(dev, "M993,718s-10,0-10,10v80s0,10,10,10h100s10,0,10-10v-80s0-10-10-10h-40l-10-20-10,20h-40z");
            ctxfg.GlobalAlpha = 0.8 * Clip((timeEnd.Second - 0.87) * 2.5) * transition;
            ctxfg.SetFillStyle("#696");
            ctxfg.Fill(_btn1);
            ctxfg.GlobalAlpha = 0.8 * Clip((timeEnd.Second - 1.07) * 2.5) * transition;
            ctxfg.SetFillStyle("#669");
            ctxfg.Fill(_btn2);
            ctxfg.SetFillStyle("#fff");
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 0.97) * 2.5) * transition;
            ctxfg.FillText("Early: " + stat.noteRank[5], 891, 759);
            ctxfg.FillText("Late: " + stat.noteRank[1], 891, 792);
            ctxfg.GlobalAlpha = Clip((timeEnd.Second - 1.17) * 2.5) * transition;
            ctxfg.FillText("Early: " + stat.noteRank[7], 1043, 759);
            ctxfg.FillText("Late: " + stat.noteRank[3], 1043, 792);
            ctxfg.ResetTransform();
            ctxfg.GlobalCompositeOperation = CompositeOp.DestinationOver;
            ctxfg.GlobalAlpha = 1;
            ctxfg.SetFillStyle("#000");
            var bgImage = background.GetImage();
            DrawImageFit(ctxfg, bgImage, app.CanvasFgWidth, app.CanvasFgHeight, 1);
            ctxfg.FillRect(0, 0, app.CanvasFgWidth, app.CanvasFgHeight);
            ctxfg.GlobalCompositeOperation = CompositeOp.SourceOver;
        }

        // ============ drawNotes ============
        private void DrawNotes(Ctx ctxfg)
        {
            if (!_skinsCached) CacheSkins();
            foreach (var note in app.holds) DrawHold(ctxfg, note, timeChart);
            foreach (var note in app.dragsReversed) DrawDrag(ctxfg, note);
            foreach (var note in app.tapsReversed) DrawTap(ctxfg, note);
            foreach (var note in app.flicksReversed) DrawFlick(ctxfg, note);
        }

        private void DrawTap(Ctx ctxfg, Note note)
        {
            bool HL = note.isMulti && app.multiHint;
            double nsr = app.noteScaleRatio;
            if (!note.visible || (note.scored && note.badTime == null)) return;
            ctxfg.SetTransform(nsr * note.cosr, nsr * note.sinr, -nsr * note.sinr, nsr * note.cosr, note.offsetX, note.offsetY);
            if (note.badTime == null)
            {
                ctxfg.GlobalAlpha = note.alpha != 0 ? note.alpha : (note.showPoint && settings.ShowPoint ? 0.45 : 0);
                if (Awawa) ctxfg.GlobalAlpha *= Math.Max(1 + (timeChart - note.seconds) / 1.5, 0);
                (HL ? _tapHL : _tap).Full(ctxfg);
            }
            else
            {
                ctxfg.GlobalAlpha = 1 - Clip((Clock.Now() - note.badTime.Value) / 500);
                _tapBad.Full(ctxfg);
            }
        }

        private void DrawDrag(Ctx ctxfg, Note note)
        {
            bool HL = note.isMulti && app.multiHint;
            double nsr = app.noteScaleRatio;
            if (!note.visible || (note.scored && note.badTime == null)) return;
            ctxfg.SetTransform(nsr * note.cosr, nsr * note.sinr, -nsr * note.sinr, nsr * note.cosr, note.offsetX, note.offsetY);
            if (note.badTime == null)
            {
                ctxfg.GlobalAlpha = note.alpha != 0 ? note.alpha : (note.showPoint && settings.ShowPoint ? 0.45 : 0);
                if (Awawa) ctxfg.GlobalAlpha *= Math.Max(1 + (timeChart - note.seconds) / 1.5, 0);
                (HL ? _dragHL : _drag).Full(ctxfg);
            }
        }

        private void DrawHold(Ctx ctxfg, Note note, double seconds)
        {
            bool HL = note.isMulti && app.multiHint;
            double nsr = app.noteScaleRatio;
            if (!note.visible || note.seconds + note.holdSeconds < seconds) return;
            ctxfg.GlobalAlpha = note.alpha != 0 ? note.alpha : (note.showPoint && settings.ShowPoint ? 0.45 : 0);
            if (Awawa) ctxfg.GlobalAlpha *= Math.Max(1 + (timeChart - note.seconds) / 1.5, 0);
            ctxfg.SetTransform(nsr * note.cosr, nsr * note.sinr, -nsr * note.sinr, nsr * note.cosr, note.offsetX, note.offsetY);
            double baseLength = app.scaleY / nsr * note.speed * app.speed;
            double holdLength = baseLength * note.holdSeconds;
            if (note.seconds > seconds)
            {
                (HL ? _hHeadHL : _hHead).Head(ctxfg);
                (HL ? _holdHL : _hold).Body(ctxfg, -holdLength, holdLength);
            }
            else (HL ? _holdHL : _hold).Body(ctxfg, -holdLength, holdLength - baseLength * (seconds - note.seconds));
            _hEnd.Tail(ctxfg, -holdLength);
        }

        private void DrawFlick(Ctx ctxfg, Note note)
        {
            bool HL = note.isMulti && app.multiHint;
            double nsr = app.noteScaleRatio;
            if (!note.visible || (note.scored && note.badTime == null)) return;
            ctxfg.SetTransform(nsr * note.cosr, nsr * note.sinr, -nsr * note.sinr, nsr * note.cosr, note.offsetX, note.offsetY);
            if (note.badTime == null)
            {
                ctxfg.GlobalAlpha = note.alpha != 0 ? note.alpha : (note.showPoint && settings.ShowPoint ? 0.45 : 0);
                if (Awawa) ctxfg.GlobalAlpha *= Math.Max(1 + (timeChart - note.seconds) / 1.5, 0);
                (HL ? _flickHL : _flick).Full(ctxfg);
            }
        }

        // ============ judgeManager ============
        private static double GetJudgeOffset(JudgeEvent je, Note note)
        {
            double r = Math.Abs((je.offsetX - note.offsetX) * note.cosr + (je.offsetY - note.offsetY) * note.sinr);
            return double.IsNaN(r) ? 0 : r;
        }
        private static double GetJudgeDistance(JudgeEvent je, Note note)
        {
            double r = Math.Abs((je.offsetX - note.offsetX) * note.cosr + (je.offsetY - note.offsetY) * note.sinr)
                     + Math.Abs((je.offsetX - note.offsetX) * note.sinr - (je.offsetY - note.offsetY) * note.cosr);
            return double.IsNaN(r) ? 0 : r;
        }

        private void JudgeAddEvent(List<Note> notes, double seconds)
        {
            var list = judgeList;
            list.Clear();
            if (app.playMode == 1)
            {
                double dispTime = Math.Min(frameTimer.Disp, 0.04);
                foreach (var note in notes)
                {
                    if (note.scored) continue;
                    double deltaTime = note.seconds - seconds;
                    if (note.type == 1) { if (deltaTime < dispTime) list.Add(new JudgeEvent(note.offsetX, note.offsetY, 1)); }
                    else if (note.type == 2) { if (deltaTime < dispTime) list.Add(new JudgeEvent(note.offsetX, note.offsetY, 2)); }
                    else if (note.type == 3)
                    {
                        if (note.holdTapTime != 0) list.Add(new JudgeEvent(note.offsetX, note.offsetY, 2));
                        else if (deltaTime < dispTime) list.Add(new JudgeEvent(note.offsetX, note.offsetY, 1));
                    }
                    else if (note.type == 4) { if (deltaTime < dispTime) list.Add(new JudgeEvent(note.offsetX, note.offsetY, 3)); }
                }
            }
            else if (EmEq(Phase.Play))
            {
                foreach (var evt in hitManager.list)
                {
                    if (!evt.isTapped) list.Add(new JudgeEvent(evt.offsetX, evt.offsetY, 1));
                    if (evt.isActive) list.Add(new JudgeEvent(evt.offsetX, evt.offsetY, 2));
                    if (evt.type == HitType.Keyboard) list.Add(new JudgeEvent(evt.offsetX, evt.offsetY, 3));
                    if (evt.flicking && !evt.flicked) list.Add(new JudgeEvent(evt.offsetX, evt.offsetY, 3, evt));
                }
            }
        }

        private void JudgeExecute(List<Note> notes, double seconds, double width)
        {
            var list = judgeList;
            foreach (var note in notes)
            {
                if (note.scored) continue;
                double deltaTime = note.seconds - seconds;
                if (deltaTime > 0.2) break;
                if (note.type != 1 && deltaTime > 0.16) continue;
                if (deltaTime < -0.16 && note.frameCount > 4 && note.holdStatus == 0) // 超时且非Hold拖判 → Miss
                {
                    note.status = 2;
                    stat.addCombo(2, note.type);
                    note.scored = true;
                }
                else if (note.type == 2)
                {
                    if (deltaTime > 0)
                        foreach (var je in list)
                        {
                            if (je.type != 1) continue;
                            if (GetJudgeOffset(je, note) > width) continue;
                            je.preventBad = true;
                        }
                    if (note.status != 4)
                    {
                        foreach (var je in list)
                        {
                            if (je.type != 2) continue;
                            if (GetJudgeOffset(je, note) > width) continue;
                            note.status = 4;
                            break;
                        }
                    }
                    else if (deltaTime < 0)
                    {
                        soundController.Play(res.HitSong1, AudioOptions.Make());
                        hitImageList.Add(HitImage.perfect(note.projectX, note.projectY, note));
                        stat.addCombo(4, 2);
                        note.scored = true;
                    }
                }
                else if (note.type == 4)
                {
                    if (deltaTime > 0 || note.status != 4)
                        foreach (var je in list)
                        {
                            if (je.type != 1) continue;
                            if (GetJudgeOffset(je, note) > width) continue;
                            je.preventBad = true;
                        }
                    if (note.status != 4)
                    {
                        foreach (var je in list)
                        {
                            if (je.type != 3) continue;
                            if (GetJudgeOffset(je, note) > width) continue;
                            double distance = GetJudgeDistance(je, note);
                            Note noteJudge = note;
                            bool nearcomp = false;
                            foreach (var nearNote in note.nearNotes)
                            {
                                if (nearNote.status != 0) continue;
                                if (nearNote.seconds - seconds > 0.16) break;
                                if (GetJudgeOffset(je, nearNote) > width) continue;
                                double nearDistance = GetJudgeDistance(je, nearNote);
                                if (nearDistance < distance) { distance = nearDistance; noteJudge = nearNote; nearcomp = true; }
                            }
                            if (je.@event == null) { noteJudge.status = 4; if (!nearcomp) break; }
                            else if (!je.@event.flicked) { noteJudge.status = 4; je.@event.flicked = true; if (!nearcomp) break; }
                        }
                    }
                    else if (deltaTime < 0)
                    {
                        soundController.Play(res.HitSong2, AudioOptions.Make());
                        hitImageList.Add(HitImage.perfect(note.projectX, note.projectY, note));
                        stat.addCombo(4, 4);
                        note.scored = true;
                    }
                }
                else // Hold
                {
                    if (note.type == 3 && note.holdTapTime != 0)
                    {
                        if ((Clock.Now() - note.holdTapTime) * note.holdTime >= 1.6e4 * note.holdSeconds)
                        {
                            if (note.holdStatus % 4 == 0) hitImageList.Add(HitImage.perfect(note.projectX, note.projectY, note));
                            else if (note.holdStatus % 4 == 1) hitImageList.Add(HitImage.perfect(note.projectX, note.projectY, note));
                            else if (note.holdStatus % 4 == 3) hitImageList.Add(HitImage.good(note.projectX, note.projectY, note));
                            note.holdTapTime = Clock.Now();
                        }
                        if (deltaTime + note.holdSeconds < 0.2)
                        {
                            if (note.status == 0) { note.status = note.holdStatus; stat.addCombo(note.status, 3); }
                            if (deltaTime + note.holdSeconds < 0) note.scored = true;
                            continue;
                        }
                        note.holdBroken = true;
                    }
                    foreach (var je in list)
                    {
                        if (note.holdTapTime != 0)
                        {
                            if (je.type != 2) continue;
                            if (GetJudgeOffset(je, note) <= width) { note.holdBroken = false; break; }
                            continue;
                        }
                        if (je.type != 1) continue;
                        if (je.judged) continue;
                        if (GetJudgeOffset(je, note) > width) continue;
                        double deltaTime2 = deltaTime;
                        double distance = GetJudgeDistance(je, note);
                        Note noteJudge = note;
                        bool nearcomp = false;
                        foreach (var nearNote in note.nearNotes)
                        {
                            if (nearNote.status != 0) continue;
                            if (nearNote.holdTapTime != 0) continue;
                            double nearDeltaTime = nearNote.seconds - seconds;
                            if (nearDeltaTime > 0.2) break;
                            if (nearNote.type == 3 && nearDeltaTime > 0.16) continue;
                            if (GetJudgeOffset(je, nearNote) > width) continue;
                            double nearDistance = GetJudgeDistance(je, nearNote);
                            if (nearDistance < distance) { deltaTime2 = nearDeltaTime; distance = nearDistance; noteJudge = nearNote; nearcomp = true; }
                        }
                        if (deltaTime2 > 0.16)
                        {
                            if (je.preventBad) continue;
                            noteJudge.status = 6;
                            noteJudge.badTime = Clock.Now();
                        }
                        else
                        {
                            Note note1 = noteJudge;
                            double fc = (-1 - (note1.frameCount ?? 0)) * 0.04;
                            stat.addDisp(Math.Max(deltaTime2, double.IsNaN(fc) ? 0 : fc));
                            soundController.Play(res.HitSong0, AudioOptions.Make());
                            if (deltaTime2 > 0.08)
                            {
                                note1.holdStatus = 7;
                                hitImageList.Add(HitImage.good(note1.projectX, note1.projectY, note1));
                                hitWordList.Add(HitWord.early(note1.projectX, note1.projectY));
                            }
                            else if (deltaTime2 > 0.04)
                            {
                                note1.holdStatus = 5;
                                hitImageList.Add(HitImage.perfect(note1.projectX, note1.projectY, note1));
                                hitWordList.Add(HitWord.early(note1.projectX, note1.projectY));
                            }
                            else if (deltaTime2 > -0.04 || note1.frameCount < 1)
                            {
                                note1.holdStatus = 4;
                                hitImageList.Add(HitImage.perfect(note1.projectX, note1.projectY, note1));
                            }
                            else if (deltaTime2 > -0.08 || note1.frameCount < 2)
                            {
                                note1.holdStatus = 1;
                                hitImageList.Add(HitImage.perfect(note1.projectX, note1.projectY, note1));
                                hitWordList.Add(HitWord.late(note1.projectX, note1.projectY));
                            }
                            else
                            {
                                note1.holdStatus = 3;
                                hitImageList.Add(HitImage.good(note1.projectX, note1.projectY, note1));
                                hitWordList.Add(HitWord.late(note1.projectX, note1.projectY));
                            }
                            if (note1.type == 1) note1.status = note1.holdStatus;
                        }
                        if (noteJudge.status != 0) { stat.addCombo(noteJudge.status, 1); noteJudge.scored = true; }
                        else { noteJudge.holdTapTime = Clock.Now(); noteJudge.holdBroken = false; }
                        je.judged = true;
                        noteJudge.statOffset = deltaTime2;
                        if (!nearcomp) break;
                    }
                    if (EmEq(Phase.Play) && note.holdTapTime != 0 && note.holdBroken)
                    {
                        note.status = 2;
                        stat.addCombo(2, 3);
                        note.scored = true;
                    }
                }
            }
        }

        // ============ playBgm ============
        private void PlayBgm(IAudioBuffer data, double? offset = null)
        {
            curTimeMS = Clock.Now();
            if (data == null) return;
            bgMusicHack = musicController.Play(data, AudioOptions.Make(
                offset: offset ?? 0,
                playbackrate: app.speed,
                interval: settings.AutoDelay ? 1 : 0));
        }

        // ============ 工具 ============
        private static void DrawImageFit(Ctx ctx, CanvasBitmap img, double dw, double dh, double scale)
        {
            if (img == null) return;
            double sw = img.SizeInPixels.Width, sh = img.SizeInPixels.Height;
            Rect r = AdjustSize(sw, sh, dw, dh, scale);
            ctx.DrawImage(img, r.X, r.Y, r.Width, r.Height);
        }

        private static Rect AdjustSize(double sw, double sh, double dw, double dh, double scale)
        {
            if (dw * sh > dh * sw) return new Rect(dw * (1 - scale) / 2, (dh - dw * sh / sw * scale) / 2, dw * scale, dw * sh / sw * scale);
            return new Rect((dw - dh * sw / sh * scale) / 2, dh * (1 - scale) / 2, dh * sw / sh * scale, dh * scale);
        }

        private static double EaseInSine(double t) { return 1 - Math.Cos(t * Math.PI / 2); }
        private static double EaseOutSine(double t) { return Math.Sin(t * Math.PI / 2); }
        private static double EaseOutCubic(double t) { return 1 + Math.Pow(t - 1, 3); }
        private static double Clip(double num) { if (num < 0) return 0; if (num > 1) return 1; return num; }
        private static string Time2Str(double time)
        {
            if (time < 0) time = 0;
            int m = (int)Math.Floor(time / 60);
            int s = (int)Math.Floor(time % 60);
            return m + ":" + ("00" + s).Substring(("00" + s).Length - 2);
        }
    }
}
