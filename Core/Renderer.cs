using System;
using System.Collections.Generic;
using System.Globalization;

namespace SimPhi.Core
{
    /// <summary>
    /// 照抄 src/core.ts 的 Renderer 类。纯数学/数据，与渲染后端无关。
    /// 与原版差异仅在于：原版自己 new 出两个 &lt;canvas&gt; 并读它们的 .width/.height，
    /// 这里把画布尺寸抽象成 CanvasWidth/CanvasFgWidth 等数值字段，由渲染层（Win2D）按这些尺寸建 CanvasRenderTarget。
    /// 其余逻辑逐行对应。
    /// </summary>
    public sealed class Renderer
    {
        // ===== 画布尺寸（替代原版的 canvas / canvasfg）=====
        /// <summary>主画布像素宽（device px），对应 canvas.width。</summary>
        public int CanvasWidth;
        public int CanvasHeight;
        /// <summary>游戏前景画布像素宽（强制 16:9），对应 canvasfg.width。</summary>
        public int CanvasFgWidth;
        public int CanvasFgHeight;
        /// <summary>canvasfg 宽的一半。</summary>
        public double wlen;
        /// <summary>canvasfg 高的一半。</summary>
        public double hlen;
        public double scaleX;
        public double scaleY;
        private double width;   // 逻辑尺寸（CSS px）
        private double height;
        /// <summary>替代 self.devicePixelRatio，由渲染层按显示器缩放设置。</summary>
        public double DevicePixelRatio = 1.0;

        private Func<double, double> matX;
        private Func<double, double> matY;
        private Func<double, double> matR;

        // ===== 用户设置项 =====
        public double speed;
        public double lineScale;
        public double noteScale;
        public double noteScaleRatio;
        public double brightness;
        public bool multiHint;
        public int playMode;        // 0:game,1:auto,2:hyper,3:auto&hyper
        public double musicVolume;
        public double soundVolume;
        public bool enableFR;
        public bool enableVP;
        public double lowResFactor;

        // ===== 谱面数据 =====
        public readonly List<JudgeLine> lines = new List<JudgeLine>();
        public readonly List<Note> notes = new List<Note>();
        public readonly List<Note> taps = new List<Note>();
        public readonly List<Note> drags = new List<Note>();
        public readonly List<Note> flicks = new List<Note>();
        public readonly List<Note> holds = new List<Note>();
        public List<JudgeLine> linesReversed = new List<JudgeLine>();
        public List<Note> notesReversed = new List<Note>();
        public List<Note> tapsReversed = new List<Note>();
        public List<Note> dragsReversed = new List<Note>();
        public List<Note> flicksReversed = new List<Note>();
        public List<Note> holdsReversed = new List<Note>();
        public readonly List<Note> tapholds = new List<Note>();

        // ===== 资源数据（用 object 解耦渲染/音频后端）=====
        public Chart chart;
        public object bgMusic;   // 运行期为 AudioBuffer 等价物
        public object bgVideo;   // 运行期为视频源
        private int _mirrorType;
        private bool initialized;

        public Renderer(double initialWidth = 0, double initialHeight = 0)
        {
            // config
            speed = 1;
            lineScale = 57.6;
            noteScale = 1;
            noteScaleRatio = 8e3;
            brightness = 0.6;
            multiHint = true;
            playMode = 1;
            musicVolume = 1;
            soundVolume = 1;
            _mirrorType = 0;
            enableFR = false;
            enableVP = false;
            // chart
            chart = null;
            bgMusic = null;
            bgVideo = null;
            // canvas
            lowResFactor = 1;
            width = initialWidth;
            height = initialHeight;
            CanvasWidth = 0;
            CanvasHeight = 0;
            CanvasFgWidth = 0;
            CanvasFgHeight = 0;
            wlen = 0;
            hlen = 0;
            scaleX = 0;
            scaleY = 0;
            matX = x => x;
            matY = y => y;
            matR = r => r;
            initialized = false;
            _setLowResFactor(1);
            ResizeCanvas(initialWidth, initialHeight);
        }

        public double Duration
        {
            get
            {
                if (bgMusic is IAudioBuffer ab) return ab.Duration;
                if (chart != null) return chart.maxSeconds + 0.5;
                throw new InvalidOperationException("Illegal state");
            }
        }

        // ===== config =====
        public void SetNoteScale(double num = 1)
        {
            noteScale = num;
            noteScaleRatio = CanvasFgWidth * noteScale / 8080;
        }

