using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Windows.UI;
using Windows.Storage.Streams;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using SimPhi.Loader;
using SimPhi.Render;

namespace SimPhi.Resource
{
    /// <summary>
    /// 照抄 index.ts 的资源加载：先拉在线资源包(官方皮肤)，缺的用 fixme 程序化兜底，再装配 noteRender。
    /// pack.json 形如 { image:{name:"url|scale"}, audio:{...}, format }；图片直接 fetch 成位图，
    /// 隐写音频(ext 以 'm' 开头)跳过(保持静音)。离线/失败则全程序化兜底。
    /// </summary>
    public static class Resources
    {
        private const string PackUrl = "https://lchzh.net/data/pack.json";

        public static async Task GenerateAsync(CanvasDevice d, Res res, NoteRender noteRender, IAudioSystem audio, HttpClient http)
        {
            IDictionary<string, object> rawImage = new Dictionary<string, object>();
            try
            {
                string json = await http.GetStringAsync(PackUrl);
                var root = JsonConvert.ParseObject(json);
                object imgObj;
                if (root != null && root.TryGetValue("image", out imgObj) && imgObj is IDictionary<string, object>)
                {
                    rawImage = (IDictionary<string, object>)imgObj;
                    await ReadImagesAsync(d, http, rawImage, res);
                }
            }
            catch { /* 离线/失败 → 全程序化兜底 */ }

            FillDefaults(d, res, audio);
            SetupNoteRender(d, rawImage, res, noteRender, audio);
        }

        private static async Task ReadImagesAsync(CanvasDevice d, HttpClient http, IDictionary<string, object> rawImage, Res res)
        {
            foreach (var kv in rawImage)
            {
                string src = kv.Value as string;
                if (string.IsNullOrEmpty(src)) continue;
                var parts = src.Split('|');
                string url = parts[0];
                string ext = parts.Length > 1 ? parts[1] : null;
                if (ext != null && ext.StartsWith("m")) continue; // 隐写音频，跳过(保持静音)
                try
                {
                    var bytes = await http.GetByteArrayAsync(url);
                    res[kv.Key] = await LoadBmpAsync(d, bytes);
                }
                catch { /* 单张失败 → 由 fixme 兜底 */ }
            }
        }

        private static async Task<CanvasBitmap> LoadBmpAsync(CanvasDevice d, byte[] bytes)
        {
            var ras = new InMemoryRandomAccessStream();
            var s = ras.AsStream();
            s.Write(bytes, 0, bytes.Length);
            s.Flush();
            ras.Seek(0);
            return await CanvasBitmap.LoadAsync(d, ras);
        }

