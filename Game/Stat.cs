using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SimPhi.Game
{
    /// <summary>结算页用的成绩数据（对应 Stat.getData 返回的 pbj）。</summary>
    public sealed class StatData
    {
        public string newBestColor;
        public string newBestStr;
        public string scoreBest;
        public string scoreDelta;
        public string textAboveColor;
        public string textAboveStr;
        public string textBelowColor;
        public string textBelowStr;
    }

    /// <summary>照抄 src/components/Stat.ts。成绩本地存储用 LocalStorage 垫片；JS 数值/字符串语义忠实复刻。</summary>
    public sealed class Stat
    {
        public double level;
        public int[] noteRank;     // 长度 8
        public int[] combos;       // 长度 5
        public int maxcombo;
        public int combo;
        private double cumDisp;
        private double curDisp;
        private int numDisp;
        private int numOfNotes;
        private readonly Dictionary<string, string> data = new Dictionary<string, string>();
        private string id;
        public string format;

        private static readonly Random _rng = new Random();

        public Stat()
        {
            level = 0;
            noteRank = new int[8];
            combos = new int[5];
            cumDisp = 0;
            curDisp = 0;
            numDisp = 0;
            maxcombo = 0;
            combo = 0;
            numOfNotes = 0;
            id = "";
            format = "";
        }

        public int good { get { return noteRank[7] + noteRank[3]; } }
        public int bad { get { return noteRank[6] + noteRank[2]; } }
        public int great { get { return noteRank[5] + noteRank[1]; } }
        public int perfect { get { return noteRank[4] + great; } }
        public int all { get { return perfect + good + bad; } }

        public double scoreNum
        {
            get
            {
                double a = 1e6 * (perfect * 0.9 + good * 0.585 + maxcombo * 0.1) / numOfNotes;
                return (double.IsNaN(a) || double.IsInfinity(a)) ? 0 : a;
            }
        }

        public string scoreStr
        {
            get
            {
                string a = scoreNum.ToString("F0", CultureInfo.InvariantCulture);
                int pad = a.Length < 7 ? 7 - a.Length : 0;
                return new string('0', pad) + a;
            }
        }

        public double accNum
        {
            get
            {
                double a = (perfect + good * 0.65) / all;
                return (double.IsNaN(a) || double.IsInfinity(a)) ? 1 : a;
            }
        }

        public string accStr
        {
            get { return (100 * accNum).ToString("F2", CultureInfo.InvariantCulture) + "％"; }
        }

        public string avgDispStr
        {
            get
            {
                double t = cumDisp / numDisp * 1e3;
                long a = (double.IsNaN(t) || double.IsInfinity(t)) ? 0 : (long)Math.Truncate(t);
                return (a > 0 ? "+" : "") + a.ToString(CultureInfo.InvariantCulture) + "ms";
            }
        }

        public string curDispStr
        {
            get
            {
                long a = (long)Math.Truncate(curDisp * 1e3);
                return (a > 0 ? "+" : "") + a.ToString(CultureInfo.InvariantCulture) + "ms";
            }
        }

        /// <summary>0:有 bad；3:有 good；否则 1。</summary>
        public int lineStatus
        {
            get
            {
                if (bad != 0) return 0;
                if (good != 0) return 3;
                return 1;
            }
        }

        public int rankStatus
        {
            get
            {
                long a = JsRound(scoreNum);
                if (a >= 1000000) return 0;
                if (a >= 960000) return 1;
                if (a >= 920000) return 2;
                if (a >= 880000) return 3;
                if (a >= 820000) return 4;
                if (a >= 700000) return 5;
                return 6;
            }
        }

        public string localData
        {
            get
            {
                string l1 = JsRadix.SliceTail(JsRadix.ToString(JsRound(accNum * 1e4 + 566), 22), 3);
                string l2 = JsRadix.SliceTail(JsRadix.ToString(JsRound(scoreNum + 40672), 32), 4);
                string l3 = JsRadix.SliceTail(JsRadix.ToString((long)level, 36), 1);
                return l1 + l2 + l3;
            }
        }

        public static void removeLegacy(string key)
        {
            string item = LocalStorage.GetItem("phi");
            if (item == null) return;
            LocalStorage.SetItem(key, item);
            LocalStorage.RemoveItem("phi");
        }

        public StatData getData(bool isAuto, string speed = "")
        {
            // 兜底：原版 reset() 仅在 id 非空时填充 data[id]；id 为空(如测试谱 md5="") 时这里懒填充，避免 KeyNotFound
            if (!data.ContainsKey(id)) data[id] = localData;
            string cur = data[id];
            string s1 = cur.Substring(0, 3);
            string s2 = cur.Substring(3, 4);
            string l1 = JsRadix.SliceTail(JsRadix.ToString(JsRound(accNum * 1e4 + 566), 22), 3);
            string l2 = JsRadix.SliceTail(JsRadix.ToString(JsRound(scoreNum + 40672), 32), 4);
            string l3 = JsRadix.SliceTail(JsRadix.ToString((long)level, 36), 1);
            long na = JsRadix.ParseInt(s2, 32) - 40672;
            string sa = na.ToString(CultureInfo.InvariantCulture);
            int pad = sa.Length < 7 ? 7 - sa.Length : 0;
            string sb = new string('0', pad) + sa;
            if (!isAuto)
                data[id] = (string.CompareOrdinal(s1, l1) > 0 ? s1 : l1)
                         + (string.CompareOrdinal(s2, l2) > 0 ? s2 : l2) + l3;

            var arr = new List<string>();
            foreach (var kv in data) arr.Add(kv.Key + kv.Value);
            Shuffle(arr);
            LocalStorage.SetItem("phi-" + speed, string.Concat(arr));

            var pbj = new StatData
            {
                newBestColor = string.CompareOrdinal(s2, l2) < 0 ? "#18ffbf" : "#fff",
                newBestStr = string.CompareOrdinal(s2, l2) < 0 ? "NEW BEST" : "BEST",
                scoreBest = sb,
                scoreDelta = (string.CompareOrdinal(s2, l2) > 0 ? "- " : "+ ")
                             + Math.Abs(na - JsRound(scoreNum)).ToString(CultureInfo.InvariantCulture),
                textAboveColor = "#65fe43",
                textAboveStr = "  ( Speed {SPEED}x )",
                textBelowColor = "#fe4365",
                textBelowStr = "AUTO PLAY"
            };

            if (isAuto)
            {
                pbj.newBestColor = "#fff";
                pbj.newBestStr = "BEST";
                pbj.scoreDelta = "";
                return pbj;
            }
            if (lineStatus == 1)
            {
                pbj.textBelowStr = "ALL  PERFECT";
                pbj.textBelowColor = "#ffc500";
                return pbj;
            }
            if (lineStatus == 3)
            {
                pbj.textBelowStr = "FULL  COMBO";
                pbj.textBelowColor = "#00bef1";
                return pbj;
            }
            pbj.textBelowStr = "";
            return pbj;
        }

        public void reset(int numOfNotes, string id, string format, string speed = "")
        {
            string key = "phi-" + speed;
            this.numOfNotes = numOfNotes;
            combo = 0;
            maxcombo = 0;
            noteRank = new int[8];   // 4:PM,5:PE,1:PL,7:GE,3:GL,6:BE,2:BL
            combos = new int[5];
            cumDisp = 0;
            curDisp = 0;
            numDisp = 0;
            if (speed == "") removeLegacy(key);
            string str = LocalStorage.GetItem(key);
            if (str == null) { LocalStorage.SetItem(key, ""); str = ""; }
            for (int i = 0; i < (int)Math.Floor(str.Length / 40.0); i++)
            {
                string d = str.Substring(i * 40, 40);
                data[d.Substring(0, 32)] = JsRadix.SliceTail(d, 8);
            }
            if (!string.IsNullOrEmpty(id))
            {
                if (!data.ContainsKey(id) || string.IsNullOrEmpty(data[id])) data[id] = localData;
                this.id = id;
            }
            this.format = format;
        }

        public void addCombo(int status, int type)
        {
            noteRank[status]++;
            combo = status % 4 == 2 ? 0 : combo + 1;
            if (combo > maxcombo) maxcombo = combo;
            combos[0]++;
            combos[type]++;
        }

        public void addDisp(double disp)
        {
            curDisp = disp;
            cumDisp += disp;
            numDisp++;
        }

        // JS Math.round（正数向上取半；本类数值均非负）
        private static long JsRound(double x) { return (long)Math.Floor(x + 0.5); }

        private static void Shuffle(List<string> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                string tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }
}
