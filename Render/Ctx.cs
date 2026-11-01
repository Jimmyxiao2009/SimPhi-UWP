using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;

namespace SimPhi.Render
{
    public enum CompositeOp { SourceOver, Difference, DestinationOut, DestinationOver }
    public enum TextAlign { Left, Center, Right }

    /// <summary>
    /// 把 Win2D 的 CanvasDrawingSession 包装成 CanvasRenderingContext2D 的样子，
    /// 让 index.ts 里的 loopCanvas / atDraw3 / drawNotes 能近乎逐行照抄。
    ///
    /// 三个 Canvas2D 与 Win2D 的语义差异都收在这里：
    ///   1) GlobalAlpha 是全局状态 —— 画图时塞进 DrawImage 的 opacity，画形状/文字时烘进颜色 alpha。
    ///   2) GlobalCompositeOperation —— DrawImage 用 CanvasComposite 重载；
    ///      非 source-over 的填充/文字走 CanvasCommandList 再以对应 composite 贴回（结算页用到）。
    ///   3) fillText 的基线 + textAlign —— 用 CanvasTextLayout 求 baseline，并按对齐换算锚点 x。
    ///
    /// 一帧的用法：new Ctx(ds) 包住 CanvasDrawingSession，用完即弃（状态不跨帧）。
    /// </summary>
    public sealed class Ctx
    {
        private CanvasDrawingSession _ds;       // 当前直接目标
        private readonly ICanvasResourceCreator _device;

        // ---- 对应 ctx 的可变状态 ----
        public double GlobalAlpha = 1.0;
        public Color FillColor = Colors.Black;          // fillStyle
        public TextAlign TextAlign = TextAlign.Left;    // textAlign
        public CompositeOp GlobalCompositeOperation = CompositeOp.SourceOver;

        private float _fontSize = 10f;
        private string _fontFamily = "Custom,Noto Sans SC";

        // 文本格式缓存：必须 static 跨帧复用。Ctx 每帧 new 一个，若用实例字段则缓存每帧失效，
        // 每帧重建一批 CanvasTextFormat 丢给 GC 终结器回收，密集动画文字下终结器跟不上 → 原生资源堆积崩溃。
        // CanvasTextFormat 与设备无关（纯 DWrite 描述），跨帧/跨 session 共享安全。
        private static readonly Dictionary<string, CanvasTextFormat> _fmtCache = new Dictionary<string, CanvasTextFormat>();
        private static readonly object _fmtLock = new object();

        // 文本布局缓存（关键修复）：实测 W10M/.NET Native ARM 上 CanvasTextLayout 的 Dispose 不归还原生内存，
        // 每帧新建 ~35 个 → ~1.2MB/s 原生泄漏 → 撞单应用上限被系统杀。缓存按 (文本|字体key) 复用，
        // 命中即不再新建（同一份原生被复用、不泄漏），只在文本内容变化时才新建（~30/s），泄漏率降 ~70 倍。
        private static readonly Dictionary<string, CanvasTextLayout> _layoutCache = new Dictionary<string, CanvasTextLayout>();
        private static ICanvasResourceCreator _layoutDevice;
        // 字体基线偏移缓存（按字号/族/粗细），用一次性 layout 测得后复用，供 DrawText 定位用
        private static readonly Dictionary<string, float> _ascentCache = new Dictionary<string, float>();

        public Ctx(CanvasDrawingSession ds)
        {
            _ds = ds;
            _device = ds.Device;
        }

        /// <summary>底层 session（特效/几何需要时用）。</summary>
        public CanvasDrawingSession Session { get { return _ds; } }

        // ===== 变换：setTransform / resetTransform =====
        public void SetTransform(double a, double b, double c, double d, double e, double f)
        {
            _ds.Transform = new Matrix3x2((float)a, (float)b, (float)c, (float)d, (float)e, (float)f);
        }
        public void ResetTransform()
        {
            _ds.Transform = Matrix3x2.Identity;
        }