        // 照抄 fixme：仅在 res 缺该项时程序化生成
        private static void FillDefaults(CanvasDevice d, Res res, IAudioSystem audio)
        {
            if (!res.Has("Tap")) res["Tap"] = Bar(d, 1089, 200, "#0ac3e1", false);
            if (!res.Has("TapHL")) res["TapHL"] = Bar(d, 1089, 200, "#0ac3e1", true);
            if (!res.Has("Drag")) res["Drag"] = Bar(d, 1089, 160, "#f0ed69", false);
            if (!res.Has("DragHL")) res["DragHL"] = Bar(d, 1089, 160, "#f0ed69", true);
            if (!res.Has("HoldHead")) res["HoldHead"] = Make(d, 1089, 100, ds => HoldBody(d, ds, 1089, 100, "#96ebfc", false));
            if (!res.Has("HoldHeadHL")) res["HoldHeadHL"] = Make(d, 1089, 100, ds => HoldBody(d, ds, 1089, 100, "#96ebfc", true));
            if (!res.Has("Hold")) res["Hold"] = Make(d, 1089, 1900, ds => HoldBody(d, ds, 1089, 1900, "#96ebfc", false));
            if (!res.Has("HoldHL")) res["HoldHL"] = Make(d, 1089, 1900, ds => HoldBody(d, ds, 1089, 1900, "#96ebfc", true));
            if (!res.Has("HoldEnd")) res["HoldEnd"] = Make(d, 1089, 100, ds => HoldBody(d, ds, 1089, 100, "#96ebfc", false));
            if (!res.Has("Flick")) res["Flick"] = Bar(d, 1089, 300, "#fe4365", false);
            if (!res.Has("FlickHL")) res["FlickHL"] = Bar(d, 1089, 300, "#fe4365", true);

            if (!res.Has("Rank")) res["Rank"] = Make(d, 256, 1792, ds =>
            {
                string[] colors = { "#fffb00", "#a937e7", "#ea61df", "#ff9e22", "#00a844", "#00e8de", "#e4e4e4" };
                int[] dots = { 5232301, 4532785, 16267326, 18415150, 16301615, 31491134, 1088575 };
                for (int i = 0; i < 7; i++)
                {
                    ds.FillRectangle(0, i * 256, 256, 256, Colors.White);
                    ds.FillRectangle(16, i * 256 + 16, 224, 224, CssColor.Parse("#303030"));
                    Color c = CssColor.Parse(colors[i]);
                    int dot = dots[i];
                    const int size = 32;
                    const float start = (256 - size * 5) / 2f;
                    for (int k = 0; k < 25; k++)
                    {
                        if (((dot >> k) & 1) != 0)
                        {
                            float x = start + (k % 5) * size;
                            float y = start + (k / 5) * size + i * 256;
                            ds.FillRectangle(x, y, size, size, c);
                        }
                    }
                }
            });

            if (!res.Has("HitFXRaw")) res["HitFXRaw"] = Make(d, 256, 256 * 30, ds =>
            {
                for (int i = 0; i < 30; i++)
                {
                    double tick = i / 30.0;
                    float lw = (float)(Math.Pow(1 - tick, 2) * 25);
                    byte a = (byte)((1 - tick) * 255 + 0.5);
                    float width = (float)(Math.Pow(tick, 0.2) * 224);
                    ds.DrawRectangle(128 - width / 2, i * 256 + 128 - width / 2, width, width, Color.FromArgb(a, 255, 255, 255), lw);
                }
            });

            if (!res.Has("LevelOver1")) res["LevelOver1"] = Make(d, 1716, 325, ds =>
            {
                var ctx = new Ctx(ds);
                ctx.SetFillStyle("#000"); ctx.GlobalAlpha = 0.8; ctx.FillRect(0, 0, 1716, 325);
                ctx.GlobalAlpha = 1; ctx.SetFillStyle("#989898"); ctx.FillRect(0, 94, 1716, 137);
                ctx.SetFillStyle("white");
                ctx.SetFont("bold 30px Custom"); ctx.TextAlign = TextAlign.Right;
                ctx.FillText("ACC:", 240, 172); ctx.FillText("Max combo:", 1420, 172);
                ctx.TextAlign = TextAlign.Center; ctx.SetFont("bold 22px Custom");
                ctx.FillText("Perfect", 788, 304); ctx.FillText("Good", 942, 304);
                ctx.FillText("Bad", 1096, 304); ctx.FillText("Miss", 1250, 304);
            });
            if (!res.Has("LevelOver4")) res["LevelOver4"] = Make(d, 633, 122, ds =>
            {
                var stops = new[]
                {
                    new CanvasGradientStop { Position = 0, Color = Color.FromArgb(200, 0, 0, 0) },
                    new CanvasGradientStop { Position = 1, Color = Color.FromArgb(0, 0, 0, 0) }
                };
                using (var brush = new CanvasLinearGradientBrush(d, stops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied))
                {
                    brush.StartPoint = new Vector2(24, 0);
                    brush.EndPoint = new Vector2(633, 0);
                    ds.FillRectangle(24, 24, 609, 72, brush);
                }
            });
            if (!res.Has("LevelOver5")) res["LevelOver5"] = Make(d, 11, 43, ds => ds.FillRectangle(3, 1, 7, 40, Colors.White));

            foreach (var entry in new[] { "HitFXRaw", "Rank", "LevelOver1", "LevelOver3", "LevelOver4", "LevelOver5" })
                if (!res.Has(entry)) res[entry] = Make(d, 1, 1, ds => { });

            if (!res.Has("JudgeLine")) res["JudgeLine"] = Make(d, 1920, 3, ds => ds.FillRectangle(0, 0, 1920, 3, Colors.White));
            if (!res.Has("ProgressBar")) res["ProgressBar"] = Make(d, 1919, 11, ds =>
            {
                ds.FillRectangle(0, 0, 1916, 11, CssColor.Parse("#919191"));
                ds.FillRectangle(1916, 0, 3, 11, Colors.White);
            });

            // 音频占位（静音；正式波形随 XAudio2 补）
            if (!res.Has("HitSong0")) res["HitSong0"] = audio.Mute(0.075);
            if (!res.Has("HitSong1")) res["HitSong1"] = audio.Mute(0.05);
            if (!res.Has("HitSong2")) res["HitSong2"] = audio.Mute(0.075);
            if (!res.Has("LevelOver0_v1")) res["LevelOver0_v1"] = audio.Mute(27.83);
            if (!res.Has("LevelOver1_v1")) res["LevelOver1_v1"] = audio.Mute(27.83);
            if (!res.Has("LevelOver2_v1")) res["LevelOver2_v1"] = audio.Mute(27.83);
            if (!res.Has("LevelOver3_v1")) res["LevelOver3_v1"] = audio.Mute(27.83);
        }

