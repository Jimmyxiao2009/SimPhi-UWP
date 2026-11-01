using System;
using System.Collections.Generic;
using Windows.UI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;

namespace SimPhi.Render
{
    /// <summary>
    /// 照抄 src/utils/ImageTools.ts。
    /// 原版基于 Canvas 的 getImageData/putImageData（straight/非预乘 alpha）做逐像素运算；
    /// Win2D 用 GetPixelColors/SetPixelColors（同为 straight RGBA）一一对应，全部为加载期一次性预处理。
    /// imgBlur 用 GaussianBlurEffect 近似 stackblur（背景虚化，非像素级敏感）。
    /// </summary>
    public static class ImageTools
    {
        /// <summary>hex2rgba：取 CSS 颜色的 RGBA 字节。</summary>
        public static Color Hex2Rgba(string color)
        {
            return CssColor.Parse(color);
        }

        private static byte Mul(byte channel, byte factor)
        {
            // data[i] *= factor/255，再按 Uint8ClampedArray 取整夹紧
            double v = channel * (factor / 255.0);
            if (v < 0) v = 0; else if (v > 255) v = 255;
            return (byte)(v + 0.5);
        }

        /// <summary>imgPainter：RGB 替换为 color，alpha 乘 color.a/255。</summary>
        public static CanvasBitmap ImgPainter(CanvasDevice device, CanvasBitmap img, string color)
        {
            Color c = Hex2Rgba(color);
            Color[] px = img.GetPixelColors();
            for (int i = 0; i < px.Length; i++)
            {
                Color o = px[i];
                px[i] = Color.FromArgb(Mul(o.A, c.A), c.R, c.G, c.B);
            }
            return CanvasBitmap.CreateFromColors(device, px, (int)img.SizeInPixels.Width, (int)img.SizeInPixels.Height);
        }

        /// <summary>imgShader：各通道乘 color 对应通道/255。</summary>
        public static CanvasBitmap ImgShader(CanvasDevice device, CanvasBitmap img, string color)
        {
            Color c = Hex2Rgba(color);
            Color[] px = img.GetPixelColors();
            for (int i = 0; i < px.Length; i++)
            {
                Color o = px[i];
                px[i] = Color.FromArgb(Mul(o.A, c.A), Mul(o.R, c.R), Mul(o.G, c.G), Mul(o.B, c.B));
            }
            return CanvasBitmap.CreateFromColors(device, px, (int)img.SizeInPixels.Width, (int)img.SizeInPixels.Height);
        }

        /// <summary>imgBlur：半径 ceil(min(w,h)*0.0125) 的虚化。</summary>
        public static CanvasBitmap ImgBlur(CanvasDevice device, CanvasBitmap img)
        {
            int w = (int)img.SizeInPixels.Width;
            int h = (int)img.SizeInPixels.Height;
            float radius = (float)Math.Ceiling(Math.Min(w, h) * 0.0125);
            // 模糊是低频信息：以半分辨率渲染目标，省 3/4 内存与带宽，背景虚化肉眼不可辨
            int bw = Math.Max(1, w / 2), bh = Math.Max(1, h / 2);
            var rt = new CanvasRenderTarget(device, bw, bh, img.Dpi);
            using (var ds = rt.CreateDrawingSession())
            {
                ds.Transform = System.Numerics.Matrix3x2.CreateScale(0.5f);
                var blur = new GaussianBlurEffect
                {
                    Source = img,
                    BlurAmount = radius,
                    BorderMode = EffectBorderMode.Hard
                };
                ds.DrawImage(blur);
            }
            return rt;
        }

        /// <summary>imgSplit：按 dx×dy 切片（行优先），返回各帧位图。</summary>
        public static List<CanvasBitmap> ImgSplit(CanvasDevice device, CanvasBitmap img, int? limitX = null, int? limitY = null)
        {
            int iw = (int)img.SizeInPixels.Width;
            int ih = (int)img.SizeInPixels.Height;
            int dx = (int)Math.Floor((double)(limitX ?? Math.Min(iw, ih)));
            int dy = (int)Math.Floor((double)(limitY ?? dx));
            var arr = new List<CanvasBitmap>();
            for (int sy = 0; sy < ih; sy += dy)
            {
                for (int sx = 0; sx < iw; sx += dx)
                {
                    var rt = new CanvasRenderTarget(device, dx, dy, img.Dpi);
                    using (var ds = rt.CreateDrawingSession())
                    {
                        ds.DrawImage(img,
                            new Windows.Foundation.Rect(0, 0, dx, dy),
                            new Windows.Foundation.Rect(sx, sy, dx, dy));
                    }
                    arr.Add(rt);
                }
            }
            return arr;
        }

        // ===== ImgAny：图片隐写音频解码（pack 资源用）=====

        /// <summary>取一张（可裁 border）图的 RGB 直通道字节序列（对应 getImageData 后挑 R,G,B 三通道）。</summary>
        private static byte[] RgbBytes(CanvasDevice device, CanvasBitmap img, int border)
        {
            int w = (int)img.SizeInPixels.Width - border * 2;
            int h = (int)img.SizeInPixels.Height - border * 2;
            Color[] px;
            if (border == 0)
            {
                px = img.GetPixelColors();
            }
            else
            {
                var rt = new CanvasRenderTarget(device, w, h, img.Dpi);
                using (var ds = rt.CreateDrawingSession())
                    ds.DrawImage(img, -border, -border);
                px = rt.GetPixelColors();
            }
            var ab = new byte[px.Length * 3];
            for (int i = 0; i < ab.Length; i++)
            {
                Color c = px[i / 3];
                int ch = i % 3;
                ab[i] = ch == 0 ? c.R : (ch == 1 ? c.G : c.B);
            }
            return ab;
        }

        /// <summary>ImgAny.decode。</summary>
        public static byte[] Decode(CanvasDevice device, CanvasBitmap img, int border = 0)
        {
            byte[] rgb = RgbBytes(device, img, border);
            var ab = new byte[rgb.Length];
            for (int i = 0; i < ab.Length; i++)
                ab[i] = (byte)(rgb[i] ^ (unchecked(i * 3473) & 0xFF));
            uint size = ReadUint32BE(ab, 0);
            return Slice(ab, 4, (int)size + 4);
        }

        /// <summary>ImgAny.decodeAlt。</summary>
        public static byte[] DecodeAlt(CanvasDevice device, CanvasBitmap img)
        {
            byte[] ab = RgbBytes(device, img, 0);
            int half = ab.Length / 2;
            var combined = new byte[half];
            for (int i = 0; i < half; i++)
            {
                int v = (((ab[i * 2] + 8) / 17) << 4) | ((ab[i * 2 + 1] + 8) / 17);
                combined[i] = (byte)(v ^ (unchecked(i * i * 3473) & 0xFF)); // mask: v ^ (i**2*3473 & 255)
            }
            uint size = ReadUint32BE(combined, 0);
            return Slice(combined, 4, (int)size + 4);
        }

        private static uint ReadUint32BE(byte[] b, int o)
        {
            return ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];
        }

        private static byte[] Slice(byte[] src, int start, int end)
        {
            if (end > src.Length) end = src.Length;
            if (start > end) start = end;
            var r = new byte[end - start];
            Array.Copy(src, start, r, 0, r.Length);
            return r;
        }
    }
}
