// PA-102X-S spike: minimal JSON reader for the golden files (no Newtonsoft in the project).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PocketArbor.Spike.TreeGen
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>Parsed JSON value. Numbers are double (culture-invariant).</summary>
    public sealed class JsonNode
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public string String;
        public List<JsonNode> Items;
        public Dictionary<string, JsonNode> Fields;

        public JsonNode this[string key]
        {
            get
            {
                if (Kind != JsonKind.Object) throw new InvalidOperationException("Not an object");
                JsonNode v;
                if (!Fields.TryGetValue(key, out v)) throw new KeyNotFoundException("JSON key not found: " + key);
                return v;
            }
        }

        public JsonNode this[int index]
        {
            get
            {
                if (Kind != JsonKind.Array) throw new InvalidOperationException("Not an array");
                return Items[index];
            }
        }

        public int Count => Kind == JsonKind.Array ? Items.Count : Kind == JsonKind.Object ? Fields.Count : 0;

        public bool Has(string key)
        {
            return Kind == JsonKind.Object && Fields.ContainsKey(key);
        }

        public double D => Kind == JsonKind.Number ? Number : Kind == JsonKind.Bool ? (Bool ? 1 : 0) : double.NaN;
        public int I => (int)Number;
        public bool B => Kind == JsonKind.Bool ? Bool : Kind == JsonKind.Number && Number != 0;
        public string S => String;

        public D3 ToD3()
        {
            return new D3(Items[0].D, Items[1].D, Items[2].D);
        }

        public double[] ToDoubles()
        {
            var a = new double[Items.Count];
            for (int i = 0; i < a.Length; i++) a[i] = Items[i].D;
            return a;
        }
    }

    /// <summary>Recursive-descent JSON parser (RFC 8259 subset sufficient for the golden files).</summary>
    public static class MiniJson
    {
        public static JsonNode Parse(string text)
        {
            int i = 0;
            JsonNode v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException("Trailing characters at " + i);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static JsonNode ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return new JsonNode { Kind = JsonKind.String, String = ParseString(s, ref i) };
            if (Match(s, ref i, "true")) return new JsonNode { Kind = JsonKind.Bool, Bool = true };
            if (Match(s, ref i, "false")) return new JsonNode { Kind = JsonKind.Bool, Bool = false };
            if (Match(s, ref i, "null")) return new JsonNode { Kind = JsonKind.Null };
            return ParseNumber(s, ref i);
        }

        private static bool Match(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        private static JsonNode ParseObject(string s, ref int i)
        {
            var node = new JsonNode { Kind = JsonKind.Object, Fields = new Dictionary<string, JsonNode>() };
            i++; // {
            SkipWs(s, ref i);
            if (s[i] == '}')
            {
                i++;
                return node;
            }

            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (s[i] != ':') throw new FormatException("Expected ':' at " + i);
                i++;
                node.Fields[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (s[i] == '}')
                {
                    i++;
                    return node;
                }

                throw new FormatException("Expected ',' or '}' at " + i);
            }
        }

        private static JsonNode ParseArray(string s, ref int i)
        {
            var node = new JsonNode { Kind = JsonKind.Array, Items = new List<JsonNode>() };
            i++; // [
            SkipWs(s, ref i);
            if (s[i] == ']')
            {
                i++;
                return node;
            }

            while (true)
            {
                node.Items.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (s[i] == ']')
                {
                    i++;
                    return node;
                }

                throw new FormatException("Expected ',' or ']' at " + i);
            }
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected string at " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape at " + i);
                }
            }
        }

        private static JsonNode ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }

            if (i == start) throw new FormatException("Unexpected character '" + s[i] + "' at " + i);
            double v = double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            return new JsonNode { Kind = JsonKind.Number, Number = v };
        }
    }
}
