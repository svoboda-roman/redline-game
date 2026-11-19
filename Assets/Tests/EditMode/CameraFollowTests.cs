using System;
using NUnit.Framework;
using Redline.Core.Presentation;

namespace Redline.Tests
{
    /// <summary>
    /// The 3D follow camera (CameraFollow): dead zone, speed-scaled look-ahead with direction hysteresis, platformer
    /// vertical framing, the 12 % safe frame, level bounds, teleport snaps, smooth shake and frame-rate independence.
    /// View 13.33 x 6.67 units (640 x 320 texels at 48 per unit).
    /// </summary>
    public class CameraFollowTests
    {
        private const float W = 640f / 48f, H = 320f / 48f;
        private const float BodyW = 0.8f, BodyH = 1.6f;
        private const float Dt = 1f / 60f;
        private const float Ground = 1f;

        private static CameraFollow Make(bool wrap = true, float levelW = 200f, float levelH = 40f)
        {
            var f = new CameraFollow(new CameraFollowSettings());
            f.SetView(W, H);
            f.SetBounds(-1f, levelW + 1f, -1f, levelH + 1f, wrap);
            return f;
        }

        private static FollowSubject At(float x, float y, float vx = 0f, float vy = 0f, bool grounded = true) =>
            new FollowSubject { X = x, Y = y, Vx = vx, Vy = vy, Width = BodyW, Height = BodyH, Grounded = grounded };

        /// <summary>Picture fractions of the subject's box: (left, right, bottom, top).</summary>
        private static (float l, float r, float b, float t) Box(CameraFollow f, FollowSubject s) =>
            (((s.X - s.Width * 0.5f) - (f.X - W * 0.5f)) / W, ((s.X + s.Width * 0.5f) - (f.X - W * 0.5f)) / W,
             (s.Y - (f.Y - H * 0.5f)) / H, ((s.Y + s.Height) - (f.Y - H * 0.5f)) / H);

        private static void AssertSafe(CameraFollow f, FollowSubject s, string what)
        {
            var b = Box(f, s);
            const float m = 0.12f - 1e-4f;
            Assert.GreaterOrEqual(b.l, m, what + " left");
            Assert.LessOrEqual(b.r, 1f - m, what + " right");
            Assert.GreaterOrEqual(b.b, m, what + " bottom");
            Assert.LessOrEqual(b.t, 1f - m, what + " top");
        }

        [Test]
        public void FirstUpdate_SnapsWithGroundedFeetAt30PercentFromTheBottom()
        {
            var f = Make();
            var s = At(50f, Ground);
            f.Update(s, Dt);
            Assert.AreEqual(50f, f.X, 1e-3f);
            Assert.AreEqual(0.30f, Box(f, s).b, 0.005f);
        }

        [Test]
        public void DeadZone_SmallMovesDoNotMoveTheCamera()
        {
            var f = Make();
            f.Update(At(50f, Ground), Dt);
            for (int i = 0; i < 120; i++) f.Update(At(50f + 0.5f * MathF.Sin(i * 0.1f), Ground), Dt);
            Assert.AreEqual(50f, f.X, 1e-3f);
        }

        [Test]
        public void RunningAtTopSpeed_LeadsAboutEighteenPercentOfTheViewAhead()
        {
            var f = Make();
            float x = 20f;
            f.Update(At(x, Ground), Dt);
            for (int i = 0; i < 240; i++) { x += 22f * Dt; f.Update(At(x, Ground, 22f), Dt); }
            float lead = f.X - x;
            Assert.Greater(lead, 0.16f * W, "the view is ahead of the runner");
            Assert.LessOrEqual(lead, 0.18f * W + 0.05f, "lead stays within 18 % of the width");
            Assert.AreEqual(1, f.LookSide);
        }

        [Test]
        public void LookAhead_ScalesWithSpeed()
        {
            float Lead(float v)
            {
                var f = Make();
                float x = 20f;
                f.Update(At(x, Ground), Dt);
                for (int i = 0; i < 300; i++) { x += v * Dt; f.Update(At(x, Ground, v), Dt); }
                return f.X - x;
            }
            Assert.Less(Lead(4f), Lead(9f));
            Assert.Less(Lead(9f), Lead(20f));
        }

