using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 009 — tier 3 is earned (hook, wall-jump, kill), not given by run + slide.</summary>
    public class TierEarningTests
    {
        private RedlineConfig _cfg;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
        }

        [Test]
        public void GroundRunAndSlide_TopsOutAtTier2()
        {
            var sim = new RedlineSim(_cfg, Box(80, 14));
            Run(sim, Hold(1f), 1f);
            sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);

            Assert.IsTrue(sim.Player.Sliding);
            Assert.Less(sim.Player.Vx, _cfg.tier3Speed);
            Assert.AreEqual(2, sim.PlayerTier);
        }

        [Test]
        public void HookSwing_FromAJumpTowardTheCentrePlatform_ReachesTier3()
        {
            // Jump at run speed from x=15 toward the high centre platform, hook at the apex,
            // swing through the bottom.
            var sim = new RedlineSim(_cfg, TileMap.Parse(ReadResource("Arena01.txt")));
            Teleport(sim, 15f, 1f);
            sim.Player.Vx = _cfg.runSpeed;
            var input = new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true };
            bool fired = false;
            float best = 0f;
            for (int i = 0; i < 120; i++)
            {
                if (!fired && !sim.Player.Grounded && sim.Player.Vy <= 0f)
                {
                    input.HookPressed = true;
                    fired = true;
                }
                input.HookHeld = fired;
                sim.Step(input, Dt);
                input.JumpPressed = false;
                input.HookPressed = false;
                if (sim.Player.Hooked) best = MathF.Max(best, MathF.Abs(sim.Player.Vx));
            }

            Assert.GreaterOrEqual(best, _cfg.tier3Speed);
        }

        [Test]
        public void WallJump_AfterSlidingIntoAWall_ReachesTier3()
        {
            var sim = new RedlineSim(_cfg, Box(40, 14));
            Teleport(sim, 33f, 1f);
            sim.Player.Vx = _cfg.runSpeed;
            sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);
            int guard = 0;
            while (sim.Player.WallDir != 1 && guard++ < 60) sim.Step(Hold(1f), Dt);
            Assert.AreEqual(1, sim.Player.WallDir, "slid into the right wall");

            sim.Step(new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true }, Dt); // ground jump
            Run(sim, new InputFrame { MoveX = 1f, JumpHeld = true }, 4 * Dt);
            sim.Step(new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true }, Dt); // wall jump

            Assert.IsTrue(sim.Events.Count > 0 && HasEvent(sim, SimEventType.WallJump));
            Assert.AreEqual(3, sim.PlayerTier);
        }

        [Test]
        public void WallJump_FromAStandstill_StaysBelowTier3()
        {
            var sim = new RedlineSim(_cfg, Box(40, 14));
            Teleport(sim, 38.595f, 4f);
            Assert.AreEqual(1, sim.Player.WallDir);

            sim.Step(new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true }, Dt);

            Assert.IsTrue(HasEvent(sim, SimEventType.WallJump));
            Assert.Less(sim.PlayerTier, 3);
        }

        [Test]
        public void KillDuringATier2Slide_PushesIntoTier3()
        {
            var sim = new RedlineSim(_cfg, Box(80, 14));
            Run(sim, Hold(1f), 1f);
            sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);
            Assert.AreEqual(2, sim.PlayerTier);
            var p = sim.Player;
            sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));

            sim.Step(new InputFrame { MoveX = 1f, AttackPressed = true }, Dt);

            Assert.AreEqual(1, sim.Kills);
            Assert.AreEqual(3, sim.PlayerTier);
        }
    }
}
