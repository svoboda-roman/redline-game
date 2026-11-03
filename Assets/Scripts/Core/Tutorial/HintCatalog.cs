using System;
using System.Collections.Generic;
using System.Globalization;

namespace Redline.Core
{
    /// <summary>How often a hint may show.</summary>
    public enum HintScope
    {
        /// <summary>Once per save: remembered in <see cref="TutorialProgress"/>.</summary>
        Save,
        /// <summary>Once per run: forgotten by <see cref="TutorialHints.BeginRun"/> (Loop01's zone hints).</summary>
        Run
    }

    /// <summary>One hint's text from Hints.json. Title and body may hold control tokens such as {HOOK}.</summary>
    public sealed class HintEntry
    {
        /// <summary>Hint id (zone ids come from ';hint' lines; card ids from <see cref="HintIds"/>).</summary>
        public string Id { get; }
        /// <summary>Short headline, e.g. "BRUTE".</summary>
        public string Title { get; }
        /// <summary>One or two sentences on what to do.</summary>
        public string Body { get; }
        /// <summary>Once per save or once per run.</summary>
        public HintScope Scope { get; }

        /// <summary>Creates an entry.</summary>
        public HintEntry(string id, string title, string body, HintScope scope)
        {
            Id = id;
            Title = title;
            Body = body;
            Scope = scope;
        }
    }

    /// <summary>Timing and detection values for <see cref="TutorialHints"/> (the "settings" object in Hints.json).</summary>
    public sealed class TutorialSettings
    {
        /// <summary>Seconds a hint stays up before anything may replace it (or a zone hint may close on leaving).</summary>
        public float minDisplayTime = 2.5f;
        /// <summary>Seconds an enemy or orb card stays up when nothing replaces it.</summary>
        public float cardTime = 5.5f;
        /// <summary>Longest a zone hint stays up while the player stays in its zone.</summary>
        public float zoneMaxTime = 8f;
        /// <summary>First sight: largest horizontal distance (tiles, around the ring) to an enemy.</summary>
        public float sightRangeX = 11f;
        /// <summary>First sight: largest vertical distance (tiles) to an enemy.</summary>
        public float sightRangeY = 7f;
        /// <summary>First sight also needs a clear line from the player's centre to the enemy's.</summary>
        public bool requireLineOfSight = true;
        /// <summary>Cards waiting behind the active hint; a request beyond it is dropped (and asked again later).</summary>
        public int queueCapacity = 3;
    }

    /// <summary>Well-known hint ids for first-sight enemy cards and first-pickup orb cards.</summary>
    public static class HintIds
    {
        private static readonly string[] EnemyIds =
            { "enemy.grunt", "enemy.brute", "enemy.drone", "enemy.shooter", "enemy.shield", "enemy.kamikaze", "enemy.summoner" };
        private static readonly string[] OrbIds = { "orb.health", "orb.speed", "orb.hook" };

        /// <summary>Card id for an enemy kind ("enemy.brute").</summary>
        public static string Enemy(EnemyKind kind) => EnemyIds[(int)kind];
        /// <summary>Card id for an orb kind ("orb.speed").</summary>
        public static string Orb(OrbKind kind) => OrbIds[(int)kind];
        /// <summary>Every enemy and orb card id (Hints.json must define them all).</summary>
        public static IEnumerable<string> AllCards()
        {
            foreach (var id in EnemyIds) yield return id;
            foreach (var id in OrbIds) yield return id;
        }
    }

    /// <summary>
    /// Hint text and settings parsed from Resources/Hints.json:
    /// { "settings": { "minDisplayTime": 2.5, ... }, "hints": [ { "id": "l1.hook", "title": "...", "body": "...", "scope": "run" } ] }.
    /// "scope" is "save" (default) or "run"; "settings" may be omitted or partial (defaults above).
    /// </summary>
    public sealed class HintCatalog
    {
        private readonly Dictionary<string, HintEntry> _byId;

