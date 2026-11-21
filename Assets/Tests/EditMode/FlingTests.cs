using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>
    /// Hook release fling and air overspeed (fix for "letting go at full speed flies no farther
    /// than a jump"): a voluntary release in the air adds hookReleaseBoost along the velocity, and
    /// in the air |Vx| may exceed maxSpeed up to airMaxSpeed, decaying at airOverspeedDecay.
    /// </summary>
    public class FlingTests
    {
        private RedlineConfig _cfg;

        [SetUp]
        public void SetUp() => _cfg = NoWaves();

        /// <summary>Full-hold jump from the floor at maxSpeed: horizontal distance to landing.</summary>
        private float FullJumpDistance()
        {
            var sim = new RedlineSim(_cfg, Box(200, 30));
            Teleport(sim, 5f, 1f);
            sim.Player.Vx = _cfg.maxSpeed;
            float x0 = sim.Player.X;
            var input = new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true };
            for (int i = 0; i < 300; i++)
            {
                sim.Step(input, Dt);
                input.JumpPressed = false;
                if (i > 3 && sim.Player.Grounded) break;
            }
            Assert.IsTrue(sim.Player.Grounded, "the jump landed");
            return sim.Player.X - x0;
        }

        /// <summary>
        /// Runs at maxSpeed, full jump, fires the hook at the apex (ceiling at y=12), swings for
        /// <paramref name="swingSteps"/>, lets go and returns the distance from release to landing
        /// (NaN when the swing hit something first).
        /// </summary>
        private float ReleaseToLanding(int swingSteps)
        {
            var sim = new RedlineSim(_cfg, Box(300, 30, ceilingRow: 12));
            var p = sim.Player;
            Teleport(sim, 5f, 1f);
            p.Vx = _cfg.maxSpeed;
            var input = new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true };
            for (int guard = 0; p.Vy > 0f || p.Grounded; guard++)
            {
                sim.Step(input, Dt);
                input.JumpPressed = false;
                if (guard > 200) return float.NaN;
            }
            input.HookPressed = true;
            input.HookHeld = true;
            for (int i = 0; i < swingSteps; i++)
            {
                sim.Step(input, Dt);
                input.HookPressed = false;
                if (!p.Hooked || p.Grounded) return float.NaN;
            }
            input.HookHeld = false;
            input.JumpHeld = false;
            sim.Step(input, Dt);
            float releaseX = p.X;
            for (int i = 0; i < 600 && !p.Grounded && p.WallDir == 0; i++) sim.Step(input, Dt);
            return p.Grounded ? p.X - releaseX : float.NaN;
        }

        [Test]
        public void WellTimedRelease_FliesClearlyFartherThanAFullJump()
        {
            float jump = FullJumpDistance();
            float best = 0f;
            for (int k = 3; k < 150; k++)
            {
                float d = ReleaseToLanding(k);
                if (!float.IsNaN(d)) best = MathF.Max(best, d);
            }
            TestContext.WriteLine($"full jump {jump:0.00} tiles, best release {best:0.00} tiles");

            Assert.GreaterOrEqual(best, jump * 1.25f, "the hook beats a jump by at least 25 %");
        }

        [Test]
        public void ReleaseWithoutFling_WouldNotBeatAJump()
        {
            // The reason for the fling: with no boost the best release falls short of a jump.
            _cfg.hookReleaseBoost = 0f;
            float jump = FullJumpDistance();
            float best = 0f;
            for (int k = 3; k < 150; k++)
            {
                float d = ReleaseToLanding(k);
                if (!float.IsNaN(d)) best = MathF.Max(best, d);
            }

            Assert.Less(best, jump);
        }

        [Test]
        public void ReleaseAtTheSwingBottom_KeepsOverspeed_ThenDecaysToMaxSpeed()
        {
            // Tall room: hook a ceiling at y=70 high in the air and fall far after the release.
            var sim = new RedlineSim(_cfg, Box(400, 80, ceilingRow: 70));
            var p = sim.Player;
            Teleport(sim, 5f, 62f);
            p.Vx = _cfg.maxSpeed;
            p.Vy = 0f;
            var input = new InputFrame { MoveX = 1f, HookPressed = true, HookHeld = true };
            float last = 0f;
            for (int i = 0; i < 120; i++)
            {
                sim.Step(input, Dt);
                input.HookPressed = false;
                Assert.IsTrue(p.Hooked);
                float speed = MathF.Abs(p.Vx);
                // Just past the bottom of the swing (after the rope has taken the entry speed).
                if (i * Dt > _cfg.hookReleaseMinTime && speed < last && last > _cfg.tier3Speed) break;
                last = speed;
            }
            input.HookHeld = false;
            sim.Step(input, Dt);

            Assert.IsFalse(p.Hooked);
            Assert.Greater(p.Vx, _cfg.maxSpeed + 3f, "the fling carries the player past maxSpeed");
            Assert.LessOrEqual(p.Vx, _cfg.airMaxSpeed);
            Assert.AreEqual(3, sim.PlayerTier);

            float decayTime = (_cfg.airMaxSpeed - _cfg.maxSpeed) / _cfg.airOverspeedDecay;
            Run(sim, new InputFrame { MoveX = 1f }, decayTime + 0.05f);

            Assert.IsFalse(p.Grounded, "still in the air");
            Assert.LessOrEqual(p.Vx, _cfg.maxSpeed + 1e-3f, "overspeed decayed back to maxSpeed");
            Assert.GreaterOrEqual(p.Vx, _cfg.maxSpeed - 1e-3f, "...and not below it");
        }

        [Test]
        public void AirOverspeed_IsCappedAtAirMaxSpeed_AndDecaysAtTheConfiguredRate()
        {
            var sim = new RedlineSim(_cfg, Box(400, 80));
            var p = sim.Player;
            Teleport(sim, 5f, 60f);
            p.Vx = _cfg.airMaxSpeed + 10f;

            sim.Step(new InputFrame(), Dt);
            Assert.AreEqual(_cfg.airMaxSpeed, p.Vx, 1e-3f, "capped at airMaxSpeed");

            Run(sim, new InputFrame(), 0.5f);
            Assert.AreEqual(_cfg.airMaxSpeed - 0.5f * _cfg.airOverspeedDecay, p.Vx, 0.2f);
        }

        [TestCase(9f)]
        [TestCase(22f)]
        public void TappingTheHook_GivesNoFling(float speed)
        {
            var sim = new RedlineSim(_cfg, Box(200, 30, ceilingRow: 20));
            var p = sim.Player;
            Teleport(sim, 5f, 10f);
            p.Vx = speed;
            sim.Step(new InputFrame { MoveX = 1f, HookPressed = true, HookHeld = true }, Dt);
            Assert.IsTrue(p.Hooked);
            sim.Step(new InputFrame { MoveX = 1f, HookHeld = true }, Dt);
            float before = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);

            sim.Step(new InputFrame { MoveX = 1f }, Dt);

            Assert.IsFalse(p.Hooked);
            float after = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
            Assert.Less(after, before + 1f, "a hook tap is not a dash");
        }

        [Test]
        public void SlowSwingRelease_GivesNoFling()
        {
            // Hanging almost still under the anchor for a while: speed below hookReleaseBoostMinSpeed.
            var sim = new RedlineSim(_cfg, Box(40, 20, ceilingRow: 15));
            var p = sim.Player;
            Teleport(sim, 5f, 8f);
            sim.Step(new InputFrame { HookPressed = true, HookHeld = true }, Dt);
            Assert.IsTrue(p.Hooked);
            Run(sim, new InputFrame { HookHeld = true }, 0.4f);
            float before = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
            Assume.That(before, Is.LessThan(_cfg.hookReleaseBoostMinSpeed));

            sim.Step(new InputFrame(), Dt);

            float after = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
            Assert.Less(after, before + 1f);
        }

        [Test]
        public void Slide_KeepsOverspeedCarriedFromTheAir()
        {
            var sim = new RedlineSim(_cfg, Box(200, 20));
            var p = sim.Player;
            Teleport(sim, 5f, 1f);
            p.Vx = _cfg.maxSpeed + 4f;

            sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true }, Dt);

            Assert.IsTrue(p.Sliding);
            Assert.Greater(p.Vx, _cfg.maxSpeed + 3f, "the slide did not cut the speed to maxSpeed");
        }

        [Test]
        public void OnTheGround_OverspeedDecaysWithOverspeedDecay()
        {
            var sim = new RedlineSim(_cfg, Box(300, 20));
            var p = sim.Player;
            Teleport(sim, 5f, 1f);
            p.Vx = _cfg.maxSpeed + 4f;

            Run(sim, Hold(1f), 1f);

            Assert.AreEqual(_cfg.maxSpeed + 4f - _cfg.overspeedDecay, p.Vx, 0.1f);
        }
    }
}
