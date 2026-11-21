using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Per-level waves: ';wave' directive lines in a level file override the config's waves.</summary>
    public class LevelWaveTests
    {
        private const string Room =
            "##########################\n" +
            "#........................#\n" +
            "#........................#\n" +
            "#........................#\n" +
            "#..P...........S.....S...#\n" +
            "##########################\n";

        private static TileMap Level(params string[] directives) =>
            TileMap.Parse(string.Join("\n", directives) + "\n" + Room);

        [Test]
        public void WaveLines_AreParsedInOrder_OmittedKindsAreZero()
        {
            var map = Level(";wave grunt=3 brute=1", ";wave drone=2 grunt=1 shooter=0");

            Assert.AreEqual(2, map.Waves.Count);
            Assert.AreEqual(3, map.Waves[0][WaveEnemy.Grunt]);
            Assert.AreEqual(1, map.Waves[0][WaveEnemy.Brute]);
            Assert.AreEqual(0, map.Waves[0][WaveEnemy.Drone]);
            Assert.AreEqual(1, map.Waves[1][WaveEnemy.Grunt]);
            Assert.AreEqual(2, map.Waves[1][WaveEnemy.Drone]);
            Assert.AreEqual(0, map.Waves[1][WaveEnemy.Summoner]);
        }

        [Test]
        public void AllSevenKeys_AreAccepted()
        {
            var map = Level(";wave grunt=1 brute=2 drone=3 shooter=4 shield=5 kamikaze=6 summoner=7");

            for (int k = 0; k < WaveSpec.KindCount; k++)
                Assert.AreEqual(k + 1, map.Waves[0][(WaveEnemy)k], WaveSpec.KeyOf((WaveEnemy)k));
        }

        [Test]
        public void UnknownKey_IsAParseError_NamingTheKeyAndLine()
        {
            var ex = Assert.Throws<FormatException>(() => Level(";wrap", ";wave grunt=2 ninja=1"));

            StringAssert.Contains("ninja", ex.Message);
            StringAssert.Contains("line 2", ex.Message);
        }

        [TestCase(";wave grunt=-1")]
        [TestCase(";wave grunt=two")]
        [TestCase(";wave grunt")]
        [TestCase(";wave grunt=1 grunt=2")]
        [TestCase(";wave grunt=0")]
        [TestCase(";wave")]
        public void MalformedWave_IsAParseError(string line)
        {
            Assert.Throws<FormatException>(() => Level(line));
        }

        [Test]
        public void CommentsThatStartWithWave_AreNotWaves()
        {
            var map = Level(";waves come from the config here", ";wavelength is a comment too");

            Assert.AreEqual(0, map.Waves.Count);
        }

        [Test]
        public void LevelWithoutWaveLines_UsesTheConfigWaves()
        {
            var cfg = new RedlineConfig();
            var sim = new RedlineSim(cfg, TileMap.Parse(ReadResource("Loop01.txt")));

            Assert.AreEqual(0, sim.Map.Waves.Count);
            Assert.AreEqual(cfg.WaveCount, sim.WaveCount);
            Assert.AreEqual(cfg.waveBrutes[1], sim.Waves[1][WaveEnemy.Brute]);
        }

        [Test]
        public void LevelWaves_ReplaceTheConfigWaves_AndSpawnTheirCounts()
        {
            var cfg = new RedlineConfig { firstWaveDelay = 0.1f, waveBreakTime = 0.1f };
            var sim = new RedlineSim(cfg, Level(";wave grunt=2 drone=1", ";wave brute=1"));
            sim.Player.InvulnTimer = 999f;

            Assert.AreEqual(2, sim.WaveCount);
            Run(sim, new InputFrame(), 0.2f);
            Assert.AreEqual(SimState.Fighting, sim.State);
            Assert.AreEqual(2, CountAlive(sim, EnemyKind.Grunt));
            Assert.AreEqual(1, CountAlive(sim, EnemyKind.Drone));
            Assert.AreEqual(0, CountAlive(sim, EnemyKind.Brute));

            foreach (var e in sim.Enemies) e.Hp = 0f;
            Run(sim, new InputFrame(), 0.3f);

            Assert.AreEqual(2, sim.CurrentWave);
            Assert.AreEqual(1, CountAlive(sim, EnemyKind.Brute));
            Assert.AreEqual(0, CountAlive(sim, EnemyKind.Grunt));

            foreach (var e in sim.Enemies) e.Hp = 0f;
            sim.Step(new InputFrame(), Dt);
            Assert.AreEqual(SimState.Victory, sim.State, "two level waves, then victory");
        }

        [TestCase("shooter", EnemyKind.Shooter)]
        [TestCase("shield", EnemyKind.Shield)]
        [TestCase("kamikaze", EnemyKind.Kamikaze)]
        [TestCase("summoner", EnemyKind.Summoner)]
        public void LevelAskingForAWaveTwoKind_IsValid_AndSpawnsIt(string key, EnemyKind kind)
        {
            var map = Level(";wave grunt=1", $";wave grunt=1 {key}=2");

            Assert.IsEmpty(RedlineSim.ValidateWaves(map.Waves));
            var cfg = new RedlineConfig { firstWaveDelay = 0.05f };
            var sim = new RedlineSim(cfg, Level($";wave {key}=2"));
            sim.Player.InvulnTimer = 999f;
            Run(sim, new InputFrame(), 0.1f);
            Assert.AreEqual(2, CountAlive(sim, kind));
        }

        [Test]
        public void ZeroCountsOfEveryKind_AreFine()
        {
            var map = Level(";wave grunt=1 brute=0 drone=0 shooter=0 shield=0 kamikaze=0 summoner=0");

            Assert.DoesNotThrow(() => new RedlineSim(new RedlineConfig(), map));
        }

        private static int CountAlive(RedlineSim sim, EnemyKind kind)
        {
            int n = 0;
            foreach (var e in sim.Enemies)
                if (e.Alive && e.Kind == kind) n++;
            return n;
        }
    }
}