        public void SetLowResFactor(double num = 1)
        {
            _setLowResFactor(num);
            _resizeCanvas();
        }

        /// <summary>对应 resizeCanvas()：渲染层每帧把当前舞台逻辑尺寸推进来，尺寸变化才重算。</summary>
        public void ResizeCanvas(double w, double h)
        {
            if (width == w && height == h) return;
            width = w;
            height = h;
            _resizeCanvas();
        }

        public void MirrorView(int code)
        {
            int n = 3 & code;
            _mirrorType = n;
            TransformView((1 & n) != 0 ? -1 : 1, (2 & n) != 0 ? -1 : 1, 0, 0);
        }

        public void MirrorView()
        {
            MirrorView(_mirrorType);
        }

        public void TransformView(double scaleXIn = 1, double scaleYIn = 1, double offsetX = 0, double offsetY = 0)
        {
            double xa = CanvasFgWidth * scaleXIn;
            double xb = (CanvasFgWidth - xa) * 0.5;
            double ya = -CanvasFgHeight * scaleYIn;
            double yb = (CanvasFgHeight - ya) * 0.5;
            double ra = -Sign(scaleXIn * scaleYIn) * Math.PI / 180;
            double rb = scaleYIn > 0 ? 0 : Math.PI;
            double tx = Sign(scaleYIn) * xa * 0.05625;
            double ty = Sign(scaleYIn) * -ya * 0.6;   // 控制 note 流速
            matX = x => xb + xa * (x - offsetX);
            matY = y => yb + ya * (y - offsetY);
            matR = r => rb + ra * r;
            scaleX = tx;
            scaleY = ty;
            initialized = true;
        }

