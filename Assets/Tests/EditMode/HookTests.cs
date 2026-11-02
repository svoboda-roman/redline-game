using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 002 — grappling hook swing.</summary>
    public class HookTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            // 40 wide, 20 tall, solid ceiling row at y=15.
            _sim = new RedlineSim(_cfg, Box(40, 20, ceilingRow: 15));
            Teleport(_sim, 5f, 8f);
            var p = _sim.Player;
            p.Vx = 0f;
            p.Vy = 0f;
            p.Facing = 1;
        }

        private InputFrame FireHook()
        {
            return new InputFrame { HookPressed = true, HookHeld = true };
        }

        [Test]
        public void Hook_FiredUnderCeiling_AttachesUpForward()
        {
            _sim.Step(FireHook(), Dt);
            var p = _sim.Player;

            Assert.IsTrue(p.Hooked);
            Assert.That(p.HookAnchor.Y, Is.EqualTo(15f).Within(0.06f));
            Assert.Greater(p.HookAnchor.X, p.X, "fires toward the facing direction");
        }

        [Test]
        public void Hook_NothingInRange_Misses()
        {
            var sim = new RedlineSim(NoWaves(), Box(40, 40));
            sim.Step(FireHook(), Dt);

            Assert.IsFalse(sim.Player.Hooked);
        }

        [Test]
        public void Swing_RopeNeverStretches()
        {
            var input = FireHook();
            _sim.Step(input, Dt);
            input.HookPressed = false;
            var p = _sim.Player;
            float length = p.HookLength;
            float maxDistance = 0f;
            for (int i = 0; i < 180; i++)
            {
                _sim.Step(input, Dt);
                maxDistance = MathF.Max(maxDistance, (p.Center - p.HookAnchor).Length);
            }

            Assert.IsTrue(p.Hooked);
            Assert.LessOrEqual(maxDistance, length + 0.05f);
        }

        [Test]
        public void Swing_ConvertsFallIntoHorizontalSpeed_AndReleaseKeepsIt()
        {
            var input = FireHook();
            _sim.Step(input, Dt);
            input.HookPressed = false;
            var p = _sim.Player;
            float best = 0f;
            int bestStep = 0;
            for (int i = 0; i < 60; i++)
            {
                _sim.Step(input, Dt);
                if (p.Vx > best)
                {
                    best = p.Vx;
                    bestStep = i;
                }
            }
            Assert.Greater(best, 5f, "a quarter swing from 60 degrees turns the drop into sideways speed");

            // Replay to the fastest point, then let go.
            SetUp();
            input = FireHook();
            _sim.Step(input, Dt);
            input.HookPressed = false;
            p = _sim.Player;
            for (int i = 0; i <= bestStep; i++) _sim.Step(input, Dt);
            float before = p.Vx;
            input.HookHeld = false;
            _sim.Step(input, Dt);

            Assert.IsFalse(p.Hooked);
            Assert.That(p.Vx, Is.EqualTo(before).Within(0.5f));
        }

        [Test]
        public void Swing_PumpingBuildsMoreSpeedThanHanging()
        {
            float Peak(float moveX)
            {
                SetUp();
                var input = FireHook();
                input.MoveX = moveX;
                _sim.Step(input, Dt);
                input.HookPressed = false;
                float peak = 0f;
                for (int i = 0; i < 240; i++)
                {
                    _sim.Step(input, Dt);
                    peak = MathF.Max(peak, MathF.Abs(_sim.Player.Vx));
                }
                return peak;
            }

            Assert.Greater(Peak(1f), Peak(0f));
        }

        [Test]
        public void Hook_TakingDamage_Releases()
        {
            var input = FireHook();
            _sim.Step(input, Dt);
            input.HookPressed = false;
            var p = _sim.Player;
            Assert.IsTrue(p.Hooked);

            _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X, p.Y));
            _sim.Step(input, Dt);

            Assert.IsFalse(p.Hooked);
            Assert.AreEqual(_cfg.playerMaxHp - _cfg.gruntContactDamage, p.Hp);
        }

        [Test]
        public void Hook_FacingLeft_FiresUpAndLeft()
        {
            Teleport(_sim, 30f, 8f);
            _sim.Player.Facing = -1;
            _sim.Step(FireHook(), Dt);

            Assert.IsTrue(_sim.Player.Hooked);
            Assert.Less(_sim.Player.HookAnchor.X, _sim.Player.X);
        }

        [Test]
        public void Swing_PumpingLeft_AlsoBuildsSpeed()
        {
            float Peak(float moveX)
            {
                SetUp();
                Teleport(_sim, 30f, 8f);
                _sim.Player.Facing = -1;
                var input = FireHook();
                input.MoveX = moveX;
                _sim.Step(input, Dt);
                input.HookPressed = false;
                float peak = 0f;
                for (int i = 0; i < 240; i++)
                {
                    _sim.Step(input, Dt);
                    peak = MathF.Max(peak, MathF.Abs(_sim.Player.Vx));
                }
                return peak;
            }

            Assert.Greater(Peak(-1f), Peak(0f));
        }

        [Test]
        public void Hook_RepressedDuringCooldown_IsBuffered()
        {
            // Regression: a hook press during the cooldown used to be thrown away.
            var input = FireHook();
            _sim.Step(input, Dt);
            Assert.IsTrue(_sim.Player.Hooked);
            _sim.Step(new InputFrame(), Dt); // let go: release starts the cooldown
            Assert.IsFalse(_sim.Player.Hooked);
            Run(_sim, new InputFrame(), _cfg.hookCooldown * 0.5f);

            _sim.Step(FireHook(), Dt); // pressed while still cooling down
            Assert.IsFalse(_sim.Player.Hooked);
            Run(_sim, new InputFrame { HookHeld = true }, _cfg.hookCooldown);

            Assert.IsTrue(_sim.Player.Hooked);
        }

        [Test]
        public void Hook_WhileStuckLowInATunnel_DoesNotSpendTheCooldown()
        {
            var sim = new RedlineSim(_cfg, Tunnel());
            Teleport(sim, 17f, 1f);
            sim.Player.Vx = _cfg.runSpeed;
            sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);
            Run(sim, Hold(1f), 0.6f);
            Assert.IsTrue(sim.Player.Sliding, "low under the ceiling");

            sim.Step(new InputFrame { MoveX = 1f, HookPressed = true, HookHeld = true }, Dt);

            Assert.IsFalse(sim.Player.Hooked);
            Assert.AreEqual(0f, sim.Player.HookCooldownTimer);
        }

        [Test]
        public void Hook_FiredWhileRunningTowardTheAnchor_IsTautAtOnce()
        {
            // Regression: the rope stayed slack (~0.5 s from the floor) while the player moved toward
            // the anchor, so the hook felt delayed.
            var sim = new RedlineSim(_cfg, Box(40, 20, ceilingRow: 9));
            Teleport(sim, 3f, 1f);
            sim.Player.Vx = _cfg.runSpeed;
            sim.Step(new InputFrame { MoveX = 1f, HookPressed = true, HookHeld = true }, Dt);
            Assert.IsTrue(sim.Player.Hooked);

            Run(sim, new InputFrame { MoveX = 1f, HookHeld = true }, 2 * Dt);

            var p = sim.Player;
            Assert.IsFalse(p.Grounded, "the hook lifted the player off the floor at once");
            Assert.Greater(p.Vy, 0f, "pulled up toward the anchor");
            float distance = (p.Center - p.HookAnchor).Length;
            Assert.LessOrEqual(distance, p.HookLength + 0.05f, "rope never longer than its length");
        }
    }
}
