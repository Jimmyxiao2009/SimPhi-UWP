using System;
using System.Globalization;
using SimPhi.Core;

namespace SimPhi.Game
{
    /// <summary>照抄 src/components/FrameTimer.ts。</summary>
    public sealed class FrameTimer
    {
        private int tick;
        private double time;
        private double fps;

        public FrameTimer()
        {
            tick = 0;
            time = Clock.Now();
            fps = 0;
        }

        public string FpsStr
        {
            get
            {
                if (fps < 10) return ToPrecision(fps, 2);   // toPrecision(2)
                return Math.Round(fps).ToString("F0", CultureInfo.InvariantCulture); // toFixed(0)
            }
        }

        public double Disp
        {
            get
            {
                double d = 0.5 / fps;                       // fps=0 → +Inf（JS 同样，truthy 直接返回）
                return (double.IsNaN(d) || d == 0) ? 0 : d; // `|| 0` 只兜 NaN/0
            }
        }

        public double AddTick(int fr = 10)
        {
            if (++tick >= fr)
            {
                tick = 0;
                double prev = time;
                time = Clock.Now();
                fps = 1e3 * fr / (time - prev);
            }
            return fps;
        }

        // 复刻 Number.prototype.toPrecision(2)（仅用于 fps<10 的展示）
        private static string ToPrecision(double v, int p)
        {
            if (v == 0) return "0." + new string('0', p - 1);
            int e = (int)Math.Floor(Math.Log10(Math.Abs(v)));
            int decimals = p - 1 - e;
            if (decimals < 0) decimals = 0;
            double r = Math.Round(v, Math.Min(15, decimals), MidpointRounding.AwayFromZero);
            return r.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }
}
