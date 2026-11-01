using System.Collections.Generic;
using Microsoft.Graphics.Canvas;
using SimPhi.Core;

namespace SimPhi.Render
{
    /// <summary>
    /// 对应 index.ts 的 res 资源表（ResourceMap）。同时存图像(CanvasBitmap)和音频(IAudioBuffer)。
    /// 加载期按名写入（res[name]=...），绘制期用具名属性取（res.JudgeLine 等），与原版读法一致。
    /// </summary>
    public sealed class Res
    {
        private readonly Dictionary<string, object> _map = new Dictionary<string, object>();

        public object this[string key]
        {
            get { object v; return _map.TryGetValue(key, out v) ? v : null; }
            set { _map[key] = value; }
        }
        public bool Has(string key) { return _map.ContainsKey(key); }

        private CanvasBitmap B(string k) { object v; return _map.TryGetValue(k, out v) ? v as CanvasBitmap : null; }
        private IAudioBuffer A(string k) { object v; return _map.TryGetValue(k, out v) ? v as IAudioBuffer : null; }

        // ---- 绘制用图像 ----
        public CanvasBitmap JudgeLine { get { return B("JudgeLine"); } }
        public CanvasBitmap JudgeLineMP { get { return B("JudgeLineMP"); } }
        public CanvasBitmap JudgeLineFC { get { return B("JudgeLineFC"); } }
        public CanvasBitmap ProgressBar { get { return B("ProgressBar"); } }
        public CanvasBitmap HitFXRaw { get { return B("HitFXRaw"); } }
        public CanvasBitmap Rank { get { return B("Rank"); } }
        public CanvasBitmap NoImageBlack { get { return B("NoImageBlack"); } }
        public CanvasBitmap NoImageWhite { get { return B("NoImageWhite"); } }
        public CanvasBitmap LevelOver1 { get { return B("LevelOver1"); } }
        public CanvasBitmap LevelOver3 { get { return B("LevelOver3"); } }
        public CanvasBitmap LevelOver4 { get { return B("LevelOver4"); } }
        public CanvasBitmap LevelOver5 { get { return B("LevelOver5"); } }

        // Rank 拆分后的 7 张（res.Ranks）
        public CanvasBitmap[] Ranks { get; set; }

        // ---- 音频 ----
        public IAudioBuffer HitSong0 { get { return A("HitSong0"); } }
        public IAudioBuffer HitSong1 { get { return A("HitSong1"); } }
        public IAudioBuffer HitSong2 { get { return A("HitSong2"); } }
        public IAudioBuffer Mute { get { return A("mute"); } }
        public IAudioBuffer LevelOverAudio(int difficulty) { return A("LevelOver" + difficulty + "_v1"); }
    }
}