        // ===== 清屏 =====
        public void ClearRect(double x, double y, double w, double h)
        {
            // 原版只整屏 clearRect(0,0,W,H)。Win2D 没有"清矩形区"，用透明 Clear 等价。
            _ds.Clear(Colors.Transparent);
        }

        // ===== 颜色（fillStyle = '#fff' / 'rgba(...)' / 'violet' ...）=====
        public void SetFillStyle(string css) { FillColor = CssColor.Parse(css); }

        private Color WithAlpha(Color c)
        {
            double a = c.A / 255.0 * GlobalAlpha;
            if (a < 0) a = 0; else if (a > 1) a = 1;
            return Color.FromArgb((byte)(a * 255 + 0.5), c.R, c.G, c.B);
        }

        // ===== fillRect =====
        public void FillRect(double x, double y, double w, double h)
        {
            var color = WithAlpha(FillColor);
            if (GlobalCompositeOperation == CompositeOp.SourceOver)
            {
                _ds.FillRectangle((float)x, (float)y, (float)w, (float)h, color);
            }
            else
            {
                // 非 source-over：把矩形画进 command list，再按 composite 贴回（结算页的 destination-out/over）
                Matrix3x2 saved = _ds.Transform;
                DrawComposited(cds => cds.FillRectangle((float)x, (float)y, (float)w, (float)h, color), MapComposite(GlobalCompositeOperation));
                _ds.Transform = saved;
            }
        }

        // command list 版本：把图像以指定 composite 贴回，变换在内部已生效，外部需先 ResetTransform。
        private void DrawComposited(Action<CanvasDrawingSession> draw, CanvasComposite composite)
        {
            Matrix3x2 tf = _ds.Transform;
            using (var cl = new CanvasCommandList(_device))
            {
                using (var cds = cl.CreateDrawingSession())
                {
                    cds.Transform = tf;
                    draw(cds);
                }
                var saved = _ds.Transform;
                _ds.Transform = Matrix3x2.Identity;
                _ds.DrawImage(cl, 0f, 0f, cl.GetBounds(_ds), 1f, CanvasImageInterpolation.Linear, composite);
                _ds.Transform = saved;
            }
        }

        // ===== drawImage 各重载 =====
        public void DrawImage(ICanvasImage img, double dx, double dy)
        {
            DrawImageCore(img, new Rect(dx, dy, NaturalWidth(img), NaturalHeight(img)), null);
        }
        public void DrawImage(ICanvasImage img, double dx, double dy, double dw, double dh)
        {
            DrawImageCore(img, new Rect(dx, dy, dw, dh), null);
        }
        public void DrawImage(ICanvasImage img, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh)
        {
            DrawImageCore(img, new Rect(dx, dy, dw, dh), new Rect(sx, sy, sw, sh));
        }

        private void DrawImageCore(ICanvasImage img, Rect dest, Rect? src)
        {
            float opacity = (float)GlobalAlpha;
            var composite = MapComposite(GlobalCompositeOperation);
            var bmp = img as CanvasBitmap;
            if (bmp != null)
            {
                Rect sourceRect = src ?? new Rect(0, 0, bmp.SizeInPixels.Width, bmp.SizeInPixels.Height);
                if (composite == CanvasComposite.SourceOver)
                    _ds.DrawImage(bmp, dest, sourceRect, opacity, CanvasImageInterpolation.Linear);
                else
                    _ds.DrawImage(bmp, dest, sourceRect, opacity, CanvasImageInterpolation.Linear, composite);
            }
            else
            {
                // 非位图（特效/视频帧）：Win2D 1.21 无 (ICanvasImage,Rect) 重载，按目标左上角偏移绘制
                if (composite == CanvasComposite.SourceOver)
                    _ds.DrawImage(img, (float)dest.X, (float)dest.Y);
                else
                    _ds.DrawImage(img, (float)dest.X, (float)dest.Y, src ?? GetBounds(img), opacity, CanvasImageInterpolation.Linear, composite);
            }
        }

