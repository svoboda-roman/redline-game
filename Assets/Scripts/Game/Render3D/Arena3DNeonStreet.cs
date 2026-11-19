using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// The neon street backdrop of <see cref="Arena3DRenderer"/> (<see cref="Arena3DLook.NeonStreet"/>): a dense
    /// East-Asian night street behind the play plane. Rows of building fronts (procedural window facades) covered with
    /// lit signs from the shop fronts up to the roofs: vertical blades, horizontal boards, pseudo-Hangul glyphs, shop
    /// windows with awnings, LED roof lines, soft halos, and some signs blinking. Four rows recede into pink fog; the
    /// nearest row is mirrored by the wet floor. Rows tile like the old city rows (world-fixed, follow the camera).
    /// </summary>
    /// <remarks>Everything is generated from <see cref="Arena3DLook.Seed"/>: same seed, same street. Sign and facade
    /// surfaces are unlit opaque (Proto3D/Fx opaque mode) so they keep their colours whatever the scene lighting.</remarks>
    public sealed partial class Arena3DRenderer
    {
        private Material _streetFacadeMat, _streetSignMat, _streetBlinkMat;
        private readonly List<SignCell> _signCells = new List<SignCell>(64);

        private struct SignCell
        {
            public Rect Uv;
            public int Kind;      // 0 vertical blade, 1 horizontal board, 2 shop front
            public Color Tint;    // average colour, for the halo
        }

        /// <summary>(depth of the facade fronts, detail 0..1) per row, nearest first.</summary>
        private static readonly (float z, float detail)[] StreetRows = { (16f, 1f), (29f, 0.85f), (48f, 0.65f), (80f, 0.45f) };

        private void NeonStreet()
        {
            var facadeTex = Own(FacadeTexture(_look.Seed));
            var atlas = Own(SignAtlas(_look.Seed, _look.SignColors, _signCells));
            _streetFacadeMat = Own(Proto3DMaterials.FxMat("streetFacade", facadeTex, Color.white, 2, 1f));
            _streetSignMat = Own(Proto3DMaterials.FxMat("streetSigns", atlas, Color.white, 2, 0.3f));
            _streetBlinkMat = Own(Proto3DMaterials.FxMat("streetSignsBlink", atlas, Color.white, 2, 0.3f));
            var haloMat = Own(Proto3DMaterials.FxMat("streetHalo", _blob, Color.white, 0, 0.45f));

            _cityRows = new Transform[StreetRows.Length];
            _cityPeriod = new float[StreetRows.Length];
            var rnd = new System.Random(_look.Seed * 41 + 17);
            for (int r = 0; r < StreetRows.Length; r++)
            {
                var (z, detail) = StreetRows[r];
                float period = Mathf.Ceil((PlaneDistance + z) * 1.2f / 8f) * 8f;
                _cityRows[r] = new GameObject("StreetRow " + z).transform;
                _cityRows[r].SetParent(_follow, false);
                _cityPeriod[r] = period;

                var facades = new UvQuads();
                var signs = new UvQuads();
                var blink = new UvQuads();
                var halos = new QuadBatch();
                float x = 0f;
                while (x < period - 2f)
                {
                    float scale = 1f + z * 0.012f;
                    float w = Mathf.Min((5.5f + (float)rnd.NextDouble() * 6f) * scale, period - x);
                    if (period - x - w < 3f) w = period - x;   // no sliver at the seam
                    float top = FloorTop + (22f + (float)rnd.NextDouble() * 26f) * scale + z * 0.25f;
                    StreetBuilding(rnd, facades, signs, blink, halos, x, w, top, z, detail, scale, r == 0);
                    x += w;
                }
                // Street floor between the deck's back edge and the nearest fronts (no void under the signs).
                if (r == 0) facades.Floor(new Vector3(period * 0.5f, FloorTop - 0.02f, (_look.DeckFar + z) * 0.5f), period, z - _look.DeckFar + 0.4f, PlasterUv, new Color(0.18f, 0.12f, 0.16f));

                var facadeMesh = Own(facades.Build("streetFacades" + r));
                var signMesh = signs.Count > 0 ? Own(signs.Build("streetSigns" + r)) : null;
                var blinkMesh = blink.Count > 0 ? Own(blink.Build("streetBlink" + r)) : null;
                var haloMesh = halos.Count > 0 ? Own(halos.Build("streetHalos" + r)) : null;
                for (int c = -1; c <= 1; c++)
                {
                    var inst = new GameObject("Strip " + c).transform;
                    inst.SetParent(_cityRows[r], false);
                    inst.localPosition = new Vector3(c * period, 0f, 0f);
                    // The nearest row is mirrored by the wet floor; the far rows are not (cost).
                    StreetPart("facades", inst, facadeMesh, _streetFacadeMat, r > 0);
                    if (signMesh != null) StreetPart("signs", inst, signMesh, _streetSignMat, r > 0);
                    if (blinkMesh != null) StreetPart("blink", inst, blinkMesh, _streetBlinkMat, r > 0);
                    if (haloMesh != null) StreetPart("halos", inst, haloMesh, haloMat, true);
                }
            }
        }

        private static void StreetPart(string name, Transform parent, Mesh mesh, Material mat, bool hideFromMirror)
        {
            var go = MeshGo(name, parent, mesh, mat);
            NoShadow(go);
            if (hideFromMirror) Hidden(go);
        }

        /// <summary>Blinks the blinking sign set (called from Update): mostly on, short irregular drop-outs.</summary>
        private void StreetUpdate(float time)
        {
            if (_streetBlinkMat == null) return;
            float a = Mathf.Repeat(time * 0.7f, 1f), b = Mathf.Repeat(time * 1.9f + 0.3f, 1f);
            bool off = a > 0.86f || (b > 0.93f && a > 0.5f);
            _streetBlinkMat.SetColor("_Color", off ? new Color(0.12f, 0.12f, 0.12f, 1f) : Color.white);
        }

        private void StreetBuilding(System.Random rnd, UvQuads facades, UvQuads signs, UvQuads blink, QuadBatch halos,
            float x, float w, float top, float z, float detail, float scale, bool near)
        {
            float R() => (float)rnd.NextDouble();
            float bottom = near ? FloorTop - 0.5f : -40f;
            // Facade: tiled window texture (8 units per tile), tinted per building (warm, never black).
            var tints = new[] { new Color(0.85f, 0.62f, 0.7f), new Color(0.7f, 0.6f, 0.85f), new Color(0.9f, 0.7f, 0.6f), new Color(0.75f, 0.55f, 0.6f), new Color(0.6f, 0.55f, 0.75f) };
            var tint = tints[rnd.Next(tints.Length)] * (0.7f + R() * 0.35f) * Mathf.Lerp(0.75f, 1f, detail);
            tint.a = 1f;
            float u0 = R(), v0 = R();
            facades.Quad(new Vector3(x + w * 0.5f, (bottom + top) * 0.5f, z), w, top - bottom,
                new Rect(u0, v0, w / (8f * scale), (top - bottom) / (8f * scale)), tint);
            // Roof parapet line and the odd LED outline.
            facades.Quad(new Vector3(x + w * 0.5f, top + 0.25f * scale, z - 0.05f), w, 0.5f * scale, PlasterUv, tint * 0.6f);
            if (R() < 0.45f * detail)
            {
                var led = _look.SignColors[rnd.Next(_look.SignColors.Length)] * 2.2f;
                signs.Quad(new Vector3(x + w * 0.5f, top + 0.55f * scale, z - 0.1f), w, 0.14f * scale, WhiteUv, led);
                signs.Quad(new Vector3(x + 0.07f * scale, (FloorTop + 4f + top) * 0.5f, z - 0.1f), 0.14f * scale, top - FloorTop - 4f, WhiteUv, led);
            }

            // Ground floor shop front with an awning (the bright band along the street).
            if (near || z < 40f)
            {
                var shop = Pick(rnd, 2);
                float sh = 3.2f * scale;
                signs.Quad(new Vector3(x + w * 0.5f, FloorTop + sh * 0.5f, z - 0.15f), w - 0.5f, sh, shop.Uv, Color.white * 0.95f);
                var aw = _look.SignColors[rnd.Next(_look.SignColors.Length)] * 1.3f;
                signs.Quad(new Vector3(x + w * 0.5f, FloorTop + sh + 0.25f * scale, z - 0.5f), w - 0.3f, 0.5f * scale, WhiteUv, aw);
                halos.Billboard(new Vector3(x + w * 0.5f, FloorTop + sh * 0.5f, z - 1.2f), w * 1.3f, sh * 2.2f, shop.Tint * 0.12f);
            }

            // Signs: blades along the building edges, boards across the floors, stacked from the shops to the roof.
            int n = Mathf.RoundToInt(detail * (9f + R() * 7f) * Mathf.Clamp(w / 7f, 0.6f, 1.6f));
            float yMin = FloorTop + 4f * scale, yMax = top - 1.5f * scale;
            for (int i = 0; i < n && yMax > yMin + 2f; i++)
            {
                bool bladeSign = R() < 0.55f;
                var cell = Pick(rnd, bladeSign ? 0 : 1);
                float sw, sh, cx;
                if (bladeSign)
                {
                    sw = (0.65f + R() * 0.4f) * scale;
                    sh = sw * 4f;
                    cx = R() < 0.7f ? (R() < 0.5f ? x + sw * 0.6f : x + w - sw * 0.6f) : x + sw + R() * Mathf.Max(0.1f, w - 2f * sw);
                }
                else
                {
                    sw = Mathf.Min(w - 0.8f * scale, (2f + R() * 2f) * scale);
                    sh = sw * 0.25f;
                    cx = x + w * 0.5f + (R() - 0.5f) * Mathf.Max(0f, w - sw - 0.4f);
                }
                float cy = yMin + sh * 0.5f + R() * Mathf.Max(0.1f, yMax - yMin - sh);
                float cz = z - (bladeSign ? 0.9f : 0.35f) * scale - i * 0.01f;
                float bright = 1.05f + R() * 0.45f;   // just over the bloom threshold: glows but keeps its colour
                var target = R() < 0.18f ? blink : signs;
                target.Quad(new Vector3(cx, cy, cz), sw, sh, cell.Uv, Color.white * bright);
                if (bladeSign) // bracket to the wall
                    facades.Quad(new Vector3(cx, cy + sh * 0.5f + 0.12f * scale, cz + 0.4f * scale), sw * 0.9f, 0.12f * scale, PlasterUv, tint * 0.4f);
                halos.Billboard(new Vector3(cx, cy, cz - 0.3f), sw * 2.6f + 2f * scale, sh * 1.8f + 2f * scale, cell.Tint * (0.2f + 0.06f * bright));
            }
        }

        private SignCell Pick(System.Random rnd, int kind)
        {
            int start = rnd.Next(_signCells.Count);
            for (int k = 0; k < _signCells.Count; k++)
            {
                var c = _signCells[(start + k) % _signCells.Count];
                if (c.Kind == kind) return c;
            }
            return _signCells[0];
        }

        /// <summary>UV rect of a plain plaster texel on the facade texture (flat facade-material strips and floor).</summary>
        private static readonly Rect PlasterUv = new Rect(2f / 128f, 4f / 128f, 0.5f / 128f, 0.5f / 128f);

        /// <summary>UV rect of the atlas's solid white texel block (for flat colour strips).</summary>
        private static readonly Rect WhiteUv = new Rect(508f / 512f, 508f / 512f, 2f / 512f, 2f / 512f);

        // ------------------------------------------------------------------------------------------------
        // Textures
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Tileable facade, 128 px = 8 units: plaster with grime, a 2-unit window grid (warm lit, cool dark, some with
        /// blinds), floor ledges every 3 units, and the odd AC unit. Point filtered to match the pixel art.
        /// </summary>
        private static Texture2D FacadeTexture(int seed)
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, name = "streetFacade" };
            var rnd = new System.Random(seed * 3 + 1);
            var px = new Color[N * N];
            for (int i = 0; i < px.Length; i++)
            {
                float n = (float)rnd.NextDouble() * 0.06f;
                px[i] = new Color(0.36f + n, 0.31f + n, 0.36f + n, 1f);
            }
            // Ledges every 3 units (48 px is not a divisor of 128, so use 32 px = 2 units per floor band + ledge).
            for (int y = 0; y < N; y += 32)
                for (int x = 0; x < N; x++) { px[y * N + x] = new Color(0.22f, 0.18f, 0.22f); px[((y + 1) % N) * N + x] = new Color(0.5f, 0.44f, 0.48f); }
            // Windows: 4 per tile horizontally (32 px pitch), 4 floors.
            for (int fy = 0; fy < 4; fy++)
            for (int fx = 0; fx < 4; fx++)
            {
                int x0 = fx * 32 + 6, y0 = fy * 32 + 8, ww = 20, wh = 18;
                double r = rnd.NextDouble();
                Color c = r < 0.5 ? new Color(1f, 0.78f, 0.48f) : r < 0.7 ? new Color(1f, 0.55f, 0.7f) : new Color(0.16f, 0.15f, 0.26f);
                for (int y = y0; y < y0 + wh; y++)
                for (int x = x0; x < x0 + ww; x++)
                {
                    var cc = c;
                    if (x == x0 || x == x0 + ww - 1 || y == y0 || y == y0 + wh - 1) cc = new Color(0.12f, 0.1f, 0.12f);
                    else if (x == x0 + ww / 2) cc *= 0.55f;                                  // mullion
                    else if (r < 0.7 && (y - y0) % 4 == 0 && rnd.NextDouble() < 0.5) cc *= 0.8f;   // blinds
                    cc.a = 1f;
                    px[y * N + x] = cc;
                }
                if (rnd.NextDouble() < 0.25) // AC unit under the window
                    for (int y = y0 - 6; y < y0 - 1; y++) for (int x = x0 + 2; x < x0 + 10; x++) px[y * N + x] = new Color(0.55f, 0.52f, 0.55f);
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        /// <summary>
        /// Sign atlas 512x512: 32 vertical blades (32x128), 24 horizontal boards (128x32), 4 shop fronts (128x64) and a
        /// white block. Two sign styles: a filled colour panel with dark glyphs, and a dark panel with neon glyphs and
        /// border. Glyphs are procedural pseudo-Hangul built from strokes on a 5x5 grid.
        /// </summary>
        private static Texture2D SignAtlas(int seed, Color[] palette, List<SignCell> cells)
        {
            const int N = 512;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "streetSigns" };
            var px = new Color[N * N];
            var rnd = new System.Random(seed * 7 + 5);
            var extra = new[] { new Color(0.3f, 0.85f, 1f), new Color(1f, 1f, 1f), new Color(0.75f, 0.4f, 1f), new Color(1f, 0.55f, 0.15f) };
            Color PickColor()
            {
                if (palette != null && palette.Length > 0 && rnd.NextDouble() < 0.72) { var p = palette[rnd.Next(palette.Length)]; return new Color(Mathf.Clamp01(p.r / 1.4f), Mathf.Clamp01(p.g / 1.4f), Mathf.Clamp01(p.b / 1.4f), 1f); }
                return extra[rnd.Next(extra.Length)];
            }
            void Fill(int x0, int y0, int w, int h, Color c) { for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) if (x >= 0 && y >= 0 && x < N && y < N) px[y * N + x] = c; }

            void Glyph(int gx, int gy, int size, Color c)
            {
                int s = Mathf.Max(1, size / 5), th = Mathf.Max(2, s);
                int parts = 2 + rnd.Next(2);
                for (int p = 0; p < parts; p++)
                {
                    switch (rnd.Next(7))
                    {
                        case 0: Fill(gx, gy + s * rnd.Next(0, 5), size, th, c); break;                               // horizontal bar
                        case 1: Fill(gx + s * (3 + rnd.Next(2)), gy, th, size, c); break;                          // vertical bar (right)
                        case 2: { int y0 = gy + (rnd.Next(2) == 0 ? 0 : size / 2); Fill(gx, y0, size * 3 / 5, th, c); Fill(gx, y0 + size / 2 - th, size * 3 / 5, th, c); Fill(gx, y0, th, size / 2, c); Fill(gx + size * 3 / 5 - th, y0, th, size / 2, c); break; } // box
                        case 3: { int cx = gx + size / 3, cy = gy + size / 3, rr = size / 4; for (int a = 0; a < 40; a++) { float an = a / 40f * 6.283f; Fill(cx + Mathf.RoundToInt(Mathf.Cos(an) * rr), cy + Mathf.RoundToInt(Mathf.Sin(an) * rr), th, th, c); } break; } // ring
                        case 4: Fill(gx, gy + size - th, size * 3 / 5, th, c); Fill(gx + size * 3 / 5 - th, gy + size / 2, th, size / 2, c); break; // corner
                        case 5: Fill(gx, gy + size / 2, th, size / 2, c); Fill(gx, gy + size / 2, size * 3 / 5, th, c); break;                     // corner
                        default: Fill(gx + size / 2 - th / 2, gy, th, size, c); break;                              // centre stroke
                    }
                }
            }

            void Sign(int x0, int y0, int w, int h, bool vertical)
            {
                var col = PickColor();
                bool filled = rnd.NextDouble() < 0.55;
                var bg = filled ? col : new Color(0.07f, 0.05f, 0.09f);
                var ink = filled ? (rnd.NextDouble() < 0.6 ? new Color(0.08f, 0.04f, 0.08f) : Color.white) : col;
                var border = filled ? Color.Lerp(col, Color.white, 0.6f) : col;
                Fill(x0, y0, w, h, border);
                Fill(x0 + 2, y0 + 2, w - 4, h - 4, bg);
                if (!filled && rnd.NextDouble() < 0.5) Fill(x0 + 4, y0 + 4, w - 8, h - 8, Color.Lerp(bg, col, 0.12f));
                int g = vertical ? w - 10 : h - 10;
                g = Mathf.Max(5, g);
                if (vertical)
                    for (int y = y0 + h - 6 - g; y >= y0 + 4; y -= g + 4) Glyph(x0 + (w - g) / 2, y, g, ink);
                else
                    for (int x = x0 + 6; x + g <= x0 + w - 5; x += g + 4) Glyph(x, y0 + (h - g) / 2, g, ink);
                cells.Add(new SignCell { Uv = new Rect(x0 / (float)N, y0 / (float)N, w / (float)N, h / (float)N), Kind = vertical ? 0 : 1, Tint = filled ? col : col * 0.6f });
            }

            for (int i = 0; i < N * N; i++) px[i] = new Color(0f, 0f, 0f, 1f);
            for (int row = 0; row < 2; row++) for (int c = 0; c < 16; c++) Sign(c * 32, row * 128, 32, 128, true);
            for (int row = 0; row < 6; row++) for (int c = 0; c < 4; c++) Sign(c * 128, 256 + row * 32, 128, 32, false);
            // Shop fronts: warm lit interior, mullions, a header sign strip, dark shelf silhouettes.
            for (int c = 0; c < 4; c++)
            {
                int x0 = c * 128, y0 = 448;
                var head = PickColor();
                for (int y = 0; y < 64; y++)
                {
                    float v = y / 63f;
                    var lit = Color.Lerp(new Color(1f, 0.6f, 0.3f), new Color(1f, 0.95f, 0.75f), v);
                    for (int x = 0; x < 128; x++) px[(y0 + y) * N + x0 + x] = lit;
                }
                for (int x = 0; x < 128; x += 21) Fill(x0 + x, y0, 3, 52, new Color(0.15f, 0.1f, 0.12f));
                Fill(x0, y0 + 30, 128, 2, new Color(0.15f, 0.1f, 0.12f));
                for (int k = 0; k < 6; k++) Fill(x0 + 6 + rnd.Next(110), y0 + 4, 4 + rnd.Next(10), 10 + rnd.Next(14), new Color(0.35f, 0.2f, 0.2f));
                Fill(x0, y0 + 52, 128, 12, head);
                for (int x = x0 + 8; x + 8 <= x0 + 120; x += 12) Glyph(x, y0 + 54, 8, new Color(0.08f, 0.04f, 0.08f));
                cells.Add(new SignCell { Uv = new Rect(x0 / (float)N, y0 / (float)N, 128f / N, 64f / N), Kind = 2, Tint = new Color(1f, 0.75f, 0.45f) });
            }
            Fill(504, 504, 8, 8, Color.white);
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        // ------------------------------------------------------------------------------------------------
        // Mesh helper: quads with arbitrary UV rects and HDR vertex colours
        // ------------------------------------------------------------------------------------------------

        private sealed class UvQuads
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<Vector2> _u = new List<Vector2>();
            private readonly List<Color> _c = new List<Color>();
            private readonly List<int> _t = new List<int>();

            public int Count => _v.Count / 4;

            /// <summary>Camera-facing quad (normal -z) centred at <paramref name="c"/>.</summary>
            public void Quad(Vector3 c, float w, float h, Rect uv, Color col)
            {
                Add(c + new Vector3(-w / 2, -h / 2, 0), c + new Vector3(-w / 2, h / 2, 0), c + new Vector3(w / 2, h / 2, 0), c + new Vector3(w / 2, -h / 2, 0), Vector3.back, uv, col);
            }

            /// <summary>Floor quad (normal +y) centred at <paramref name="c"/>, w (x) by d (z).</summary>
            public void Floor(Vector3 c, float w, float d, Rect uv, Color col)
            {
                Add(c + new Vector3(-w / 2, 0, -d / 2), c + new Vector3(-w / 2, 0, d / 2), c + new Vector3(w / 2, 0, d / 2), c + new Vector3(w / 2, 0, -d / 2), Vector3.up, uv, col);
            }

            private void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Rect uv, Color col)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); }
                _u.Add(new Vector2(uv.xMin, uv.yMin)); _u.Add(new Vector2(uv.xMin, uv.yMax)); _u.Add(new Vector2(uv.xMax, uv.yMax)); _u.Add(new Vector2(uv.xMax, uv.yMin));
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _u); m.SetColors(_c); m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
