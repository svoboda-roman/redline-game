using System;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Engine-free generators for the tiling world-surface patterns of the 3D mode (street, slab top, fascia, grime).
    /// Every pattern is built on a <b>logical grid of 2×2-texel cells</b> (24 cells per world unit at 48 texels per
    /// unit), so no feature is ever narrower than 2 texels and nothing single-texel shimmers when the camera moves.
    /// All patterns tile seamlessly (periodic noise, features wrapped) and are deterministic for a seed.
    /// Values are greyscale detail (albedo luminance around 0.8, muted, low contrast): the material colour carries
    /// the level hue (art bible: one hue per level).
    /// </summary>
    /// <remarks>Pure C# (no UnityEngine) so the patterns can be previewed outside the editor.
    /// <see cref="SurfaceTextures3D"/> turns them into point-filtered textures.</remarks>
    public static class SurfacePatterns
    {
        /// <summary>World texel density the patterns are designed for (texels per world unit on the play plane).</summary>
        public const int TexelsPerUnit = 48;

        /// <summary>Texels per logical cell (the minimum feature size).</summary>
        public const int Cell = 2;

        /// <summary>Logical cells per world unit.</summary>
        public const int CellsPerUnit = TexelsPerUnit / Cell;

        /// <summary>Floor styles: Loop01 factory deck, Loop02 asphalt overpass, Loop03 stone setts.</summary>
        public enum FloorKind { ConcreteDeck, Asphalt, Setts }

        /// <summary>Platform front styles: Loop01 riveted steel, Loop02 cast concrete, Loop03 brick.</summary>
        public enum FasciaKind { RivetedSteel, Concrete, Brick }

        /// <summary>A pattern on the logical grid: luminance, height (0..1), smoothness, metallic; W×H cells.</summary>
        public sealed class Surface
        {
            /// <summary>Size in logical cells.</summary>
            public readonly int W, H;
            /// <summary>Albedo luminance multiplier (≈0.5..1).</summary>
            public readonly float[] Lum;
            /// <summary>Height for the normal map (0..1).</summary>
            public readonly float[] Height;
            /// <summary>URP smoothness (0..1).</summary>
            public readonly float[] Smooth;
            /// <summary>URP metallic (0..1).</summary>
            public readonly float[] Metal;
            /// <summary>Height-to-normal strength for this surface.</summary>
            public float NormalStrength = 1.5f;

            /// <summary>Creates a blank surface (lum 0.8, height 0.5, smooth 0.4, metal 0).</summary>
            public Surface(int w, int h)
            {
                W = w; H = h;
                Lum = new float[w * h]; Height = new float[w * h]; Smooth = new float[w * h]; Metal = new float[w * h];
                for (int i = 0; i < w * h; i++) { Lum[i] = 0.8f; Height[i] = 0.5f; Smooth[i] = 0.4f; }
            }

            /// <summary>Width in world units.</summary>
            public float UnitsX => W / (float)CellsPerUnit;

            /// <summary>Height in world units.</summary>
            public float UnitsY => H / (float)CellsPerUnit;

            /// <summary>Wrapped index of cell (x, y).</summary>
            public int I(int x, int y) => Mod(y, H) * W + Mod(x, W);
        }

        // ------------------------------------------------------------------------------------------------ noise

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        /// <summary>Deterministic hash of (x, y, seed) to 0..1.</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Periodic value noise: lattice cell <paramref name="cs"/> cells, period W×H (cs must divide both).</summary>
        private static float VNoise(int x, int y, int w, int h, int cs, int seed)
        {
            int nx = Math.Max(1, w / cs), ny = Math.Max(1, h / cs);
            float fx = (float)x / cs, fy = (float)y / cs;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float a = Hash(Mod(x0, nx), Mod(y0, ny), seed), b = Hash(Mod(x0 + 1, nx), Mod(y0, ny), seed);
            float c = Hash(Mod(x0, nx), Mod(y0 + 1, ny), seed), d = Hash(Mod(x0 + 1, nx), Mod(y0 + 1, ny), seed);
            return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
        }

        /// <summary>Periodic fBm, octaves halve the lattice cell down to 2 cells (≈0..1, mean 0.5).</summary>
        private static float Fbm(int x, int y, int w, int h, int cs, int oct, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < oct && cs >= 2; o++, cs /= 2, amp *= 0.5f)
            {
                sum += VNoise(x, y, w, h, cs, seed + o * 101) * amp;
                norm += amp;
            }
            return sum / norm;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>Wrapped random-walk crack: darkens and lowers the cells it crosses.</summary>
        private static void Crack(Surface s, Random rnd, int len, float dark)
        {
            int x = rnd.Next(s.W), y = rnd.Next(s.H);
            int dir = rnd.Next(4);
            for (int i = 0; i < len; i++)
            {
                int k = s.I(x, y);
                s.Lum[k] *= dark; s.Height[k] = Math.Min(s.Height[k], 0.15f); s.Smooth[k] = Math.Max(s.Smooth[k], 0.6f);
                if (rnd.NextDouble() < 0.3) dir = (dir + (rnd.NextDouble() < 0.5 ? 1 : 3)) % 4;
                if (dir == 0) x++; else if (dir == 1) y++; else if (dir == 2) x--; else y--;
            }
        }

        /// <summary>Puddles (hard pixel edge): dark, mirror-smooth, flat; a damp rim around each.</summary>
        private static void Puddles(Surface s, int seed, float threshold, float waterLevel)
        {
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                float p = Fbm(x, y, s.W, s.H, 32, 2, seed) * 0.8f + Fbm(x, y, s.W, s.H, 8, 1, seed + 7) * 0.2f;
                int k = s.I(x, y);
                if (p > threshold)
                {
                    s.Lum[k] = s.Lum[k] * 0.48f;
                    s.Smooth[k] = 0.96f;
                    s.Metal[k] = 0f;
                    s.Height[k] = waterLevel;
                }
                else if (p > threshold - 0.035f)
                {
                    s.Lum[k] *= 0.82f;
                    s.Smooth[k] = Math.Max(s.Smooth[k], 0.72f);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ patterns

        /// <summary>
        /// Wet floor, 8×8 units (192×192 cells): base material by kind, stains, cracks, puddles.
        /// <paramref name="puddleThreshold"/> sets puddle coverage (0.645 scattered; ≈ 0.56 flooded).
        /// </summary>
        public static Surface Street(FloorKind kind, int seed, float puddleThreshold = 0.645f)
        {
            var s = new Surface(8 * CellsPerUnit, 8 * CellsPerUnit) { NormalStrength = 2.2f };
            var rnd = new Random(seed);
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                int k = s.I(x, y);
                float n1 = Fbm(x, y, s.W, s.H, 48, 3, seed), n2 = Fbm(x, y, s.W, s.H, 8, 2, seed + 3);
                float lum = 0.84f + (n1 - 0.5f) * 0.18f + (n2 - 0.5f) * 0.06f;
                float h = 0.5f, sm = 0.46f + n2 * 0.1f;
                switch (kind)
                {
                    case FloorKind.ConcreteDeck:
                    {
                        // 2×2-unit cast slabs with expansion joints holding water.
                        int sx = x / 48, sy = y / 48;
                        lum += (Hash(sx, sy, seed + 5) - 0.5f) * 0.1f;
                        if (x % 48 == 0 || y % 48 == 0) { lum *= 0.6f; h = 0.1f; sm = 0.75f; }
                        else if (x % 48 == 1 || y % 48 == 1) h = 0.4f;
                        break;
                    }
                    case FloorKind.Asphalt:
                        lum = 0.76f + (n1 - 0.5f) * 0.14f + (Hash(x, y, seed + 9) - 0.5f) * 0.05f;
                        break;
                    case FloorKind.Setts:
                    {
                        // Running-bond stone setts, 6×4 cells (0.25 × 0.17 units), 1-cell mortar.
                        int row = y / 4, ox = (row & 1) * 3;
                        int col = (x + ox) / 6;
                        int lx = (x + ox) % 6, ly = y % 4;
                        float tone = (Hash(col, row, seed + 11) - 0.5f) * 0.16f;
                        if (lx == 0 || ly == 0) { lum *= 0.58f; h = 0.1f; sm = 0.7f; }
                        else
                        {
                            lum += tone;
                            h = (lx == 1 || lx == 5 || ly == 1 || ly == 3) ? 0.6f : 0.8f;
                            if (Hash(col, row, seed + 12) < 0.04f) lum *= 0.7f; // odd dark stone
                        }
                        break;
                    }
                }
                s.Lum[k] = lum; s.Height[k] = h; s.Smooth[k] = sm;
            }
            if (kind == FloorKind.Asphalt)
            {
                // Repair patches: rectangles of a different tone with a dark tar-seal outline.
                for (int i = 0; i < 6; i++)
                {
                    int px = rnd.Next(s.W), py = rnd.Next(s.H), pw = 10 + rnd.Next(30), ph = 8 + rnd.Next(20);
                    float tone = (float)(rnd.NextDouble() - 0.5) * 0.16f;
                    for (int y = 0; y < ph; y++)
                    for (int x = 0; x < pw; x++)
                    {
                        int k = s.I(px + x, py + y);
                        bool edge = x == 0 || y == 0 || x == pw - 1 || y == ph - 1;
                        s.Lum[k] = edge ? s.Lum[k] * 0.7f : s.Lum[k] + tone;
                        if (edge) s.Height[k] = 0.3f;
                    }
                }
            }
            int cracks = kind == FloorKind.Setts ? 4 : 14;
            for (int i = 0; i < cracks; i++) Crack(s, rnd, 12 + rnd.Next(30), 0.72f);
            // Oil / soot stains: large soft-edged but quantised darkening.
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                float st = Fbm(x, y, s.W, s.H, 24, 2, seed + 21);
                if (st > 0.6f) s.Lum[s.I(x, y)] *= st > 0.66f ? 0.8f : 0.9f;
            }
            Puddles(s, seed + 31, puddleThreshold, kind == FloorKind.Setts ? 0.85f : 0.5f);
            if (kind == FloorKind.Setts) DullSetts(s);
            return s;
        }

        /// <summary>
        /// Setts carry a bevel on every stone (height steps every 2-3 cells), so with a gloss of 0.5-0.7 each bevel
        /// cell caught the lamp's specular lobe on its own: a dotted grid of warm 2-texel specks in every lit pool.
        /// Stones and mortar go matte (≤ 0.32), the bevel normals are softened, and puddle cells that touch dry
        /// stone (tilted normals from the height step) drop to 0.4 so only the flat water interior stays mirror-like.
        /// </summary>
        private static void DullSetts(Surface s)
        {
            s.NormalStrength = 1.1f;
            var wet = new bool[s.W * s.H];
            for (int i = 0; i < wet.Length; i++) wet[i] = s.Smooth[i] > 0.9f;
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                int k = s.I(x, y);
                if (!wet[k]) { s.Smooth[k] = Math.Min(s.Smooth[k], 0.32f); continue; }
                if (!wet[s.I(x - 1, y)] || !wet[s.I(x + 1, y)] || !wet[s.I(x, y - 1)] || !wet[s.I(x, y + 1)]) s.Smooth[k] = 0.4f;
            }
        }

        /// <summary>Platform top, 4×4 units (96×96 cells): bolted steel plates with rust and wet patches.</summary>
        public static Surface SlabTop(int seed)
        {
            var s = new Surface(4 * CellsPerUnit, 4 * CellsPerUnit) { NormalStrength = 1.8f };
            var rnd = new Random(seed);
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                int k = s.I(x, y);
                int px = x / 48, py = y / 24;
                int lx = x % 48, ly = y % 24;
                float n1 = Fbm(x, y, s.W, s.H, 24, 3, seed), n2 = Fbm(x, y, s.W, s.H, 6, 2, seed + 4);
                float lum = 0.84f + (Hash(px, py, seed + 2) - 0.5f) * 0.1f + (n1 - 0.5f) * 0.14f;
                float h = 0.6f, sm = 0.5f + n2 * 0.1f, me = 0.55f;
                if (lx == 0 || ly == 0) { lum *= 0.6f; h = 0.15f; sm = 0.7f; }
                else if ((lx == 3 || lx == 45) && (ly == 3 || ly == 21)) { lum *= 1.12f; h = 1f; } // bolt heads (2×2 texels)
                float rust = Fbm(x, y, s.W, s.H, 12, 2, seed + 8);
                if (rust > 0.62f) { lum *= 0.82f; sm = 0.3f; me = 0.15f; h -= 0.05f; }
                s.Lum[k] = lum; s.Height[k] = h; s.Smooth[k] = sm; s.Metal[k] = me;
            }
            for (int i = 0; i < 4; i++) Crack(s, rnd, 8 + rnd.Next(10), 0.8f); // scuffs
            Puddles(s, seed + 40, 0.68f, 0.55f);
            return s;
        }

        /// <summary>Platform front, 4 units wide × 1 unit tall (96×24 cells), V period one unit (one course).</summary>
        public static Surface Fascia(FasciaKind kind, int seed)
        {
            var s = new Surface(4 * CellsPerUnit, CellsPerUnit) { NormalStrength = 2f };
            var rnd = new Random(seed);
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                int k = s.I(x, y);
                float n1 = Fbm(x, y, s.W, s.H, 12, 2, seed), n2 = Fbm(x, y, s.W, s.H, 6, 1, seed + 1);
                float lum = 0.8f + (n1 - 0.5f) * 0.14f, h = 0.6f, sm = 0.35f, me = 0f;
                int top = s.H - 1 - y; // cells from the top edge
                switch (kind)
                {
                    case FasciaKind.RivetedSteel:
                    {
                        me = 0.5f; sm = 0.45f;
                        int plate = x / 24, lx = x % 24;
                        lum += (Hash(plate, 0, seed + 3) - 0.5f) * 0.08f;
                        if (top < 2) { lum *= 1.12f; h = 0.9f; }                // top lip catches light
                        else if (y < 3) { lum *= 0.72f; h = 0.85f; }            // bottom flange in shadow
                        else if (lx == 0) { lum *= 0.62f; h = 0.2f; }           // plate seam
                        else if ((top == 3 || y == 4) && x % 4 == 2) { lum *= 1.15f; h = 1f; } // rivets
                        break;
                    }
                    case FasciaKind.Concrete:
                    {
                        lum = 0.82f + (n1 - 0.5f) * 0.16f + (n2 - 0.5f) * 0.05f;
                        if (top < 2) { lum *= 1.08f; h = 0.85f; }
                        else if (y == 11) { lum *= 0.86f; h = 0.45f; }          // formwork board line
                        else if ((y == 6 || y == 17) && x % 24 == 12) { lum *= 0.55f; h = 0.1f; } // tie holes
                        break;
                    }
                    default:
                    {
                        // Brick courses 3 cells tall, 8 long, running bond; a stone coping on top.
                        if (top < 3) { lum = 0.9f + (n1 - 0.5f) * 0.08f; h = top == 0 ? 0.7f : 0.9f; break; }
                        int course = (y) / 3, ox = (course & 1) * 4;
                        int bx = (x + ox) / 8, lx = (x + ox) % 8, ly = y % 3;
                        if (lx == 0 || ly == 0) { lum *= 0.62f; h = 0.2f; }
                        else { lum += (Hash(bx, course, seed + 6) - 0.5f) * 0.16f; h = 0.75f; }
                        break;
                    }
                }
                s.Lum[k] = lum; s.Height[k] = h; s.Smooth[k] = sm; s.Metal[k] = me;
            }
            // Run-off streaks dripping from the top (2 cells wide, varied length), and dirt splash at the bottom.
            for (int i = 0; i < 9; i++)
            {
                int x = rnd.Next(s.W), len = 5 + rnd.Next(s.H - 6);
                float dark = 0.78f + (float)rnd.NextDouble() * 0.1f;
                for (int d = 2; d < len; d++)
                for (int w = 0; w < 2; w++)
                    s.Lum[s.I(x + w, s.H - 1 - d)] *= dark + (1f - dark) * d / (float)len;
            }
            for (int y = 0; y < 5; y++)
            for (int x = 0; x < s.W; x++)
                if (Fbm(x, y, s.W, s.H, 6, 1, seed + 9) > 0.45f + y * 0.06f) s.Lum[s.I(x, y)] *= 0.85f;
            return s;
        }

        /// <summary>Generic grime for structure and kit props, 2×2 units (48×48 cells): mottling, streaks, blotches.</summary>
        public static Surface Grime(int seed)
        {
            var s = new Surface(2 * CellsPerUnit, 2 * CellsPerUnit);
            var rnd = new Random(seed);
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                float n1 = Fbm(x, y, s.W, s.H, 24, 3, seed), n2 = Fbm(x, y, s.W, s.H, 4, 1, seed + 2);
                float lum = 0.84f + (n1 - 0.5f) * 0.2f + (n2 - 0.5f) * 0.05f;
                float blot = Fbm(x, y, s.W, s.H, 12, 2, seed + 5);
                if (blot > 0.63f) lum *= 0.82f;
                s.Lum[s.I(x, y)] = lum;
            }
            for (int i = 0; i < 10; i++)
            {
                int x = rnd.Next(s.W), y0 = rnd.Next(s.H), len = 6 + rnd.Next(24);
                float dark = 0.8f + (float)rnd.NextDouble() * 0.1f;
                for (int d = 0; d < len; d++)
                for (int w = 0; w < 2; w++)
                    s.Lum[s.I(x + w, y0 - d)] *= dark + (1f - dark) * d / (float)len;
            }
            return s;
        }

        /// <summary>Normal (x, y, z) per cell from <see cref="Surface.Height"/> (wrapped central differences).</summary>
        public static void NormalAt(Surface s, int x, int y, out float nx, out float ny, out float nz)
        {
            float hl = s.Height[s.I(x - 1, y)], hr = s.Height[s.I(x + 1, y)];
            float hd = s.Height[s.I(x, y - 1)], hu = s.Height[s.I(x, y + 1)];
            nx = (hl - hr) * s.NormalStrength; ny = (hd - hu) * s.NormalStrength; nz = 1f;
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= l; ny /= l; nz /= l;
        }

        /// <summary>Mean luminance of a surface (used to compensate material colours).</summary>
        public static float MeanLum(Surface s)
        {
            double sum = 0; foreach (var v in s.Lum) sum += v;
            return (float)(sum / s.Lum.Length);
        }

        /// <summary>Clamps every channel to 0..1.</summary>
        public static Surface Clamp(Surface s)
        {
            for (int i = 0; i < s.Lum.Length; i++)
            {
                s.Lum[i] = Clamp01(s.Lum[i]); s.Height[i] = Clamp01(s.Height[i]);
                s.Smooth[i] = Clamp01(s.Smooth[i]); s.Metal[i] = Clamp01(s.Metal[i]);
            }
            return s;
        }
    }
}