        // ===== 几何填充：fill(new Path2D(...)) / arc =====
        public void Fill(CanvasGeometry geometry)
        {
            _ds.FillGeometry(geometry, WithAlpha(FillColor));
        }
        public void FillCircle(double cx, double cy, double r)
        {
            _ds.FillCircle((float)cx, (float)cy, (float)r, WithAlpha(FillColor));
        }

        // ===== 文本：font / textAlign / fillText / measureText =====
        public void SetFont(double sizePx, string family)
        {
            _fontSize = (float)sizePx;
            _fontFamily = family;
        }

        /// <summary>解析 "bold 64px Custom,Noto Sans SC" 这类字符串（与 ctx.font 同形）。</summary>
        public void SetFont(string css)
        {
            bool bold = css.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0;
            int px = css.IndexOf("px", StringComparison.Ordinal);
            if (px > 0)
            {
                int start = px;
                while (start > 0 && (char.IsDigit(css[start - 1]) || css[start - 1] == '.')) start--;
                double size;
                if (double.TryParse(css.Substring(start, px - start), NumberStyles.Float, CultureInfo.InvariantCulture, out size))
                    _fontSize = (float)size;
                string fam = css.Substring(px + 2).Trim();
                if (fam.Length > 0) _fontFamily = fam;
            }
            _pendingBold = bold;
        }
        private bool _pendingBold;

        // 字号量化到整数桶：原版每帧把 ctx.font 设成动画字号（连续浮点值若直接做 key 会让缓存无界增长）。
        private float QuantSize()
        {
            float sz = (float)Math.Round(_fontSize);
            return sz < 1f ? 1f : sz;
        }
        private string FontKey(float sz) { return sz.ToString(CultureInfo.InvariantCulture) + "|" + _fontFamily + "|" + (_pendingBold ? "b" : "n"); }

        private static CanvasHorizontalAlignment MapAlign(TextAlign a)
        {
            return a == TextAlign.Center ? CanvasHorizontalAlignment.Center
                 : a == TextAlign.Right ? CanvasHorizontalAlignment.Right
                 : CanvasHorizontalAlignment.Left;
        }

        // 按 (字号|族|粗细|对齐) 取缓存 CanvasTextFormat。对齐塞进 format，配合 DrawText(rect) 定位，
        // 这样高基数文字（分数/combo）无需建 CanvasTextLayout —— 既不泄漏、也不产生 GPU 资源 churn（实测会触发 Adreno 驱动崩）。
        private CanvasTextFormat GetFormat(CanvasHorizontalAlignment halign)
        {
            float sz = QuantSize();
            string key = FontKey(sz) + "|" + (int)halign;
            lock (_fmtLock)
            {
                CanvasTextFormat fmt;
                if (!_fmtCache.TryGetValue(key, out fmt))
                {
                    if (_fmtCache.Count >= 256)
                    {
                        foreach (var f in _fmtCache.Values) f.Dispose();
                        _fmtCache.Clear();
                    }
                    fmt = new CanvasTextFormat
                    {
                        FontFamily = PrimaryFamily(_fontFamily),
                        FontSize = sz,
                        FontWeight = _pendingBold ? Windows.UI.Text.FontWeights.Bold : Windows.UI.Text.FontWeights.Normal,
                        HorizontalAlignment = halign,
                        VerticalAlignment = CanvasVerticalAlignment.Top,
                        WordWrapping = CanvasWordWrapping.NoWrap,
                        Options = CanvasDrawTextOptions.Default
                    };
                    _fmtCache[key] = fmt;
                }
                return fmt;
            }
        }

        // 基线偏移（布局框顶→字母基线），每字号测一次缓存。Canvas 的 y 是基线，DrawText 以 rect 顶定位，需 top=y-ascent。
        private float GetAscent(float sz)
        {
            string key = FontKey(sz);
            lock (_fmtLock)
            {
                float a;
                if (_ascentCache.TryGetValue(key, out a)) return a;
                a = sz;
                try
                {
                    using (var layout = new CanvasTextLayout(_device, "Ag", GetFormat(CanvasHorizontalAlignment.Left), 0f, 0f))
                        if (layout.LineMetrics != null && layout.LineMetrics.Length > 0) a = layout.LineMetrics[0].Baseline;
                }
                catch { }
                _ascentCache[key] = a;
                return a;
            }
        }

