using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SimPhi.Pec
{
    /// <summary>照抄 @sim-phi/extends src/pec/readInfo.ts：解析 info.txt（# 分记录，Key: Value 行）。</summary>
    public static class ReadInfo
    {
        public static List<Dictionary<string, string>> Read(string text)
        {
            var lines = Regex.Split(text ?? "", "\r?\n");
            var result = new List<Dictionary<string, string>>();
            var current = new Dictionary<string, string>();
            foreach (var i in lines)
            {
                if (i.StartsWith("#"))
                {
                    if (current.Count != 0) result.Add(current);
                    current = new Dictionary<string, string>();
                }
                else
                {
                    // i.split(/:(.+)/) → 取第一个冒号之后全部作为值
                    int idx = i.IndexOf(':');
                    string key, value;
                    if (idx < 0) { key = i.Trim(); value = null; }
                    else { key = i.Substring(0, idx).Trim(); value = i.Substring(idx + 1).Trim(); }
                    if (key == "Song") key = "Music";
                    if (key == "Picture") key = "Image";
                    if (!string.IsNullOrEmpty(key)) current[key] = value;
                }
            }
            if (current.Count != 0) result.Add(current);
            return result;
        }
    }
}
