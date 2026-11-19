using NUnit.Framework;
using Redline.Core;
using UnityEngine;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>City01: an ';endless' ';randomspawn' level — waves grow with the waves beaten, enemies spawn at random spots.</summary>
    public class EndlessLevelTests
    {
        [TestCase("City01")]
        [TestCase("City01B")]
        public void City01_ParsesAsEndlessRandomSpawnLevel(string level)
        {
            var map = TileMap.Parse(Resources.Load<TextAsset>(level).text);
            Assert.IsTrue(map.Endless);
            Assert.IsTrue(map.RandomSpawn);
            Assert.IsFalse(map.WrapsX);
            Assert.Greater(map.StandSpots.Count, 50);
            Assert.Less(map.PlayerStart.X, map.Width * 0.2f, "the player starts on the left");
        }

        [Test]
        public void EndlessWaves_GrowWithEveryWave()
        {
            var waves = WaveSpec.Endless(20);
            for (int i = 1; i < waves.Count; i++) Assert.GreaterOrEqual(waves[i].Total, waves[i - 1].Total);
            Assert.AreEqual(3, waves[0].Total);
            Assert.Greater(waves[10].Total, waves[2].Total);
            Assert.AreEqual(0, waves[0][WaveEnemy.Brute], "the first wave is grunts only");
            Assert.Greater(waves[10][WaveEnemy.Brute], 0, "heavier kinds join later");
        }

        [Test]
        public void EndlessLevel_SpawnsAwayFromThePlayer_AndNeverReachesVictory()
        {
            var cfg = new RedlineConfig();
            var sim = new RedlineSim(cfg, TileMap.Parse(Resources.Load<TextAsset>("City01").text));
            Assert.IsTrue(sim.Endless);
            Run(sim, Hold(0f), cfg.firstWaveDelay + 0.1f);
            Assert.AreEqual(SimState.Fighting, sim.State);
            Assert.AreEqual(3, sim.EnemiesAlive);
            foreach (var e in sim.Enemies)
                Assert.GreaterOrEqual(Mathf.Abs(sim.Map.Dx(sim.Player.X, e.X)), RedlineSim.RandomSpawnClearance - 0.01f);
            Assert.AreEqual(RedlineSim.EndlessWaveCount, sim.WaveCount);
        }
    }
}
