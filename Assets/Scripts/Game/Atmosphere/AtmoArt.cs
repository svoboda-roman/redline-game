using UnityEngine;
using C = Redline.Game.Atmosphere.AtmosphereConfig;

namespace Redline.Game.Atmosphere
{
    /// <summary>
    /// Procedural art for the night city: the wet-metal tile atlas (with a generated normal map),
    /// skyline layers, sky gradient, fog band, rain streak, splash, and lamp fixtures. Nothing is
    /// imported; every texture is built once per session from deterministic seeds and cached.
    /// </summary>
    /// <example>
    /// <code>
    /// var sprite = AtmoArt.Tile(AtmoArt.TileMask(topOpen: true, false, false, false), variant: 2);
    /// var layer  = AtmoArt.City(0);   // far skyline layer: sprite + beacon positions
    /// </code>
    /// </example>
    /// <remarks>Main-thread only (creates textures). Build cost is paid on first use, never per frame.</remarks>
    public static class AtmoArt
    {
        /// <summary>Tile look variants: wide panel, bolt rows, vent grille, hazard paint, rust, stencil, cable run, oil stain.</summary>
        public const int TileVariants = 8;
        /// <summary>Exposure masks: bit 1 top, 2 bottom, 4 left, 8 right open.</summary>
        public const int TileMasks = 16;

        private const int Pad = 1;
        private const int Bevel = 5;
        private const float NormalStrength = 2.6f;
        private const string NormalMapProperty = "_NormalMap";

        private static Sprite[] _tiles;
        private static CityLayerArt[] _city;
        private static Sprite _sky, _fog, _rain, _splash, _post, _ceiling, _signal, _cone, _reflection;

        /// <summary>One generated skyline layer.</summary>
        public sealed class CityLayerArt
        {
            /// <summary>The layer sprite, pivot bottom-left, width = the layer period.</summary>
            public Sprite Sprite;
            /// <summary>Beacon positions (world units from the sprite's bottom-left).</summary>
            public Vector2[] Beacons;
            /// <summary>Repeat period in world units.</summary>
            public float Period;
        }

        /// <summary>Clears cached art before each Play (Play Mode runs without a domain reload).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _tiles = null;
            _city = null;
            _sky = _fog = _rain = _splash = _post = _ceiling = _signal = _cone = _reflection = null;
        }

        /// <summary>Builds the exposure mask for <see cref="Tile"/>.</summary>
        public static int TileMask(bool top, bool bottom, bool left, bool right) =>
            (top ? 1 : 0) | (bottom ? 2 : 0) | (left ? 4 : 0) | (right ? 8 : 0);

        /// <summary>Picks a variant for a cell, deterministic by position and weighted toward plain panels.</summary>
        public static int TileVariant(int x, int y)
        {
            uint h = Hash(x, y, 17) % 100u;
            if (h < 22) return 0;
            if (h < 40) return 1;
            if (h < 50) return 2;
            if (h < 59) return 3;
            if (h < 72) return 4;
            if (h < 79) return 5;
            if (h < 88) return 6;
            return 7;
        }

        /// <summary>A 1×1 world-unit wet-metal tile with a bevel on its open faces (normal-mapped).</summary>
        public static Sprite Tile(int mask, int variant)
        {
            if (_tiles == null) BuildTileAtlas();
            return _tiles[(mask & 15) * TileVariants + Mathf.Clamp(variant, 0, TileVariants - 1)];
        }

        // ------------------------------------------------------------------ tiles

        private static void BuildTileAtlas()
        {
            const int t = C.TilePixels;
            const int stride = t + Pad * 2;
            int cols = TileVariants, rows = TileMasks;
            int w = cols * stride, h = rows * stride;
            var albedo = new Color32[w * h];
            var normal = new Color32[w * h];
            var cellA = new Color32[t * t];
            var cellH = new float[t * t];
            for (int mask = 0; mask < TileMasks; mask++)
            {
                for (int v = 0; v < TileVariants; v++)
                {
                    DrawTile(mask, v, cellA, cellH);
                    int ox = v * stride + Pad, oy = mask * stride + Pad;
                    for (int y = -Pad; y < t + Pad; y++)
                        for (int x = -Pad; x < t + Pad; x++)
                        {
                            int sx = Mathf.Clamp(x, 0, t - 1), sy = Mathf.Clamp(y, 0, t - 1);
                            int di = (oy + y) * w + ox + x;
                            albedo[di] = cellA[sy * t + sx];
                            normal[di] = NormalAt(cellH, sx, sy, t);
                        }
                }
            }
            var albedoTex = NewTexture("AtmoTileAtlas", w, h, albedo, FilterMode.Point, false);
            var normalTex = NewTexture("AtmoTileAtlasNormal", w, h, normal, FilterMode.Point, true);
            var secondary = new[] { new SecondarySpriteTexture { name = NormalMapProperty, texture = normalTex } };
            _tiles = new Sprite[TileMasks * TileVariants];
            for (int mask = 0; mask < TileMasks; mask++)
                for (int v = 0; v < TileVariants; v++)
                {
                    var rect = new Rect(v * stride + Pad, mask * stride + Pad, t, t);
                    _tiles[mask * TileVariants + v] = Sprite.Create(albedoTex, rect, new Vector2(0.5f, 0.5f), t, 0,
                        SpriteMeshType.FullRect, Vector4.zero, false, secondary);
                }
        }

