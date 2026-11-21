using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Reeling the hook rope in and out, and the wall-jump grace after leaving a wall.</summary>
    public class HookReelTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _sim = new RedlineSim(_cfg, Box(40, 20, ceilingRow: 15));
            Teleport(_sim, 5f, 8f);
            var p = _sim.Player;
            p.Vx = 0f;
            p.Vy = 0f;
            p.Facing = 1;
            _sim.Step(new InputFrame { HookPressed = true, HookHeld = true }, Dt);
            Assume.That(p.Hooked);
        }

        [Test]
        public void HoldingUp_ReelsTheRopeIn_AndPullsThePlayerUp()
        {
            var p = _sim.Player;
            float length = p.HookLength;
            float y = p.Y;
            Run(_sim, new InputFrame { HookHeld = true, MoveY = 1f }, 0.8f);

            Assert.IsTrue(p.Hooked);
            Assert.Less(p.HookLength, length - 1.5f);
            Assert.Greater(p.Y, y);
            Assert.GreaterOrEqual(p.HookLength, _cfg.hookMinLength);
        }

        [Test]
        public void HoldingDown_LetsTheRopeOut()
        {
            var p = _sim.Player;
            Run(_sim, new InputFrame { HookHeld = true }, 0.5f);   // settle under the anchor
            float length = p.HookLength;
            Run(_sim, new InputFrame { HookHeld = true, MoveY = -1f }, 0.6f);

            Assert.IsTrue(p.Hooked);
            Assert.Greater(p.HookLength, length + 1f);
        }

        [Test]
        public void UpKeyJumpPress_WhileHooked_DoesNotLetGo()
        {
            _sim.Step(new InputFrame { HookHeld = true, MoveY = 1f, JumpPressed = true, JumpPressedByUp = true, JumpHeld = true }, Dt);
            Assert.IsTrue(_sim.Player.Hooked);

            _sim.Step(new InputFrame { HookHeld = true, JumpPressed = true, JumpHeld = true }, Dt);
            Assert.IsFalse(_sim.Player.Hooked, "Space / pad jump still releases");
        }

        [Test]
        public void WallJump_StillWorks_JustAfterLeavingTheWall()
        {
            var sim = new RedlineSim(_cfg, Box(80, 14));
            var p = sim.Player;
            Teleport(sim, 78.595f, 6f);
            sim.Step(Hold(1f), Dt);
            Assert.AreEqual(1, p.WallDir, "touching the right wall");

            p.X -= 0.4f;
            sim.Step(Hold(0f), Dt);
            Assert.AreEqual(0, p.WallDir, "left the wall");

            sim.Step(new InputFrame { JumpPressed = true, JumpHeld = true }, Dt);
            Assert.IsTrue(HasEvent(sim, SimEventType.WallJump));
            Assert.Less(p.Vx, 0f, "kicks away from the wall it just left");
        }
    }
}
