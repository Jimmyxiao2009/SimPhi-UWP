using System.Collections.Generic;
using System.Linq;
using Microsoft.Graphics.Canvas;

namespace SimPhi.Render
{
    /// <summary>对应 index.ts 里的 ScaledHitFX 接口。</summary>
    public sealed class ScaledHitFX
    {
        public ScaledNote[] effects;
        public int numOfParts;
        public int duration;
    }

    /// <summary>
    /// 照抄 index.ts 的 noteRender 对象：持有各类 note 的 ScaledNote 与打击特效帧。
    /// update/updateFX 用 ImageTools 着色，对应 imgPainter/imgSplit/imgShader。
    /// </summary>
    public sealed class NoteRender
    {
        public readonly Dictionary<string, ScaledNote> note = new Dictionary<string, ScaledNote>();
        public readonly Dictionary<string, ScaledHitFX> hitFX = new Dictionary<string, ScaledHitFX>();

        public void Update(CanvasDevice device, string name, CanvasBitmap img, double scale, bool compacted = false)
        {
            note[name] = new ScaledNote(img, scale, compacted);
            if (name == "Tap")
                note["TapBad"] = new ScaledNote(ImageTools.ImgPainter(device, img, "#6c4343"), scale);
        }

        public void UpdateFX(CanvasDevice device, CanvasBitmap img0, double scale,
            int? limitX = null, int? limitY = null, bool hideParts = false, int? duration = null)
        {
            var hitRaw = ImageTools.ImgSplit(device, img0, limitX, limitY);
            var hitPerfect = hitRaw
                .Select(img => new ScaledNote(ImageTools.ImgShader(device, img, "rgba(255,236,160,0.8823529)"), scale))
                .ToArray();
            var hitGood = hitRaw
                .Select(img => new ScaledNote(ImageTools.ImgShader(device, img, "rgba(180,225,255,0.9215686)"), scale))
                .ToArray();
            int dur = duration ?? 0;            // Number(duration)|0||500
            if (dur == 0) dur = 500;
            hitFX["Perfect"] = new ScaledHitFX { effects = hitPerfect, numOfParts = hideParts ? 0 : 4, duration = dur };
            hitFX["Good"] = new ScaledHitFX { effects = hitGood, numOfParts = hideParts ? 0 : 3, duration = dur };
            foreach (var img in hitRaw) img.Dispose(); // 对应 hitRaw.forEach(img => img.close())
        }
    }
}
