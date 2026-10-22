using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Glasshouse
{
    // Small JSON reader/writer (no NuGet packages, so the exe stays one file).
    // Objects -> Dictionary<string, object> (insertion order kept), arrays -> List<object>,
    // whole numbers -> long, other numbers -> double.
    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("extra text after JSON at " + i);
            return v;
        }

        public static Dictionary<string, object> ParseObject(string s) =>
            Parse(s) as Dictionary<string, object> ?? throw new FormatException("expected a JSON object");

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    var k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("expected : at " + i);
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("expected , or } at " + i);
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("expected , or ] at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            var num = s.Substring(st, i - st);
            if (num.Length == 0) throw new FormatException("bad JSON value at " + st);
            if (num.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
            return double.Parse(num, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected string at " + i);
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }

        public static string Write(object o)
        {
            var sb = new StringBuilder();
            W(sb, o, 0);
            return sb.ToString();
        }

        static void W(StringBuilder sb, object o, int ind)
        {
            switch (o)
            {
                case null: sb.Append("null"); return;
                case string s: Quote(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case uint n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> d:
                    if (d.Count == 0) { sb.Append("{}"); return; }
                    sb.Append("{\n");
                    int k = 0;
                    foreach (var kv in d)
                    {
                        sb.Append(' ', ind + 2); Quote(sb, kv.Key); sb.Append(": ");
                        W(sb, kv.Value, ind + 2);
                        if (++k < d.Count) sb.Append(',');
                        sb.Append('\n');
                    }
                    sb.Append(' ', ind).Append('}');
                    return;
                case IEnumerable e:
                    var items = new List<object>();
                    foreach (var x in e) items.Add(x);
                    if (items.Count == 0) { sb.Append("[]"); return; }
                    bool simple = items.TrueForAll(x => !(x is IDictionary<string, object>) && !(x is IList));
                    if (simple)
                    {
                        sb.Append('[');
                        for (int j = 0; j < items.Count; j++) { if (j > 0) sb.Append(", "); W(sb, items[j], ind); }
                        sb.Append(']');
                        return;
                    }
                    sb.Append("[\n");
                    for (int j = 0; j < items.Count; j++)
                    {
                        sb.Append(' ', ind + 2); W(sb, items[j], ind + 2);
                        if (j < items.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                    sb.Append(' ', ind).Append(']');
                    return;
                default: Quote(sb, o.ToString()); return;
            }
        }

        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // typed helpers for reading parsed objects
        public static string Str(this Dictionary<string, object> d, string k, string def = null) =>
            d != null && d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : def;
        public static bool Bool(this Dictionary<string, object> d, string k, bool def = false) =>
            d != null && d.TryGetValue(k, out var v) && v != null ? (v is bool b ? b : Convert.ToInt64(v) != 0) : def;
        public static long Long(this Dictionary<string, object> d, string k, long def = 0) =>
            d != null && d.TryGetValue(k, out var v) && v != null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : def;
        public static double Dbl(this Dictionary<string, object> d, string k, double def = 0) =>
            d != null && d.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : def;
        public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string k) =>
            d != null && d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;
        public static List<object> Arr(this Dictionary<string, object> d, string k) =>
            d != null && d.TryGetValue(k, out var v) ? v as List<object> : null;
    }
}
