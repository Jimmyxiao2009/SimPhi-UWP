using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Microsoft.Graphics.Canvas;
using SimPhi.Core;
using SimPhi.Pec;
using SimPhi.Audio;

namespace SimPhi.Loader
{
    public sealed class PezData
    {
        public Chart Chart;
        public CanvasBitmap Background;
        public IAudioBuffer Music;
        public string Md5;
        public string Format;
        public string Name;
        public string Artist;
        public string Charter;
        public string LevelText;
        public int DifficultyIndex = -1;
        public double LevelNumber;
    }

    /// <summary>
    /// 加载 .pez（PhiZone）：zip 内含 RPE 谱面 json + 曲绘 + 音乐 + info.txt。
    /// 对应原版 reader-.ts 的 .json→PEC.parseRPE 流程 + zip 解压。音乐解码留给音频任务（暂静音，时间轴用谱面长度）。
    /// </summary>
    public static class PezLoader
    {
        public static async Task<PezData> LoadAsync(CanvasDevice device, IRandomAccessStream stream)
        {
            var ms = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(ms);
            ms.Position = 0;

            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                ZipArchiveEntry chartEntry = null, imageEntry = null, infoEntry = null, musicEntry = null;
                foreach (var e in zip.Entries)
                {
                    string n = e.Name.ToLowerInvariant();
                    if (n == "info.txt") infoEntry = e;
                    else if (n.EndsWith(".json")) chartEntry = e;
                    else if (n.EndsWith(".jpg") || n.EndsWith(".jpeg") || n.EndsWith(".png") || n.EndsWith(".bmp")) imageEntry = e;
                    else if (n.EndsWith(".mp3") || n.EndsWith(".ogg") || n.EndsWith(".wav") || n.EndsWith(".m4a")) musicEntry = e;
                }
                if (chartEntry == null) throw new Exception("未找到谱面 json");

                string chartText = ReadAllText(chartEntry);
                var root = JsonConvert.ParseObject(chartText);
                var rpe = Rpe2Json.Parse(root, chartEntry.FullName, chartEntry.Name);

                var data = new PezData
                {
                    Chart = rpe.data,
                    Md5 = Md5Hex(chartText),
                    Format = rpe.format
                };

                // info：优先用 RPE meta，再用 info.txt 补
                var info = rpe.info;
                if (infoEntry != null)
                {
                    var recs = ReadInfo.Read(ReadAllText(infoEntry));
                    if (recs.Count > 0) foreach (var kv in recs[0]) info[kv.Key] = kv.Value;
                }
                data.Name = Val(info, "Name");
                data.Artist = Val(info, "Artist");
                data.Charter = Val(info, "Charter");
                data.LevelText = Val(info, "Level");
                ParseLevel(data.LevelText, out data.DifficultyIndex, out data.LevelNumber);

                if (imageEntry != null) data.Background = await LoadBitmap(device, imageEntry);
                if (musicEntry != null)
                {
                    double dur = ReadDuration(root);
                    data.Music = new MediaAudioBuffer(ReadAllBytes(musicEntry), ContentType(musicEntry.Name), dur);
                }
                return data;
            }
        }

        private static double ReadDuration(IDictionary<string, object> root)
        {
            IDictionary<string, object> meta = root;
            object m;
            if (root.TryGetValue("META", out m) && m is IDictionary<string, object>) meta = (IDictionary<string, object>)m;
            object d;
            if (meta.TryGetValue("duration", out d) && d != null)
            {
                try { return Convert.ToDouble(d); } catch { }
            }
            return 0;
        }

        private static byte[] ReadAllBytes(ZipArchiveEntry e)
        {
            using (var s = e.Open())
            {
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static string ContentType(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.EndsWith(".wav")) return "audio/wav";
            if (n.EndsWith(".ogg")) return "audio/ogg";
            if (n.EndsWith(".m4a")) return "audio/mp4";
            return "audio/mpeg";
        }

        private static string Val(IDictionary<string, string> m, string k)
        {
            string v; return m.TryGetValue(k, out v) ? (v ?? "") : "";
        }

        private static string ReadAllText(ZipArchiveEntry e)
        {
            using (var s = e.Open())
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        // 背景图长边上限：手机屏顶多 1080p，超出的曲绘降采样省内存（且更贴近原版浏览器把图缩到屏幕尺寸的呈现）
        private const int BgMaxEdge = 1920;

        private static async Task<CanvasBitmap> LoadBitmap(CanvasDevice device, ZipArchiveEntry e)
        {
            var mem = new MemoryStream();
            using (var s = e.Open()) await s.CopyToAsync(mem);
            mem.Position = 0;
            var full = await CanvasBitmap.LoadAsync(device, mem.AsRandomAccessStream());
            int w = (int)full.SizeInPixels.Width, h = (int)full.SizeInPixels.Height;
            int max = Math.Max(w, h);
            if (max <= BgMaxEdge) return full;
            double scale = (double)BgMaxEdge / max;
            int nw = Math.Max(1, (int)Math.Round(w * scale));
            int nh = Math.Max(1, (int)Math.Round(h * scale));
            var rt = new CanvasRenderTarget(device, nw, nh, full.Dpi);
            using (var ds = rt.CreateDrawingSession())
                ds.DrawImage(full, new Windows.Foundation.Rect(0, 0, nw, nh), new Windows.Foundation.Rect(0, 0, w, h));
            full.Dispose();
            return rt;
        }

        private static string Md5Hex(string text)
        {
            var alg = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
            var buf = CryptographicBuffer.ConvertStringToBinary(text, BinaryStringEncoding.Utf8);
            var hash = alg.HashData(buf);
            return CryptographicBuffer.EncodeToHexString(hash); // 32 位小写十六进制
        }

        // "IN Lv.15" → 难度索引(EZ/HD/IN/AT=0..3) + 等级数
        private static void ParseLevel(string level, out int difficulty, out double levelNumber)
        {
            difficulty = -1;
            levelNumber = 0;
            if (string.IsNullOrEmpty(level)) return;
            string up = level.ToUpperInvariant();
            if (up.Contains("EZ")) difficulty = 0;
            else if (up.Contains("HD")) difficulty = 1;
            else if (up.Contains("IN")) difficulty = 2;
            else if (up.Contains("AT")) difficulty = 3;
            // 取最后一段数字作为等级
            var sb = new StringBuilder();
            for (int i = level.Length - 1; i >= 0; i--)
            {
                char c = level[i];
                if (char.IsDigit(c)) sb.Insert(0, c);
                else if (sb.Length > 0) break;
            }
            double n;
            if (double.TryParse(sb.ToString(), out n)) levelNumber = n;
        }
    }
}
