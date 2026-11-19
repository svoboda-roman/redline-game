using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>A ground slide is shaped by the speed tier it starts at: lower, faster and longer at higher tiers.</summary>
    public class SlideTierTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _sim = new RedlineSim(_cfg, Box(80, 14));
        }

        private Player SlideAt(float speed)
        {
            Teleport(_sim, 10f, 1f);
            _sim.Player.Vx = speed;
            _sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);
            Assert.IsTrue(_sim.Player.Sliding);
            return _sim.Player;
        }

        [Test]
        public void Tier1Slide_IsAHalfCrouch()
        {
            var p = SlideAt(_cfg.slideMinSpeed + 1f);
            Assert.AreEqual(1, p.SlideTier);
            Assert.AreEqual(_cfg.slideTier1Height, p.Height);
            Assert.That(p.Vx, Is.EqualTo(_cfg.slideMinSpeed + 1f + _cfg.slideTier1Boost).Within(0.001f));
            Assert.AreEqual(_cfg.slideTier1Duration, p.SlideLength);
        }

        [Test]
        public void Tier2Slide_IsTheNormalSlide()
        {
            var p = SlideAt(_cfg.runSpeed);
            Assert.AreEqual(2, p.SlideTier);
            Assert.AreEqual(_cfg.slideHeight, p.Height);
            Assert.That(p.Vx, Is.EqualTo(_cfg.runSpeed + _cfg.slideBoost).Within(0.001f));
        }

        [Test]
        public void Tier3Slide_IsLowerFasterAndLonger()
        {
            var p = SlideAt(_cfg.tier3Speed + 1f);
            Assert.AreEqual(3, p.SlideTier);
            Assert.AreEqual(_cfg.slideTier3Height, p.Height);
            Assert.Less(p.Height, _cfg.slideHeight);
            Assert.That(p.Vx, Is.EqualTo(_cfg.tier3Speed + 1f + _cfg.slideTier3Boost).Within(0.001f));
            Assert.Greater(p.SlideLength, _cfg.slideDuration);
        }

        [Test]
        public void EndingASlide_ClearsTheTierAndStandsUp()
        {
            var p = SlideAt(_cfg.tier3Speed + 1f);
            Run(_sim, Hold(1f), _cfg.slideTier3Duration + 0.1f);
            Assert.IsFalse(p.Sliding);
            Assert.AreEqual(0, p.SlideTier);
            Assert.AreEqual(_cfg.playerHeight, p.Height);
        }

        [Test]
        public void HoldingAgainstTheSlide_CancelsIt()
        {
            var p = SlideAt(_cfg.runSpeed);
            _sim.Step(Hold(-1f), Dt);
            Assert.IsFalse(p.Sliding);
        }
    }
}
