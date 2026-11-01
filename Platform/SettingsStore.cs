using Windows.Storage;
using SimPhi.Render;

namespace SimPhi.Platform
{
    /// <summary>把 Settings 持久化到 ApplicationData.LocalSettings（每个开关一个键）。</summary>
    public static class SettingsStore
    {
        private static ApplicationDataContainer S { get { return ApplicationData.Current.LocalSettings; } }

        private static bool GetBool(string key, bool def)
        {
            object v;
            return S.Values.TryGetValue(key, out v) && v is bool ? (bool)v : def;
        }
        private static void SetBool(string key, bool v) { S.Values[key] = v; }
        private static double GetDouble(string key, double def)
        {
            object v;
            return S.Values.TryGetValue(key, out v) && v is double ? (double)v : def;
        }

        public static void Load(Settings s)
        {
            s.Autoplay = GetBool("set.Autoplay", true);
            s.LineColor = GetBool("set.LineColor", false);
            s.ShowAcc = GetBool("set.ShowAcc", false);
            s.ShowStat = GetBool("set.ShowStat", false);
            s.ShowPoint = GetBool("set.ShowPoint", false);
            s.ShowCE2 = GetBool("set.ShowCE2", false);
            s.Feedback = GetBool("set.Feedback", false);
            s.ImageBlur = GetBool("set.ImageBlur", false);
            s.ShowTransition = GetBool("set.ShowTransition", true);
            s.AutoDelay = GetBool("set.AutoDelay", true);
            s.Offset = GetDouble("set.Offset", 0);
        }

        public static void Save(Settings s)
        {
            SetBool("set.Autoplay", s.Autoplay);
            SetBool("set.LineColor", s.LineColor);
            SetBool("set.ShowAcc", s.ShowAcc);
            SetBool("set.ShowStat", s.ShowStat);
            SetBool("set.ShowPoint", s.ShowPoint);
            SetBool("set.ShowCE2", s.ShowCE2);
            SetBool("set.Feedback", s.Feedback);
            SetBool("set.ImageBlur", s.ImageBlur);
            SetBool("set.ShowTransition", s.ShowTransition);
            SetBool("set.AutoDelay", s.AutoDelay);
        }
    }
}
