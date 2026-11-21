using System.Collections.Generic;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Orbs dropped by killed enemies: seeded drops, falling, pickup effects, buffs and expiry.</summary>
    public class OrbTests
    {
        private RedlineConfig _cfg;

        [SetUp]
        public void SetUp() => _cfg = NoWaves();

        private RedlineSim NewSim() => new RedlineSim(_cfg, Box(60, 20));

        /// <summary>Kills a grunt standing just in front of the player; returns the orb kinds dropped that step.</summary>
        private static List<OrbKind> KillGrunt(RedlineSim sim)
        {
            var dropped = new List<OrbKind>();
            var p = sim.Player;
            p.InvulnTimer = 99f;
            p.Facing = 1;
            sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));
            var input = new InputFrame { AttackPressed = true };
            for (int i = 0; i < 20; i++)
            {
                sim.Step(input, Dt);
                input.AttackPressed = false;
                foreach (var e in sim.Events)
                    if (e.Type == SimEventType.OrbSpawn) dropped.Add((OrbKind)e.Detail);
                if (HasEvent(sim, SimEventType.Kill)) return dropped;
            }
            Assert.Fail("the grunt was not killed");
            return dropped;
        }

        private List<OrbKind> DropSequence(int seed, int kills)
        {
            _cfg.rngSeed = seed;
            _cfg.gruntOrbChance = 0.5f;
            _cfg.orbPickupDelay = 1e6f; // keep them lying around
            var sim = NewSim();
            var all = new List<OrbKind>();
            for (int i = 0; i < kills; i++)
            {
                Teleport(sim, 10f, 1f);
                var dropped = KillGrunt(sim);
                Assert.LessOrEqual(dropped.Count, 1, "one kill drops at most one orb");
                all.AddRange(dropped);
            }
            return all;
        }

        [Test]
        public void Drops_WithTheSameSeed_AreIdentical()
        {
            var a = DropSequence(seed: 7, kills: 10);
            var b = DropSequence(seed: 7, kills: 10);

            CollectionAssert.AreEqual(a, b);
            Assert.That(a.Count, Is.InRange(1, 9), "a 50 % chance over 10 kills drops some, not all");
        }

        [Test]
        public void Restart_ReplaysTheSameDrops()
        {
            _cfg.gruntOrbChance = 0.5f;
            _cfg.orbPickupDelay = 1e6f;
            var sim = NewSim();
            var first = new List<OrbKind>();
            for (int i = 0; i < 8; i++) first.AddRange(KillGrunt(sim));

            sim.Step(new InputFrame { RestartPressed = true }, Dt);
            Assert.AreEqual(0, sim.Orbs.Count, "restart clears orbs");
            var second = new List<OrbKind>();
            for (int i = 0; i < 8; i++) second.AddRange(KillGrunt(sim));

            CollectionAssert.AreEqual(first, second);
        }

        [TestCase(0f, 0)]
        [TestCase(1f, 1)]
        public void DropChance_ZeroNeverDrops_OneAlwaysDrops(float chance, int expected)
        {
            _cfg.gruntOrbChance = chance;
            var sim = NewSim();
            Teleport(sim, 10f, 1f);

            var dropped = KillGrunt(sim);

            Assert.AreEqual(expected, dropped.Count);
            Assert.AreEqual(expected, sim.Orbs.Count);
        }

        [Test]
        public void Orb_SpawnsAtTheEnemyCentre_FallsAndRestsOnTheGround()
        {
            _cfg.gruntOrbChance = 1f;
            _cfg.orbPickupDelay = 1e6f;
            var sim = NewSim();
            Teleport(sim, 10f, 1f);

            KillGrunt(sim);
            var orb = sim.Orbs[0];
            Assert.Greater(orb.Y, 1.1f, "spawned at the enemy's centre, above the floor");

            Run(sim, new InputFrame(), 1.5f);

            Assert.IsTrue(orb.Grounded);
            Assert.AreEqual(1f, orb.Y, 1e-3f, "resting on the floor top");
        }

        [Test]
        public void Orb_ExpiresAfterItsLifetime_WithAnEvent()
        {
            var sim = NewSim();
            var orb = sim.SpawnOrb(OrbKind.Health, new Vec2(40f, 1f));
            Assert.IsNotNull(orb);
            Assert.IsTrue(HasEvent(sim, SimEventType.OrbSpawn));

            Run(sim, new InputFrame(), _cfg.orbLifetime - 0.1f);
            Assert.AreEqual(1, sim.Orbs.Count, "still there just before its lifetime");
            Assert.That(sim.Orbs[0].TimeLeft, Is.InRange(0f, 0.2f), "remaining time is exposed");

            bool expired = false;
            for (int i = 0; i < 12 && !expired; i++)
            {
                sim.Step(new InputFrame(), Dt);
                expired = HasEvent(sim, SimEventType.OrbExpire);
            }

            Assert.IsTrue(expired);
            Assert.AreEqual(0, sim.Orbs.Count);
        }

        [Test]
        public void Pool_SkipsDropsBeyondOrbMaxCount()
        {
            _cfg.orbMaxCount = 3;
            var sim = NewSim();
            for (int i = 0; i < 3; i++) Assert.IsNotNull(sim.SpawnOrb(OrbKind.Speed, new Vec2(30f + i, 1f)));

            Assert.IsNull(sim.SpawnOrb(OrbKind.Speed, new Vec2(40f, 1f)));
            Assert.AreEqual(3, sim.Orbs.Count);
        }

        [Test]
        public void HealthOrb_HealsOne_AfterThePickupDelay()
        {
            var sim = NewSim();
            var p = sim.Player;
            p.Hp = 3;
            sim.SpawnOrb(OrbKind.Health, new Vec2(p.X, p.Y));

            Run(sim, new InputFrame(), _cfg.orbPickupDelay * 0.5f);
            Assert.AreEqual(3, p.Hp, "not before the pickup delay");

            bool picked = false;
            for (int i = 0; i < 30 && !picked; i++)
            {
                sim.Step(new InputFrame(), Dt);
                picked = HasEvent(sim, SimEventType.OrbPickup);
            }

            Assert.IsTrue(picked);
            Assert.AreEqual(3 + _cfg.orbHealAmount, p.Hp);
            Assert.AreEqual(0, sim.Orbs.Count);
        }

        [Test]
        public void HealthOrb_IsCappedAtMaxHp()
        {
            var sim = NewSim();
            var p = sim.Player;
            Assert.AreEqual(_cfg.playerMaxHp, p.Hp);
            sim.SpawnOrb(OrbKind.Health, new Vec2(p.X, p.Y));

            Run(sim, new InputFrame(), 0.5f);

            Assert.AreEqual(0, sim.Orbs.Count, "picked up");
            Assert.AreEqual(_cfg.playerMaxHp, p.Hp);
        }

        [Test]
        public void SpeedOrb_RaisesRunSpeed_UntilItExpires()
        {
            var sim = NewSim();
            var p = sim.Player;
            sim.SpawnOrb(OrbKind.Speed, new Vec2(p.X, p.Y));
            Run(sim, new InputFrame(), 0.5f);
            Assert.That(p.SpeedBuffTimer, Is.GreaterThan(_cfg.orbSpeedDuration - 0.5f), "remaining time is exposed");

            Run(sim, Hold(1f), 1f);
            Assert.AreEqual(_cfg.runSpeed * _cfg.orbSpeedMultiplier, p.Vx, 1e-3f, "buffed run speed");

            Teleport(sim, 3f, 1f);
            Run(sim, Hold(1f), 0.2f);
            p.SpeedBuffTimer = Dt * 0.5f; // about to run out
            // The extra speed fades with the ground overspeedDecay, like any overspeed.
            float fade = (_cfg.orbSpeedMultiplier - 1f) * _cfg.runSpeed / _cfg.overspeedDecay;
            Run(sim, Hold(1f), fade + 0.1f);

            Assert.AreEqual(0f, p.SpeedBuffTimer);
            Assert.AreEqual(_cfg.runSpeed, p.Vx, 1e-3f, "back to normal run speed after the buff");
        }

        [Test]
        public void SpeedOrb_RaisesTheSpeedCaps()
        {
            var sim = new RedlineSim(_cfg, Box(400, 80));
            var p = sim.Player;
            p.SpeedBuffTimer = 10f;
            Teleport(sim, 5f, 60f);
            p.Vx = 100f;

            sim.Step(new InputFrame(), Dt);

            Assert.AreEqual(_cfg.airMaxSpeed * _cfg.orbSpeedMultiplier, p.Vx, 1e-3f);
        }

        [Test]
        public void HookOrb_RemovesTheCooldown_AddsRange_AndExpires()
        {
            var sim = new RedlineSim(_cfg, Box(60, 30, ceilingRow: 15));
            var p = sim.Player;
            sim.SpawnOrb(OrbKind.Hook, new Vec2(p.X, p.Y));
            Run(sim, new InputFrame(), 0.5f);
            Assert.That(p.HookBuffTimer, Is.GreaterThan(_cfg.orbHookDuration - 0.5f));

            // Ceiling 14 tiles above the centre along the 60 degree ray: out of normal range, in buffed range.
            Teleport(sim, 5f, 2.5f);
            float reach = (15f - p.Center.Y) / System.MathF.Sin(_cfg.hookAngleDeg * System.MathF.PI / 180f);
            Assume.That(reach, Is.GreaterThan(_cfg.hookRange).And.LessThan(_cfg.hookRange + _cfg.orbHookRangeBonus));
            sim.Step(new InputFrame { HookPressed = true, HookHeld = true }, Dt);
            Assert.IsTrue(p.Hooked, "the range bonus reaches the ceiling");

            sim.Step(new InputFrame(), Dt);
            Assert.IsFalse(p.Hooked);
            Assert.AreEqual(0f, p.HookCooldownTimer, "no cooldown with the hook buff");

            p.HookBuffTimer = Dt * 0.5f;
            sim.Step(new InputFrame(), Dt);
            Assert.AreEqual(0f, p.HookBuffTimer);
            sim.Step(new InputFrame { HookPressed = true, HookHeld = true }, Dt);
            Assert.IsFalse(p.Hooked, "out of range again once the buff is over");
            Assert.AreEqual(_cfg.hookCooldown, p.HookCooldownTimer, 1e-4f, "the cooldown is back");
        }
    }
}
