using System;
using System.Collections.Generic;
using System.Text;

namespace SimPhi.Game
{
    /// <summary>
    /// 复刻 Stat.ts 里用到的 JS 内建：Number.prototype.toString(radix) 与 parseInt(s, radix)。
    /// 用于成绩本地存储的编码（base22/32/36）。
    /// </summary>
    public static class JsRadix
    {
        private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

        /// <summary>(value).toString(radix)，仅处理整数；负数前缀 '-'。</summary>
        public static string ToString(long value, int radix)
        {
            if (radix < 2 || radix > 36) throw new ArgumentOutOfRangeException(nameof(radix));
            if (value == 0) return "0";
            bool neg = value < 0;
            ulong v = neg ? (ulong)(-value) : (ulong)value;
            var sb = new StringBuilder();
            while (v > 0)
            {
                sb.Insert(0, Digits[(int)(v % (ulong)radix)]);
                v /= (ulong)radix;
            }
            if (neg) sb.Insert(0, '-');
            return sb.ToString();
        }

        /// <summary>parseInt(s, radix)：跳过前导空白与符号，解析合法前缀，无合法位则视作 NaN（返回 long.MinValue 标记）。</summary>
        public static long ParseInt(string s, int radix)
        {
            if (s == null) return long.MinValue;
            int i = 0, n = s.Length;
            while (i < n && char.IsWhiteSpace(s[i])) i++;
            bool neg = false;
            if (i < n && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
            long result = 0;
            bool any = false;
            for (; i < n; i++)
            {
                int d = DigitValue(s[i]);
                if (d < 0 || d >= radix) break;
                result = result * radix + d;
                any = true;
            }
            if (!any) return long.MinValue; // NaN
            return neg ? -result : result;
        }

        private static int DigitValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'z') return c - 'a' + 10;
            if (c >= 'A' && c <= 'Z') return c - 'A' + 10;
            return -1;
        }

        /// <summary>取字符串末尾 n 个字符（对应 JS 的 .slice(-n)）。</summary>
        public static string SliceTail(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(s.Length - n);
        }
    }

    /// <summary>
    /// localStorage 垫片：默认内存实现（让 Core/Game 可独立编译验证），
    /// UWP 外壳启动时把 Backend 换成 ApplicationData.Current.LocalSettings 支撑的实现。
    /// </summary>
    public static class LocalStorage
    {
        public interface IBackend
        {
            string GetItem(string key);
            void SetItem(string key, string value);
            void RemoveItem(string key);
        }

        private sealed class MemoryBackend : IBackend
        {
            private readonly Dictionary<string, string> _map = new Dictionary<string, string>();
            public string GetItem(string key) { string v; return _map.TryGetValue(key, out v) ? v : null; }
            public void SetItem(string key, string value) { _map[key] = value; }
            public void RemoveItem(string key) { _map.Remove(key); }
        }

        public static IBackend Backend = new MemoryBackend();

        public static string GetItem(string key) { return Backend.GetItem(key); }
        public static void SetItem(string key, string value) { Backend.SetItem(key, value); }
        public static void RemoveItem(string key) { Backend.RemoveItem(key); }
    }
}
