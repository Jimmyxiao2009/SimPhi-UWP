using System;
using SimPhi.Core;

namespace SimPhi.Render
{
    /// <summary>照抄 src/components/HitImage.ts。numOfParts 个方块的随机方向。</summary>
    public sealed class HitImage
    {
        public double offsetX;
        public double offsetY;
        public double time;
        public double duration;
        public ScaledNote[] effects;
        public double[][] direction;
        public string color;

        private static readonly Random _rng = new Random();

        /// <summary>对应原版的全局 hook.noteRender，由 GameRenderer 初始化时注入。</summary>
        public static NoteRender Provider;

        public HitImage(double offsetX, double offsetY, string n1, string n3)
        {
            ScaledHitFX packs = Provider.hitFX[n1];
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            time = Clock.Now();
            duration = packs.duration;
            effects = packs.effects;
            int parts = packs.numOfParts;
            direction = new double[parts][];
            for (int i = 0; i < parts; i++)
                direction[i] = new[] { _rng.NextDouble() * 80 + 185, _rng.NextDouble() * 2 * Math.PI };
            color = n3;
        }

        public static HitImage perfect(double offsetX, double offsetY, Note note)
        {
            return new HitImage(offsetX, offsetY, "Perfect", "#ffeca0");
        }
        public static HitImage good(double offsetX, double offsetY, Note note)
        {
            return new HitImage(offsetX, offsetY, "Good", "#b4e1ff");
        }
    }

    /// <summary>照抄 src/components/HitWord.ts。</summary>
    public sealed class HitWord
    {
        public double offsetX;
        public double offsetY;
        public double time;
        public double duration;
        public string color;
        public string text;

        public HitWord(double offsetX, double offsetY, string n1, string n2)
        {
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            time = Clock.Now();
            duration = 250;
            color = n1;
            text = n2;
        }

        public static HitWord early(double offsetX, double offsetY)
        {
            return new HitWord(offsetX, offsetY, "#03aaf9", "Early");
        }
        public static HitWord late(double offsetX, double offsetY)
        {
            return new HitWord(offsetX, offsetY, "#ff4612", "Late");
        }
    }

    /// <summary>照抄 src/components/HitFeedback.ts。time 是帧计数。</summary>
    public sealed class HitFeedback
    {
        public double offsetX;
        public double offsetY;
        public string color;
        public string text;
        public int time;

        public HitFeedback(double offsetX, double offsetY, string n1, string n2)
        {
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            color = n1;
            text = n2;
            time = 0;
        }

        public static HitFeedback tap(double offsetX, double offsetY) { return new HitFeedback(offsetX, offsetY, "cyan", ""); }
        public static HitFeedback hold(double offsetX, double offsetY) { return new HitFeedback(offsetX, offsetY, "lime", ""); }
        public static HitFeedback move(double offsetX, double offsetY) { return new HitFeedback(offsetX, offsetY, "violet", ""); }
    }
}
