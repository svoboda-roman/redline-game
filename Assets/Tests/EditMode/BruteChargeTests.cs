using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 011 — charging brute: telegraph, charge, crash into walls with armor down.</summary>
    public class BruteChargeTests
    {
        private RedlineConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
        }

        private static Enemy ChargingBrute(RedlineSim sim, float x)
        {
            var brute = sim.SpawnEnemy(EnemyKind.Brute, new Vec2(x, 1f));
            brute.Charges = true;
            return brute;
        }

        [Test]
        public void WalkingBrute_NeverCharges()
        {
            var sim = new RedlineSim(_cfg, Box(40, 14));
            Teleport(sim, 15f, 1f);
            var brute = sim.SpawnEnemy(EnemyKind.Brute, new Vec2(22f, 1f));

            Run(sim, Hold(0f), 1f);

            Assert.AreEqual(BruteMode.Walk, brute.Mode);
            Assert.LessOrEqual(MathF.Abs(brute.Vx), _cfg.bruteSpeed);
        }

        [Test]
        public void PlayerInRange_BruteTelegraphsThenChargesAtThem()
        {
            var sim = new RedlineSim(_cfg, Box(40, 14));
            Teleport(sim, 15f, 1f);
            var brute = ChargingBrute(sim, 22f);

            sim.Step(Hold(0f), Dt);

            Assert.AreEqual(BruteMode.Windup, brute.Mode);
            Assert.IsTrue(HasEvent(sim, SimEventType.BruteWindup));
            Assert.AreEqual(0f, brute.Vx, "stands still while telegraphing");

            Run(sim, Hold(0f), _cfg.bruteWindupTime + Dt);

            Assert.AreEqual(BruteMode.Charge, brute.Mode);
            Assert.AreEqual(-_cfg.bruteChargeSpeed, brute.Vx, "charges toward the player on its left");
        }

        [Test]
        public void Charge_IntoAWall_CrashesAndDropsArmor()
        {
            var sim = new RedlineSim(_cfg, Box(40, 14));
            Teleport(sim, 5f, 1f);
            var brute = ChargingBrute(sim, 11f);
            sim.Step(Hold(0f), Dt);
            Assert.AreEqual(BruteMode.Windup, brute.Mode);
            Teleport(sim, 30f, 1f); // step out of the charge line

            int guard = 0;
            while (brute.Mode != BruteMode.Crashed && guard++ < 120) sim.Step(Hold(0f), Dt);

            Assert.AreEqual(BruteMode.Crashed, brute.Mode);
            Assert.IsTrue(brute.Dazed);
            Assert.AreEqual(1, brute.ArmorTier, "armor is down while dazed");
        }

        [Test]
        public void CrashedBrute_TakesDamageAtTier1_AndDealsNoContactDamage()
        {
            var sim = new RedlineSim(_cfg, Box(40, 14));
            Teleport(sim, 5f, 1f);
            var brute = ChargingBrute(sim, 11f);
            sim.Step(Hold(0f), Dt);
            Teleport(sim, 30f, 1f);
            int guard = 0;
            while (brute.Mode != BruteMode.Crashed && guard++ < 120) sim.Step(Hold(0f), Dt);

            Teleport(sim, brute.X + 1.2f, 1f);
            sim.Player.Facing = -1;
            sim.Step(new InputFrame { AttackPressed = true }, Dt);

            Assert.AreEqual(1, sim.PlayerTier);
            Assert.Less(brute.Hp, _cfg.bruteHp, "a tier-1 strike damages a crashed brute");
            Assert.AreEqual(_cfg.playerMaxHp, sim.Player.Hp, "a dazed brute does not hurt on contact");
        }

        [Test]
        public void ChargeThatMisses_EndsRecoversAndCoolsDown()
        {
            var sim = new RedlineSim(_cfg, Box(120, 14));
            Teleport(sim, 50f, 1f);
            var brute = ChargingBrute(sim, 57f);
            sim.Step(Hold(0f), Dt);
            Teleport(sim, 110f, 1f); // far behind it: the charge runs into open floor

            Run(sim, Hold(0f), _cfg.bruteWindupTime + _cfg.bruteChargeTime + Dt);
            Assert.AreEqual(BruteMode.Recover, brute.Mode);

            Run(sim, Hold(0f), _cfg.bruteRecoverTime + Dt);
            Assert.AreEqual(BruteMode.Walk, brute.Mode);

            Teleport(sim, brute.X + 3f, 1f); // in range again, but cooling down
            sim.Step(Hold(0f), Dt);
            Assert.AreEqual(BruteMode.Walk, brute.Mode);
        }

        [Test]
        public void ChargingBrute_StaysArmoredWhileTelegraphing()
        {
            var sim = new RedlineSim(_cfg, Box(40, 14));
            Teleport(sim, 15f, 1f);
            var brute = ChargingBrute(sim, 17f);
            sim.Step(Hold(0f), Dt);
            Assert.AreEqual(BruteMode.Windup, brute.Mode);

            sim.Player.Vx = _cfg.runSpeed; // tier 2
            sim.Step(new InputFrame { MoveX = 1f, AttackPressed = true }, Dt);

            Assert.IsTrue(HasEvent(sim, SimEventType.Clang));
            Assert.AreEqual(_cfg.bruteHp, brute.Hp);
        }

        [Test]
        public void WaveBrutes_ChargeOnlyFromTheConfiguredWave()
        {
            var cfg = new RedlineConfig
            {
                waveGrunts = new[] { 0, 0, 0, 0 },
                waveBrutes = new[] { 1, 1, 1, 1 },
                waveDrones = new[] { 0, 0, 0, 0 },
                bruteChargeFromWave = 3
            };
            var sim = new RedlineSim(cfg, Box(40, 14));
            Run(sim, Hold(0f), cfg.firstWaveDelay + Dt);

            for (int wave = 1; wave <= 4; wave++)
            {
                Assert.AreEqual(wave, sim.CurrentWave);
                Assert.AreEqual(wave >= 3, sim.Enemies[0].Charges, $"wave {wave}");
                foreach (var e in sim.Enemies) e.Hp = 0f;
                sim.Step(Hold(0f), Dt);
                Run(sim, Hold(0f), cfg.waveBreakTime + Dt);
            }
        }
    }
}
