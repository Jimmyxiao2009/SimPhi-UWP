using System;
using System.IO;
using System.Collections.Generic;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using SimPhi.Core;
using SimPhi.Render;

namespace SimPhi.Audio
{
    /// <summary>mp3/ogg/wav 的 BGM 缓冲：只存一份内存流，播放时 CloneStream 共享底层缓冲（不复制字节）。</summary>
    public sealed class MediaAudioBuffer : IAudioBuffer
    {
        private readonly InMemoryRandomAccessStream _ras;
        public string ContentType;
        public double Duration { get; set; }

        public MediaAudioBuffer(byte[] data, string contentType, double duration)
        {
            // 写入一次后丢弃 data（构造参数随即可回收），全程只保留这一份缓冲
            _ras = new InMemoryRandomAccessStream();
            var s = _ras.AsStream();       // 不要 dispose：会连带关掉 _ras
            s.Write(data, 0, data.Length);
            s.Flush();
            _ras.Seek(0);
            ContentType = contentType;
            Duration = duration;
        }

        public IRandomAccessStream NewStream()
        {
            // 克隆共享同一底层缓冲、独立读指针——不再每次播放复制整段 mp3
            return _ras.CloneStream();
        }
    }

    /// <summary>
    /// 真实音频系统：BGM 用 Windows.Media.Playback.MediaPlayer（支持 PlaybackRate=app.speed、按 offset 起播、循环）。
    /// 主时钟仍是 Stopwatch（Clock），音乐 fire-and-forget——与原版 Web Audio 的语义一致。
    /// 打击音效/静音(SilentBuffer)走空实现（音效需低延迟 PCM，留待后续 XAudio2/AudioGraph）。
    /// </summary>
    public sealed class RealAudio : IAudioSystem
    {
        private readonly List<MediaPlayer> _players = new List<MediaPlayer>();
        private readonly object _lock = new object();

        public object Play(IAudioBuffer res, AudioOptions opt)
        {
            var mab = res as MediaAudioBuffer;
            if (mab == null) return null; // 静音/打击音占位

            var mp = new MediaPlayer { AutoPlay = false, IsLoopingEnabled = opt.Loop };
            try { mp.RealTimePlayback = true; } catch { } // 降低起播缓冲延迟（属性 1607+ 才有，旧版忽略）
            try { mp.PlaybackRate = opt.Playbackrate <= 0 ? 1 : opt.Playbackrate; } catch { }
            double offset = opt.Offset;
            mp.MediaOpened += (s, e) =>
            {
                try { s.Position = TimeSpan.FromSeconds(offset < 0 ? 0 : offset); s.Play(); } catch { }
            };
            mp.Source = MediaSource.CreateFromStream(mab.NewStream(), mab.ContentType);
            lock (_lock) _players.Add(mp);
            return null;
        }

        public void Stop()
        {
            lock (_lock)
            {
                foreach (var mp in _players)
                {
                    try { mp.Pause(); mp.Source = null; mp.Dispose(); } catch { }
                }
                _players.Clear();
            }
        }

        public IAudioController CreateController() { return new Controller(this); }

        public IAudioBuffer Mute(double length) { return new SilentBuffer(length); }

        private sealed class Controller : IAudioController
        {
            private readonly RealAudio _sys;
            public Controller(RealAudio sys) { _sys = sys; }
            public object Play(IAudioBuffer res, AudioOptions opt) { return _sys.Play(res, opt); }
        }
    }
}