        /// <summary>Packs a tangent-space normal so both RGB and DXT5nm-style (a·r, g) decoders read it.</summary>
        private static Color32 NormalAt(float[] hgt, int x, int y, int t)
        {
            float l = hgt[y * t + Mathf.Max(0, x - 1)], r = hgt[y * t + Mathf.Min(t - 1, x + 1)];
            float d = hgt[Mathf.Max(0, y - 1) * t + x], u = hgt[Mathf.Min(t - 1, y + 1) * t + x];
            var n = new Vector3((l - r) * NormalStrength, (d - u) * NormalStrength, 1f).normalized;
            return new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f),
                (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
        }

        private static void DrawTile(int mask, int variant, Color32[] a, float[] hgt)
        {
            const int t = C.TilePixels;
            bool top = (mask & 1) != 0, bottom = (mask & 2) != 0, left = (mask & 4) != 0, right = (mask & 8) != 0;
            var rng = new Rng((uint)(mask * 131 + variant * 977 + 7));
            var col = new Color[t * t];
            var baseCol = new Color(0.2f, 0.225f, 0.255f);
            var seam = new Color(0.075f, 0.085f, 0.1f);
            float seed = mask * 3.7f + variant * 11.3f;

            // Base: brushed metal (row streaks + fine noise + low-frequency mottling).
            for (int y = 0; y < t; y++)
            {
                float row = rng.Range(-0.012f, 0.012f);
                for (int x = 0; x < t; x++)
                {
                    float n = rng.Range(-0.014f, 0.014f) + row;
                    float m = (Mathf.PerlinNoise(x * 0.09f + seed, y * 0.07f + seed) - 0.5f) * 0.05f;
                    col[y * t + x] = baseCol + new Color(n + m, n + m, (n + m) * 1.1f);
                    hgt[y * t + x] = 1f;
                }
            }

            // Panel seams with a bevel: a dark groove and a lit edge above/right of it.
            bool vSeam = variant % 2 == 0;
            for (int i = 0; i < t; i++)
            {
                Groove(col, hgt, i, 0, seam, true);
                if (vSeam) Groove(col, hgt, 0, i, seam, false);
                if (variant == 0) Groove(col, hgt, i, t / 2, seam, true);
            }

            switch (variant)
            {
                case 0: // wide panel: bolts at the ends of the mid seam
                    Bolt(col, hgt, 4, t / 2 + 3); Bolt(col, hgt, t - 6, t / 2 + 3);
                    Bolt(col, hgt, 4, t / 2 - 5); Bolt(col, hgt, t - 6, t / 2 - 5);
                    break;
                case 1: // bolt rows
                    for (int x = 4; x < t - 2; x += 8) { Bolt(col, hgt, x, t - 8); Bolt(col, hgt, x, 4); }
                    break;
                case 2: // vent grille: recessed frame, slats lit on top
                {
                    int x0 = 7, x1 = t - 8, y0 = 9, y1 = t - 12;
                    for (int y = y0 - 1; y <= y1 + 1; y++)
                        for (int x = x0 - 1; x <= x1 + 1; x++)
                        {
                            int i = y * t + x;
                            bool frame = x == x0 - 1 || x == x1 + 1 || y == y0 - 1 || y == y1 + 1;
                            if (frame) { col[i] = y == y0 - 1 || x == x1 + 1 ? col[i] * 1.3f : seam; hgt[i] = frame && y == y0 - 1 ? 1.2f : 0.5f; continue; }
                            int k = (y - y0) % 4;
                            col[i] = k == 3 ? new Color(0.3f, 0.32f, 0.35f) : k == 2 ? new Color(0.19f, 0.2f, 0.22f) : new Color(0.035f, 0.04f, 0.05f);
                            hgt[i] = k >= 2 ? 0.8f : 0.15f;
                        }
                    Bolt(col, hgt, x0 - 4, y0 - 4); Bolt(col, hgt, x1 + 3, y0 - 4); Bolt(col, hgt, x0 - 4, y1 + 3); Bolt(col, hgt, x1 + 3, y1 + 3);
                    break;
                }
                case 3: // worn hazard paint along the open edge (top first)
                {
                    for (int y = 0; y < t; y++)
                        for (int x = 0; x < t; x++)
                        {
                            bool band = top ? y >= t - 12 && y < t - 4
                                : left ? x >= 3 && x < 11
                                : right ? x >= t - 11 && x < t - 3
                                : y >= t / 2 - 4 && y < t / 2 + 4;
                            if (!band) continue;
                            float wear = Mathf.PerlinNoise(x * 0.35f + seed, y * 0.35f);
                            if (wear > 0.68f || rng.Value() < 0.08f) continue;
                            bool stripe = ((x + y) / 5) % 2 == 0;
                            col[y * t + x] = stripe ? C.HazardYellow * (0.85f + 0.3f * wear) : C.HazardBlack;
                        }
                    break;
                }
                case 4: // rust bloom under a bolt row, rust runs down
                {
                    for (int x = 6; x < t - 2; x += 10) Bolt(col, hgt, x, t - 8);
                    for (int y = 0; y < t; y++)
                        for (int x = 0; x < t; x++)
                        {
                            float p = Mathf.PerlinNoise(x * 0.16f + seed, y * 0.16f + 5f);
                            float k = Mathf.Clamp01((p - 0.55f) * 4f);
                            if (k > 0f) col[y * t + x] = Color.Lerp(col[y * t + x], C.Rust * (0.8f + 0.4f * rng.Value()), k * 0.85f);
                        }
                    for (int x = 6; x < t - 2; x += 10) Streak(col, x + 1, t - 10, 14 + (int)(Hash(x, mask, 3) % 12u), C.Rust, 0.6f);
                    break;
                }
                case 5: // warning stencil
                {
                    string word = top ? "NO STEP" : (mask & 12) != 0 ? "07" : "REDLINE";
                    Stencil(col, word, t / 2, t / 2 - 3, ref rng);
                    Bolt(col, hgt, 4, t - 8); Bolt(col, hgt, t - 6, t - 8);
                    break;
                }
                case 6: // cable run between two clamps, sagging
                {
                    int yc = t / 2 + 4;
                    for (int x = 2; x < t - 2; x++)
                    {
                        float k = (x - 2f) / (t - 5f);
                        int y = yc - Mathf.RoundToInt(4f * 4f * k * (1f - k));
                        for (int d = -1; d <= 1; d++)
                        {
                            int i = (y + d) * t + x;
                            col[i] = d == 1 ? new Color(0.17f, 0.18f, 0.2f) : new Color(0.04f, 0.04f, 0.05f);
                            hgt[i] = 1.6f - Mathf.Abs(d) * 0.3f;
                        }
                        if (rng.Value() < 0.5f) col[(y - 2) * t + x] *= 0.6f; // contact shadow
                    }
                    for (int cx = 0; cx < 2; cx++)
                    {
                        int x0 = cx == 0 ? 5 : t - 9;
                        for (int y = yc - 3; y <= yc + 2; y++)
                            for (int x = x0; x < x0 + 4; x++)
                            {
                                col[y * t + x] = y == yc + 2 ? new Color(0.4f, 0.42f, 0.45f) : new Color(0.27f, 0.28f, 0.3f);
                                hgt[y * t + x] = 1.8f;
                            }
                    }
                    break;
                }
                default: // oil stain with a faint sheen ring, bolt row on top
                {
                    for (int x = 4; x < t - 2; x += 8) Bolt(col, hgt, x, t - 8);
                    float ox = t * 0.4f + rng.Range(-4f, 4f), oy = t * 0.38f;
                    for (int y = 0; y < t; y++)
                        for (int x = 0; x < t; x++)
                        {
                            float dx = (x - ox) / 11f, dy = (y - oy) / 7f;
                            float r = Mathf.Sqrt(dx * dx + dy * dy) + (Mathf.PerlinNoise(x * 0.3f + seed, y * 0.3f) - 0.5f) * 0.5f;
                            int i = y * t + x;
                            if (r < 0.85f) col[i] = Color.Lerp(col[i], C.Oil, 0.8f);
                            else if (r < 1f) col[i] = Color.Lerp(col[i], new Color(0.2f, 0.26f, 0.32f), 0.4f);
                        }
                    break;
                }
            }

            // Grime streaks running down from an exposed top edge (rain wash).
            if (top)
                for (int x = 1; x < t - 1; x++)
                {
                    uint hsh = Hash(x, mask * 8 + variant, 41);
                    if (hsh % 100u < 38u) Streak(col, x, t - 4, 6 + (int)(hsh / 100u % 22u), new Color(0.09f, 0.095f, 0.1f), 0.55f);
                }

            for (int y = 0; y < t; y++)
                for (int x = 0; x < t; x++)
                {
                    int i = y * t + x;
                    var c = col[i];
                    int dist = 99;
                    if (top) dist = Mathf.Min(dist, t - 1 - y);
                    if (bottom) dist = Mathf.Min(dist, y);
                    if (left) dist = Mathf.Min(dist, x);
                    if (right) dist = Mathf.Min(dist, t - 1 - x);
                    if (dist < Bevel) hgt[i] *= (dist + 0.5f) / Bevel;
                    // Lips on open faces: a light rim on top (catches the light), darker below and right.
                    if (top && y >= t - 2) c = new Color(0.4f, 0.43f, 0.47f) * (y == t - 1 ? 1f : 0.88f);
                    else if (top && y == t - 3) c *= 0.7f;
                    if (bottom && y <= 1) c = new Color(0.065f, 0.07f, 0.08f);
                    if (left && x <= 1) c = Color.Lerp(c, new Color(0.3f, 0.32f, 0.35f), x == 0 ? 0.6f : 0.3f);
                    if (right && x >= t - 2) c *= x == t - 1 ? 0.5f : 0.75f;
                    c.a = 1f;
                    a[i] = c;
                }
        }