        [Test]
        public void QuickTurn_DoesNotFlipTheLookAheadSide()
        {
            var f = Make();
            float x = 20f;
            f.Update(At(x, Ground), Dt);
            for (int i = 0; i < 120; i++) { x += 9f * Dt; f.Update(At(x, Ground, 9f), Dt); }
            for (int i = 0; i < 9; i++) { x -= 9f * Dt; f.Update(At(x, Ground, -9f), Dt); }   // 0.15 s, 1.35 units
            Assert.AreEqual(1, f.LookSide, "a 0.15 s turn keeps the side");
            for (int i = 0; i < 60; i++) { x += 9f * Dt; f.Update(At(x, Ground, 9f), Dt); }
            Assert.AreEqual(1, f.LookSide);
        }

        [Test]
        public void SustainedReverse_FlipsAfterAQuarterSecond()
        {
            var f = Make();
            float x = 20f;
            f.Update(At(x, Ground), Dt);
            for (int i = 0; i < 120; i++) { x += 4f * Dt; f.Update(At(x, Ground, 4f), Dt); }
            int frames = 0;
            while (f.LookSide == 1 && frames < 120) { x -= 4f * Dt; f.Update(At(x, Ground, -4f), Dt); frames++; }
            Assert.AreEqual(-1, f.LookSide);
            Assert.AreEqual(0.25f, frames * Dt, 2.5f * Dt, "slow reversal flips on the time limit");
        }

        [Test]
        public void FastReverse_FlipsAfterOneAndAHalfUnits()
        {
            var f = Make();
            float x = 20f;
            f.Update(At(x, Ground), Dt);
            for (int i = 0; i < 120; i++) { x += 20f * Dt; f.Update(At(x, Ground, 20f), Dt); }
            float start = x;
            while (f.LookSide == 1 && start - x < 10f) { x -= 20f * Dt; f.Update(At(x, Ground, -20f), Dt); }
            Assert.AreEqual(-1, f.LookSide);
            Assert.AreEqual(1.5f, start - x, 20f * Dt + 0.01f);
        }

        [Test]
        public void SmallHop_DoesNotMoveTheViewVertically()
        {
            var f = Make();
            f.Update(At(50f, Ground), Dt);
            for (int i = 0; i < 60; i++) f.Update(At(50f, Ground), Dt);
            float y0 = f.Y;
            float y = Ground, vy = 8f;
            for (int i = 0; i < 200 && (i == 0 || y > Ground); i++)
            {
                vy -= 45f * Dt; y = MathF.Max(Ground, y + vy * Dt);
                f.Update(At(50f, y, 0f, vy, grounded: y <= Ground), Dt);
                Assert.AreEqual(y0, f.Y, 1e-3f, "hop apex 0.7 stays inside the band");
            }
        }

        [Test]
        public void HighClimb_ViewFollowsWhenThePlayerLeavesTheBand()
        {
            var f = Make();
            f.Update(At(50f, Ground), Dt);
            float y0 = f.Y;
            for (int i = 0; i < 90; i++) f.Update(At(50f, Ground + 6f * i / 90f, 0f, 4f, grounded: false), Dt);
            Assert.Greater(f.Y, y0 + 1f);
            AssertSafe(f, At(50f, Ground + 6f, 0f, 0f, false), "rising");
        }

        [Test]
        public void Landing_OnAHigherLedge_EasesToTheNewGroundFraming()
        {
            var f = Make();
            f.Update(At(50f, Ground), Dt);
            for (int i = 0; i < 180; i++) f.Update(At(50f, Ground + 3f), Dt);
            Assert.AreEqual(0.30f, Box(f, At(50f, Ground + 3f)).b, 0.01f);
        }

        [Test]
        public void FastFall_EasesTheViewDownSoTheLandingShows()
        {
            var band = Make();
            var fast = Make();
            band.Update(At(50f, 30f), Dt);
            fast.Update(At(50f, 30f), Dt);
            float yb = 30f, yf = 30f;
            for (int i = 0; i < 45; i++)
            {
                yb -= 8f * Dt; band.Update(At(50f, yb, 0f, -8f, false), Dt);
                yf -= 20f * Dt; fast.Update(At(50f, yf, 0f, -20f, false), Dt);
            }
            float feetSlow = Box(band, At(50f, yb)).b, feetFast = Box(fast, At(50f, yf)).b;
            Assert.Greater(feetFast, feetSlow + 0.08f, "falling fast, more of the picture is below the feet");
        }