        private static void SetupNoteRender(CanvasDevice d, IDictionary<string, object> rawImage, Res res, NoteRender noteRender, IAudioSystem audio)
        {
            string[] skins = { "Tap", "TapHL", "Drag", "DragHL", "HoldHead", "HoldHeadHL", "Hold", "HoldHL", "HoldEnd", "Flick", "FlickHL" };
            foreach (var name in skins)
                noteRender.Update(d, name, (CanvasBitmap)res[name], 8080.0 / PackScale(rawImage, name));
            noteRender.UpdateFX(d, (CanvasBitmap)res["HitFXRaw"], 8080.0 / PackScale(rawImage, "HitFXRaw"));
            res["NoImageBlack"] = CanvasBitmap.CreateFromColors(d, new[] { Colors.Black }, 1, 1);
            res["NoImageWhite"] = CanvasBitmap.CreateFromColors(d, new[] { Colors.White }, 1, 1);
            res["JudgeLineMP"] = ImageTools.ImgShader(d, res.JudgeLine, "#feffa9");
            res["JudgeLineFC"] = ImageTools.ImgShader(d, res.JudgeLine, "#a2eeff");
            res.Ranks = ImageTools.ImgSplit(d, res.Rank).ToArray();
            res["mute"] = audio.Mute(1);
        }

        // pack 里 "url|scale" 的 scale；缺省 8080（→ scale 1，与 fixme 一致）
        private static double PackScale(IDictionary<string, object> rawImage, string name)
        {
            object v;
            if (rawImage.TryGetValue(name, out v) && v is string)
            {
                var parts = ((string)v).Split('|');
                double n;
                if (parts.Length > 1 && double.TryParse(parts[1], out n) && n != 0) return n;
            }
            return 8080;
        }

        private static CanvasBitmap Make(CanvasDevice d, double w, double h, Action<CanvasDrawingSession> draw)
        {
            var rt = new CanvasRenderTarget(d, (float)w, (float)h, 96);
            using (var ds = rt.CreateDrawingSession())
            {
                ds.Clear(Colors.Transparent);
                draw(ds);
            }
            return rt;
        }

        private static Color Lighten(Color c, double f)
        {
            return Color.FromArgb(c.A,
                (byte)(c.R + (255 - c.R) * f),
                (byte)(c.G + (255 - c.G) * f),
                (byte)(c.B + (255 - c.B) * f));
        }

        // 水平音符（Tap/Drag/Flick）：竖向渐变填充圆角药丸条 + 顶部高光，HL 加金边
        private static CanvasBitmap Bar(CanvasDevice d, double w, double h, string colorHex, bool hl)
        {
            return Make(d, w, h, ds =>
            {
                Color c = CssColor.Parse(colorHex);
                const float inset = 16f;
                float bw = (float)w - 2 * inset, bh = (float)h - 2 * inset, r = bh / 2;
                var stops = new[]
                {
                    new CanvasGradientStop { Position = 0f, Color = Lighten(c, 0.55) },
                    new CanvasGradientStop { Position = 1f, Color = c }
                };
                using (var brush = new CanvasLinearGradientBrush(d, stops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied))
                {
                    brush.StartPoint = new Vector2(0, inset);
                    brush.EndPoint = new Vector2(0, inset + bh);
                    ds.FillRoundedRectangle(inset, inset, bw, bh, r, r, brush);
                }
                ds.FillRoundedRectangle(inset + 40, (float)(h * 0.2), bw - 80, (float)(h * 0.12), 10, 10, Color.FromArgb(170, 255, 255, 255));
                if (hl)
                {
                    Color g = CssColor.Parse("#fdfd66");
                    ds.DrawRoundedRectangle(inset, inset, bw, bh, r, r, g, 14);
                }
            });
        }

        // Hold 体/头/尾：纵向可平铺，实色填充 + 两侧亮边，HL 加金边
        private static void HoldBody(CanvasDevice d, CanvasDrawingSession ds, double w, double h, string colorHex, bool hl)
        {
            Color c = CssColor.Parse(colorHex);
            float x = 40f, bw = (float)w - 80f, fh = (float)h;
            ds.FillRectangle(x, 0, bw, fh, c);
            ds.FillRectangle(x, 0, 26, fh, Lighten(c, 0.6));
            ds.FillRectangle(x + bw - 26, 0, 26, fh, Lighten(c, 0.6));
            if (hl)
            {
                Color g = CssColor.Parse("#fdfd66");
                ds.FillRectangle(x, 0, 12, fh, g);
                ds.FillRectangle(x + bw - 12, 0, 12, fh, g);
            }
        }
    }
}
