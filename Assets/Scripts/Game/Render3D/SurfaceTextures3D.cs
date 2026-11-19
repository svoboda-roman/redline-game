using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Turns <see cref="SurfacePatterns"/> into tiling world textures for the 3D mode at
    /// <see cref="SurfacePatterns.TexelsPerUnit"/> texels per world unit: each logical cell becomes 2×2 texels,
    /// Point filtered, Repeat wrapped, with a mip chain (nearest-mip selection keeps receding surfaces from
    /// shimmering while the texels stay crisp). Built at load from a fixed seed, so nothing is imported.
    /// </summary>
    /// <remarks>Main thread only; allocates (build time). The caller owns and destroys the textures.</remarks>
    public static class SurfaceTextures3D
    {
        /// <summary>Albedo (sRGB, greyscale detail), normal (linear) and metallic/smoothness (linear, R/A) maps.</summary>
        public sealed class Set
        {
            /// <summary>sRGB albedo detail (multiplied by the material colour).</summary>
            public Texture2D Albedo;
            /// <summary>Linear tangent-space normal (rgb = xyz*0.5+0.5, a = 1), or null.</summary>
            public Texture2D Normal;
            /// <summary>Linear metallic (R) / smoothness (A), or null.</summary>
            public Texture2D MetalGloss;
            /// <summary>World units covered by one tile (x, y).</summary>
            public Vector2 Units;
            /// <summary>Mean albedo detail luminance (divide the material colour by it to keep overall value).</summary>
            public float MeanLum;

            /// <summary>GPU bytes of all maps including mips (RGBA32).</summary>
            public long Bytes => SizeOf(Albedo) + SizeOf(Normal) + SizeOf(MetalGloss);
        }

        /// <summary>Builds the maps for <paramref name="s"/>; normal and metal/gloss are optional.</summary>
        public static Set Build(string name, SurfacePatterns.Surface s, bool normal, bool metalGloss)
        {
            SurfacePatterns.Clamp(s);
            int c = SurfacePatterns.Cell, w = s.W * c, h = s.H * c;
            var al = new Color32[w * h];
            var nm = normal ? new Color32[w * h] : null;
            var mg = metalGloss ? new Color32[w * h] : null;
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                int k = s.I(x, y);
                byte l = (byte)(s.Lum[k] * 255f + 0.5f);
                var a = new Color32(l, l, l, 255);
                Color32 n = default, m = default;
                if (normal)
                {
                    SurfacePatterns.NormalAt(s, x, y, out float nx, out float ny, out float nz);
                    n = new Color32(B(nx * 0.5f + 0.5f), B(ny * 0.5f + 0.5f), B(nz * 0.5f + 0.5f), 255);
                }
                if (metalGloss) m = new Color32(B(s.Metal[k]), 0, 0, B(s.Smooth[k]));
                for (int dy = 0; dy < c; dy++)
                for (int dx = 0; dx < c; dx++)
                {
                    int i = (y * c + dy) * w + x * c + dx;
                    al[i] = a;
                    if (normal) nm[i] = n;
                    if (metalGloss) mg[i] = m;
                }
            }
            return new Set
            {
                Albedo = Tex(name + "_albedo", w, h, false, al),
                Normal = normal ? Tex(name + "_normal", w, h, true, nm) : null,
                MetalGloss = metalGloss ? Tex(name + "_mg", w, h, true, mg) : null,
                Units = new Vector2(s.UnitsX, s.UnitsY),
                MeanLum = SurfacePatterns.MeanLum(s),
            };
        }

        /// <summary>Pixel puddle mask for alpha-clipped puddle quads: irregular hard-edged blob (alpha 0/1), clamped.</summary>
        public static Texture2D PuddleMask(int seed, int w = 64, int h = 32)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
            {
                float u = (x + 1f) / w * 2f - 1f, v = (y + 1f) / h * 2f - 1f;
                float r = u * u + v * v;
                float n = SurfacePatterns.Hash(x / 6, y / 6, seed) * 0.35f + SurfacePatterns.Hash(x / 2, y / 2, seed + 1) * 0.1f;
                byte a = r + n < 0.8f ? (byte)255 : (byte)0;
                for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) px[(y + dy) * w + x + dx] = new Color32(255, 255, 255, a);
            }
            var t = Tex("puddleMask", w, h, false, px, false);
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        private static byte B(float v) => (byte)(Mathf.Clamp01(v) * 255f + 0.5f);

        private static Texture2D Tex(string name, int w, int h, bool linear, Color32[] px, bool mips = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mips, linear) { name = name };
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Repeat;
            t.anisoLevel = 0;
            t.SetPixels32(px);
            t.Apply(mips, true); // no CPU copy kept
            return t;
        }

        /// <summary>GPU bytes of an RGBA32 texture including its mip chain (0 for null).</summary>
        public static long SizeOf(Texture2D t)
        {
            if (t == null) return 0;
            long b = (long)t.width * t.height * 4;
            return t.mipmapCount > 1 ? b * 4 / 3 : b;
        }
    }
}