        // ===== note 预处理（照抄 prerenderChart）=====
        public void PrerenderChart(Chart chartIn)
        {
            lines.Clear();
            notes.Clear();
            taps.Clear();
            drags.Clear();
            flicks.Clear();
            holds.Clear();
            tapholds.Clear();
            var chartNew = chartIn.Duplicate();
            foreach (var line in chartNew.judgeLineList)
            {
                double y = Fround(line.speedEvents[0].startTime / line.bpm * 1.875);
                double y2 = y; // float32
                foreach (var evt in line.speedEvents)
                {
                    evt.floorPosition = y;
                    evt.floorPosition2 = y2;
                    double dy = (evt.endTime - evt.startTime) / line.bpm * 1.875;
                    y += dy * evt.value;
                    y2 += Fround(Fround(dy) * evt.value);
                    y = Fround(y);
                    y2 = Fround(y2);
                }
            }

            double aniStart = 1e9, aniEnd = 0, hitStart = 1e9, hitEnd = 0;
            Action<double> aniUpdate = seconds =>
            {
                if (seconds < aniStart) aniStart = seconds;
                if (seconds > aniEnd) aniEnd = seconds;
            };
            Action<double> hitUpdate = seconds =>
            {
                aniUpdate(seconds);
                if (seconds < hitStart) hitStart = seconds;
                if (seconds > hitEnd) hitEnd = seconds;
            };

            // 添加 seconds（speedEvents 与三类判定线事件共用）
            Action<IList<SpeedEvent>, double> addSecondsS = (events, bpm) =>
            {
                for (int k = 0; k < events.Count; k++)
                {
                    var evt = events[k];
                    evt.startSeconds = evt.startTime / bpm * 1.875;
                    evt.endSeconds = evt.endTime / bpm * 1.875;
                    if (evt.startTime > 1 - 1e6) aniUpdate(evt.startSeconds);
                    if (evt.endTime < 1e9 && evt.endTime != events[events.Count - 1].endTime) aniUpdate(evt.endSeconds);
                }
            };
            Action<IList<JudgeLineEvent>, double> addSecondsL = (events, bpm) =>
            {
                for (int k = 0; k < events.Count; k++)
                {
                    var evt = events[k];
                    evt.startSeconds = evt.startTime / bpm * 1.875;
                    evt.endSeconds = evt.endTime / bpm * 1.875;
                    if (evt.startTime > 1 - 1e6) aniUpdate(evt.startSeconds);
                    if (evt.endTime < 1e9 && evt.endTime != events[events.Count - 1].endTime) aniUpdate(evt.endSeconds);
                }
            };

            // 向 Renderer 添加 Note
            Action<Note, double, JudgeLine, int, bool> addNote = (note, beat32, line, noteId, isAbove) =>
            {
                note.offsetX = 0;
                note.offsetY = 0;
                note.alpha = 0;
                note.seconds = note.time * beat32;
                note.holdSeconds = note.holdTime * beat32;
                note.maxVisiblePos = GetMaxVisiblePos(note.floorPosition);
                if (note.time < 1e9)
                {
                    hitUpdate(note.seconds);
                    hitUpdate(note.seconds + note.holdSeconds);
                }
                note.line = line;
                note.lineId = line.lineId;
                note.noteId = noteId;
                note.isAbove = isAbove;
                char tc = (note.type >= 0 && note.type < 5) ? "?tdhf"[note.type] : '?';
                note.name = line.lineId.ToString() + (isAbove ? "+" : "-") + noteId.ToString() + tc;
                notes.Add(note);
                if (note.type == 1) taps.Add(note);
                else if (note.type == 2) drags.Add(note);
                else if (note.type == 3) holds.Add(note);
                else if (note.type == 4) flicks.Add(note);
                if (note.type == 1 || note.type == 3) tapholds.Add(note);
            };

            Comparison<Note> sortNote = (a, b) =>
            {
                double d = a.seconds - b.seconds;
                if (d < 0) return -1;
                if (d > 0) return 1;
                int e = a.lineId - b.lineId;
                if (e != 0) return e;
                return a.noteId - b.noteId;
            };

            // 优化 events
            for (int i = 0; i < chartNew.judgeLineList.Count; i++) chartNew.judgeLineList[i].lineId = i;
            foreach (var line in chartNew.judgeLineList)
            {
                line.bpm *= speed;
                line.offsetX = 0;
                line.offsetY = 0;
                line.alpha = 0;
                line.rotation = 0;
                line.positionY = 0;
                line.positionY2 = 0;
                line.disappearEventsIndex = 0;
                line.moveEventsIndex = 0;
                line.rotateEventsIndex = 0;
                line.speedEventsIndex = 0;
                addSecondsS(line.speedEvents, line.bpm);
                addSecondsL(line.judgeLineDisappearEvents, line.bpm);
                addSecondsL(line.judgeLineMoveEvents, line.bpm);
                addSecondsL(line.judgeLineRotateEvents, line.bpm);
                lines.Add(line);
                JudgeLine lineRef = line;
                for (int noteId = 0; noteId < line.notesAbove.Count; noteId++)
                    addNote(line.notesAbove[noteId], 1.875 / line.bpm, lineRef, noteId, true);
                for (int noteId = 0; noteId < line.notesBelow.Count; noteId++)
                    addNote(line.notesBelow[noteId], 1.875 / line.bpm, lineRef, noteId, false);
            }
            notes.Sort(sortNote);
            taps.Sort(sortNote);
            drags.Sort(sortNote);
            holds.Sort(sortNote);
            flicks.Sort(sortNote);
            notesReversed = Reversed(notes);
            tapsReversed = Reversed(taps);
            dragsReversed = Reversed(drags);
            holdsReversed = Reversed(holds);
            flicksReversed = Reversed(flicks);
            linesReversed = Reversed(lines);
            tapholds.Sort(sortNote);

            // 多押标记
            var timeOfMulti = new Dictionary<string, int>();
            foreach (var note in notes)
            {
                string key = note.seconds.ToString("F6", CultureInfo.InvariantCulture);
                int cur;
                timeOfMulti[key] = (timeOfMulti.TryGetValue(key, out cur) && cur != 0) ? 2 : 1;
            }
            foreach (var note in notes)
            {
                string key = note.seconds.ToString("F6", CultureInfo.InvariantCulture);
                int v;
                note.isMulti = timeOfMulti.TryGetValue(key, out v) && v == 2;
            }

            // 邻近 Note（0.01s 内）
            for (int i = 0; i < flicks.Count; i++)
            {
                var note = flicks[i];
                note.nearNotes = new List<Note>();
                for (int j = i + 1; j < flicks.Count; j++)
                {
                    var note2 = flicks[j];
                    if (Fround(note2.seconds - note.seconds) > 0.01) break;
                    note.nearNotes.Add(note2);
                }
            }
            for (int i = 0; i < tapholds.Count; i++)
            {
                var note = tapholds[i];
                note.nearNotes = new List<Note>();
                for (int j = i + 1; j < tapholds.Count; j++)
                {
                    var note2 = tapholds[j];
                    if (Fround(note2.seconds - note.seconds) > 0.01) break;
                    note.nearNotes.Add(note2);
                }
            }
            chart = chartNew;
            chart.maxSeconds = aniEnd;
        }

