using System;

namespace SimPhi.Pec
{
    /// <summary>照抄 @sim-phi/extends src/pec/cubicBezier.js（WebKit 牛顿法 + 二分法求解）。</summary>
    public static class CubicBezier
    {
        public static Func<double, double> Make(double p1x, double p1y, double p2x, double p2y)
        {
            const double ZERO_LIMIT = 1e-6;
            double ax = 3 * p1x - 3 * p2x + 1;
            double bx = 3 * p2x - 6 * p1x;
            double cx = 3 * p1x;
            double ay = 3 * p1y - 3 * p2y + 1;
            double by = 3 * p2y - 6 * p1y;
            double cy = 3 * p1y;

            Func<double, double> sampleCurveDerivativeX = t => (3 * ax * t + 2 * bx) * t + cx;
            Func<double, double> sampleCurveX = t => ((ax * t + bx) * t + cx) * t;
            Func<double, double> sampleCurveY = t => ((ay * t + by) * t + cy) * t;

            Func<double, double> solveCurveX = x =>
            {
                double t2 = x, x2;
                for (int i = 0; i < 8; i++)
                {
                    x2 = sampleCurveX(t2) - x;
                    if (Math.Abs(x2) < ZERO_LIMIT) return t2;
                    double derivative = sampleCurveDerivativeX(t2);
                    if (Math.Abs(derivative) < ZERO_LIMIT) break;
                    t2 -= x2 / derivative;
                }
                double t1 = 1, t0 = 0;
                t2 = x;
                while (t1 > t0)
                {
                    x2 = sampleCurveX(t2) - x;
                    if (Math.Abs(x2) < ZERO_LIMIT) return t2;
                    if (x2 > 0) t1 = t2; else t0 = t2;
                    t2 = (t1 + t0) / 2;
                }
                return t2;
            };

            return x => sampleCurveY(solveCurveX(x));
        }
    }
}
