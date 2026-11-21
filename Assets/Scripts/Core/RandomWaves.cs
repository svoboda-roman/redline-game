using System;

namespace Redline.Core
{
    /// <summary>
    /// Random endless waves (<see cref="RedlineConfig.randomWaves"/> = 1, every run started from the menu): each wave rolls
    /// its size and its enemy kinds from the sim's seeded RNG, so no two runs are alike but a seed replays exactly.
    /// <list type="bullet">
    /// <item>Size: grows every wave, and every <see cref="RedlineConfig.randomWaveBigEvery"/>-th wave jumps further
    /// (<see cref="Count"/>), plus 0..<see cref="RedlineConfig.randomWaveJitter"/> extra, capped.</item>
    /// <item>Kinds: grunts always; brutes, drones, shooters, shields, kamikazes and summoners unlock at their wave; a
    /// grunt share, the rest spread evenly over the unlocked kinds.</item>
    /// <item>Toughness: every <see cref="RedlineConfig.randomWaveHpEvery"/> waves every enemy needs one more tier-1
    /// katana hit (<see cref="HpBonus"/>).</item>
    /// </list>
    /// </summary>
    /// <example>
    /// <code>
    /// var spec = RandomWaves.Generate(7, rng.NextFloat, cfg);   // wave 7
    /// float extra = RandomWaves.HpBonus(21, cfg);              // +2 hits from wave 21
    /// </code>
    /// </example>
    public static class RandomWaves
    {
        /// <summary>Base size of 1-based wave <paramref name="wave"/> before the random extra.</summary>
        public static int Count(int wave, RedlineConfig c)
        {
            int n = Math.Max(1, wave);
            int every = Math.Max(1, c.randomWaveBigEvery);
            int count = c.randomWaveBase + (n - 1) * c.randomWavePerWave + (n / every) * c.randomWaveBigStep;
            return Math.Max(1, Math.Min(c.randomWaveMax, count));
        }

        /// <summary>Extra HP every enemy of 1-based wave <paramref name="wave"/> gets (whole hits, 0 for waves 1..HpEvery).</summary>
        public static float HpBonus(int wave, RedlineConfig c)
        {
            int every = Math.Max(1, c.randomWaveHpEvery);
            return ((Math.Max(1, wave) - 1) / every) * c.randomWaveHpPerStep;
        }

        /// <summary>The wave at which <paramref name="kind"/> may first appear.</summary>
        public static int UnlockWave(WaveEnemy kind, RedlineConfig c)
        {
            switch (kind)
            {
                case WaveEnemy.Grunt: return 1;
                case WaveEnemy.Brute: return c.randomUnlockBrute;
                case WaveEnemy.Drone: return c.randomUnlockDrone;
                case WaveEnemy.Shooter: return c.randomUnlockShooter;
                case WaveEnemy.Shield: return c.randomUnlockShield;
                case WaveEnemy.Kamikaze: return c.randomUnlockKamikaze;
                case WaveEnemy.Summoner: return c.randomUnlockSummoner;
                default: return int.MaxValue;
            }
        }

        /// <summary>
        /// Rolls 1-based wave <paramref name="wave"/>: its size (<see cref="Count"/> plus a random extra) and a random kind
        /// per enemy. <paramref name="next"/> returns uniform values in [0, 1).
        /// </summary>
        public static WaveSpec Generate(int wave, Func<float> next, RedlineConfig c)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (c == null) throw new ArgumentNullException(nameof(c));
            int total = Count(wave, c) + Math.Min(c.randomWaveJitter, (int)(next() * (c.randomWaveJitter + 1)));
            total = Math.Max(1, Math.Min(c.randomWaveMax, total));
            var counts = new int[WaveSpec.KindCount];
            var unlocked = new int[WaveSpec.KindCount];
            int u = 0;
            for (int k = 1; k < WaveSpec.KindCount; k++)
                if (wave >= UnlockWave((WaveEnemy)k, c)) unlocked[u++] = k;
            for (int i = 0; i < total; i++)
            {
                if (u == 0 || next() < c.randomGruntShare) { counts[(int)WaveEnemy.Grunt]++; continue; }
                counts[unlocked[Math.Min(u - 1, (int)(next() * u))]]++;
            }
            return new WaveSpec(counts);
        }
    }
}
