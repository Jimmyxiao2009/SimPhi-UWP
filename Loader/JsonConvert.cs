using System.Collections.Generic;
using Windows.Data.Json;

namespace SimPhi.Loader
{
    /// <summary>把 Windows.Data.Json 解析结果转成可移植结构（IDictionary/IList/double/string/bool/null），喂给 Pec.Rpe2Json。</summary>
    public static class JsonConvert
    {
        public static IDictionary<string, object> ParseObject(string json)
        {
            return ToPortable(JsonObject.Parse(json)) as IDictionary<string, object>;
        }

        private static object ToPortable(IJsonValue v)
        {
            switch (v.ValueType)
            {
                case JsonValueType.Object:
                {
                    var d = new Dictionary<string, object>();
                    foreach (var kv in v.GetObject()) d[kv.Key] = ToPortable(kv.Value);
                    return d;
                }
                case JsonValueType.Array:
                {
                    var l = new List<object>();
                    foreach (var e in v.GetArray()) l.Add(ToPortable(e));
                    return l;
                }
                case JsonValueType.Number: return v.GetNumber();
                case JsonValueType.String: return v.GetString();
                case JsonValueType.Boolean: return v.GetBoolean();
                default: return null;
            }
        }
    }
}
