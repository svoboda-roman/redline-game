using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>
    /// Enemy kinds a wave can ask for, by their <c>;wave</c> key (the lower-case name). Each maps to
    /// an <see cref="EnemyKind"/> through <c>RedlineSim.TryGetEnemyKind</c>; a kind added here
    /// without a mapping is rejected by <see cref="RedlineSim"/> when a level asks for it.
    /// </summary>
    public enum WaveEnemy
    {
        Grunt,
        Brute,
        Drone,
        Shooter,
        Shield,
        Kamikaze,
        Summoner
    }

    /// <summary>How many enemies of each <see cref="WaveEnemy"/> one wave spawns.</summary>
    /// <example>
    /// <code>
    /// var wave = WaveSpec.Parse(";wave grunt=3 brute=1");
    /// int brutes = wave[WaveEnemy.Brute]; // 1
    /// </code>
    /// </example>
    public sealed class WaveSpec
    {
        /// <summary>Number of <see cref="WaveEnemy"/> values (the length of a count table).</summary>
        public static readonly int KindCount = Enum.GetValues(typeof(WaveEnemy)).Length;

        private static readonly string[] Keys = BuildKeys();

        private readonly int[] _counts;

        /// <summary>Creates a wave from counts indexed by <see cref="WaveEnemy"/> (missing entries are 0).</summary>
        public WaveSpec(params int[] counts)
        {
            _counts = new int[KindCount];
            if (counts != null) Array.Copy(counts, _counts, Math.Min(counts.Length, KindCount));
        }

        /// <summary>Count of one kind in this wave.</summary>
        public int this[WaveEnemy kind] => _counts[(int)kind];

        /// <summary>Total enemies in the wave.</summary>
        public int Total
        {
            get
            {
                int t = 0;
                for (int i = 0; i < _counts.Length; i++) t += _counts[i];
                return t;
            }
        }

        /// <summary>The <c>;wave</c> key of a kind ("grunt", "brute", ...).</summary>
        public static string KeyOf(WaveEnemy kind) => Keys[(int)kind];

        /// <summary>
        /// Parses one directive line: <c>;wave grunt=3 brute=1 drone=0 shooter=0 shield=0 kamikaze=0 summoner=0</c>.
        /// Omitted kinds are 0. Throws FormatException on an unknown key, a repeated key, a malformed
        /// or negative count, or a wave with no enemies.
        /// </summary>
        public static WaveSpec Parse(string line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));
            var parts = line.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] != ";wave") throw new FormatException($"not a ;wave line: '{line}'");
            var counts = new int[KindCount];
            var seen = new bool[KindCount];
            for (int i = 1; i < parts.Length; i++)
            {
                string token = parts[i];
                int eq = token.IndexOf('=');
                if (eq <= 0 || eq == token.Length - 1)
                    throw new FormatException($"wave entry '{token}' must look like kind=count");
                string key = token.Substring(0, eq);
                int k = Array.IndexOf(Keys, key);
                if (k < 0)
                    throw new FormatException($"unknown wave key '{key}' (known: {string.Join(", ", Keys)})");
                if (seen[k]) throw new FormatException($"wave key '{key}' appears twice");
                if (!int.TryParse(token.Substring(eq + 1), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int n))
                    throw new FormatException($"wave count '{token.Substring(eq + 1)}' for '{key}' must be a whole number >= 0");
                seen[k] = true;
                counts[k] = n;
            }
            var wave = new WaveSpec(counts);
            if (wave.Total == 0) throw new FormatException("a wave needs at least one enemy");
            return wave;
        }

        private static string[] BuildKeys()
        {
            var names = Enum.GetNames(typeof(WaveEnemy));
            var keys = new string[names.Length];
            for (int i = 0; i < names.Length; i++) keys[i] = names[i].ToLowerInvariant(); // GetNames is in value order
            return keys;
        }

        /// <summary>The config's global waves (waveGrunts/waveBrutes/waveDrones) as specs.</summary>
        public static List<WaveSpec> FromConfig(RedlineConfig cfg)
        {
            var list = new List<WaveSpec>(cfg.WaveCount);
            for (int i = 0; i < cfg.WaveCount; i++)
                list.Add(new WaveSpec(cfg.waveGrunts[i], cfg.waveBrutes[i], cfg.waveDrones[i]));
            return list;
        }
    }
}