        /// <summary>A one-pixel groove with a lit bevel beside it: horizontal at row <paramref name="b"/> or vertical at column <paramref name="a"/>.</summary>
        private static void Groove(Color[] col, float[] h, int a, int b, Color seam, bool horizontal)
        {
            const int t = C.TilePixels;
            int x = horizontal ? a : b, y = horizontal ? b : a;
            col[y * t + x] = seam;
            h[y * t + x] = 0.45f;
            int lx = horizontal ? x : x + 1, ly = horizontal ? y + 1 : y;
            if (lx < t && ly < t) { col[ly * t + lx] *= 1.22f; h[ly * t + lx] = 1.1f; }
        }

        /// <summary>A 2×2 bolt head with a lit top-left, a shadow pixel and a raised height.</summary>
        private static void Bolt(Color[] col, float[] h, int x, int y)
        {
            const int t = C.TilePixels;
            if (x < 1 || y < 1 || x > t - 3 || y > t - 3) return;
            col[(y + 1) * t + x] = new Color(0.42f, 0.44f, 0.47f);
            col[(y + 1) * t + x + 1] = new Color(0.31f, 0.33f, 0.36f);
            col[y * t + x] = new Color(0.27f, 0.29f, 0.32f);
            col[y * t + x + 1] = new Color(0.16f, 0.17f, 0.19f);
            col[(y - 1) * t + x + 1] *= 0.55f;
            h[(y + 1) * t + x] = h[(y + 1) * t + x + 1] = h[y * t + x] = h[y * t + x + 1] = 1.7f;
        }