        /// <summary>从 0 开始，将事件 index 定位到正确位置。</summary>
        public void SeekLineEventIndex(double? time = null)
        {
            if (!initialized) throw new InvalidOperationException("Not initialized");
            foreach (var line in lines)
            {
                line.speedEventsIndex = 0;
                line.disappearEventsIndex = 0;
                line.moveEventsIndex = 0;
                line.rotateEventsIndex = 0;
                if (time == null) continue;
                double t = time.Value;
                while (line.speedEventsIndex < line.speedEvents.Count && line.speedEvents[line.speedEventsIndex].endSeconds < t) line.speedEventsIndex++;
                while (line.disappearEventsIndex < line.judgeLineDisappearEvents.Count && line.judgeLineDisappearEvents[line.disappearEventsIndex].endSeconds < t) line.disappearEventsIndex++;
                while (line.moveEventsIndex < line.judgeLineMoveEvents.Count && line.judgeLineMoveEvents[line.moveEventsIndex].endSeconds < t) line.moveEventsIndex++;
                while (line.rotateEventsIndex < line.judgeLineRotateEvents.Count && line.judgeLineRotateEvents[line.rotateEventsIndex].endSeconds < t) line.rotateEventsIndex++;
            }
        }

        public void UpdateByTime(double time)
        {
            if (!initialized) throw new InvalidOperationException("Not initialized");
            foreach (var line in lines)
            {
                JudgeLine ln = line;
                for (int i = ln.disappearEventsIndex, len = ln.judgeLineDisappearEvents.Count; i < len; i++)
                {
                    var evt = ln.judgeLineDisappearEvents[i];
                    if (time > evt.endSeconds) continue;
                    double dt = (time - evt.startSeconds) / (evt.endSeconds - evt.startSeconds);
                    ln.alpha = evt.start + (evt.end - evt.start) * dt;
                    if (ln.alpha > 1) ln.alpha = 1;
                    ln.disappearEventsIndex = i;
                    break;
                }
                for (int i = ln.moveEventsIndex, len = ln.judgeLineMoveEvents.Count; i < len; i++)
                {
                    var evt = ln.judgeLineMoveEvents[i];
                    if (time > evt.endSeconds) continue;
                    double dt = (time - evt.startSeconds) / (evt.endSeconds - evt.startSeconds);
                    ln.offsetX = matX(evt.start + (evt.end - evt.start) * dt);
                    ln.offsetY = matY(evt.start2 + (evt.end2 - evt.start2) * dt);
                    ln.moveEventsIndex = i;
                    break;
                }
                for (int i = ln.rotateEventsIndex, len = ln.judgeLineRotateEvents.Count; i < len; i++)
                {
                    var evt = ln.judgeLineRotateEvents[i];
                    if (time > evt.endSeconds) continue;
                    double dt = (time - evt.startSeconds) / (evt.endSeconds - evt.startSeconds);
                    ln.rotation = matR(evt.start + (evt.end - evt.start) * dt);
                    ln.cosr = Math.Cos(ln.rotation);
                    ln.sinr = Math.Sin(ln.rotation);
                    ln.rotateEventsIndex = i;
                    break;
                }
                for (int i = ln.speedEventsIndex, len = ln.speedEvents.Count; i < len; i++)
                {
                    var evt = ln.speedEvents[i];
                    if (time > evt.endSeconds) continue;
                    ln.positionY = (time - evt.startSeconds) * speed * evt.value + (enableFR ? evt.floorPosition2 : evt.floorPosition);
                    ln.positionY2 = (time - evt.startSeconds) * speed * evt.value + evt.floorPosition2;
                    ln.speedEventsIndex = i;
                    break;
                }

                foreach (var note in ln.notesAbove)
                {
                    note.cosr = ln.cosr;
                    note.sinr = ln.sinr;
                    SetAlpha(note, scaleX * note.positionX, scaleY * GetBadY(note, ln, time), ln, time);
                }
                foreach (var note in ln.notesBelow)
                {
                    note.cosr = -ln.cosr;
                    note.sinr = -ln.sinr;
                    SetAlpha(note, -scaleX * note.positionX, scaleY * GetBadY(note, ln, time), ln, time);
                }
            }
        }

