using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 006 — wave flow, HP, game over, victory.</summary>
    public class WaveTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = new RedlineConfig();
            _sim = new RedlineSim(_cfg, Box(40, 14));
        }

        private void KillAll()
        {
            foreach (var e in _sim.Enemies) e.Hp = 0f;
        }

        [Test]
        public void Start_IsAWaveBreak_ThenWave1Spawns()
        {
            Assert.AreEqual(SimState.WaveBreak, _sim.State);
            Assert.AreEqual(1, _sim.CurrentWave);

            Run(_sim, Hold(0f), _cfg.firstWaveDelay + 0.05f);

            Assert.AreEqual(SimState.Fighting, _sim.State);
            Assert.AreEqual(_cfg.waveGrunts[0] + _cfg.waveBrutes[0], _sim.EnemiesAlive);
        }

        [Test]
        public void ClearingAWave_BreaksThenSpawnsTheNext()
        {
            Run(_sim, Hold(0f), _cfg.firstWaveDelay + 0.05f);
            KillAll();
            _sim.Step(Hold(0f), Dt);

            Assert.AreEqual(SimState.WaveBreak, _sim.State);
            Assert.AreEqual(2, _sim.CurrentWave);

            Run(_sim, Hold(0f), _cfg.waveBreakTime + 0.05f);

            Assert.AreEqual(SimState.Fighting, _sim.State);
            Assert.AreEqual(_cfg.waveGrunts[1] + _cfg.waveBrutes[1], _sim.EnemiesAlive);
            Assert.AreEqual(_cfg.waveGrunts[1] + _cfg.waveBrutes[1], _sim.Enemies.Count, "dead enemies are cleared");
        }

        [Test]
        public void ClearingAWave_HealsUpToMaxHp()
        {
            Run(_sim, Hold(0f), _cfg.firstWaveDelay + 0.05f);
            _sim.Player.Hp = 1;
            KillAll();
            _sim.Step(Hold(0f), Dt);

            Assert.AreEqual(1 + _cfg.waveClearHeal, _sim.Player.Hp);

            Run(_sim, Hold(0f), _cfg.waveBreakTime + 0.05f);
            _sim.Player.Hp = _cfg.playerMaxHp;
            KillAll();
            _sim.Step(Hold(0f), Dt);

            Assert.AreEqual(_cfg.playerMaxHp, _sim.Player.Hp, "never above max");
        }

        [Test]
        public void ClearingTheLastWave_IsVictory()
        {
            Run(_sim, Hold(0f), _cfg.firstWaveDelay + 0.05f);
            for (int w = 0; w < _cfg.WaveCount; w++)
            {
                KillAll();
                _sim.Step(Hold(0f), Dt);
                if (w < _cfg.WaveCount - 1) Run(_sim, Hold(0f), _cfg.waveBreakTime + 0.05f);
            }

            Assert.AreEqual(SimState.Victory, _sim.State);
            Assert.AreEqual(_cfg.WaveCount, _sim.CurrentWave);
        }

        [Test]
        public void HpZero_IsGameOver_AndTheSimFreezes()
        {
            _sim.Player.Hp = 1;
            _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(_sim.Player.X + 0.5f, _sim.Player.Y));
            _sim.Step(Hold(0f), Dt);

            Assert.AreEqual(SimState.GameOver, _sim.State);
            float x = _sim.Player.X;
            Run(_sim, Hold(1f), 0.5f);
            Assert.AreEqual(x, _sim.Player.X);
        }

        [Test]
        public void Restart_ResetsToWave1WithFullHp()
        {
            Run(_sim, Hold(0f), _cfg.firstWaveDelay + 0.05f);
            KillAll();
            _sim.Step(Hold(0f), Dt);
            _sim.Player.Hp = 0;
            _sim.Step(Hold(0f), Dt);
            Assert.AreEqual(SimState.GameOver, _sim.State);

            _sim.Step(new InputFrame { RestartPressed = true }, Dt);

            Assert.AreEqual(SimState.WaveBreak, _sim.State);
            Assert.AreEqual(1, _sim.CurrentWave);
            Assert.AreEqual(_cfg.playerMaxHp, _sim.Player.Hp);
            Assert.AreEqual(0, _sim.Enemies.Count);
            Assert.AreEqual(0, _sim.Kills);
        }

        [Test]
        public void InvalidConfig_IsRejected()
        {
            var bad = new RedlineConfig { waveBrutes = new[] { 1 } };
            Assert.Throws<System.ArgumentException>(() => new RedlineSim(bad, Box(40, 14)));
        }

        [Test]
        public void Restart_FromVictory_StartsOver()
        {
            Run(_sim, Hold(0f), _cfg.firstWaveDelay + 0.05f);
            for (int w = 0; w < _cfg.WaveCount; w++)
            {
                KillAll();
                _sim.Step(Hold(0f), Dt);
                if (w < _cfg.WaveCount - 1) Run(_sim, Hold(0f), _cfg.waveBreakTime + 0.05f);
            }
            Assert.AreEqual(SimState.Victory, _sim.State);

            _sim.Step(new InputFrame { RestartPressed = true }, Dt);

            Assert.AreEqual(SimState.WaveBreak, _sim.State);
            Assert.AreEqual(1, _sim.CurrentWave);
        }

        [Test]
        public void Restart_WhileSwinging_ResetsThePlayer()
        {
            var sim = new RedlineSim(_cfg, Box(40, 20, ceilingRow: 15));
            Teleport(sim, 5f, 8f);
            sim.Step(new InputFrame { HookPressed = true, HookHeld = true }, Dt);
            Assert.IsTrue(sim.Player.Hooked);
            int run = sim.RunId;

            sim.Step(new InputFrame { RestartPressed = true, HookHeld = true }, Dt);

            Assert.IsFalse(sim.Player.Hooked);
            Assert.AreEqual(sim.Map.PlayerStart.X, sim.Player.X);
            Assert.AreEqual(run + 1, sim.RunId);
        }

        [Test]
        public void LargestWave_SpawnsInsideArena01WithoutTerrainOverlap()
        {
            int grunts = 0, brutes = 0;
            foreach (int g in _cfg.waveGrunts) grunts = System.Math.Max(grunts, g);
            foreach (int b in _cfg.waveBrutes) brutes = System.Math.Max(brutes, b);
            var cfg = new RedlineConfig { waveGrunts = new[] { grunts }, waveBrutes = new[] { brutes } };
            var map = TileMap.Parse(ReadResource("Arena01.txt"));
            var sim = new RedlineSim(cfg, map);

            Run(sim, Hold(0f), cfg.firstWaveDelay + Dt);

            Assert.AreEqual(grunts + brutes, sim.Enemies.Count);
            foreach (var e in sim.Enemies)
            {
                Assert.IsFalse(map.OverlapsSolid(e.MinX, e.Y, e.MaxX, e.MaxY), $"{e.Kind} at ({e.X}, {e.Y}) is inside terrain");
                Assert.IsTrue(e.MinX > 0f && e.MaxX < map.Width && e.Y > 0f, $"{e.Kind} at ({e.X}, {e.Y}) is outside the arena");
            }
        }
    }
}
