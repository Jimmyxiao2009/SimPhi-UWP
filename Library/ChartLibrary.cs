using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using SimPhi.Pec;

namespace SimPhi.Library
{
    /// <summary>谱面库里的一首（用于歌曲选择界面绑定）。</summary>
    public sealed class ChartItem
    {
        public string FilePath { get; set; }
        public string Name { get; set; }
        public string Level { get; set; }
        public string Charter { get; set; }
        public BitmapImage Cover { get; set; }
    }

    /// <summary>
    /// 谱面库：把导入的 .pez 复制到 LocalFolder\charts，扫描成歌曲列表，读 info.txt 的曲名/难度 + 封面缩略图。
    /// </summary>
    public static class ChartLibrary
    {
        private static async Task<StorageFolder> GetFolderAsync()
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("charts", CreationCollisionOption.OpenIfExists);
        }

        public static async Task ImportAsync(StorageFile file)
        {
            var folder = await GetFolderAsync();
            await file.CopyAsync(folder, file.Name, NameCollisionOption.GenerateUniqueName);
        }

        public static async Task DeleteAsync(string path)
        {
            try { var f = await StorageFile.GetFileFromPathAsync(path); await f.DeleteAsync(); } catch { }
        }

        public static async Task<List<ChartItem>> ListAsync()
        {
            var folder = await GetFolderAsync();
            var files = await folder.GetFilesAsync();
            var items = new List<ChartItem>();
            foreach (var f in files)
            {
                string ext = Path.GetExtension(f.Name).ToLowerInvariant();
                if (ext != ".pez" && ext != ".zip") continue;
                try { items.Add(await ReadMetaAsync(f)); }
                catch
                {
                    items.Add(new ChartItem { FilePath = f.Path, Name = Path.GetFileNameWithoutExtension(f.Name), Level = "?" });
                }
            }
            return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static async Task<ChartItem> ReadMetaAsync(StorageFile f)
        {
            var item = new ChartItem
            {
                FilePath = f.Path,
                Name = Path.GetFileNameWithoutExtension(f.Name),
                Level = "",
                Charter = ""
            };
            using (var s = await f.OpenStreamForReadAsync())
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                var info = zip.Entries.FirstOrDefault(e => e.Name.Equals("info.txt", StringComparison.OrdinalIgnoreCase));
                if (info != null)
                {
                    string txt;
                    using (var r = new StreamReader(info.Open(), Encoding.UTF8)) txt = r.ReadToEnd();
                    var recs = ReadInfo.Read(txt);
                    if (recs.Count > 0)
                    {
                        var m = recs[0];
                        string v;
                        if (m.TryGetValue("Name", out v) && !string.IsNullOrEmpty(v)) item.Name = v;
                        if (m.TryGetValue("Level", out v)) item.Level = v;
                        if (m.TryGetValue("Charter", out v)) item.Charter = v;
                    }
                }
                var imgEntry = zip.Entries.FirstOrDefault(e =>
                {
                    string n = e.Name.ToLowerInvariant();
                    return n.EndsWith(".jpg") || n.EndsWith(".jpeg") || n.EndsWith(".png");
                });
                if (imgEntry != null)
                {
                    var mem = new MemoryStream();
                    using (var es = imgEntry.Open()) await es.CopyToAsync(mem);
                    mem.Position = 0;
                    var bmp = new BitmapImage { DecodePixelWidth = 360 }; // 缩略图，控制内存
                    await bmp.SetSourceAsync(mem.AsRandomAccessStream());
                    item.Cover = bmp;
                }
            }
            return item;
        }
    }
}
