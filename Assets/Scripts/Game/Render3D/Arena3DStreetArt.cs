using System.Collections.Generic;
using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Generated art for the neon street (<see cref="Arena3DLook.NeonStreet"/>): facade textures for several building
    /// types (panel block, brick tenement, glass office tower, ribbed concrete tower), a billboard atlas of made-up brands
    /// (logo mark + wordmark in a 5x7 pixel font + Hangul tagline, in several layouts), a star field and a half moon
    /// for the night sky. All point filtered to match the 48 texel/unit pixel art. Deterministic per seed.
    /// </summary>
    public sealed partial class Arena3DRenderer
    {
        /// <summary>Number of facade styles: 0 plaster (Arena3DNeonStreet.FacadeTexture), 1 panel block, 2 brick, 3 glass, 4 concrete.</summary>
        private const int FacadeStyles = 5;

        private readonly List<SignCell> _boardCells = new List<SignCell>(32);
        private Material _streetBoardMat;

        // ------------------------------------------------------------------------------------------------
        // Night sky: stars and a half moon on the right
        // ------------------------------------------------------------------------------------------------

        private void StreetSky()
        {
            // Stars: an additive, unfogged dot field just in front of the sky quad, fading toward the horizon.
            var stars = Own(StarTexture(_look.Seed));
            var starMat = Own(Proto3DMaterials.FxMat("stars", stars, Color.white * 1.3f, 0, 0f, 0f, 390));
            var sq = MeshGo("stars", _follow, Own(Proto3DMeshes.Quad(1600f, 520f)), starMat);
            sq.transform.localPosition = new Vector3(0f, 170f, 425f);
            NoShadow(Hidden(sq));
            // Half moon high on the right, with a soft halo.
            var moon = Own(MoonTexture());
            var moonMat = Own(Proto3DMaterials.FxMat("moon", moon, Color.white * 1.15f, 0, 0f, 0f, 392));
            var mq = MeshGo("moon", _follow, Own(Proto3DMeshes.Quad(26f, 26f)), moonMat);
            mq.transform.localPosition = new Vector3(105f, 78f, 410f);
            NoShadow(Hidden(mq));
            var haloMat = Own(Proto3DMaterials.FxMat("moonHalo", _blob, Color.white, 0, 0f, 0f, 391));
            var halo = new QuadBatch();
            halo.Billboard(new Vector3(105f, 78f, 412f), 95f, 95f, new Color(0.35f, 0.32f, 0.45f));
            halo.Billboard(new Vector3(105f, 78f, 412f), 45f, 45f, new Color(0.4f, 0.38f, 0.45f));
            NoShadow(Hidden(MeshGo("moonHalo", _follow, Own(halo.Build("moonHalo")), haloMat)));
            // Thin cloud streaks lit from below by the city (pink), one passing near the moon (cool).
            var clouds = new QuadBatch();
            var crnd = new System.Random(_look.Seed * 5 + 1);
            for (int i = 0; i < 9; i++)
            {
                float cx = -260f + (float)crnd.NextDouble() * 520f, cy = 30f + (float)crnd.NextDouble() * 60f;
                float cw = 110f + (float)crnd.NextDouble() * 180f, ch = 7f + (float)crnd.NextDouble() * 12f;
                var cc = Color.Lerp(new Color(0.09f, 0.03f, 0.07f), new Color(0.05f, 0.04f, 0.08f), (cy - 30f) / 60f);
                clouds.Billboard(new Vector3(cx, cy, 400f), cw, ch, cc);
                clouds.Billboard(new Vector3(cx + cw * 0.2f, cy + ch * 0.3f, 400f), cw * 0.6f, ch * 0.7f, cc * 0.8f);
            }
            clouds.Billboard(new Vector3(90f, 72f, 405f), 120f, 9f, new Color(0.1f, 0.1f, 0.16f));
            NoShadow(Hidden(MeshGo("clouds", _follow, Own(clouds.Build("clouds")), haloMat)));
        }

        private static Texture2D StarTexture(int seed)
        {
            const int W = 1024, H = 512;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "stars" };
            var px = new Color[W * H];
            var rnd = new System.Random(seed * 13 + 3);
            for (int i = 0; i < 1400; i++)
            {
                int x = rnd.Next(W), y = rnd.Next(H);
                float v = (float)rnd.NextDouble();
                float h = y / (float)H;                                   // 0 bottom (horizon) .. 1 top
                float fade = Mathf.SmoothStep(0.35f, 0.75f, h);
                float b = (0.25f + v * v * 0.9f) * fade;
                var tint = rnd.NextDouble() < 0.15 ? new Color(1f, 0.85f, 0.7f) : rnd.NextDouble() < 0.15 ? new Color(0.75f, 0.85f, 1f) : Color.white;
                px[y * W + x] = tint * b;
                if (v > 0.93f && x > 1 && y > 1 && x < W - 2 && y < H - 2)   // a few bright stars with a cross
                {
                    var c = tint * b * 0.45f;
                    px[y * W + x - 1] = c; px[y * W + x + 1] = c; px[(y - 1) * W + x] = c; px[(y + 1) * W + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        /// <summary>Half moon (lit right half, a soft terminator), pale with a few darker maria. RGB premultiplied.</summary>
        private static Texture2D MoonTexture()
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "moon" };
            var px = new Color[N * N];
            var maria = new[] { new Vector3(0.25f, 0.2f, 0.18f), new Vector3(0.45f, -0.25f, 0.14f), new Vector3(0.15f, -0.45f, 0.1f), new Vector3(0.55f, 0.35f, 0.09f) };
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 0.92f) continue;
                float lit = Mathf.SmoothStep(-0.08f, 0.08f, dx);            // right half lit
                var c = new Color(1f, 0.97f, 0.86f);
                foreach (var m in maria) if ((new Vector2(dx, dy) - new Vector2(m.x, m.y)).sqrMagnitude < m.z * m.z) c *= 0.8f;
                c *= lit * (0.9f + 0.1f * (1f - r));
                float a = lit;
                c.a = a;
                px[y * N + x] = c;
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        // ------------------------------------------------------------------------------------------------
        // Facade styles
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Tileable facade for <paramref name="style"/> 1..4, 128 px = 8 units: 1 panel block (concrete panels, strip
        /// windows, coloured loggia bands), 2 brick tenement (bricks, tall windows with sills), 3 glass office tower
        /// (blue-teal curtain wall, mullion grid, lit floors), 4 ribbed concrete tower (vertical ribs, narrow windows).
        /// </summary>
        private static Texture2D FacadeStyleTexture(int seed, int style)
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, name = "streetFacade" + style };
            var rnd = new System.Random(seed * 31 + style * 7);
            var px = new Color[N * N];
            float R() => (float)rnd.NextDouble();
            void Fill(int x0, int y0, int w, int h, Color c) { for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) px[((y % N + N) % N) * N + ((x % N + N) % N)] = c; }
            Color Lit() => R() < 0.55f ? new Color(1f, 0.8f, 0.5f) : R() < 0.5f ? new Color(1f, 0.6f, 0.75f) : new Color(0.65f, 0.85f, 1f);
            var dark = new Color(0.12f, 0.12f, 0.2f);
            switch (style)
            {
                case 1: // panel block
                {
                    var conc = new Color(0.55f, 0.52f, 0.55f);
                    for (int i = 0; i < px.Length; i++) { float n = R() * 0.05f; px[i] = new Color(conc.r + n, conc.g + n, conc.b + n, 1f); }
                    for (int y = 0; y < N; y += 32) Fill(0, y, N, 1, conc * 0.6f);           // panel joints
                    for (int x = 0; x < N; x += 32) Fill(x, 0, 1, N, conc * 0.6f);
                    for (int fy = 0; fy < 4; fy++)
                    {
                        var loggia = new[] { new Color(0.85f, 0.35f, 0.4f), new Color(0.9f, 0.7f, 0.3f), new Color(0.4f, 0.55f, 0.75f), new Color(0.6f, 0.75f, 0.5f) }[rnd.Next(4)];
                        Fill(0, fy * 32 + 3, N, 6, loggia * 0.8f);                             // loggia parapet band
                        for (int fx = 0; fx < 4; fx++)
                        {
                            var c = R() < 0.6f ? Lit() : dark;
                            Fill(fx * 32 + 4, fy * 32 + 12, 24, 14, new Color(0.15f, 0.14f, 0.16f));
                            Fill(fx * 32 + 5, fy * 32 + 13, 22, 12, c);
                            Fill(fx * 32 + 15, fy * 32 + 13, 1, 12, c * 0.5f);
                        }
                    }
                    break;
                }
                case 2: // brick tenement
                {
                    var brick = new Color(0.52f, 0.26f, 0.22f);
                    for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        bool mortar = y % 4 == 0 || ((x + (y / 4 % 2) * 4) % 8 == 0);
                        float n = R() * 0.08f;
                        px[y * N + x] = mortar ? new Color(0.33f, 0.28f, 0.28f) : new Color(brick.r + n, brick.g + n * 0.5f, brick.b + n * 0.5f);
                    }
                    for (int fy = 0; fy < 4; fy++)
                    for (int fx = 0; fx < 4; fx++)
                    {
                        int x0 = fx * 32 + 9, y0 = fy * 32 + 6;
                        var c = R() < 0.55f ? Lit() : dark;
                        Fill(x0 - 1, y0 - 2, 16, 2, new Color(0.6f, 0.58f, 0.55f));            // sill
                        Fill(x0, y0, 14, 22, new Color(0.18f, 0.12f, 0.12f));
                        Fill(x0 + 1, y0 + 1, 12, 20, c);
                        Fill(x0 + 1, y0 + 11, 12, 1, c * 0.5f);
                        Fill(x0 + 6, y0 + 1, 1, 20, c * 0.5f);
                        Fill(x0 - 1, y0 + 22, 16, 3, new Color(0.45f, 0.24f, 0.2f));          // lintel
                    }
                    break;
                }
                case 3: // glass office tower
                {
                    for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float v = y / (float)N;
                        px[y * N + x] = Color.Lerp(new Color(0.1f, 0.2f, 0.3f), new Color(0.18f, 0.32f, 0.42f), v) + new Color(R() * 0.02f, R() * 0.02f, R() * 0.03f);
                    }
                    for (int fy = 0; fy < 8; fy++)
                    {
                        bool litFloor = R() < 0.45f;
                        var lc = R() < 0.7f ? new Color(0.8f, 0.92f, 1f) : new Color(1f, 0.85f, 0.6f);
                        if (litFloor) for (int fx = 0; fx < 8; fx++) if (R() < 0.8f) Fill(fx * 16 + 1, fy * 16 + 4, 14, 10, lc * (0.75f + R() * 0.25f));
                        Fill(0, fy * 16, N, 2, new Color(0.08f, 0.1f, 0.14f));                 // spandrel
                    }
                    for (int x = 0; x < N; x += 16) Fill(x, 0, 1, N, new Color(0.25f, 0.3f, 0.35f));   // mullions
                    break;
                }
                default: // ribbed concrete tower
                {
                    var conc = new Color(0.42f, 0.4f, 0.46f);
                    for (int i = 0; i < px.Length; i++) { float n = R() * 0.05f; px[i] = new Color(conc.r + n, conc.g + n, conc.b + n, 1f); }
                    for (int x = 0; x < N; x += 16) { Fill(x, 0, 3, N, conc * 1.25f); Fill(x + 3, 0, 1, N, conc * 0.6f); }
                    for (int fy = 0; fy < 8; fy++)
                    for (int fx = 0; fx < 8; fx++)
                    {
                        var c = R() < 0.5f ? Lit() : dark;
                        Fill(fx * 16 + 6, fy * 16 + 4, 7, 9, c);
                    }
                    break;
                }
            }
            // Grime streaks down from the windows.
            for (int k = 0; k < 40; k++) { int gx = rnd.Next(N), gy = rnd.Next(N), gl = 4 + rnd.Next(14); for (int y = gy; y > gy - gl; y--) { int i = ((y % N + N) % N) * N + gx; px[i] = px[i] * 0.88f; px[i].a = 1f; } }
            for (int i = 0; i < px.Length; i++) px[i].a = 1f;
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        // ------------------------------------------------------------------------------------------------
        // Billboards: made-up brands
        // ------------------------------------------------------------------------------------------------

        private static readonly string[] BrandHead = { "NEO", "KURO", "ZEN", "RYU", "HANA", "TAKA", "VOLT", "NOVA", "SORA", "KAI", "MIRA", "OKA", "YAMI", "JIN", "AKI", "SEO", "MOMO", "TORA" };
        private static readonly string[] BrandTail = { "TEK", "SOFT", "COLA", "RAMEN", "MART", "BANK", "CORP", "AIR", "FONE", "LUX", "NET", "GAS", "BEER", "GO", "DYNE", "SODA" };

        /// <summary>5x7 pixel font rows (top first) for A-Z, 0-9 and a few signs.</summary>
        private static readonly Dictionary<char, string> Font = new Dictionary<char, string>
        {
            ['A'] = "01110100011000111111100011000110001", ['B'] = "11110100011000111110100011000111110",
            ['C'] = "01110100011000010000100001000101110", ['D'] = "11110100011000110001100011000111110",
            ['E'] = "11111100001000011110100001000011111", ['F'] = "11111100001000011110100001000010000",
            ['G'] = "01110100011000010111100011000101111", ['H'] = "10001100011000111111100011000110001",
            ['I'] = "01110001000010000100001000010001110", ['J'] = "00111000100001000010000101001001100",
            ['K'] = "10001100101010011000101001001010001", ['L'] = "10000100001000010000100001000011111",
            ['M'] = "10001110111010110101100011000110001", ['N'] = "10001100011100110101100111000110001",
            ['O'] = "01110100011000110001100011000101110", ['P'] = "11110100011000111110100001000010000",
            ['Q'] = "01110100011000110001101011001001101", ['R'] = "11110100011000111110101001001010001",
            ['S'] = "01111100001000001110000010000111110", ['T'] = "11111001000010000100001000010000100",
            ['U'] = "10001100011000110001100011000101110", ['V'] = "10001100011000110001100010101000100",
            ['W'] = "10001100011000110101101011010101010", ['X'] = "10001100010101000100010101000110001",
            ['Y'] = "10001100010101000100001000010000100", ['Z'] = "11111000010001000100010001000011111",
            ['0'] = "01110100011001110101110011000101110", ['1'] = "00100011000010000100001000010001110",
            ['2'] = "01110100010000100010001000100011111", ['3'] = "11110000010000101110000010000111110",
            ['4'] = "00010001100101010010111110001000010", ['5'] = "11111100001111000001000011000101110",
            ['6'] = "00110010001000011110100011000101110", ['7'] = "11111000010001000100010000100001000",
            ['8'] = "01110100011000101110100011000101110", ['9'] = "01110100011000101111000010001001100",
            ['-'] = "00000000000000011111000000000000000", ['.'] = "00000000000000000000000000110001100",
        };

        /// <summary>
        /// Billboard atlas 512x512: 32 boards of 128x64 (4 x 8), each a made-up brand: a logo mark (ring, triangle,
        /// diamond, sunburst, bolt, wave, hexagon or initial in a circle) and the brand name in the 5x7 pixel font,
        /// laid out as a colour panel, a dark panel with neon wordmark, a product shot (can / bottle / phone + price) or
        /// a split-colour poster. Some carry a Hangul tagline.
        /// </summary>
        private Texture2D BoardAtlas(int seed, Color[] palette, List<SignCell> cells)
        {
            const int N = 512, BW = 128, BH = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "streetBoards" };
            var px = new Color[N * N];
            var rnd = new System.Random(seed * 17 + 11);
            float R() => (float)rnd.NextDouble();
            void Fill(int x0, int y0, int w, int h, Color c) { for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) if (x >= 0 && y >= 0 && x < N && y < N) px[y * N + x] = c; }
            void Disc(int cx, int cy, float r, Color c) { for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++) for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++) if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) Fill(x, y, 1, 1, c); }
            void Text(string s, int x0, int y0, int scale, Color c)
            {
                int x = x0;
                foreach (char ch in s)
                {
                    if (Font.TryGetValue(ch, out var g))
                        for (int row = 0; row < 7; row++) for (int col = 0; col < 5; col++)
                            if (g[row * 5 + col] == '1') Fill(x + col * scale, y0 + (6 - row) * scale, scale, scale, c);
                    x += 6 * scale;
                }
            }
            int TextW(string s, int scale) => s.Length * 6 * scale - scale;
            var brights = new List<Color>();
            if (palette != null) foreach (var p in palette) brights.Add(new Color(Mathf.Clamp01(p.r / 1.4f), Mathf.Clamp01(p.g / 1.4f), Mathf.Clamp01(p.b / 1.4f)));
            brights.Add(new Color(0.3f, 0.85f, 1f)); brights.Add(new Color(0.45f, 1f, 0.55f)); brights.Add(new Color(1f, 0.55f, 0.15f)); brights.Add(new Color(0.75f, 0.45f, 1f));
            Color Pick() => brights[rnd.Next(brights.Count)];

            void Logo(int cx, int cy, int r, Color a, Color b, char initial)
            {
                switch (rnd.Next(8))
                {
                    case 0: Disc(cx, cy, r, a); Disc(cx, cy, r * 0.6f, b); Disc(cx, cy, r * 0.3f, a); break;                       // target ring
                    case 1: for (int y = 0; y < 2 * r; y++) Fill(cx - (2 * r - y) / 2, cy - r + y, 2 * r - y, 1, a); break;     // triangle
                    case 2: for (int y = -r; y <= r; y++) Fill(cx - (r - Mathf.Abs(y)), cy + y, 2 * (r - Mathf.Abs(y)) + 1, 1, a); Disc(cx, cy, r * 0.35f, b); break; // diamond
                    case 3: Disc(cx, cy, r * 0.45f, a); for (int k = 0; k < 8; k++) { float an = k * Mathf.PI / 4f; for (int d = (int)(r * 0.6f); d <= r; d++) Fill(cx + Mathf.RoundToInt(Mathf.Cos(an) * d), cy + Mathf.RoundToInt(Mathf.Sin(an) * d), 2, 2, a); } break; // sun
                    case 4: for (int y = 0; y < r; y++) Fill(cx - r / 3 + y / 2, cy + y, r / 2, 1, a); for (int y = 0; y < r; y++) Fill(cx - r / 2 + y / 2 - r / 4, cy - y, r / 2, 1, a); break; // bolt
                    case 5: for (int k = 0; k < 3; k++) for (int x = -r; x <= r; x++) Fill(cx + x, cy - r / 2 + k * r / 2 + Mathf.RoundToInt(Mathf.Sin(x * 0.5f) * 2f), 1, 2, k % 2 == 0 ? a : b); break; // waves
                    case 6: for (int y = -r; y <= r; y++) { int hw = r - Mathf.Max(0, Mathf.Abs(y) - r / 2); Fill(cx - hw, cy + y, 2 * hw + 1, 1, a); } Disc(cx, cy, r * 0.4f, b); break; // hexagon
                    default: Disc(cx, cy, r, a); Text(initial.ToString(), cx - 4, cy - 6, 2, b); break;                     // initial in a disc
                }
            }

            for (int i = 0; i < N * N; i++) px[i] = Color.black;
            for (int row = 0; row < 8; row++)
            for (int col = 0; col < 4; col++)
            {
                int x0 = col * BW, y0 = row * BH;
                string name = BrandHead[rnd.Next(BrandHead.Length)] + BrandTail[rnd.Next(BrandTail.Length)];
                if (name.Length > 9) name = name.Substring(0, 9);
                var a = Pick();
                var b = Pick();
                if (b == a) b = Color.white;
                int layout = rnd.Next(4);
                int sc = TextW(name, 2) <= 80 ? 2 : 1;
                switch (layout)
                {
                    case 0: // colour panel: logo left, name right, tagline under
                    {
                        Fill(x0, y0, BW, BH, a * 0.85f);
                        Fill(x0, y0, BW, 3, a * 0.5f);
                        Logo(x0 + 22, y0 + 34, 14, Color.white, a, name[0]);
                        Text(name, x0 + 42, y0 + 30, sc, Color.white);
                        for (int k = 0; k < 4; k++) Fill(x0 + 42 + k * 12, y0 + 14, 9, 7, new Color(1f, 1f, 1f) * 0.85f);
                        break;
                    }
                    case 1: // dark panel, neon wordmark centred, logo above
                    {
                        Fill(x0, y0, BW, BH, new Color(0.05f, 0.04f, 0.08f));
                        Fill(x0, y0, BW, 2, a); Fill(x0, y0 + BH - 2, BW, 2, a); Fill(x0, y0, 2, BH, a); Fill(x0 + BW - 2, y0, 2, BH, a);
                        Logo(x0 + BW / 2, y0 + 44, 10, a, b, name[0]);
                        Text(name, x0 + (BW - TextW(name, sc)) / 2, y0 + 12, sc, a);
                        break;
                    }
                    case 2: // product shot: can / bottle / phone on the left, name + price
                    {
                        for (int y = 0; y < BH; y++) Fill(x0, y0 + y, BW, 1, Color.Lerp(a * 0.55f, a, y / (float)BH));
                        int kind = rnd.Next(3);
                        if (kind == 0) { Fill(x0 + 14, y0 + 8, 18, 40, b); Fill(x0 + 16, y0 + 48, 14, 3, Color.white * 0.8f); Fill(x0 + 14, y0 + 24, 18, 6, Color.white); }   // can
                        else if (kind == 1) { Fill(x0 + 16, y0 + 8, 14, 30, b); Fill(x0 + 20, y0 + 38, 6, 12, b); Fill(x0 + 21, y0 + 50, 4, 3, Color.white); Fill(x0 + 16, y0 + 18, 14, 8, Color.white); } // bottle
                        else { Fill(x0 + 12, y0 + 6, 22, 44, new Color(0.08f, 0.08f, 0.1f)); Fill(x0 + 14, y0 + 10, 18, 36, b); Disc(x0 + 23, y0 + 28, 5, Color.white); } // phone
                        Text(name, x0 + 42, y0 + 36, sc, Color.white);
                        string price = (1 + rnd.Next(9)).ToString() + (rnd.Next(2) == 0 ? "900" : "500");
                        Text(price, x0 + 42, y0 + 12, 2, b);
                        break;
                    }
                    default: // split poster: diagonal two-colour field, big logo, name on a bar
                    {
                        for (int y = 0; y < BH; y++) for (int x = 0; x < BW; x++) px[(y0 + y) * N + x0 + x] = x + y * 0.8f < BW * 0.55f ? a : b * 0.85f;
                        Logo(x0 + 34, y0 + 36, 18, Color.white, a, name[0]);
                        Fill(x0 + 56, y0 + 8, BW - 60, 18, new Color(0.06f, 0.05f, 0.08f));
                        Text(name, x0 + 60, y0 + 10, 1, b);
                        Text("NEW", x0 + 60 + TextW(name, 1) + 6, y0 + 10, 1, Color.white);
                        break;
                    }
                }
                cells.Add(new SignCell { Uv = new Rect(x0 / (float)N, y0 / (float)N, BW / (float)N, BH / (float)N), Kind = 3, Tint = a });
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        private SignCell PickBoard(System.Random rnd) => _boardCells[rnd.Next(_boardCells.Count)];

        // ------------------------------------------------------------------------------------------------
        // Neon tube lettering (additive: black = transparent)
        // ------------------------------------------------------------------------------------------------

        private readonly List<SignCell> _tubeCells = new List<SignCell>(16);
        private Material _streetTubeMat;

        private SignCell PickTube(System.Random rnd, bool vertical)
        {
            int start = rnd.Next(_tubeCells.Count);
            for (int k = 0; k < _tubeCells.Count; k++)
            {
                var c = _tubeCells[(start + k) % _tubeCells.Count];
                if ((c.Kind == 0) == vertical) return c;
            }
            return _tubeCells[0];
        }

        /// <summary>
        /// Neon tube sign atlas 512x512, drawn for additive blending (black = clear): 8 horizontal signs (256x64) and
        /// 8 vertical (64x256). Each glyph is a hand-bent tube: 2-3 rounded strokes (curved hooks, loops, sweeps), the
        /// odd dakuten pair and a final "!", in a coloured outer tube with a near-white hot core; letters alternate
        /// colours like real neon lettering, behind them a faint wavy acrylic backing outline.
        /// </summary>
        private static Texture2D NeonTubeAtlas(int seed, Color[] palette, List<SignCell> cells)
        {
            const int N = 512;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "neonTubes" };
            var px = new Color[N * N];
            for (int i = 0; i < px.Length; i++) px[i] = Color.black;
            var rnd = new System.Random(seed * 37 + 9);
            float R() => (float)rnd.NextDouble();
            void Dot(float cx, float cy, float r, Color c)
            {
                for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                    if (x >= 0 && y >= 0 && x < N && y < N && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) px[y * N + x] = c;
            }
            var cols = new List<Color> { new Color(1f, 0.82f, 0.15f), new Color(0.95f, 0.97f, 1f) };
            if (palette != null) foreach (var p in palette) cols.Add(new Color(Mathf.Clamp01(p.r / 1.4f), Mathf.Clamp01(p.g / 1.4f), Mathf.Clamp01(p.b / 1.4f)));
            cols.Add(new Color(0.3f, 0.9f, 1f));

            // One glyph in the box (x0, y0, s): returns stroke sample points (tube centre line).
            List<Vector2> Glyph(float x0, float y0, float s, bool bang)
            {
                var pts = new List<Vector2>();
                void Bez(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
                {
                    for (int i = 0; i <= 24; i++)
                    {
                        float u = i / 24f, iu = 1f - u;
                        pts.Add(iu * iu * iu * a + 3f * iu * iu * u * b + 3f * iu * u * u * c + u * u * u * d);
                    }
                }
                Vector2 P(float u, float v) => new Vector2(x0 + u * s, y0 + v * s);   // u right, v up (0..1)
                if (bang)
                {
                    Bez(P(0.5f, 0.95f), P(0.5f, 0.75f), P(0.5f, 0.55f), P(0.5f, 0.35f));
                    pts.Add(P(0.5f, 0.12f)); pts.Add(P(0.5f, 0.1f));
                    return pts;
                }
                int strokes = 2 + rnd.Next(2);
                for (int k = 0; k < strokes; k++)
                {
                    switch (rnd.Next(5))
                    {
                        case 0: Bez(P(0.1f, 0.75f + R() * 0.15f), P(0.4f, 0.85f), P(0.7f, 0.8f), P(0.9f, 0.7f + R() * 0.15f)); break;                 // top sweep
                        case 1: Bez(P(0.35f + R() * 0.2f, 0.95f), P(0.3f, 0.6f), P(0.25f, 0.3f), P(0.15f + R() * 0.2f, 0.05f)); break;               // falling stroke
                        case 2: { float cx = 0.45f + R() * 0.15f, cy = 0.35f + R() * 0.15f, r = 0.22f + R() * 0.08f; for (int i = 0; i <= 30; i++) { float an = Mathf.PI * (0.6f + i / 30f * 1.7f); pts.Add(P(cx + Mathf.Cos(an) * r, cy + Mathf.Sin(an) * r)); } break; } // loop
                        case 3: Bez(P(0.6f, 0.9f), P(0.65f, 0.45f), P(0.7f, 0.1f), P(0.95f, 0.2f)); break;                                          // hook
                        default: Bez(P(0.1f, 0.45f), P(0.45f, 0.65f), P(0.6f, 0.2f), P(0.9f, 0.35f)); break;                                        // wave
                    }
                }
                if (R() < 0.3f) { Bez(P(0.82f, 0.98f), P(0.85f, 0.93f), P(0.87f, 0.88f), P(0.88f, 0.84f)); Bez(P(0.95f, 0.96f), P(0.97f, 0.91f), P(0.99f, 0.86f), P(1f, 0.82f)); } // dakuten
                return pts;
            }

            void Sign(int x0, int y0, int w, int h, bool vertical)
            {
                int n = vertical ? 3 + rnd.Next(2) : 3 + rnd.Next(2);
                float s = vertical ? Mathf.Min(w - 14, (h - 16f) / n - 6f) : Mathf.Min(h - 16, (w - 16f) / n - 6f);
                float rOut = Mathf.Max(2.2f, s * 0.085f), rCore = rOut * 0.45f;
                // Faint wavy acrylic backing outline.
                var plate = new Color(0.07f, 0.07f, 0.08f);
                for (int i = 0; i < 400; i++)
                {
                    float u = i / 400f;
                    float wob = Mathf.Sin(u * 40f) * 1.2f;
                    Dot(x0 + 3 + u * (w - 6), y0 + 3 + wob, 0.8f, plate); Dot(x0 + 3 + u * (w - 6), y0 + h - 4 + wob, 0.8f, plate);
                    Dot(x0 + 3 + wob, y0 + 3 + u * (h - 6), 0.8f, plate); Dot(x0 + w - 4 + wob, y0 + 3 + u * (h - 6), 0.8f, plate);
                }
                bool alternate = R() < 0.6f;
                var ca = cols[rnd.Next(cols.Count)];
                var cb = cols[rnd.Next(cols.Count)];
                Color avg = Color.black;
                for (int g = 0; g < n; g++)
                {
                    float gx = vertical ? x0 + (w - s) * 0.5f : x0 + 8 + g * (s + 6f);
                    float gy = vertical ? y0 + h - 8 - (g + 1) * (s + 6f) + 6f : y0 + (h - s) * 0.5f;
                    var c = alternate && g % 2 == 1 ? cb : ca;
                    avg += c / n;
                    var pts = Glyph(gx, gy, s, !vertical && g == n - 1 && R() < 0.4f);
                    foreach (var p in pts) Dot(p.x, p.y, rOut, c * 0.85f);
                    foreach (var p in pts) Dot(p.x, p.y, rCore, Color.Lerp(c, Color.white, 0.75f));
                }
                cells.Add(new SignCell { Uv = new Rect(x0 / (float)N, y0 / (float)N, w / (float)N, h / (float)N), Kind = vertical ? 0 : 1, Tint = avg });
            }

            for (int row = 0; row < 4; row++) for (int c = 0; c < 2; c++) Sign(c * 256, row * 64, 256, 64, false);
            for (int c = 0; c < 8; c++) Sign(c * 64, 256, 64, 256, true);
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }
    }
}
