using SimPhi.Core;
using SimPhi.Render;

namespace SimPhi.Audio
{
    /// <summary>
    /// 占位音频系统：无声，但提供正确的时长以驱动时间轴（duration0 = chart.maxSeconds 时不依赖音频）。
    /// 让工程先 build 起来、先出画面。正式的 XAudio2 实现见 [[task-5]]，替换它即可。
    /// </summary>
    public sealed class SilentBuffer : IAudioBuffer
    {
        public double Duration { get; private set; }
        public SilentBuffer(double duration) { Duration = duration; }
    }

    public sealed class SilentAudio : IAudioSystem
    {
        public object Play(IAudioBuffer res, AudioOptions opt) { return null; }
        public void Stop() { }
        public IAudioController CreateController() { return new SilentController(); }
        public IAudioBuffer Mute(double length) { return new SilentBuffer(length); }
    }

    public sealed class SilentController : IAudioController
    {
        public object Play(IAudioBuffer res, AudioOptions opt) { return null; }
    }
}
