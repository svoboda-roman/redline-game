using NUnit.Framework;
using Redline.Core.Presentation;

namespace Redline.Tests
{
    /// <summary>Integer upscale of the 640x320 pixel-art target (Pixel3DCamera layout maths).</summary>
    public class PixelUpscaleTests
    {
        private const int W = 640, H = 320, Guard = 1;
        private const float Overscan = 0.06f;

        private static UpscaleLayout At(int outW, int outH, float remX = 0f, float remY = 0f, int extra = 0) =>
            PixelUpscale.Compute(outW, outH, W, H, Guard, remX, remY, extra, Overscan);

        [Test]
        public void Upscale_1920x1080_StaysExactlyTimesThree()
        {
            var l = At(1920, 1080);
            Assert.AreEqual(3, l.Scale);
            Assert.AreEqual((0, 60, 1920, 960), (l.PictureX, l.PictureY, l.PictureW, l.PictureH));
            Assert.AreEqual((0, 60, 1920, 1020), (l.VisibleX0, l.VisibleY0, l.VisibleX1, l.VisibleY1));
        }

        [Test]
        public void Upscale_1280x720_TimesTwo()
        {
            var l = At(1280, 720);
            Assert.AreEqual(2, l.Scale);
            Assert.AreEqual((0, 40, 1280, 640), (l.PictureX, l.PictureY, l.PictureW, l.PictureH));
        }

        [Test]
        public void Upscale_2560x1440_TimesFour()
        {
            var l = At(2560, 1440);
            Assert.AreEqual(4, l.Scale);
            Assert.AreEqual((0, 80, 2560, 1280), (l.PictureX, l.PictureY, l.PictureW, l.PictureH));
        }

        [Test]
        public void Upscale_3840x2160_TimesSix()
        {
            var l = At(3840, 2160);
            Assert.AreEqual(6, l.Scale);
            Assert.AreEqual((0, 120, 3840, 1920), (l.PictureX, l.PictureY, l.PictureW, l.PictureH));
        }

        [Test]
        public void Upscale_1366x768_TimesTwoWithBars()
        {
            var l = At(1366, 768);
            Assert.AreEqual(2, l.Scale);
            Assert.AreEqual((43, 64, 1280, 640), (l.PictureX, l.PictureY, l.PictureW, l.PictureH));
        }

        [Test]
        public void Upscale_1900x1000_TimesThreeCroppingUnderSixPercent()
        {
            var l = At(1900, 1000);
            Assert.AreEqual(3, l.Scale);
            Assert.AreEqual((-10, 20, 1920, 960), (l.PictureX, l.PictureY, l.PictureW, l.PictureH));
            Assert.AreEqual((0, 20, 1900, 980), (l.VisibleX0, l.VisibleY0, l.VisibleX1, l.VisibleY1));
            Assert.AreEqual(2, PixelUpscale.IntegerScale(1900, 1000, W, H, 0f), "without overscan");
        }

        [Test]
        public void Upscale_TinyOutput_NeverBelowOne()
        {
            Assert.AreEqual(1, PixelUpscale.IntegerScale(320, 200, W, H, Overscan));
        }

        [Test]
        public void Slide_RoundsToWholeOutputPixels()
        {
            var l = At(1920, 1080, remX: 0.5f, remY: 0.3f);
            Assert.AreEqual(2, l.SlideX);
            Assert.AreEqual(1, l.SlideY);
            Assert.AreEqual(0 - 3 - 2, l.QuadX);
            Assert.AreEqual(60 - 3 - 1, l.QuadY);
            Assert.AreEqual((642 * 3, 322 * 3), (l.QuadW, l.QuadH));
            Assert.AreEqual(3, At(1920, 1080, remX: 0.99f).SlideX);
        }

        [Test]
        public void QuadSize_IsAlwaysAWholeMultipleOfTheScale()
        {
            foreach (var (w, h) in new[] { (1280, 720), (1366, 768), (1920, 1080), (2560, 1440), (3840, 2160), (1600, 900) })
            {
                var l = At(w, h, 0.37f, 0.81f);
                Assert.AreEqual((W + 2 * Guard) * l.Scale, l.QuadW, $"{w}x{h}");
                Assert.AreEqual((H + 2 * Guard) * l.Scale, l.QuadH, $"{w}x{h}");
                Assert.LessOrEqual(l.QuadX, l.PictureX, $"{w}x{h}: the guard covers the left edge");
                Assert.GreaterOrEqual(l.QuadX + l.QuadW, l.PictureX + l.PictureW, $"{w}x{h}: the guard covers the right edge");
            }
        }

        [Test]
        public void Cinematic_ExtraRowsGrowBothBarsInWholeTexels()
        {
            var l = At(1920, 1080, extra: 26);
            Assert.AreEqual(60 + 78, l.VisibleY0);
            Assert.AreEqual(1020 - 78, l.VisibleY1);
            Assert.AreEqual(2.39f, (l.VisibleX1 - l.VisibleX0) / (float)(l.VisibleY1 - l.VisibleY0), 0.01f);
        }
    }
}
