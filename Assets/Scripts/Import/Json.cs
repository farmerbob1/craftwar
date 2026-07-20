using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Craftwar.Import
{
    /// <summary>
    /// Minimal recursive-descent JSON reader.
    ///
    /// Exists because Unity ships no usable option: <c>JsonUtility</c> cannot
    /// deserialize arbitrary string-keyed maps (it needs a concrete field per
    /// key), and Newtonsoft is not in Packages/manifest.json. Both files M8
    /// needs are maps — Strings/enUS.json is 1613 flat keys, and the
    /// TexturePacker atlases nest a "frames" object of 784 entries.
    ///
    /// Deliberately free of UnityEngine so it compiles into the standalone
    /// dotnet + NUnitLite harness and can be tested without opening the editor.
    /// Read-only; there is no writer, and no attempt at streaming — the largest
    /// input is ~135 KB.
    /// </summary>
    public sealed class JsonValue
    {
        public enum Kind { Null, Bool, Number, String, Array, Object }

        public Kind Type { get; private set; }
        public bool Bool { get; private set; }
        public double Number { get; private set; }
        public string String { get; private set; }
        public List<JsonValue> Array { get; private set; }
        public Dictionary<string, JsonValue> Object { get; private set; }

        /// <summary>Member by name, or null when absent or not an object. Chainable.</summary>
        public JsonValue this[string key]
        {
            get
            {
                if (Type != Kind.Object || key == null) return null;
                return Object.TryGetValue(key, out var v) ? v : null;
            }
        }

        /// <summary>Element by index, or null when out of range or not an array.</summary>
        public JsonValue this[int index] =>
            Type == Kind.Array && index >= 0 && index < Array.Count ? Array[index] : null;

        public int Count => Type == Kind.Array ? Array.Count
                          : Type == Kind.Object ? Object.Count : 0;

        public string AsString(string fallback = null) => Type == Kind.String ? String : fallback;

        /// <summary>Numbers arrive as double; atlas rects and sizes are all integral.</summary>
        public int AsInt(int fallback = 0) =>
            Type == Kind.Number ? (int)System.Math.Round(Number) : fallback;

        public double AsDouble(double fallback = 0) => Type == Kind.Number ? Number : fallback;
        public bool AsBool(bool fallback = false) => Type == Kind.Bool ? Bool : fallback;

        public static JsonValue Parse(string text)
        {
            if (text == null)
                throw new JsonException("input is null");
            int i = 0;
            var v = ParseValue(text, ref i);
            SkipWhitespace(text, ref i);
            if (i != text.Length)
                throw new JsonException($"trailing content at {i}");
            return v;
        }

        /// <summary>Convenience for the common flat string-to-string case.</summary>
        public Dictionary<string, string> ToStringMap()
        {
            var map = new Dictionary<string, string>(Count, StringComparer.Ordinal);
            if (Type != Kind.Object)
                return map;
            foreach (var kv in Object)
                if (kv.Value != null && kv.Value.Type == Kind.String)
                    map[kv.Key] = kv.Value.String;
            return map;
        }

        // ---------- parser ----------

        static JsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
                throw new JsonException("unexpected end of input");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonValue { Type = Kind.String, String = ParseString(s, ref i) };
                case 't': Expect(s, ref i, "true"); return new JsonValue { Type = Kind.Bool, Bool = true };
                case 'f': Expect(s, ref i, "false"); return new JsonValue { Type = Kind.Bool, Bool = false };
                case 'n': Expect(s, ref i, "null"); return new JsonValue { Type = Kind.Null };
                default: return ParseNumber(s, ref i);
            }
        }

        static JsonValue ParseObject(string s, ref int i)
        {
            i++; // '{'
            var obj = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return new JsonValue { Type = Kind.Object, Object = obj }; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new JsonException($"expected key string at {i}");
                string key = ParseString(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new JsonException($"expected ':' at {i}");
                i++;

                // Last duplicate key wins, matching every mainstream parser.
                obj[key] = ParseValue(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    throw new JsonException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new JsonException($"expected ',' or '}}' at {i}");
            }
            return new JsonValue { Type = Kind.Object, Object = obj };
        }

        static JsonValue ParseArray(string s, ref int i)
        {
            i++; // '['
            var list = new List<JsonValue>();
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return new JsonValue { Type = Kind.Array, Array = list }; }

            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    throw new JsonException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new JsonException($"expected ',' or ']' at {i}");
            }
            return new JsonValue { Type = Kind.Array, Array = list };
        }

        static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length)
                    throw new JsonException("unterminated string");
                char c = s[i++];
                if (c == '"')
                    break;
                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length)
                    throw new JsonException("unterminated escape");
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
                        if (i + 4 > s.Length)
                            throw new JsonException("truncated \\u escape");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber,
                                                     CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default:
                        throw new JsonException($"bad escape '\\{e}' at {i - 1}");
                }
            }
            return sb.ToString();
        }

        static JsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E'
                                    || ((s[i] == '-' || s[i] == '+') && (s[i - 1] == 'e' || s[i - 1] == 'E'))))
                i++;
            if (i == start)
                throw new JsonException($"unexpected character '{s[start]}' at {start}");

            string span = s.Substring(start, i - start);
            if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new JsonException($"bad number '{span}' at {start}");
            return new JsonValue { Type = Kind.Number, Number = d };
        }

        static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new JsonException($"expected '{literal}' at {i}");
            i += literal.Length;
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }
}