        /// <summary>A wash streak running down from (x, y0), fading over <paramref name="len"/> pixels.</summary>
        private static void Streak(Color[] col, int x, int y0, int len, Color tint, float strength)
        {
            const int t = C.TilePixels;
            for (int k = 0; k < len; k++)
            {
                int y = y0 - k;
                if (y < 0) break;
                float f = strength * (1f - (float)k / len);
                col[y * t + x] = Color.Lerp(col[y * t + x], tint, f);
            }
        }

        // 3×5 stencil font (rows top to bottom), only the glyphs our own warning words use.
        private const string GlyphChars = "07NOSTEPRDLI";
        private static readonly string[] GlyphRows =
        {
            "111101101101111", "111001010010010", "101111111101101", "010101101101010",
            "111100111001111", "111010010010010", "111100110100111", "110101110100100",
            "110101110101101", "110101101101110", "100100100100111", "111010010010111"
        };

        /// <summary>Worn spray-stencil word centred on (cx, cy), stencil bridges and flaked paint included.</summary>
        private static void Stencil(Color[] col, string word, int cx, int cy, ref Rng rng)
        {
            const int t = C.TilePixels;
            int w = word.Length * 4 - 1;
            int x0 = cx - w / 2;
            for (int ci = 0; ci < word.Length; ci++)
            {
                int g = GlyphChars.IndexOf(word[ci]);
                if (g < 0) continue;
                for (int r = 0; r < 5; r++)
                    for (int k = 0; k < 3; k++)
                    {
                        if (GlyphRows[g][r * 3 + k] != '1' || (r == 2 && k == 1 && word[ci] == '0')) continue;
                        if (rng.Value() < 0.14f) continue; // flaked
                        int x = x0 + ci * 4 + k, y = cy + 2 - r;
                        if (x < 0 || x >= t || y < 0 || y >= t) continue;
                        col[y * t + x] = Color.Lerp(col[y * t + x], C.StencilPaint, rng.Range(0.55f, 0.85f));
                    }
            }
        }

