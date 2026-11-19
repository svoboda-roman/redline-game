using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Procedural textures for the 2.5D render prototype: wet metal panels (albedo, normal, metallic/smoothness),
    /// lit-window facades, soft glow and gradient masks, rain streaks. All deterministic (fixed seeds).
    /// </summary>
    /// <remarks>Prototype-only. Builds textures once at scene build time; not per-frame code.</remarks>
    public static class Proto3DTextures
    {
        /// <summary>Panel set: one texture covers 4×4 world units (64 px per unit at 256 px).</summary>
        public sealed class PanelSet
        {
            /// <summary>sRGB albedo.</summary>
            public Texture2D Albedo;
            /// <summary>Linear tangent-space normal (rgb = xyz*0.5+0.5, a = 1; unpacks on both RGB and AG paths).</summary>
            public Texture2D Normal;
            /// <summary>Linear metallic (R) / smoothness (A) map for URP Lit _MetallicGlossMap.</summary>
            public Texture2D MetalGloss;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        private static Texture2D New(int w, int h, bool linear, TextureWrapMode wrap = TextureWrapMode.Repeat,
            FilterMode filter = FilterMode.Bilinear, bool mips = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mips, linear);
            t.wrapMode = wrap;
            t.filterMode = filter;
            t.anisoLevel = 4;
            return t;
        }

        /// <summary>
        /// Metal deck panels. <paramref name="wet"/> adds puddle patches: darker albedo, near-mirror smoothness.
        /// <paramref name="baseCol"/> is the mid albedo; seams, rivets, grime streaks and per-panel variation on top.
        /// </summary>
        public static PanelSet Panels(int seed, Color baseCol, bool wet, float panelW = 1f, float panelH = 1f)
        {
            const int N = 256;
            const float ppu = N / 4f;
            var albedo = New(N, N, false);
            var normal = New(N, N, true);
            var mg = New(N, N, true);
            var height = new float[N * N];
            var a = new Color[N * N];
            var m = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / ppu, v = y / ppu;
                int px = Mathf.FloorToInt(u / panelW), py = Mathf.FloorToInt(v / panelH);
                float fx = u / panelW - px, fy = v / panelH - py;
                float edge = Mathf.Min(Mathf.Min(fx, 1f - fx) * panelW, Mathf.Min(fy, 1f - fy) * panelH) * ppu; // px to seam
                float seam = Mathf.Clamp01(1f - (edge - 1f) / 1.5f);
                float panelVar = Hash(px, py, seed) * 0.25f - 0.12f;
                // Rivets near the corners of each panel.
                float rx = Mathf.Min(fx, 1f - fx) * panelW * ppu, ry = Mathf.Min(fy, 1f - fy) * panelH * ppu;
                float rivet = Mathf.Clamp01(1f - (Mathf.Sqrt((rx - 5) * (rx - 5) + (ry - 5) * (ry - 5)) - 1.2f));
                float n1 = Mathf.PerlinNoise(x * 0.045f + seed, y * 0.045f + seed * 0.7f);
                float n2 = Mathf.PerlinNoise(x * 0.18f + seed * 3f, y * 0.18f);
                float streak = Mathf.PerlinNoise(x * 0.35f + seed, y * 0.012f) * Mathf.PerlinNoise(x * 0.05f, y * 0.05f + seed);
                float h = (1f - seam) * 0.6f + rivet * 0.4f + n2 * 0.05f;
                height[y * N + x] = h;
                float lum = 1f + panelVar + (n1 - 0.5f) * 0.35f - seam * 0.55f + rivet * 0.25f - streak * 0.35f;
                float puddle = 0f;
                if (wet)
                {
                    float pn = Mathf.PerlinNoise(x * 0.012f + seed * 1.3f, y * 0.03f + 11f) * 0.75f
                               + Mathf.PerlinNoise(x * 0.05f, y * 0.05f + seed) * 0.25f;
                    puddle = Mathf.Clamp01((pn - 0.5f) * 8f);
                    lum *= 1f - 0.45f * puddle;
                }
                var c = baseCol * lum;
                c.a = 1f;
                a[y * N + x] = c;
                float smooth = wet ? Mathf.Lerp(0.5f + n2 * 0.15f, 0.9f, puddle) : 0.45f + n2 * 0.15f - streak * 0.2f;
                smooth *= 1f - seam * 0.4f;
                m[y * N + x] = new Color(wet ? Mathf.Lerp(0.15f, 0f, puddle) : 0.6f, 0f, 0f, smooth);
            }
            var nrm = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float hl = height[y * N + (x + N - 1) % N], hr = height[y * N + (x + 1) % N];
                float hd = height[((y + N - 1) % N) * N + x], hu = height[((y + 1) % N) * N + x];
                var n = new Vector3((hl - hr) * 2.2f, (hd - hu) * 2.2f, 1f).normalized;
                nrm[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            albedo.SetPixels(a); albedo.Apply(true);
            normal.SetPixels(nrm); normal.Apply(true);
            mg.SetPixels(m); mg.Apply(true);
            return new PanelSet { Albedo = albedo, Normal = normal, MetalGloss = mg };
        }

        /// <summary>
        /// Facade window grid, 8×8 windows per texture. Returns albedo (dark wall, dark glass) and emission
        /// (mostly off, a few warm sodium, rare cool). <paramref name="litFraction"/> = share of lit windows.
        /// </summary>
        public static (Texture2D albedo, Texture2D emission) Windows(int seed, float litFraction)
        {
            const int N = 256, Cells = 8, C = N / Cells;
            var al = New(N, N, false, TextureWrapMode.Repeat, FilterMode.Bilinear);
            var em = New(N, N, false, TextureWrapMode.Repeat, FilterMode.Bilinear);
            var a = new Color[N * N];
            var e = new Color[N * N];
            var rnd = new System.Random(seed);
            var cellCol = new Color[Cells * Cells];
            for (int i = 0; i < cellCol.Length; i++)
            {
                double r = rnd.NextDouble();
                Color lit = Color.black;
                if (r < litFraction * 0.65) lit = new Color(1f, 0.55f, 0.22f) * (0.5f + (float)rnd.NextDouble() * 0.5f);
                else if (r < litFraction * 0.85) lit = new Color(1f, 0.78f, 0.5f) * (0.4f + (float)rnd.NextDouble() * 0.4f);
                else if (r < litFraction) lit = new Color(0.35f, 0.75f, 1f) * (0.4f + (float)rnd.NextDouble() * 0.4f);
                cellCol[i] = lit;
            }
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int cx = x / C, cy = y / C;
                int lx = x % C, ly = y % C;
                bool glass = lx >= 6 && lx < C - 6 && ly >= 8 && ly < C - 5;
                float n = Mathf.PerlinNoise(x * 0.07f + seed, y * 0.07f);
                Color wall = new Color(0.09f, 0.1f, 0.12f) * (0.8f + n * 0.4f);
                if (ly < 3) wall *= 0.6f; // floor slab line
                Color col = wall, emc = Color.black;
                if (glass)
                {
                    var lit = cellCol[cy * Cells + cx];
                    col = lit.maxColorComponent > 0 ? lit * 0.3f : new Color(0.05f, 0.06f, 0.08f);
                    // blinds: horizontal bands inside lit windows
                    float blind = (ly % 4 == 0) ? 0.6f : 1f;
                    emc = lit * blind;
                }
                col.a = 1f;
                emc.a = 1f;
                a[y * N + x] = col;
                e[y * N + x] = emc;
            }
            al.SetPixels(a); al.Apply(true);
            em.SetPixels(e); em.Apply(true);
            return (al, em);
        }

        /// <summary>Radial soft blob (white, alpha = (1-r²)^p) for glows, splashes and haze puffs.</summary>
        public static Texture2D Blob(int size = 64, float power = 2f)
        {
            var t = New(size, size, false, TextureWrapMode.Clamp, FilterMode.Bilinear, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float v = Mathf.Pow(Mathf.Clamp01(1f - (dx * dx + dy * dy)), power);
                px[y * size + x] = new Color(v, v, v, v);
            }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        /// <summary>
        /// Haze band: vertical gradient (dense at the bottom, fading up) with horizontal soft edges and a
        /// low-frequency cloudy variation. Used on large fog cards.
        /// </summary>
        public static Texture2D HazeBand(int seed, int w = 256, int h = 128)
        {
            var t = New(w, h, false, TextureWrapMode.Clamp, FilterMode.Bilinear, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float vert = Mathf.Pow(1f - v, 1.6f) * Mathf.SmoothStep(0f, 1f, v * 6f);
                float side = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 5f);
                float cloud = 0.6f + 0.4f * Mathf.PerlinNoise(u * 5f + seed, v * 2.5f + seed * 0.3f);
                float val = vert * side * cloud;
                px[y * w + x] = new Color(1f, 1f, 1f, val);
            }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        /// <summary>Light shaft gradient along V: bright under the lamp head (v = 1), fading to the floor (v = 0).</summary>
        public static Texture2D Shaft(int h = 64)
        {
            var t = New(4, h, false, TextureWrapMode.Clamp, FilterMode.Bilinear, false);
            var px = new Color[4 * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                float val = Mathf.Pow(v, 1.4f) * Mathf.SmoothStep(0f, 1f, (1f - v) * 12f);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(val, val, val, val);
            }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        /// <summary>Rain streak: thin vertical line, soft across, tapered along.</summary>
        public static Texture2D Streak()
        {
            const int W = 8, H = 64;
            var t = New(W, H, false, TextureWrapMode.Clamp, FilterMode.Bilinear, false);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H;
                float val = Mathf.Clamp01(1f - u * u * 1.2f) * Mathf.Sin(v * Mathf.PI);
                px[y * W + x] = new Color(1f, 1f, 1f, val);
            }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        /// <summary>Vertical sky gradient (bottom = horizon glow, top = deep night), opaque.</summary>
        public static Texture2D Sky(Color horizon, Color zenith, Color glow)
        {
            const int H = 128;
            var t = New(2, H, false, TextureWrapMode.Clamp, FilterMode.Bilinear, false);
            var px = new Color[2 * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                var c = Color.Lerp(horizon, zenith, Mathf.SmoothStep(0f, 1f, v));
                c += glow * Mathf.Pow(1f - v, 4f);
                c.a = 1f;
                px[y * 2] = px[y * 2 + 1] = c;
            }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        /// <summary>
        /// Loads a PNG from disk into a readable texture. <paramref name="linear"/> for data maps (normals).
        /// Used for the character normal sheets, which are imported as sRGB default textures for the 2D game.
        /// </summary>
        public static Texture2D LoadPng(string path, bool linear, FilterMode filter = FilterMode.Point)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            t.LoadImage(System.IO.File.ReadAllBytes(path), false);
            t.filterMode = filter;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>Forces alpha to 1 on a normal sheet so it unpacks on both RGB and RG/AG normal paths.</summary>
        public static void OpaqueNormals(Texture2D t)
        {
            var px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a == 0) { px[i].r = 128; px[i].g = 128; px[i].b = 255; }
                px[i].a = 255;
            }
            t.SetPixels32(px);
            t.Apply(false);
        }
    }
}
