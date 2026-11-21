using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Random endless waves: size grows every wave with a bigger jump every 5th, every enemy needs one more hit
    /// every 10 waves, kinds are random from wave 1, a seed replays exactly, and the sim spawns them on any map.</summary>
    public class RandomWavesTests
    {
        private static Func<float> Rng(int seed)
        {
            var r = new Random(seed);
            return () => (float)r.NextDouble();
        }

        [Test]
        public void Count_GrowsEveryWave_WithABiggerJumpEveryFifth()
        {
            var c = new RedlineConfig();
            for (int w = 2; w <= 30; w++)
            {
                int step = RandomWaves.Count(w, c) - RandomWaves.Count(w - 1, c);
                int expected = w % c.randomWaveBigEvery == 0 ? c.randomWavePerWave + c.randomWaveBigStep : c.randomWavePerWave;
                if (RandomWaves.Count(w, c) < c.randomWaveMax) Assert.AreEqual(expected, step, $"wave {w}");
            }
            Assert.AreEqual(c.randomWaveBase, RandomWaves.Count(1, c));
        }

        [TestCase(1, 0f)]
        [TestCase(10, 0f)]
        [TestCase(11, 1f)]
        [TestCase(20, 1f)]
        [TestCase(21, 2f)]
        public void HpBonus_OneMoreHitEveryTenWaves(int wave, float hits)
        {
            var c = new RedlineConfig();
            Assert.AreEqual(hits * c.randomWaveHpPerStep, RandomWaves.HpBonus(wave, c), 1e-5f);
        }

        [Test]
        public void Generate_SizeWithinJitter_AndSameSeedReplays()
        {
            var c = new RedlineConfig();
            for (int w = 1; w <= 25; w++)
            {
                var a = RandomWaves.Generate(w, Rng(7 + w), c);
                var b = RandomWaves.Generate(w, Rng(7 + w), c);
                Assert.That(a.Total, Is.InRange(RandomWaves.Count(w, c), RandomWaves.Count(w, c) + c.randomWaveJitter), $"wave {w}");
                for (int k = 0; k < WaveSpec.KindCount; k++) Assert.AreEqual(a[(WaveEnemy)k], b[(WaveEnemy)k], $"wave {w} kind {k}");
            }
        }

        [Test]
        public void Generate_KindsAreRandomFromWaveOne()
        {
            var c = new RedlineConfig();
            var seen = new bool[WaveSpec.KindCount];
            for (int s = 0; s < 200; s++)
            {
                var wave = RandomWaves.Generate(1, Rng(s), c);
                for (int k = 0; k < WaveSpec.KindCount; k++) if (wave[(WaveEnemy)k] > 0) seen[k] = true;
            }
            for (int k = 0; k < WaveSpec.KindCount; k++) Assert.IsTrue(seen[k], $"{(WaveEnemy)k} never rolled in wave 1");
        }

        [Test]
        public void Sim_RandomMode_SpawnsTheRolledWave_AndIsEndless()
        {
            var cfg = new RedlineConfig { randomWaves = 1, firstWaveDelay = 0.01f, rngSeed = 42 };
            var sim = new RedlineSim(cfg, Box(80, 14));
            for (int i = 0; i < 10 && sim.State != SimState.Fighting; i++) sim.Step(new InputFrame(), Dt);
            Assert.AreEqual(SimState.Fighting, sim.State);
            Assert.IsTrue(sim.Endless, "random waves never end in a victory");
            Assert.That(sim.Enemies.Count, Is.InRange(RandomWaves.Count(1, cfg), RandomWaves.Count(1, cfg) + cfg.randomWaveJitter));
        }

        [Test]
        public void AddMaxHp_RaisesMaxAndRefills()
        {
            var cfg = NoWaves();
            var sim = new RedlineSim(cfg, Box(80, 14));
            var e = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(20f, 1f));
            float before = e.MaxHp;
            e.AddMaxHp(1f);
            Assert.AreEqual(before + 1f, e.MaxHp, 1e-5f);
            Assert.AreEqual(e.MaxHp, e.Hp, 1e-5f);
        }
    }
}