        /// <summary>取（缓存的）文本布局。命中即复用，避免每帧新建 CanvasTextLayout 造成原生泄漏。</summary>
        private CanvasTextLayout GetLayout(string text)
        {
            float sz;
            string fkey = FontKey(QuantSize());
            string key = text + "" + fkey;
            lock (_fmtLock)
            {
                // 设备变了（device-lost 重建）→ 旧布局作废，整体清掉
                if (!ReferenceEquals(_layoutDevice, _device))
                {
                    foreach (var l in _layoutCache.Values) { try { l.Dispose(); } catch { } }
                    _layoutCache.Clear();
                    _layoutDevice = _device;
                }
                CanvasTextLayout layout;
                if (!_layoutCache.TryGetValue(key, out layout))
                {
                    // 上限兜底：变化的数字会不断产生新串，超阈值整体清空（已缓存的常驻串下帧会重建）
                    if (_layoutCache.Count >= 600)
                    {
                        foreach (var l in _layoutCache.Values) { try { l.Dispose(); } catch { } }
                        _layoutCache.Clear();
                    }
                    layout = new CanvasTextLayout(_device, text, GetFormat(CanvasHorizontalAlignment.Left), 0f, 0f);
                    _layoutCache[key] = layout;
                }
                return layout;
            }
        }

        // CanvasTextFormat.FontFamily 只认单个族名，取 "Custom,Noto Sans SC" 的第一个
        private static string PrimaryFamily(string css)
        {
            int comma = css.IndexOf(',');
            return comma < 0 ? css.Trim() : css.Substring(0, comma).Trim();
        }

        public double MeasureText(string text)
        {
            return GetLayout(text).LayoutBounds.Width;   // 缓存复用，不再每次新建+dispose
        }

        public void FillText(string text, double x, double y)
        {
            if (string.IsNullOrEmpty(text)) return;
            // 普通绘制（含版权字的 difference 退化）：用 DrawText 直绘，内部复用字形图集，
            // 不建 CanvasTextLayout 对象 —— 既不泄漏、也不产生 GPU 资源 churn（实测会触发 Adreno 驱动 AV）。
            if (GlobalCompositeOperation == CompositeOp.SourceOver || GlobalCompositeOperation == CompositeOp.Difference)
            {
                float sz0 = QuantSize();
                float ascent0 = GetAscent(sz0);
                const float BIG = 8192f;
                double rx = TextAlign == TextAlign.Center ? x - BIG / 2 : (TextAlign == TextAlign.Right ? x - BIG : x);
                _ds.DrawText(text, new Rect(rx, (float)(y - ascent0), BIG, ascent0 * 3 + sz0), WithAlpha(FillColor), GetFormat(MapAlign(TextAlign)));
                return;
            }
            var layout = GetLayout(text);                // 缓存复用（不在此 dispose，归缓存所有）
            double w = layout.LayoutBounds.Width;
            double drawX = x;
            if (TextAlign == TextAlign.Center) drawX = x - w / 2;
            else if (TextAlign == TextAlign.Right) drawX = x - w;
            // Canvas 的 y 是字母基线；Win2D DrawTextLayout 以布局框左上角定位，需减去基线
            float baseline = (layout.LineMetrics != null && layout.LineMetrics.Length > 0)
                ? layout.LineMetrics[0].Baseline
                : _fontSize;
            var color = WithAlpha(FillColor);
            if (GlobalCompositeOperation == CompositeOp.SourceOver)
            {
                _ds.DrawTextLayout(layout, (float)drawX, (float)(y - baseline), color);
            }
            else if (GlobalCompositeOperation == CompositeOp.Difference)
            {
                // 主画布右下角版权字用 difference：留待渲染层用 BlendEffect 处理，
                // 这里先以 source-over 退化绘制，保证位置正确（phase 2 接 BlendEffect 时替换）。
                _ds.DrawTextLayout(layout, (float)drawX, (float)(y - baseline), color);
            }
            else
            {
                Matrix3x2 saved = _ds.Transform;
                DrawComposited(cds => cds.DrawTextLayout(layout, (float)drawX, (float)(y - baseline), color), MapComposite(GlobalCompositeOperation));
                _ds.Transform = saved;
            }
        }