        // ------------------------------------------------------------------ skyline

        /// <summary>The skyline layer <paramref name="index"/> (0 far .. 3 near), built on first use.</summary>
        public static CityLayerArt City(int index)
        {
            if (_city == null) _city = new CityLayerArt[C.CityLayers];
            return _city[index] ?? (_city[index] = BuildCity(index));
        }

        private static CityLayerArt BuildCity(int layer)
        {
            int ppu = C.LayerPixelsPerUnit[layer];
            int w = Mathf.RoundToInt(C.LayerPeriod[layer] * ppu);
            int h = Mathf.RoundToInt(C.LayerHeight[layer] * ppu);
            var px = new Color32[w * h];
            var rng = new Rng((uint)(layer * 7919 + 101));
            var wall = C.BuildingColor[layer];
            float fog = C.LayerFog[layer];
            var lit = Color.Lerp(C.WindowWarm, C.GroundHaze, fog);
            var cool = Color.Lerp(C.WindowCool, C.GroundHaze, fog);
            var sign = Color.Lerp(C.SignRed, C.GroundHaze, fog);
            var glass = Color.Lerp(wall, new Color(0.16f, 0.2f, 0.24f), 0.35f);
            var beacons = new System.Collections.Generic.List<Vector2>();

            // Towers first (far layers), so ordinary blocks overlap their feet.
            int towers = C.TowerCount[layer];
            for (int i = 0; i < towers; i++)
            {
                int tw = Mathf.Max(3, ppu * rng.Range(2, 4) / 2);
                int tx = (w * (i * 2 + 1)) / (towers * 2) + rng.Range(-w / 10, w / 10);
                int th = h - rng.Range(ppu, ppu * 3);
                Block(px, w, h, tx, tw, th, wall, glass, lit, cool, sign, layer, ref rng, false);
                int mast = Mathf.Max(3, ppu);
                Rect(px, w, h, tx + tw / 2, th, 1, Mathf.Min(mast, h - th), wall);
                beacons.Add(new Vector2((tx + tw / 2 + 0.5f) / ppu, Mathf.Min(h - 1, th + mast) / (float)ppu));
            }

            // Then a continuous row of blocks around the whole period (wrapping), varied heights.
            int x = 0;
            while (x < w)
            {
                int bw = Mathf.Max(4, Mathf.RoundToInt(rng.Range(1.6f, 4.2f) * ppu));
                int bh = Mathf.RoundToInt(h * rng.Range(0.22f, 0.7f));
                Block(px, w, h, x, bw, bh, wall, glass, lit, cool, sign, layer, ref rng, true);
                x += bw + (rng.Value() < 0.3f ? rng.Range(1, Mathf.Max(2, ppu / 2)) : 0);
            }

            // Ground haze: the base fades toward the haze (far layers fully, near ones barely);
            // the bottom rows are opaque in the layer's base colour.
            float hazeAmount = C.LayerBaseHaze[layer];
            var baseColor = CityBaseColor(layer);
            int hazeRows = Mathf.RoundToInt(h * 0.35f);
            int solidRows = Mathf.Max(2, ppu / 2);
            for (int y = 0; y < hazeRows; y++)
            {
                float k = 1f - (float)y / hazeRows;
                k *= k * hazeAmount;
                for (int i = 0; i < w; i++)
                {
                    int idx = y * w + i;
                    if (y < solidRows) { px[idx] = baseColor; continue; }
                    if (px[idx].a == 0) continue;
                    var c = Color.Lerp(px[idx], C.GroundHaze, k);
                    c.a = 1f;
                    px[idx] = c;
                }
            }

            var tex = NewTexture("AtmoCity" + layer, w, h, px, FilterMode.Point, false);
            return new CityLayerArt
            {
                Sprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.zero, ppu, 0, SpriteMeshType.FullRect),
                Beacons = beacons.ToArray(),
                Period = C.LayerPeriod[layer]
            };
        }

        /// <summary>Opaque colour at the base of a layer (also its fill below): the wall pulled toward the haze.</summary>
        public static Color CityBaseColor(int layer) =>
            Color.Lerp(C.BuildingColor[layer], C.GroundHaze, C.LayerBaseHaze[layer]);

        /// <summary>
        /// Volumetric light cone (white, tint it): apex at the top centre (the pivot), 1×1 world unit,
        /// fading along its length and toward its soft edges. Scale X to 2·L·tan(half-angle), Y to L.
        /// </summary>
        public static Sprite LightCone
        {
            get
            {
                if (_cone != null) return _cone;
                const int n = 64;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                {
                    float d = 1f - (y + 0.5f) / n; // 0 at the apex (top), 1 at the far end
                    for (int x = 0; x < n; x++)
                    {
                        float u = Mathf.Abs((x + 0.5f) / n - 0.5f) * 2f; // 0 centre, 1 edge of the square
                        float edge = d <= 0f ? 0f : Mathf.Clamp01(1f - u / Mathf.Max(0.001f, d));
                        float a = edge * edge * Mathf.Pow(1f - d, 1.3f) * Mathf.Clamp01(d * 6f);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
                var tex = NewTexture("AtmoLightCone", n, n, px, FilterMode.Bilinear, false);
                return _cone = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 1f), n);
            }
        }

        /// <summary>
        /// Broken vertical reflection smear (white, tint it), 1×1 world unit, pivot at its top centre:
        /// bright at the floor line, dashed and fading downward.
        /// </summary>
        public static Sprite Reflection
        {
            get
            {
                if (_reflection != null) return _reflection;
                const int w = 8, h = 32;
                var px = new Color32[w * h];
                var rng = new Rng(77);
                for (int y = 0; y < h; y++)
                {
                    float down = 1f - (y + 0.5f) / h;          // 0 at the top (floor line)
                    bool gap = rng.Value() < 0.18f + down * 0.4f;
                    for (int x = 0; x < w; x++)
                    {
                        float u = Mathf.Abs((x + 0.5f) / w - 0.5f) * 2f;
                        float a = gap ? 0f : (1f - u * u) * Mathf.Pow(1f - down, 1.5f) * rng.Range(0.6f, 1f);
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                    }
                }
                var tex = NewTexture("AtmoReflection", w, h, px, FilterMode.Point, false);
                return _reflection = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), h);
            }
        }

        /// <summary>One building: silhouette, window grid (mostly dark), rooftop antenna or water tank.</summary>
        private static void Block(Color32[] px, int w, int h, int x0, int bw, int bh, Color wall, Color glass,
            Color lit, Color cool, Color sign, int layer, ref Rng rng, bool roofProps)
        {
            Rect(px, w, h, x0, 0, bw, bh, wall);
            // Window grid: 1px windows on far layers, 2px on near ones.
            int win = layer >= 2 ? 2 : 1;
            int gapX = win + 1 + (rng.Value() < 0.5f ? 1 : 0), gapY = win + 1 + (layer >= 2 ? 1 : 0);
            float litChance = C.WindowLitChance[layer] * rng.Range(0.3f, 1.8f);
            for (int wy = 3; wy + win < bh - 1; wy += gapY)
                for (int wx = 1; wx + win < bw - 1; wx += gapX)
                {
                    float r = rng.Value();
                    Color c;
                    if (r < litChance) c = rng.Value() < 0.15f ? cool : lit;
                    else if (r < 0.55f) c = glass;
                    else continue;
                    Rect(px, w, h, x0 + wx, wy, win, win, c);
                }
            // Occasional vertical sign bar on the nearer layers (blank: no text).
            if (layer >= 2 && rng.Value() < 0.12f && bh > 12)
            {
                int sx = x0 + rng.Range(1, Mathf.Max(2, bw - 3));
                Rect(px, w, h, sx, bh / 3, 2, Mathf.Min(bh / 2, 10), sign);
            }
            if (!roofProps) return;
            float p = rng.Value();
            if (p < 0.25f)
            {
                // Antenna.
                int ax = x0 + rng.Range(1, Mathf.Max(2, bw - 1));
                Rect(px, w, h, ax, bh, 1, rng.Range(2, 3 + layer * 2), wall);
            }
            else if (p < 0.42f && bw > 6)
            {
                // Water tank on legs.
                int tx = x0 + rng.Range(1, bw - 5);
                int tw = 3 + layer / 2, th = 2 + layer / 2;
                Rect(px, w, h, tx, bh, 1, 2, wall);
                Rect(px, w, h, tx + tw - 1, bh, 1, 2, wall);
                Rect(px, w, h, tx, bh + 2, tw, th, wall);
            }
            else if (p < 0.55f)
            {
                // Rooftop box (stairwell / HVAC).
                int rx = x0 + rng.Range(0, Mathf.Max(1, bw - 3));
                Rect(px, w, h, rx, bh, Mathf.Min(3 + layer, bw), 1 + layer / 2 + 1, wall);
            }
        }

        /// <summary>Fills a rectangle, wrapping horizontally (the layer tiles seamlessly) and clipping vertically.</summary>
        private static void Rect(Color32[] px, int w, int h, int x0, int y0, int rw, int rh, Color c)
        {
            var c32 = (Color32)new Color(c.r, c.g, c.b, 1f);
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y0 + rh); y++)
                for (int x = x0; x < x0 + rw; x++)
                    px[y * w + ((x % w) + w) % w] = c32;
        }

        // ------------------------------------------------------------------ sky, fog, rain

        /// <summary>Vertical sky gradient (horizon at the bottom), colours baked in; scale it to cover the view.</summary>
        public static Sprite Sky
        {
            get
            {
                if (_sky != null) return _sky;
                const int n = 64;
                var px = new Color32[n];
                for (int y = 0; y < n; y++)
                {
                    float k = (float)y / (n - 1);
                    px[y] = Color.Lerp(C.SkyHorizon, C.SkyTop, Mathf.SmoothStep(0f, 1f, k));
                }
                var tex = NewTexture("AtmoSky", 1, n, px, FilterMode.Bilinear, false);
                return _sky = Sprite.Create(tex, new Rect(0, 0, 1, n), new Vector2(0.5f, 0.5f), n);
            }
        }

        /// <summary>
        /// A soft horizontal fog band (white, alpha only; tint it), seamless horizontally, one
        /// <see cref="AtmosphereConfig.FogPeriod"/> wide and 1 world unit tall. Pivot bottom-left.
        /// </summary>
        public static Sprite Fog
        {
            get
            {
                if (_fog != null) return _fog;
                const int w = 256, h = 32;
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h * 2f - 1f;
                    float profile = Mathf.Exp(-v * v * 3f);
                    for (int x = 0; x < w; x++)
                    {
                        float u = (float)x / w * Mathf.PI * 2f;
                        // Integer frequencies: the band wraps without a seam.
                        float wisps = 0.55f + 0.25f * Mathf.Sin(u * 3f + y * 0.15f) + 0.15f * Mathf.Sin(u * 7f + 1.3f)
                                      + 0.08f * Mathf.Sin(u * 13f + y * 0.3f);
                        float a = Mathf.Clamp01(profile * wisps);
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
                var tex = NewTexture("AtmoFog", w, h, px, FilterMode.Bilinear, false);
                return _fog = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.zero, w / C.FogPeriod * 1f, 0,
                    SpriteMeshType.FullRect);
            }
        }

        /// <summary>Fog band height in world units at scale 1 (the sprite is FogPeriod wide).</summary>
        public static float FogUnitHeight => Fog.bounds.size.y;

        /// <summary>A rain streak: 1 world unit tall, bright head at the bottom fading to the tail.</summary>
        public static Sprite Rain
        {
            get
            {
                if (_rain != null) return _rain;
                const int h = 12;
                var px = new Color32[h];
                for (int y = 0; y < h; y++)
                    px[y] = new Color32(255, 255, 255, (byte)(255f * Mathf.Pow(1f - (float)y / h, 1.4f)));
                var tex = NewTexture("AtmoRain", 1, h, px, FilterMode.Bilinear, false);
                return _rain = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0f), h);
            }
        }

        /// <summary>A tiny splash crown, 1 world unit wide, pivot at its bottom centre.</summary>
        public static Sprite Splash => _splash != null ? _splash : (_splash = FromRows("AtmoSplash", new[]
        {
            "W.....W",
            ".W...W.",
            "..W.W..",
            ".WWWWW."
        }, new Vector2(0.5f, 0f), 7, false));

        // ------------------------------------------------------------------ lamp fixtures

        /// <summary>Lamp post (lit metal), 24 px per unit, pivot at the foot of the pole.</summary>
        public static Sprite LampPost => _post != null ? _post : (_post = FromRows("AtmoLampPost", PostRows(),
            new Vector2(3f / 12f, 0f), C.FixturePixels, true));

        /// <summary>Offset from the post's pivot to the bottom of its lamp glass (world units).</summary>
        public static readonly Vector2 LampPostHead = new Vector2(6.5f / 24f, 57f / 24f);

        /// <summary>Ceiling fixture (lit metal) hanging under an overhang, pivot at its top centre.</summary>
        public static Sprite CeilingLamp => _ceiling != null ? _ceiling : (_ceiling = FromRows("AtmoCeilingLamp", new[]
        {
            ".....MM.....",
            ".....MM.....",
            "..MMMMMMMM..",
            ".MLLLLLLLLM.",
            ".MDDDDDDDDM.",
            "..WWWWWWWW.."
        }, new Vector2(0.5f, 1f), C.FixturePixels, true));

        /// <summary>Offset from the ceiling fixture's pivot to its glass (world units).</summary>
        public static readonly Vector2 CeilingLampHead = new Vector2(0f, -5.5f / 24f);

        /// <summary>Signal box (lit metal) with a lens, pivot at its top centre.</summary>
        public static Sprite SignalBox => _signal != null ? _signal : (_signal = FromRows("AtmoSignal", new[]
        {
            "..M..",
            "MMMMM",
            "MDWDM",
            "MDWDM",
            "MMMMM",
            "MDDDM",
            "MMMMM"
        }, new Vector2(0.5f, 1f), C.FixturePixels, true));

        /// <summary>Offset from the signal box's pivot to its lens (world units).</summary>
        public static readonly Vector2 SignalLens = new Vector2(0f, -3f / 24f);

        private static string[] PostRows()
        {
            // 12 wide, 64 tall; pole at columns 2-3, arm reaching right to a lamp head at 6-10.
            var rows = new string[64];
            for (int i = 0; i < 64; i++)
            {
                int y = 63 - i; // row index from the top -> y from the bottom
                char[] r = "............".ToCharArray();
                if (y < 64) { r[2] = 'L'; r[3] = 'M'; }
                if (y < 3) { r[1] = 'M'; r[2] = 'L'; r[3] = 'M'; r[4] = 'D'; }
                if (y == 3 || y == 30) { r[1] = 'D'; r[4] = 'D'; }
                if (y >= 61 && y <= 62) for (int x = 2; x <= 9; x++) r[x] = y == 62 ? 'L' : 'M';
                if (y >= 58 && y <= 60) for (int x = 5; x <= 10; x++) r[x] = y == 60 ? 'M' : 'D';
                if (y == 57) for (int x = 6; x <= 9; x++) r[x] = 'W';
                if (y == 63) r[2] = r[3] = '.';
                rows[i] = new string(r);
            }
            return rows;
        }

        // ------------------------------------------------------------------ helpers

        private static Sprite FromRows(string name, string[] rows, Vector2 pivot, int ppu, bool fullRect)
        {
            int h = rows.Length, w = rows[0].Length;
            var px = new Color32[w * h];
            for (int row = 0; row < h; row++)
            {
                int y = h - 1 - row;
                for (int x = 0; x < w; x++)
                {
                    switch (rows[row][x])
                    {
                        case 'W': px[y * w + x] = new Color32(255, 255, 255, 255); break;
                        case 'L': px[y * w + x] = new Color32(170, 175, 180, 255); break;
                        case 'M': px[y * w + x] = new Color32(110, 114, 120, 255); break;
                        case 'D': px[y * w + x] = new Color32(60, 63, 68, 255); break;
                        default: px[y * w + x] = new Color32(0, 0, 0, 0); break;
                    }
                }
            }
            var tex = NewTexture(name, w, h, px, FilterMode.Point, false);
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, ppu, 0,
                fullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight);
        }

        private static Texture2D NewTexture(string name, int w, int h, Color32[] px, FilterMode filter, bool linear)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear)
            {
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Deterministic integer hash (position → pseudo-random bits).</summary>
        public static uint Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)salt * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        /// <summary>Small xorshift generator: deterministic art from a seed, no allocation.</summary>
        internal struct Rng
        {
            private uint _s;

            public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed * 2654435761u + 1u; }

            public uint Next()
            {
                _s ^= _s << 13;
                _s ^= _s >> 17;
                _s ^= _s << 5;
                return _s;
            }

            public float Value() => (Next() & 0xFFFFFF) / 16777216f;
            public int Range(int min, int max) => max <= min ? min : min + (int)(Next() % (uint)(max - min));
            public float Range(float min, float max) => min + (max - min) * Value();
        }
    }
}