        [Test]
        public void SafeFrame_HoldsAtExtremeSpeedsAndFrameRates()
        {
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 144f })
            {
                var f = Make();
                float x = 20f, y = 10f;
                f.Update(At(x, y), dt);
                for (float t = 0f; t < 3f; t += dt)
                {
                    float vx = t < 1.5f ? 30f : -30f, vy = -24f;
                    x += vx * dt; y += vy * dt;
                    var s = At(x, y, vx, vy, false);
                    f.Update(s, dt);
                    AssertSafe(f, s, $"dt={dt} t={t:F2}");
                }
            }
        }

        [Test]
        public void Bounds_ClampTheViewInsideABoundedLevel()
        {
            var f = Make(wrap: false, levelW: 60f, levelH: 20f);
            f.Update(At(3f, Ground), Dt);
            for (int i = 0; i < 60; i++) f.Update(At(3f, Ground), Dt);
            Assert.AreEqual(-1f + W * 0.5f, f.X, 1e-3f);
            Assert.GreaterOrEqual(f.Y - H * 0.5f, -1f - 1e-3f);
        }

        [Test]
        public void SafeFrame_WinsOverTheLevelBounds()
        {
            var f = Make(wrap: false, levelW: 60f, levelH: 20f);
            var s = At(-1.2f, Ground);
            f.Update(s, Dt);
            AssertSafe(f, s, "subject past the level edge");
        }

        [Test]
        public void WrappingLevel_IsNotClampedHorizontally()
        {
            var f = Make(wrap: true, levelW: 60f);
            f.Update(At(1f, Ground), Dt);
            Assert.AreEqual(1f, f.X, 1e-3f);
        }

        [Test]
        public void Teleport_SnapsWithoutEasing()
        {
            var f = Make();
            f.Update(At(20f, Ground), Dt);
            for (int i = 0; i < 30; i++) f.Update(At(20f, Ground), Dt);
            var s = At(80f, 12f);
            f.Update(s, Dt);
            Assert.AreEqual(80f, f.X, 1e-3f);
            Assert.AreEqual(0.30f, Box(f, s).b, 0.005f);
            Assert.AreEqual(0f, f.VelocityX, 1e-4f);
        }

        [Test]
        public void CameraSpeed_StaysUnderTheCapUnlessTheSafeFrameForcesIt()
        {
            var cfg = new CameraFollowSettings();
            var f = Make();
            float x = 20f;
            f.Update(At(x, Ground), Dt);
            for (int i = 0; i < 240; i++)
            {
                x += 22f * Dt;
                f.Update(At(x, Ground, 22f), Dt);
                Assert.LessOrEqual(MathF.Abs(f.VelocityX), cfg.maxSpeed + 1e-3f);
            }
        }

        [Test]
        public void Motion_IsFrameRateIndependent()
        {
            float Run(float dt)
            {
                var f = Make();
                float x = 20f;
                f.Update(At(x, Ground), dt);
                for (float t = 0f; t < 2f - dt * 0.5f; t += dt) { x += 12f * dt; f.Update(At(x, Ground, 12f), dt); }
                return f.X - x;
            }
            Assert.AreEqual(Run(1f / 60f), Run(1f / 144f), 0.08f);
            Assert.AreEqual(Run(1f / 60f), Run(1f / 30f), 0.15f);
        }

        [Test]
        public void Shake_IsSmoothAndZeroWithoutAmplitude()
        {
            Assert.AreEqual((0f, 0f), CameraFollow.ShakeOffset(0f, 1.234f, 12f));
            float maxStep = 0f, maxAbs = 0f;
            var prev = CameraFollow.ShakeOffset(0.4f, 0f, 12f);
            for (int i = 1; i < 600; i++)
            {
                var o = CameraFollow.ShakeOffset(0.4f, i * Dt, 12f);
                maxStep = MathF.Max(maxStep, MathF.Max(MathF.Abs(o.x - prev.x), MathF.Abs(o.y - prev.y)));
                maxAbs = MathF.Max(maxAbs, MathF.Max(MathF.Abs(o.x), MathF.Abs(o.y)));
                prev = o;
            }
            Assert.LessOrEqual(maxAbs, 0.4f + 1e-4f);
            Assert.Greater(maxAbs, 0.1f, "it does shake");
            Assert.Less(maxStep, 0.3f, "no frame-to-frame random jumps across the whole range");
        }
    }
}
