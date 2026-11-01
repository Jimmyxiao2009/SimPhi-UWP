using System;
using System.Collections.Generic;
using System.Globalization;

namespace SimPhi.Core
{
    /// <summary>
    /// 照抄 src/utils/Type.ts。
    /// 关键：忠实复刻 JS 的 Number() 强制转换语义。
    ///   Type.Int(a)   === Number(a) | 0     （ECMAScript ToInt32：NaN/Inf→0，向零截断，模 2^32）
    ///   Type.Float(a) === Number(a) || 0    （NaN/0 → 0）
    /// 谱面 JSON 经 reader 解析成 IDictionary&lt;string,object&gt; / IList&lt;object&gt; 后喂进各构造函数，
    /// 与原项目"从无类型 JSON 读字段"的写法一一对应。
    /// </summary>
    public static class Type
    {
        private const double TwoPow32 = 4294967296.0;

        /// <summary>复刻 JS 全局 Number(a)。null/undefined 缺键 → NaN（Int/Float 都会归 0）。</summary>
        public static double ToNumber(object a)
        {
            if (a == null) return double.NaN;            // 缺键(undefined)：Number(undefined)=NaN
            if (a is bool b) return b ? 1d : 0d;
            if (a is string s)
            {
                string t = s.Trim();
                if (t.Length == 0) return 0d;            // Number("") === 0
                if (t == "Infinity" || t == "+Infinity") return double.PositiveInfinity;
                if (t == "-Infinity") return double.NegativeInfinity;
                double r;
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
                return double.NaN;
            }
            if (a is double d) return d;
            if (a is float f) return f;
            if (a is IConvertible)
            {
                try { return Convert.ToDouble(a, CultureInfo.InvariantCulture); }
                catch { return double.NaN; }
            }
            return double.NaN;
        }

        /// <summary>Number(a) | 0</summary>
        public static int Int(object a)
        {
            double n = ToNumber(a);
            if (double.IsNaN(n) || double.IsInfinity(n)) return 0;
            double tr = Math.Truncate(n);                // 向零截断
            double m = tr % TwoPow32;
            if (m < 0) m += TwoPow32;
            return unchecked((int)(uint)m);              // 回绕成有符号 32 位
        }

        /// <summary>Number(a) || 0</summary>
        public static double Float(object a)
        {
            double n = ToNumber(a);
            return double.IsNaN(n) ? 0d : n;             // NaN→0；±0 本身就是 0
        }

        /// <summary>Boolean(a)</summary>
        public static bool Bool(object a)
        {
            if (a == null) return false;
            if (a is bool b) return b;
            if (a is string s) return s.Length != 0;
            double n = ToNumber(a);
            return !(double.IsNaN(n) || n == 0d);
        }

        /// <summary>String(a)</summary>
        public static string Str(object a)
        {
            if (a == null) return "null";
            if (a is bool b) return b ? "true" : "false";
            if (a is string s) return s;
            if (a is IFormattable fmt) return fmt.ToString(null, CultureInfo.InvariantCulture);
            return a.ToString();
        }

        /// <summary>把一个 JSON 对象当作字典取出（构造函数参数）。</summary>
        public static IDictionary<string, object> AsMap(object a)
        {
            return a as IDictionary<string, object> ?? new Dictionary<string, object>();
        }

        /// <summary>Type.arr：数组里每一项 new ctor(item || {})。</summary>
        public static List<T> Arr<T>(object a, Func<IDictionary<string, object>, T> ctor)
        {
            var list = new List<T>();
            var src = a as IList<object>;
            if (src == null) return list;
            foreach (var item in src) list.Add(ctor(AsMap(item)));
            return list;
        }

        /// <summary>Type.obj：new ctor(a || {})。</summary>
        public static T Obj<T>(object a, Func<IDictionary<string, object>, T> ctor)
        {
            return ctor(AsMap(a));
        }

        // ---- 字典取值快捷方式（构造函数里用，等价于 JS 的 obj.key 取 undefined）----
        public static object Get(IDictionary<string, object> map, string key)
        {
            object v;
            return map != null && map.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>该键是否存在（区分 JS 的 `x == null` 判断，用于 numOfNotes 等可缺省字段）。</summary>
        public static bool Has(IDictionary<string, object> map, string key)
        {
            object v;
            return map != null && map.TryGetValue(key, out v) && v != null;
        }
    }
}
