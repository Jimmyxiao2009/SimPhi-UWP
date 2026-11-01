using SimPhi.Core;

namespace SimPhi.Render
{
    /// <summary>对应 AudioTools 的 AudioParamOptions。</summary>
    public struct AudioOptions
    {
        public bool Loop;
        public double Offset;
        public double Playbackrate;
        public double Interval;

        public static AudioOptions Make(bool loop = false, double offset = 0, double playbackrate = 1, double interval = 0)
        {
            return new AudioOptions { Loop = loop, Offset = offset, Playbackrate = playbackrate, Interval = interval };
        }
    }

    /// <summary>对应 AudioController：连到某条增益的播放器。</summary>
    public interface IAudioController
    {
        /// <summary>返回值对应原版的 bgMusicHack（() =&gt; AudioBufferSourceNode），插件用，渲染层不依赖。</summary>
        object Play(IAudioBuffer res, AudioOptions opt);
    }

    /// <summary>对应全局 audio 对象（XAudio2 实现）。</summary>
    public interface IAudioSystem
    {
        object Play(IAudioBuffer res, AudioOptions opt);
        void Stop();
        IAudioController CreateController();
        IAudioBuffer Mute(double length);
    }
}
