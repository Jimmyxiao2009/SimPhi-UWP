using Windows.Storage;
using SimPhi.Game;

namespace SimPhi.Platform
{
    /// <summary>用 ApplicationData.LocalSettings 支撑 Stat 的成绩本地存储（替代 localStorage）。</summary>
    public sealed class UwpLocalStorage : LocalStorage.IBackend
    {
        private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;

        public string GetItem(string key)
        {
            object v;
            return _settings.Values.TryGetValue(key, out v) ? v as string : null;
        }

        public void SetItem(string key, string value)
        {
            _settings.Values[key] = value;
        }

        public void RemoveItem(string key)
        {
            _settings.Values.Remove(key);
        }
    }
}
