using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Redline.Core
{
    /// <summary>
    /// A small JSON reader for engine-free data files (Unity's JsonUtility is not available in Core).
    /// Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double,
    /// plus string, bool and null. Throws FormatException with a character offset on bad input.
    /// </summary>
    public static class MiniJson
    {
        /// <summary>Parses one JSON value that spans the whole text.</summary>
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int i = 0;
            var value = Value(text, ref i);
            Skip(text, ref i);
            if (i < text.Length) throw Err(i, "unexpected text after the JSON value");
            return value;
        }

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw Err(i, "unexpected end of JSON");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            return Num(s, ref i);
        }

        private static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Skip(s, ref i);
                if (i >= s.Length || s[i] != '"') throw Err(i, "expected a quoted key");
                string key = Str(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':') throw Err(i, "expected ':'");
                i++;
                if (d.ContainsKey(key)) throw Err(i, $"duplicate key '{key}'");
                d[key] = Value(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw Err(i, "expected ',' or '}'");
            }
        }

        private static List<object> Arr(string s, ref int i)
        {
            var list = new List<object>();
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(Value(s, ref i));
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return list; }
                throw Err(i, "expected ',' or ']'");
            }
        }

        private static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw Err(i, "bad \\u escape");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw Err(i - 1, $"bad escape '\\{e}'");
                }
            }
            throw Err(i, "unterminated string");
        }

        private static double Num(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                throw Err(start, "expected a value");
            return v;
        }

        private static bool Word(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static FormatException Err(int at, string message) => new FormatException($"JSON offset {at}: {message}");
    }
}