        /// <summary>Timing and detection values.</summary>
        public TutorialSettings Settings { get; }

        /// <summary>Every hint id in file order.</summary>
        public IReadOnlyList<string> Ids { get; }

        private HintCatalog(Dictionary<string, HintEntry> byId, List<string> ids, TutorialSettings settings)
        {
            _byId = byId;
            Ids = ids;
            Settings = settings;
        }

        /// <summary>Looks up a hint; false when the id is not in the catalog.</summary>
        public bool TryGet(string id, out HintEntry entry)
        {
            if (id == null) { entry = null; return false; }
            return _byId.TryGetValue(id, out entry);
        }

        /// <summary>Parses Hints.json. Throws FormatException naming the bad hint or setting.</summary>
        public static HintCatalog Parse(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) throw new FormatException("Hints.json must be a JSON object");
            var settings = new TutorialSettings();
            if (root.TryGetValue("settings", out var s))
            {
                if (!(s is Dictionary<string, object> so)) throw new FormatException("'settings' must be an object");
                foreach (var kv in so) ApplySetting(settings, kv.Key, kv.Value);
                if (settings.cardTime < settings.minDisplayTime || settings.zoneMaxTime < settings.minDisplayTime)
                throw new FormatException("cardTime and zoneMaxTime must be at least minDisplayTime (" +
                                          settings.minDisplayTime.ToString(CultureInfo.InvariantCulture) + ")");
            }
            if (!root.TryGetValue("hints", out var h) || !(h is List<object> list)) throw new FormatException("Hints.json needs a 'hints' array");
            var byId = new Dictionary<string, HintEntry>();
            var ids = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Dictionary<string, object> o)) throw new FormatException($"hints[{i}] must be an object");
                string id = Field(o, "id", i, true);
                if (!HintZone.IsValidId(id)) throw new FormatException($"hints[{i}]: id '{id}' must use only a-z, 0-9, '.', '_' or '-'");
                if (byId.ContainsKey(id)) throw new FormatException($"hints[{i}]: id '{id}' is defined twice");
                string scopeText = Field(o, "scope", i, false) ?? "save";
                HintScope scope;
                if (scopeText == "save") scope = HintScope.Save;
                else if (scopeText == "run") scope = HintScope.Run;
                else throw new FormatException($"hint '{id}': scope must be 'save' or 'run', got '{scopeText}'");
                byId[id] = new HintEntry(id, Field(o, "title", i, true), Field(o, "body", i, true), scope);
                ids.Add(id);
            }
            return new HintCatalog(byId, ids, settings);
        }

        private static string Field(Dictionary<string, object> o, string key, int index, bool required)
        {
            if (!o.TryGetValue(key, out var v) || v == null)
            {
                if (required) throw new FormatException($"hints[{index}] has no '{key}'");
                return null;
            }
            if (!(v is string str) || (required && str.Trim().Length == 0)) throw new FormatException($"hints[{index}]: '{key}' must be a non-empty string");
            return str;
        }

        private static void ApplySetting(TutorialSettings t, string key, object value)
        {
            if (key == "requireLineOfSight")
            {
                if (!(value is bool b)) throw new FormatException("setting 'requireLineOfSight' must be true or false");
                t.requireLineOfSight = b;
                return;
            }
            if (!(value is double d) || !(d > 0)) throw new FormatException($"setting '{key}' must be a positive number");
            float f = (float)d;
            switch (key)
            {
                case "minDisplayTime": t.minDisplayTime = f; break;
                case "cardTime": t.cardTime = f; break;
                case "zoneMaxTime": t.zoneMaxTime = f; break;
                case "sightRangeX": t.sightRangeX = f; break;
                case "sightRangeY": t.sightRangeY = f; break;
                case "queueCapacity":
                    if (d != Math.Floor(d)) throw new FormatException("setting 'queueCapacity' must be a whole number");
                    t.queueCapacity = (int)d;
                    break;
                default: throw new FormatException($"unknown setting '{key}'");
            }
        }
    }
}