        // ===== 杂项 =====
        private static CanvasComposite MapComposite(CompositeOp op)
        {
            switch (op)
            {
                case CompositeOp.DestinationOut: return CanvasComposite.DestinationOut;
                case CompositeOp.DestinationOver: return CanvasComposite.DestinationOver;
                // Difference 不是 Porter-Duff composite，而是 blend，DrawImage 的 composite 不支持；
                // 调用点（仅版权字）会单独处理，这里回退 SourceOver。
                case CompositeOp.Difference: return CanvasComposite.SourceOver;
                default: return CanvasComposite.SourceOver;
            }
        }

        private static double NaturalWidth(ICanvasImage img)
        {
            var b = img as CanvasBitmap;
            return b != null ? b.SizeInPixels.Width : 0;
        }
        private static double NaturalHeight(ICanvasImage img)
        {
            var b = img as CanvasBitmap;
            return b != null ? b.SizeInPixels.Height : 0;
        }
        private Rect GetBounds(ICanvasImage img) { return img.GetBounds(_ds); }
    }

    /// <summary>CSS 颜色字符串解析：支持 #rgb / #rrggbb / #rrggbbaa / rgb()/rgba() / 项目里用到的具名色。</summary>
    public static class CssColor
    {
        public static Color Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return Colors.Black;
            s = s.Trim();
            if (s[0] == '#') return ParseHex(s.Substring(1));
            if (s.StartsWith("rgba", StringComparison.OrdinalIgnoreCase) || s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                return ParseRgb(s);
            switch (s.ToLowerInvariant())
            {
                case "white": return Colors.White;
                case "black": return Colors.Black;
                case "lime": return Colors.Lime;
                case "cyan": return Colors.Cyan;
                case "yellow": return Colors.Yellow;
                case "violet": return Colors.Violet;
                case "red": return Colors.Red;
                default: return Colors.Black;
            }
        }

        private static Color ParseHex(string h)
        {
            byte r, g, b, a = 255;
            if (h.Length == 3)
            {
                r = (byte)(Hex(h[0]) * 17); g = (byte)(Hex(h[1]) * 17); b = (byte)(Hex(h[2]) * 17);
            }
            else if (h.Length == 6 || h.Length == 8)
            {
                r = (byte)(Hex(h[0]) * 16 + Hex(h[1]));
                g = (byte)(Hex(h[2]) * 16 + Hex(h[3]));
                b = (byte)(Hex(h[4]) * 16 + Hex(h[5]));
                if (h.Length == 8) a = (byte)(Hex(h[6]) * 16 + Hex(h[7]));
            }
            else return Colors.Black;
            return Color.FromArgb(a, r, g, b);
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }

        private static Color ParseRgb(string s)
        {
            int lp = s.IndexOf('('), rp = s.IndexOf(')');
            if (lp < 0 || rp < 0) return Colors.Black;
            var parts = s.Substring(lp + 1, rp - lp - 1).Split(',');
            byte r = ToByte(parts, 0), g = ToByte(parts, 1), b = ToByte(parts, 2);
            byte a = 255;
            if (parts.Length > 3)
            {
                double af;
                if (double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out af))
                    a = (byte)(Math.Max(0, Math.Min(1, af)) * 255 + 0.5);
            }
            return Color.FromArgb(a, r, g, b);
        }

        private static byte ToByte(string[] parts, int i)
        {
            if (i >= parts.Length) return 0;
            double v;
            if (double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return (byte)Math.Max(0, Math.Min(255, v));
            return 0;
        }
    }
}
