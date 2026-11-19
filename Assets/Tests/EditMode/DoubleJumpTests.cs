using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Double jump: one extra jump in the air, only from speed tier 2 up.</summary>
    public class DoubleJumpTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _sim = new RedlineSim(_cfg, Box(200, 30));
        }

        /// <summary>Puts the player in mid-air in open space moving right at the given speed.</summary>
        private Player Airborne(float speed)
        {
            Teleport(_sim, 40f, 12f);
            var p = _sim.Player;
            p.Vx = speed;
            p.Vy = -1f;
            _sim.Step(Hold(1f), Dt);
            Assume.That(p.Grounded, Is.False);
            return p;
        }

        private static InputFrame JumpPress() => new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true };

        [Test]
        public void Tier2_InTheAir_JumpPressDoubleJumps_Once()
        {
            var p = Airborne(_cfg.tier2Speed + 1f);
            Assume.That(_sim.PlayerTier, Is.GreaterThanOrEqualTo(2));

            _sim.Step(JumpPress(), Dt);
            Assert.IsTrue(p.AirJumpUsed);
            Assert.Greater(p.Vy, _cfg.doubleJumpSpeed - 1f);
            Assert.IsTrue(HasEvent(_sim, SimEventType.Jump));

            Run(_sim, Hold(1f), 0.3f);
            float vy = p.Vy;
            _sim.Step(JumpPress(), Dt);
            Assert.Less(p.Vy, vy, "no third jump: the second press does nothing");
        }

        [Test]
        public void Tier1_InTheAir_NoDoubleJump()
        {
            var p = Airborne(2f);
            Assume.That(_sim.PlayerTier, Is.EqualTo(1));
            _sim.Step(new InputFrame { MoveX = 0f, JumpPressed = true, JumpHeld = true }, Dt);
            Assert.IsFalse(p.AirJumpUsed);
            Assert.Less(p.Vy, 0f);
        }

        [Test]
        public void Landing_GivesTheDoubleJumpBack()
        {
            var p = Airborne(_cfg.tier2Speed + 1f);
            _sim.Step(JumpPress(), Dt);
            Assume.That(p.AirJumpUsed);
            for (int i = 0; i < 400 && !p.Grounded; i++) _sim.Step(Hold(1f), Dt);
            Assert.IsTrue(p.Grounded);
            _sim.Step(Hold(1f), Dt);   // landing is applied at the start of the next step
            Assert.IsFalse(p.AirJumpUsed);
        }
    }
}
