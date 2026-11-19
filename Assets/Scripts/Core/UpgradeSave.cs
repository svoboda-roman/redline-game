using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Redline.Core
{
    /// <summary>How a save string was read.</summary>
    public enum SaveLoadStatus
    {
        /// <summary>Read as written.</summary>
        Ok,
        /// <summary>No save (null or blank): fresh defaults, nothing lost.</summary>
        Missing,
        /// <summary>Not valid JSON or not a save object: defaults.</summary>
        Corrupt,
        /// <summary>A version this build cannot read (older without migration, or newer): defaults.</summary>
        UnsupportedVersion,
        /// <summary>Read, but some fields were invalid and fell back to defaults (or were clamped).</summary>
        Repaired
    }

    /// <summary>
    /// Engine-free JSON serialisation of <see cref="UpgradeState"/>. Format (version 1):
    /// <c>{"version":1,"points":120,"levels":{"MaxHp":1,"Speed":0,"Hook":2,"SwordAndOrbs":0}}</c>.
    /// Reading never throws: missing, corrupt or unsupported data gives a fresh state and a status
    /// the caller can log (and use to back up the bad file).
    /// </summary>
    /// <example>
    /// <code>
    /// string json = UpgradeSave.Serialize(state);
    /// var loaded = UpgradeSave.Deserialize(json, upgradeCfg, out var status);
    /// </code>
    /// </example>
    public static class UpgradeSave
    {
        /// <summary>Version written by <see cref="Serialize"/>.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Writes the state as JSON (invariant culture, stable key order).</summary>
        public static string Serialize(UpgradeState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var sb = new StringBuilder(128);
            sb.Append("{\"version\":").Append(CurrentVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"points\":").Append(state.Points.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"spins\":").Append(state.Spins.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"levels\":{");
            for (int i = 0; i < UpgradeState.Kinds.Length; i++)
            {
                var kind = UpgradeState.Kinds[i];
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(kind.ToString()).Append("\":")
                  .Append(state.LevelOf(kind).ToString(CultureInfo.InvariantCulture));
            }
            sb.Append("}}");
            return sb.ToString();
        }

        /// <summary>
        /// Reads a save. Never throws. Levels are clamped to the config's max levels; unknown upgrade
        /// names are ignored; an invalid field falls back to its default (status Repaired).
        /// </summary>
        public static UpgradeState Deserialize(string json, UpgradeConfig cfg, out SaveLoadStatus status)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            var state = new UpgradeState();
            if (string.IsNullOrWhiteSpace(json))
            {
                status = SaveLoadStatus.Missing;
                return state;
            }

            Dictionary<string, object> root;
            try
            {
                root = new MiniJson(json).ParseDocument() as Dictionary<string, object>;
            }
            catch (FormatException)
            {
                root = null;
            }
            if (root == null)
            {
                status = SaveLoadStatus.Corrupt;
                return state;
            }

            if (!root.TryGetValue("version", out var v) || !TryInt(v, out int version))
            {
                status = SaveLoadStatus.Corrupt;
                return state;
            }
            // Future versions add a migration step here (e.g. version 1 -> 2) before reading.
            if (version != CurrentVersion)
            {
                status = SaveLoadStatus.UnsupportedVersion;
                return state;
            }

            bool repaired = false;
            if (root.TryGetValue("points", out var p) && TryInt(p, out int points) && points >= 0)
                state.SetPoints(points);
            else
                repaired = true;
            // Optional (older saves have no spins: the random upgrade price starts at its base).
            if (root.TryGetValue("spins", out var sp) && TryInt(sp, out int spins) && spins >= 0)
                state.SetSpins(spins);

            if (root.TryGetValue("levels", out var l) && l is Dictionary<string, object> levels)
            {
                foreach (var kind in UpgradeState.Kinds)
                {
                    if (!levels.TryGetValue(kind.ToString(), out var raw)) continue; // not bought yet / added later
                    if (TryInt(raw, out int level) && level >= 0)
                    {
                        state.SetLevel(kind, level, cfg);
                        if (state.LevelOf(kind) != level) repaired = true; // clamped to max
                    }
                    else repaired = true;
                }
            }
            else repaired = true;

            status = repaired ? SaveLoadStatus.Repaired : SaveLoadStatus.Ok;
            return state;
        }

        private static bool TryInt(object value, out int result)
        {
            result = 0;
            if (!(value is double d) || d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue) return false;
            result = (int)d;
            return true;
        }

        /// <summary>
        /// Minimal strict JSON reader: objects (Dictionary), arrays (List), numbers (double), strings,
        /// true/false/null. Throws FormatException on anything else, including trailing garbage.
        /// </summary>
        private sealed class MiniJson
        {
            private const int MaxDepth = 32;
            private readonly string _s;
            private int _i;

            public MiniJson(string s) { _s = s; }

            public object ParseDocument()
            {
                object value = ParseValue(0);
                SkipWhitespace();
                if (_i != _s.Length) throw Error("trailing characters");
                return value;
            }

            private object ParseValue(int depth)
            {
                if (depth > MaxDepth) throw Error("nested too deep");
                SkipWhitespace();
                if (_i >= _s.Length) throw Error("unexpected end");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error("unexpected character");
                }
            }

            private Dictionary<string, object> ParseObject(int depth)
            {
                var obj = new Dictionary<string, object>();
                _i++; // {
                SkipWhitespace();
                if (Peek() == '}') { _i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Error("expected a key");
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("expected ':'");
                    _i++;
                    obj[key] = ParseValue(depth + 1);
                    SkipWhitespace();
                    char c = Peek();
                    _i++;
                    if (c == '}') return obj;
                    if (c != ',') throw Error("expected ',' or '}'");
                }
            }

            private List<object> ParseArray(int depth)
            {
                var list = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (Peek() == ']') { _i++; return list; }
                while (true)
                {
                    list.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    char c = Peek();
                    _i++;
                    if (c == ']') return list;
                    if (c != ',') throw Error("expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                _i++; // opening quote
                while (true)
                {
                    if (_i >= _s.Length) throw Error("unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ') throw Error("control character in string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) throw Error("unterminated escape");
                    char e = _s[_i++];
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
                            if (_i + 4 > _s.Length
                                || !int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                                throw Error("bad unicode escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw Error("bad escape");
                    }
                }
            }

            private double ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                    || double.IsNaN(d) || double.IsInfinity(d))
                    throw Error("bad number");
                return d;
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error("unexpected token");
                _i += word.Length;
            }

            private char Peek() => _i < _s.Length ? _s[_i] : '\0';

            private void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            private FormatException Error(string what) => new FormatException($"save JSON: {what} at {_i}");
        }
    }
}
