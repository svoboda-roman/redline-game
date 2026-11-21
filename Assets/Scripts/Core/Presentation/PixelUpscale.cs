using System;

namespace Redline.Core.Presentation
{
    /// <summary>
    /// Where the low-res picture lands on an output of a given size, in whole output pixels (origin bottom-left).
    /// Produced by <see cref="PixelUpscale.Compute"/>.
    /// </summary>
    public readonly struct UpscaleLayout
    {
        /// <summary>Output pixels per texel (an integer, at least 1).</summary>
        public readonly int Scale;
        /// <summary>The full visible picture (Width x Height texels, no guard), uncropped; may start off-screen when overscanned.</summary>
        public readonly int PictureX, PictureY, PictureW, PictureH;
        /// <summary>The part of the picture actually shown: clipped to the output and inside the cinematic bars. X1/Y1 exclusive.</summary>
        public readonly int VisibleX0, VisibleY0, VisibleX1, VisibleY1;
        /// <summary>The guarded quad (Width+2G x Height+2G texels) including the sub-texel slide.</summary>
        public readonly int QuadX, QuadY, QuadW, QuadH;
        /// <summary>The sub-texel slide in whole output pixels (0..Scale).</summary>
        public readonly int SlideX, SlideY;

        /// <summary>Builds a layout (see <see cref="PixelUpscale.Compute"/>).</summary>
        public UpscaleLayout(int scale, int px, int py, int pw, int ph, int vx0, int vy0, int vx1, int vy1,
            int qx, int qy, int qw, int qh, int slideX, int slideY)
        {
            Scale = scale; PictureX = px; PictureY = py; PictureW = pw; PictureH = ph;
            VisibleX0 = vx0; VisibleY0 = vy0; VisibleX1 = vx1; VisibleY1 = vy1;
            QuadX = qx; QuadY = qy; QuadW = qw; QuadH = qh; SlideX = slideX; SlideY = slideY;
        }
    }

    /// <summary>
    /// Integer point upscale of the pixel-art render target. Every texel covers exactly <c>Scale x Scale</c> output
    /// pixels, so columns never come out uneven and nothing swims while the sub-texel slide moves the picture.
    /// The remainder of the output is letterboxed / pillarboxed. A one-step larger scale is used when it crops at
    /// most <c>maxOverscan</c> of the picture on each axis (1900x1000 with a 640x320 picture: x3 cropping 1 %, instead
    /// of x2).
    /// </summary>
    /// <example>
    /// <code>
    /// var l = PixelUpscale.Compute(2560, 1440, 640, 320, 1, remX, remY, 0, 0.06f);   // Scale 4, 2560x1280 at (0, 80)
    /// </code>
    /// </example>
    /// <remarks>Pure, allocation-free, engine-free; unit tested in PixelUpscaleTests.</remarks>
    public static class PixelUpscale
    {
        /// <summary>
        /// The integer pixels-per-texel for an output: floor of the fit (at least 1), or one more when that crops
        /// no more than <paramref name="maxOverscan"/> (fraction of the picture) on either axis.
        /// </summary>
        public static int IntegerScale(int outW, int outH, int width, int height, float maxOverscan)
        {
            if (width <= 0 || height <= 0) return 1;
            int k = Math.Max(1, Math.Min(outW / width, outH / height));
            if (maxOverscan > 0f)
            {
                int k1 = k + 1;
                int cropW = Math.Max(0, width * k1 - outW), cropH = Math.Max(0, height * k1 - outH);
                if (cropW <= maxOverscan * width * k1 && cropH <= maxOverscan * height * k1) k = k1;
            }
            return k;
        }

        /// <summary>
        /// The layout of a <paramref name="width"/> x <paramref name="height"/> picture (plus a
        /// <paramref name="guard"/>-texel border on the render target) on an <paramref name="outW"/> x
        /// <paramref name="outH"/> output. <paramref name="remX"/>/<paramref name="remY"/> are the camera's sub-texel
        /// remainders (0..1 texel), rounded to whole output pixels; the quad moves left/down by them.
        /// <paramref name="extraRows"/> is the cinematic bar growth in texel rows per bar.
        /// </summary>
        public static UpscaleLayout Compute(int outW, int outH, int width, int height, int guard,
            float remX, float remY, int extraRows, float maxOverscan)
        {
            outW = Math.Max(1, outW); outH = Math.Max(1, outH);
            width = Math.Max(1, width); height = Math.Max(1, height);
            int k = IntegerScale(outW, outH, width, height, maxOverscan);
            int pw = width * k, ph = height * k;
            int px = (outW - pw) >> 1, py = (outH - ph) >> 1;   // arithmetic shift: floor, also when negative
            int e = Math.Max(0, Math.Min(extraRows, (height - 1) / 2)) * k;
            int sx = Math.Max(0, Math.Min(k, (int)Math.Floor(remX * k + 0.5f)));
            int sy = Math.Max(0, Math.Min(k, (int)Math.Floor(remY * k + 0.5f)));
            int vx0 = Math.Max(0, px), vx1 = Math.Min(outW, px + pw);
            int vy0 = Math.Max(0, py + e), vy1 = Math.Min(outH, py + ph - e);
            int g = Math.Max(0, guard) * k;
            return new UpscaleLayout(k, px, py, pw, ph, vx0, vy0, vx1, vy1,
                px - g - sx, py - g - sy, pw + 2 * g, ph + 2 * g, sx, sy);
        }
    }
}