        // 由 UpdateByTime 内联调用（原为每帧每线分配的闭包，改实例方法省 GC + 委托开销）
        private double GetGoodY(Note i, JudgeLine ln, double time)
        {
            if (i.type != 3) return (i.floorPosition - ln.positionY) * i.speed;
            if (i.seconds < time) return (i.seconds - time) * speed * i.speed;
            return i.floorPosition - ln.positionY;
        }

        private double GetBadY(Note i, JudgeLine ln, double time)
        {
            if (i.badTime == null) return GetGoodY(i, ln, time);
            if (Clock.Now() - i.badTime.Value > 500) i.badTime = null;
            if (i.badY == null) i.badY = GetGoodY(i, ln, time);
            return i.badY.Value;
        }

        private void SetAlpha(Note i, double dx, double dy, JudgeLine ln, double time)
        {
            i.projectX = ln.offsetX + dx * i.cosr;
            i.offsetX = i.projectX + dy * i.sinr;
            i.projectY = ln.offsetY + dx * i.sinr;
            i.offsetY = i.projectY - dy * i.cosr;
            double ddx = i.offsetX - wlen;
            double ddy = i.offsetY - hlen;
            double rad = wlen * 1.23625 + hlen + scaleY * i.holdSeconds * speed * i.speed;
            i.visible = ddx * ddx + ddy * ddy < rad * rad;
            i.showPoint = false;
            if (i.badTime != null)
            {
                // Not bad
            }
            else if (i.seconds > time)
            {
                i.showPoint = true;
                bool isHidden = i.maxVisiblePos < ln.positionY2;
                bool cond = isHidden || (enableVP && GetGoodY(i, ln, time) * 0.6 > 2);
                i.alpha = cond ? 0 : (i.type == 3 && i.speed == 0 ? 0 : 1);
            }
            else
            {
                i.frameCount = (i.frameCount == null) ? 0 : i.frameCount.Value + 1;
                if (i.type == 3)
                {
                    i.showPoint = true;
                    i.alpha = i.speed == 0 ? 0 : (i.status % 4 == 2 ? 0.45 : 1);
                }
                else i.alpha = Math.Max(1 - (time - i.seconds) / 0.16, 0);
            }
        }

        private void _setLowResFactor(double num = 1)
        {
            lowResFactor = num;
        }

        private void _resizeCanvas()
        {
            double lrf = lowResFactor * DevicePixelRatio;
            double widthLowRes = width * lrf;
            double heightLowRes = height * lrf;
            CanvasWidth = (int)widthLowRes;
            CanvasHeight = (int)heightLowRes;
            CanvasFgWidth = (int)Math.Min(widthLowRes, heightLowRes * 16 / 9);
            CanvasFgHeight = (int)heightLowRes;
            wlen = CanvasFgWidth / 2.0;
            hlen = CanvasFgHeight / 2.0;
            MirrorView();
            SetNoteScale(noteScale);
            lineScale = CanvasFgWidth > CanvasFgHeight * 0.75 ? CanvasFgHeight / 18.75 : CanvasFgWidth / 14.0625;
        }

        // ===== 工具：忠实复刻 JS 数值语义 =====

        /// <summary>Math.fround(x)：取最接近的 float32 再回到 double。</summary>
        private static double Fround(double x)
        {
            return (float)x;
        }

        /// <summary>Math.sign，对 ±1 输入与 JS 一致。</summary>
        private static double Sign(double x)
        {
            return x > 0 ? 1 : (x < 0 ? -1 : 0);
        }

        private static List<T> Reversed<T>(List<T> src)
        {
            var list = new List<T>(src);
            list.Reverse();
            return list;
        }

        /// <summary>照抄 getMaxVisiblePos（含 Float32/Uint32 位运算）。</summary>
        private static double GetMaxVisiblePos(double x)
        {
            double n = (float)x;
            if (double.IsNaN(n) || double.IsInfinity(n)) throw new ArgumentException("Argument must be a finite number");
            const double magic = 11718.75;
            double prime = n >= magic ? Math.Pow(2, Math.Floor(1 + Math.Log(n / magic, 2))) : 1;
            double a = n / prime + 0.001;
            float r = (float)a;
            if (r <= a) return r * prime;
            uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(r), 0);
            bits = unchecked(bits + (uint)((r <= 0) ? 1 : -1));
            float r2 = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
            return r2 * prime;
        }
    }

    /// <summary>音频缓冲抽象（让 Core 不直接依赖 XAudio2）。渲染/音频层实现它。</summary>
    public interface IAudioBuffer
    {
        double Duration { get; }
    }
}
