using System;
using Microsoft.Graphics.Canvas;

namespace SimPhi.Render
{
    /// <summary>
    /// 照抄 src/components/ScaledNote.ts。
    /// 把一张 note 贴图按 scale 预算好 dx/dy/dw/dh，绘制时配合 ctx 当前的 setTransform 一次 DrawImage。
    /// 注意：原版用 ImageBitmap 的像素宽高（设备像素空间），对应 Win2D 的 SizeInPixels。
    /// </summary>
    public sealed class ScaledNote
    {
        public Action<Ctx> Full;
        public Action<Ctx> Head;
        public Action<Ctx, double, double> Body;
        public Action<Ctx, double> Tail;

        public ScaledNote(CanvasBitmap img, double scale, bool compacted = false)
        {
            double iw = img.SizeInPixels.Width;
            double ih = img.SizeInPixels.Height;
            double dx = -iw / 2 * scale;
            double dy = -ih / 2 * scale;
            double dw = iw * scale;
            double dh = ih * scale;
            Full = ctx => ctx.DrawImage(img, dx, dy, dw, dh);
            Head = ctx => ctx.DrawImage(img, dx, 0, dw, dh);
            Body = (ctx, offset, length) => ctx.DrawImage(img, dx, offset, dw, length);
            Tail = (ctx, offset) => ctx.DrawImage(img, dx, offset - dh, dw, dh);
            if (compacted)
            {
                Head = ctx => ctx.DrawImage(img, dx, dy, dw, dh);
                Tail = (ctx, offset) => ctx.DrawImage(img, dx, offset - dh - dy, dw, dh);
            }
        }
    }
}
