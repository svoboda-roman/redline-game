using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 001 — momentum movement and speed tiers.</summary>
    public class MovementTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _sim = new RedlineSim(_cfg, Box(80, 14));
        }

        [Test]
        public void Run_HoldingRight_ReachesRunSpeedAndTier2()
        {
            Run(_sim, Hold(1f), 1f);

            Assert.That(_sim.Player.Vx, Is.EqualTo(_cfg.runSpeed).Within(0.001f));
            Assert.AreEqual(2, _sim.PlayerTier);
            Assert.IsTrue(_sim.Player.Grounded);
        }

        [Test]
        public void Run_ReleasingDirection_BrakesToStop()
        {
            Run(_sim, Hold(1f), 1f);
            Run(_sim, Hold(0f), 0.5f);

            Assert.AreEqual(0f, _sim.Player.Vx);
            Assert.AreEqual(1, _sim.PlayerTier);
        }

        [Test]
        public void Jump_FullHold_ApexMatchesJumpSpeed()
        {
            float startY = _sim.Player.Y;
            var input = new InputFrame { JumpPressed = true, JumpHeld = true };
            float apex = startY;
            for (int i = 0; i < 90; i++)
            {
                _sim.Step(input, Dt);
                input.JumpPressed = false;
                if (_sim.Player.Y > apex) apex = _sim.Player.Y;
            }
            float expected = _cfg.jumpSpeed * _cfg.jumpSpeed / (2f * _cfg.gravity);

            Assert.That(apex - startY, Is.EqualTo(expected).Within(0.2f));
            Assert.IsTrue(_sim.Player.Grounded, "lands again");
        }

        [Test]
        public void Jump_EarlyRelease_CutsJumpShort()
        {
            float startY = _sim.Player.Y;
            var input = new InputFrame { JumpPressed = true, JumpHeld = true };
            float apex = startY;
            for (int i = 0; i < 90; i++)
            {
                if (i == 3) input.JumpHeld = false;
                _sim.Step(input, Dt);
                input.JumpPressed = false;
                if (_sim.Player.Y > apex) apex = _sim.Player.Y;
            }
            float full = _cfg.jumpSpeed * _cfg.jumpSpeed / (2f * _cfg.gravity);

            Assert.Less(apex - startY, full * 0.5f);
        }

        [Test]
        public void Jump_BufferedJustBeforeLanding_Triggers()
        {
            Teleport(_sim, _sim.Player.X, 1.5f); // just above the floor (floor top is y=1)
            _sim.Player.Vy = -3f;
            var input = new InputFrame { JumpPressed = true, JumpHeld = true };
            bool jumped = false;
            for (int i = 0; i < 10; i++)
            {
                _sim.Step(input, Dt);
                input.JumpPressed = false;
                foreach (var e in _sim.Events)
                    if (e.Type == SimEventType.Jump) jumped = true;
            }

            Assert.IsTrue(jumped);
        }

        [Test]
        public void Slide_AtRunSpeed_BoostsToTier3AndLowersHitbox()
        {
            Run(_sim, Hold(1f), 1f);
            var input = Hold(1f);
            input.SlidePressed = true;
            _sim.Step(input, Dt);

            Assert.IsTrue(_sim.Player.Sliding);
            Assert.That(_sim.Player.Vx, Is.EqualTo(_cfg.runSpeed + _cfg.slideBoost).Within(0.001f));
            Assert.AreEqual(_cfg.slideHeight, _sim.Player.Height);
            Assert.AreEqual(3, _sim.PlayerTier);

            Run(_sim, Hold(1f), _cfg.slideDuration + 0.1f);

            Assert.IsFalse(_sim.Player.Sliding);
            Assert.AreEqual(_cfg.playerHeight, _sim.Player.Height);
        }

        [Test]
        public void Slide_BelowMinimumSpeed_DoesNothing()
        {
            var input = Hold(0f);
            input.SlidePressed = true;
            _sim.Step(input, Dt);

            Assert.IsFalse(_sim.Player.Sliding);
        }

        [Test]
        public void Overspeed_DecaysSlowlyOnGround()
        {
            _sim.Player.Vx = 15f;
            Run(_sim, Hold(1f), 1f);

            Assert.That(_sim.Player.Vx, Is.EqualTo(15f - _cfg.overspeedDecay).Within(0.05f));
        }

        [Test]
        public void Overspeed_IsKeptInTheAir()
        {
            Teleport(_sim, 10f, 8f);
            _sim.Player.Vx = 15f;
            Run(_sim, Hold(1f), 0.3f);

            Assert.IsFalse(_sim.Player.Grounded);
            Assert.That(_sim.Player.Vx, Is.EqualTo(15f).Within(0.001f));
        }

        [Test]
        public void WallJump_KeepsTheSpeedTheWallWasHitWith()
        {
            var p = _sim.Player;
            Teleport(_sim, 76f, 6f);
            p.Vx = 14f;
            for (int i = 0; i < 30 && p.WallDir != 1; i++) _sim.Step(Hold(1f), Dt);
            Assert.AreEqual(1, p.WallDir, "reached the right wall");

            var input = Hold(1f);
            input.JumpPressed = true;
            input.JumpHeld = true;
            _sim.Step(input, Dt);

            Assert.That(p.Vx, Is.EqualTo(-(14f + _cfg.wallJumpKick)).Within(0.01f));
            Assert.Greater(p.Vy, 0f);
            Assert.AreEqual(-1, p.Facing);
        }

        [Test]
        public void Collision_MaxSpeedIntoWall_NeverPassesThrough()
        {
            var p = _sim.Player;
            p.X = 70f;
            p.Vx = _cfg.maxSpeed;
            Run(_sim, Hold(1f), 1f);

            Assert.LessOrEqual(p.MaxX, 79f, "right wall cell starts at x=79");
            Assert.GreaterOrEqual(p.Y, 1f, "never sinks into the floor");
        }

        [Test]
        public void Coyote_JumpJustAfterLeavingLedge_StillJumps()
        {
            var sim = new RedlineSim(_cfg, Ledge());
            Teleport(sim, 11f, 5f);
            sim.Player.Vx = _cfg.runSpeed;
            int guard = 0;
            while (sim.Player.Grounded && guard++ < 60) sim.Step(Hold(1f), Dt);
            sim.Step(Hold(1f), Dt); // one more step in the air: inside coyote time

            sim.Step(new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true }, Dt);

            Assert.IsTrue(HasEvent(sim, SimEventType.Jump));
            Assert.Greater(sim.Player.Vy, _cfg.jumpSpeed * 0.9f);
        }

        [Test]
        public void Coyote_JumpTooLongAfterLeavingLedge_DoesNothing()
        {
            var sim = new RedlineSim(_cfg, Ledge());
            Teleport(sim, 11f, 5f);
            sim.Player.Vx = _cfg.runSpeed;
            int guard = 0;
            while (sim.Player.Grounded && guard++ < 60) sim.Step(Hold(1f), Dt);
            Run(sim, Hold(1f), _cfg.coyoteTime + 0.05f);

            sim.Step(new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true }, Dt);

            Assert.IsFalse(HasEvent(sim, SimEventType.Jump));
            Assert.Less(sim.Player.Vy, 0f);
        }

        [Test]
        public void Slide_RightAfterASlide_WaitsForCooldown()
        {
            Run(_sim, Hold(1f), 1f);
            _sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);
            Run(_sim, Hold(1f), _cfg.slideDuration + Dt);
            Assert.IsFalse(_sim.Player.Sliding, "first slide over");

            _sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);
            Assert.IsFalse(_sim.Player.Sliding, "blocked by cooldown");

            Run(_sim, Hold(1f), _cfg.slideCooldown);
            _sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);
            Assert.IsTrue(_sim.Player.Sliding, "allowed after cooldown");
        }

        [Test]
        public void Slide_PressedJustBeforeLanding_IsBuffered()
        {
            Teleport(_sim, 10f, 1.4f);
            _sim.Player.Vx = _cfg.runSpeed;
            _sim.Player.Vy = -6f;
            Assert.IsFalse(_sim.Player.Grounded);

            _sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);
            Run(_sim, Hold(1f), 0.1f);

            Assert.IsTrue(_sim.Player.Sliding);
        }

        [Test]
        public void Slide_UnderLowCeiling_StaysLowThenCrawlsWithGroundControl()
        {
            // Regression: stuck low under a ceiling, the player used to skate at full slide speed
            // with no friction or control.
            var sim = new RedlineSim(_cfg, Tunnel());
            Teleport(sim, 17f, 1f);
            sim.Player.Vx = _cfg.runSpeed;
            sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);
            Run(sim, Hold(1f), _cfg.slideDuration + 0.1f);
            var p = sim.Player;
            Assert.IsTrue(p.X > 20.5f && p.X < 44f, "inside the tunnel");
            Assert.IsTrue(p.Sliding);
            Assert.LessOrEqual(p.MaxY, 2f, "kept low under the ceiling");

            Run(sim, Hold(0f), 0.3f);

            Assert.AreEqual(0f, p.Vx, "no input: ground friction stops the crawl");
            Run(sim, Hold(1f), 3f);
            Assert.Greater(p.X, 45f, "crawled out the far end");
            Assert.IsFalse(p.Sliding);
            Assert.AreEqual(_cfg.playerHeight, p.Height, "stood up once there was head room");
        }

        [Test]
        public void WallSlide_HoldingIntoWall_CapsFallSpeed()
        {
            Teleport(_sim, 78.595f, 8f);
            _sim.Player.Vy = -20f;
            Assert.AreEqual(1, _sim.Player.WallDir);

            _sim.Step(Hold(1f), Dt);

            Assert.That(_sim.Player.Vy, Is.EqualTo(-_cfg.wallSlideMaxFall).Within(0.001f));
        }

        [Test]
        public void WallSlide_NotHoldingIntoWall_FallsFreely()
        {
            Teleport(_sim, 78.595f, 8f);
            _sim.Player.Vy = -20f;

            _sim.Step(Hold(0f), Dt);

            Assert.Less(_sim.Player.Vy, -_cfg.wallSlideMaxFall);
        }

        [Test]
        public void WallJump_ReleasingJumpEarly_DoesNotCutIt()
        {
            Teleport(_sim, 78.595f, 6f);
            _sim.Step(new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true }, Dt);
            Assert.IsTrue(HasEvent(_sim, SimEventType.WallJump));
            float vy = _sim.Player.Vy;

            _sim.Step(Hold(0f), Dt);

            Assert.That(_sim.Player.Vy, Is.EqualTo(vy - _cfg.gravity * Dt).Within(0.001f));
        }
    }
}
