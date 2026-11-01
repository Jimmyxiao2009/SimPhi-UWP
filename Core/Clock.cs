using System.Diagnostics;

namespace SimPhi.Core
{
    /// <summary>
    /// 替换 JS 的 performance.now()：返回自启动以来的高精度毫秒数（double）。
    /// 原项目主时钟用的就是 performance.now（注释明说不读音频时钟，因为 actx.currentTime 有延迟），
    /// 这里用单调递增的 Stopwatch 一一对应，音频走 fire-and-forget 按 offset 起播。
    /// </summary>
    public static class Clock
    {
        private static readonly Stopwatch _sw = Stopwatch.StartNew();
        private static readonly double _msPerTick = 1000.0 / Stopwatch.Frequency;

        /// <summary>performance.now()，单位毫秒。</summary>
        public static double Now()
        {
            return _sw.ElapsedTicks * _msPerTick;
        }
    }
}
