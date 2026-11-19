using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Redline.Core
{
    /// <summary>
    /// The player's settings: volumes in percent (0-100), display and hint toggles, and the
    /// keyboard bindings. Plain data; the game layer applies it.
    /// </summary>
    public sealed class SettingsData
    {
        /// <summary>Master volume, percent 0-100.</summary>
        public int MasterVolume = 100;
        /// <summary>Music volume, percent 0-100.</summary>
        public int MusicVolume = 100;
        /// <summary>Sound-effect volume, percent 0-100.</summary>
        public int SfxVolume = 100;
        /// <summary>Fullscreen window.</summary>
        public bool Fullscreen = true;
        /// <summary>Show tutorial hints.</summary>
        public bool HintsEnabled = true;
        /// <summary>Tutorial hints already shown (comma-separated hint ids, see TutorialProgress).</summary>
        public string TutorialSeen = "";
        /// <summary>
        /// True once the training level was finished or skipped; while false the game starts it on
        /// launch. Optional in the file: absent (older files, no file) reads as false.
        /// </summary>
        public bool TrainingDone;
        /// <summary>
        /// Graphics: true = Classic 2D view, false = Cinematic 3D (the default). Optional in the file:
        /// absent (older files, no file) reads as false.
        /// </summary>
        public bool Classic2D;
        /// <summary>
        /// Reduced Motion: UI transitions, blinks and flashes are replaced by steady or instant states.
        /// Optional in the file: absent reads as false.
        /// </summary>
        public bool ReducedMotion;
        /// <summary>
        /// HUD text scale in percent: 100, 125 or 150. Optional in the file: absent reads as 100;
        /// any other value is repaired to 100.
        /// </summary>
        public int HudTextScale = 100;
        /// <summary>Keyboard bindings.</summary>
        public readonly ControlBindings Bindings = ControlBindings.CreateDefaults();

        /// <summary>Copies every value from another instance.</summary>
        public void CopyFrom(SettingsData other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            MasterVolume = other.MasterVolume;
            MusicVolume = other.MusicVolume;
            SfxVolume = other.SfxVolume;
            Fullscreen = other.Fullscreen;
            HintsEnabled = other.HintsEnabled;
            TutorialSeen = other.TutorialSeen ?? "";
            TrainingDone = other.TrainingDone;
            Classic2D = other.Classic2D;
            ReducedMotion = other.ReducedMotion;
            HudTextScale = other.HudTextScale;
            Bindings.CopyFrom(other.Bindings);
        }
    }

    /// <summary>
    /// Engine-free JSON for <see cref="SettingsData"/>. Format (version 1):
    /// <c>{"version":1,"master":100,"music":80,"sfx":100,"fullscreen":true,"hints":true,
    /// "bindings":{"MoveLeft":["A","LeftArrow"],...,"Pause":["Escape"]}}</c>.
    /// Reading never throws: missing or corrupt data gives defaults; a bad field falls back to its
    /// default (status <see cref="SaveLoadStatus.Repaired"/>). Bindings are all-or-nothing: an unknown
    /// key, a duplicate or an action without keys resets every binding to the defaults.
    /// </summary>
    /// <example>
    /// <code>
    /// string json = SettingsSave.Serialize(settings);
    /// var loaded = SettingsSave.Deserialize(json, IsKnownKey, out var status);
    /// </code>
    /// </example>
    public static class SettingsSave
    {
        /// <summary>Version written by <see cref="Serialize"/>.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Writes the settings as JSON (invariant culture, stable key order).</summary>
        public static string Serialize(SettingsData s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var sb = new StringBuilder(512);
            sb.Append("{\"version\":").Append(CurrentVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"master\":").Append(s.MasterVolume.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"music\":").Append(s.MusicVolume.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"sfx\":").Append(s.SfxVolume.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"fullscreen\":").Append(s.Fullscreen ? "true" : "false");
            sb.Append(",\"hints\":").Append(s.HintsEnabled ? "true" : "false");
            sb.Append(",\"tutorialSeen\":");
            AppendString(sb, s.TutorialSeen ?? "");
            sb.Append(",\"trainingDone\":").Append(s.TrainingDone ? "true" : "false");
            sb.Append(",\"classic2d\":").Append(s.Classic2D ? "true" : "false");
            sb.Append(",\"reducedMotion\":").Append(s.ReducedMotion ? "true" : "false");
            sb.Append(",\"hudTextScale\":").Append(s.HudTextScale.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"bindings\":{");
            var actions = ControlBindings.Actions;
            for (int i = 0; i < actions.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(actions[i].ToString()).Append("\":[");
                bool first = true;
                for (int slot = 0; slot < ControlBindings.SlotsPerAction; slot++)
                {
                    string key = s.Bindings.Get(actions[i], slot);
                    if (key == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    AppendString(sb, key);
                }
                sb.Append(']');
            }
            sb.Append("}}");
            return sb.ToString();
        }

        /// <summary>
        /// Reads settings. Never throws.
        /// </summary>
        /// <param name="json">The file text (null or blank means no file).</param>
        /// <param name="isKnownKey">Validates key names (the game passes the Input System check); null accepts any non-empty name.</param>
        /// <param name="status">How the data was read.</param>
        public static SettingsData Deserialize(string json, Func<string, bool> isKnownKey, out SaveLoadStatus status)
        {
            var s = new SettingsData();
            if (string.IsNullOrWhiteSpace(json))
            {
                status = SaveLoadStatus.Missing;
                return s;
            }
            Dictionary<string, object> root;
            try
            {
                root = new Json(json).ParseDocument() as Dictionary<string, object>;
            }
            catch (FormatException)
            {
                root = null;
            }
            if (root == null)
            {
                status = SaveLoadStatus.Corrupt;
                return s;
            }
            if (!root.TryGetValue("version", out var v) || !(v is double ver) || ver != CurrentVersion)
            {
                status = SaveLoadStatus.UnsupportedVersion;
                return s;
            }

            bool repaired = false;
            s.MasterVolume = ReadPercent(root, "master", s.MasterVolume, ref repaired);
            s.MusicVolume = ReadPercent(root, "music", s.MusicVolume, ref repaired);
            s.SfxVolume = ReadPercent(root, "sfx", s.SfxVolume, ref repaired);
            s.Fullscreen = ReadBool(root, "fullscreen", s.Fullscreen, ref repaired);
            s.HintsEnabled = ReadBool(root, "hints", s.HintsEnabled, ref repaired);
            // Optional (added after version 1 shipped): absent means nothing seen yet, not a repair.
            if (root.TryGetValue("tutorialSeen", out var seen))
            {
                if (seen is string text) s.TutorialSeen = text;
                else repaired = true;
            }
            // Optional too: absent means the training has not been played yet.
            if (root.TryGetValue("trainingDone", out var done))
            {
                if (done is bool d) s.TrainingDone = d;
                else repaired = true;
            }
            // Optional too: absent means the default Cinematic 3D view.
            if (root.TryGetValue("classic2d", out var classic))
            {
                if (classic is bool c) s.Classic2D = c;
                else repaired = true;
            }
            // Optional too: Reduced Motion and the HUD text scale (100/125/150).
            if (root.TryGetValue("reducedMotion", out var rm))
            {
                if (rm is bool r) s.ReducedMotion = r;
                else repaired = true;
            }
            if (root.TryGetValue("hudTextScale", out var ts))
            {
                if (ts is double td && (td == 100d || td == 125d || td == 150d)) s.HudTextScale = (int)td;
                else repaired = true;
            }
            if (!ReadBindings(root, s.Bindings, isKnownKey))
            {
                s.Bindings.ResetToDefaults();
                repaired = true;
            }
            status = repaired ? SaveLoadStatus.Repaired : SaveLoadStatus.Ok;
            return s;
        }

        private static bool ReadBindings(Dictionary<string, object> root, ControlBindings target, Func<string, bool> isKnownKey)
        {
            if (!root.TryGetValue("bindings", out var b)) return false;
            if (!(b is Dictionary<string, object> map)) return false;
            var actions = ControlBindings.Actions;
            for (int i = 0; i < actions.Length; i++)
            {
                if (!map.TryGetValue(actions[i].ToString(), out var raw) || !(raw is List<object> list)) return false;
                if (list.Count == 0 || list.Count > ControlBindings.SlotsPerAction) return false;
                var keys = new string[list.Count];
                for (int k = 0; k < list.Count; k++)
                {
                    if (!(list[k] is string key) || key.Length == 0 || key.Length > 32) return false;
                    if (isKnownKey != null && !isKnownKey(key)) return false;
                    keys[k] = key;
                }
                target.SetRaw(actions[i], keys);
            }
            return target.IsValid();
        }

        private static int ReadPercent(Dictionary<string, object> root, string name, int fallback, ref bool repaired)
        {
            if (!root.TryGetValue(name, out var v)) { repaired = true; return fallback; }
            if (!(v is double d) || double.IsNaN(d)) { repaired = true; return fallback; }
            if (d < 0 || d > 100) repaired = true;
            return (int)Math.Round(Math.Max(0, Math.Min(100, d)));
        }

        private static bool ReadBool(Dictionary<string, object> root, string name, bool fallback, ref bool repaired)
        {
            if (root.TryGetValue(name, out var v) && v is bool b) return b;
            repaired = true;
            return fallback;
        }

        private static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append('"');
        }

        /// <summary>Minimal JSON reader: objects, arrays, strings, numbers, true/false/null. Throws FormatException.</summary>
        private sealed class Json
        {
            private const int MaxDepth = 16;
            private readonly string _s;
            private int _i;

            public Json(string s) { _s = s; }

            public object ParseDocument()
            {
                object v = Value(0);
                Skip();
                if (_i != _s.Length) throw new FormatException("trailing data");
                return v;
            }

            private object Value(int depth)
            {
                if (depth > MaxDepth) throw new FormatException("too deep");
                Skip();
                if (_i >= _s.Length) throw new FormatException("unexpected end");
                char c = _s[_i];
                if (c == '{') return Obj(depth);
                if (c == '[') return Arr(depth);
                if (c == '"') return Str();
                if (Lit("true")) return true;
                if (Lit("false")) return false;
                if (Lit("null")) return null;
                return Num();
            }

            private Dictionary<string, object> Obj(int depth)
            {
                var d = new Dictionary<string, object>();
                _i++;
                Skip();
                if (Peek() == '}') { _i++; return d; }
                while (true)
                {
                    Skip();
                    if (Peek() != '"') throw new FormatException("key expected");
                    string k = Str();
                    Skip();
                    if (Peek() != ':') throw new FormatException("':' expected");
                    _i++;
                    d[k] = Value(depth + 1);
                    Skip();
                    char c = Peek();
                    _i++;
                    if (c == '}') return d;
                    if (c != ',') throw new FormatException("',' expected");
                }
            }

            private List<object> Arr(int depth)
            {
                var l = new List<object>();
                _i++;
                Skip();
                if (Peek() == ']') { _i++; return l; }
                while (true)
                {
                    l.Add(Value(depth + 1));
                    Skip();
                    char c = Peek();
                    _i++;
                    if (c == ']') return l;
                    if (c != ',') throw new FormatException("',' expected");
                }
            }

            private string Str()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) break;
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': case '\\': case '/': sb.Append(e); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new FormatException("bad escape");
                            if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw new FormatException("bad escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default: throw new FormatException("bad escape");
                    }
                }
                throw new FormatException("unterminated string");
            }

            private double Num()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                if (_i == start) throw new FormatException("value expected");
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new FormatException("bad number");
                return d;
            }

            private bool Lit(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return false;
                _i += word.Length;
                return true;
            }

            private char Peek() => _i < _s.Length ? _s[_i] : '\0';

            private void Skip()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }
        }
    }
}
