using System;
using NUnit.Framework;
using Redline.Core.Presentation;

namespace Redline.Tests
{
    /// <summary>Camera / sprite texel snapping (Pixel3DCamera follow lock, sprite quads).</summary>
    public class PixelSnapTests
    {
        private const float Tpu = 48f;                      // Pixel3DCamera.TexelsPerUnit
        private const float Step = 1f / 48f;                // FramingHeight / Height = one texel (6.67 / 320)
        private const float StepY = Step / 0.99862953f;     // texel / cos(3 deg pitch)
        private const float Lag = -0.3712f;                 // camera minus player, a non-whole number of texels
        private const float SpeedPerFrame = 3.8f / 48f;     // 0.6 x run speed at 60 Hz: 3.8 texels per frame
        private const int Frames = 240;

        private static float Player(int f) => 117.13f + f * SpeedPerFrame;

        /// <summary>Snapped sprite texel minus snapped camera texel, for frame f.</summary>
        private static int RtOffset(float sprite, float camSnapped) =>
            (int)Math.Round((sprite - camSnapped) * Tpu);

        private static int CountReversals(int[] offsets)
        {
            int n = 0, last = 0;
            for (int f = 1; f < offsets.Length; f++)
            {
                int d = offsets[f] - offsets[f - 1];
                if (d == 0) continue;
                if (last != 0 && Math.Sign(d) != Math.Sign(last)) n++;
                last = d;
            }
            return n;
        }

        [Test]
        public void Snap_MatchesRoundToEvenOnTheGrid()
        {
            Assert.AreEqual(1f, PixelSnap.Snap(1.005f, Tpu), 1e-6f);
            Assert.AreEqual(49f / 48f, PixelSnap.Snap(1.0208f, Tpu), 1e-6f);
            Assert.AreEqual(-2f / 48f, PixelSnap.Snap(-2.4f / 48f, Tpu), 1e-6f);
        }

        [Test]
        public void SnapCamera_SteadyFollow_SpriteStepsBackAndForth()
        {
            // The pre-fix behaviour: independent snaps make the sprite reverse against the camera.
            var off = new int[Frames];
            for (int f = 0; f < Frames; f++)
            {
                float p = Player(f);
                float cam = PixelSnap.SnapCamera(p + Lag, Step, out _);
                off[f] = RtOffset(PixelSnap.Snap(p, Tpu), cam);
            }
            Assert.Greater(CountReversals(off), 10);
        }

        [Test]
        public void LockCamera_SteadyFollow_ConstantOffsetAndRemainder()
        {
            float firstRem = 0f; int firstOff = 0;
            for (int f = 0; f < Frames; f++)
            {
                float p = Player(f);
                float cam = PixelSnap.LockCamera(p + Lag, p, Tpu, Step, out float rem);
                int off = RtOffset(PixelSnap.Snap(p, Tpu), cam);
                if (f == 0) { firstRem = rem; firstOff = off; continue; }
                Assert.AreEqual(firstOff, off, "texel offset, frame " + f);
                Assert.AreEqual(firstRem, rem, 1e-3f, "remainder, frame " + f);
            }
        }

        [Test]
        public void LockCamera_SnappedSubjectLandsAtItsExactScreenOffset()
        {
            for (int f = 0; f < Frames; f++)
            {
                float p = Player(f), c = p + Lag + 0.01f * (float)Math.Sin(f * 0.1);
                float cam = PixelSnap.LockCamera(c, p, Tpu, Step, out float rem);
                float screen = (PixelSnap.Snap(p, Tpu) - cam) * Tpu - rem;   // texels from the slid camera
                Assert.AreEqual((p - c) * Tpu, screen, 2e-3f, "frame " + f);
            }
        }

        [Test]
        public void LockCamera_ResultStaysOnTheCameraGrid()
        {
            for (int f = 0; f < Frames; f++)
            {
                float p = Player(f);
                float x = PixelSnap.LockCamera(p + Lag, p, Tpu, Step, out float rx);
                float y = PixelSnap.LockCamera(3.2f + f * 0.013f, 1.7f + f * 0.011f, Tpu, StepY, out float ry);
                Assert.AreEqual(Math.Round(x / Step), x / Step, 2e-3, "x frame " + f);
                Assert.AreEqual(Math.Round(y / StepY), y / StepY, 2e-3, "y frame " + f);
                Assert.That(rx, Is.InRange(0f, 1f));
                Assert.That(ry, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void LockCamera_StandingSubject_CameraEasesMonotonically()
        {
            float p = 40.27f, last = float.MinValue;
            for (int f = 0; f < Frames; f++)
            {
                float c = p - 1f + f * 0.0011f;   // easing camera, standing player: the picture scrolls smoothly
                float cam = PixelSnap.LockCamera(c, p, Tpu, Step, out float rem);
                float exact = cam + rem * Step;
                Assert.GreaterOrEqual(exact, last - 1e-5f, "frame " + f);
                last = exact;
            }
        }
    }
}
