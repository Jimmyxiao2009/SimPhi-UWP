using System;

namespace SimPhi.Pec
{
    /// <summary>照抄 @sim-phi/extends src/pec/easing.js：30 个缓动函数（index 0/1 与 &gt;29 为线性=null）。</summary>
    public static class Easing
    {
        private static readonly Func<double, double>[] Tl = new Func<double, double>[30];

        private static double Pow(double x, double n) { return Math.Pow(x, n); }

        static Easing()
        {
            Tl[2] = t => Math.Sin(t * Math.PI / 2);
            Tl[3] = t => 1 - Math.Cos(t * Math.PI / 2);
            Tl[4] = t => 1 - Pow(t - 1, 2);
            Tl[5] = t => Pow(t, 2);
            Tl[6] = t => (1 - Math.Cos(t * Math.PI)) / 2;
            Tl[7] = t => { t *= 2; return (t < 1 ? Pow(t, 2) : -(Pow(t - 2, 2) - 2)) / 2; };
            Tl[8] = t => 1 + Pow(t - 1, 3);
            Tl[9] = t => Pow(t, 3);
            Tl[10] = t => 1 - Pow(t - 1, 4);
            Tl[11] = t => Pow(t, 4);
            Tl[12] = t => { t *= 2; return (t < 1 ? Pow(t, 3) : Pow(t - 2, 3) + 2) / 2; };
            Tl[13] = t => { t *= 2; return (t < 1 ? Pow(t, 4) : -(Pow(t - 2, 4) - 2)) / 2; };
            Tl[14] = t => 1 + Pow(t - 1, 5);
            Tl[15] = t => Pow(t, 5);
            Tl[16] = t => 1 - Pow(2, -10 * t);
            Tl[17] = t => Pow(2, 10 * (t - 1));
            Tl[18] = t => Math.Sqrt(1 - Pow(t - 1, 2));
            Tl[19] = t => 1 - Math.Sqrt(1 - Pow(t, 2));
            Tl[20] = t => (2.70158 * t - 1) * Pow(t - 1, 2) + 1;
            Tl[21] = t => (2.70158 * t - 1.70158) * Pow(t, 2);
            Tl[22] = t => { t *= 2; return (t < 1 ? 1 - Math.Sqrt(1 - Pow(t, 2)) : Math.Sqrt(1 - Pow(t - 2, 2)) + 1) / 2; };
            Tl[23] = t => t < 0.5 ? (14.379638 * t - 5.189819) * Pow(t, 2) : (14.379638 * t - 9.189819) * Pow(t - 1, 2) + 1;
            Tl[24] = t => 1 - Pow(2, -10 * t) * Math.Cos(t * Math.PI / 0.15);
            Tl[25] = t => Pow(2, 10 * (t - 1)) * Math.Cos((t - 1) * Math.PI / 0.15);
            Tl[26] = t =>
            {
                t *= 11;
                double v = t < 4 ? Pow(t, 2) : t < 8 ? Pow(t - 6, 2) + 12 : t < 10 ? Pow(t - 9, 2) + 15 : Pow(t - 10.5, 2) + 15.75;
                return v / 16;
            };
            Tl[27] = t => 1 - Tl[26](1 - t);
            Tl[28] = t => { t *= 2; return t < 1 ? Tl[26](t) / 2 : Tl[27](t - 1) / 2 + 0.5; };
            Tl[29] = t => t < 0.5
                ? Pow(2, 20 * t - 11) * Math.Sin((160 * t + 1) * Math.PI / 18)
                : 1 - Pow(2, 9 - 20 * t) * Math.Sin((160 * t + 1) * Math.PI / 18);
        }

        public static bool IsLinear(double index)
        {
            int i = (int)Math.Truncate(index);
            return i < 2 || i > 29;
        }

        /// <summary>easing(index)：越界返回 null（线性）。</summary>
        public static Func<double, double> Get(double index)
        {
            int i = (int)Math.Truncate(index);
            return (i >= 0 && i < 30) ? Tl[i] : null;
        }
    }
}
