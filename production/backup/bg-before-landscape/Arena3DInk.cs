using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// The ink-wash (shan shui) backdrop of <see cref="Arena3DRenderer"/> (<see cref="Arena3DLook.InkWash"/>): layered
    /// mountain ranges painted procedurally like a sumi-e scroll - tall karst peaks with dark brushed ridge lines that
    /// fade into white mist toward their feet, paler and higher with distance - plus pagodas on a mid-range peak,
    /// windswept pines on the nearer ridges, a few birds, and a pale moon. Each range is a horizontally seamless strip
    /// that tiles like the city rows (same follow and wrap logic in <c>Update</c>), so it scrolls with real parallax.
    /// </summary>
    /// <remarks>
    /// Ink value and mist are baked into the textures (fog amount 0); the paper-coloured haze veils of
    /// <c>Haze()</c> sit between the ranges and add the depth mist on top.
    /// </remarks>
    public sealed partial class Arena3DRenderer
    {
        /// <summary>Paper white of the backdrop (mist between the ranges blends toward it).</summary>
        private static readonly Color InkPaper = new Color(0.93f, 0.92f, 0.88f);

        /// <summary>Ink black.</summary>
        private static readonly Color InkBlack = new Color(0.07f, 0.07f, 0.08f);

        private struct InkRange
        {
            public float Z, Period, Height, Base, Value, Alpha, Peaks, PineScale;
            public int Seed;
            public bool Pagodas, Pines, Birds, Waterfall;
        }

        private void InkLandscape()
        {
            // Far to near. Value: 0 = ink black, 1 = paper (distance fades the ink).
            var ranges = new[]
            {
                new InkRange { Z = 310f, Period = 980f, Height = 175f, Base = -40f, Value = 0.5f, Alpha = 0.9f, Peaks = 1.0f, Seed = 11, Birds = true },
                new InkRange { Z = 210f, Period = 700f, Height = 128f, Base = -26f, Value = 0.36f, Alpha = 0.95f, Peaks = 1.1f, Seed = 12 },
                new InkRange { Z = 130f, Period = 440f, Height = 86f, Base = -16f, Value = 0.22f, Alpha = 1f, Peaks = 1.2f, Seed = 13, Pines = true, Waterfall = true, PineScale = 0.42f },
                new InkRange { Z = 75f, Period = 270f, Height = 46f, Base = -8f, Value = 0.12f, Alpha = 1f, Peaks = 1.0f, Seed = 14, Pagodas = true, Pines = true, PineScale = 0.32f },
                new InkRange { Z = 38f, Period = 150f, Height = 17f, Base = -3f, Value = 0.2f, Alpha = 1f, Peaks = 0.55f, Seed = 15, Pines = true, PineScale = 0.18f },
            };
            _cityRows = new Transform[ranges.Length];
            _cityPeriod = new float[ranges.Length];
            for (int i = 0; i < ranges.Length; i++)
            {
                var r = ranges[i];
                var tex = Own(RangeTexture(r));
                string dump = System.Environment.GetEnvironmentVariable("REDLINE_INK_DUMP");
                if (!string.IsNullOrEmpty(dump))
                {
                    System.IO.Directory.CreateDirectory(dump);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dump, "range" + i + ".png"), tex.EncodeToPNG());
                }
                var mat = Own(Proto3DMaterials.FxMat("inkRange" + i, tex, Color.white, 1, 0f, 0f, -95 + i));
                var mesh = Own(Proto3DMeshes.Quad(r.Period, r.Height));
                var row = new GameObject("InkRange " + r.Z).transform;
                row.SetParent(_follow, false);
                _cityRows[i] = row;
                _cityPeriod[i] = r.Period;
                for (int c = -1; c <= 1; c++)
                {
                    var go = MeshGo("range", row, mesh, mat);
                    go.transform.localPosition = new Vector3(c * r.Period, FloorTop + r.Base + r.Height * 0.5f, r.Z);
                    NoShadow(Hidden(go));
                }
            }

            // Pale moon with a faint grey ring, high in the sky behind every range.
            var moonTex = Own(InkMoonTexture());
            var moonMat = Own(Proto3DMaterials.FxMat("inkMoon", moonTex, Color.white, 1, 0f, 0f, -97));
            float size = _look.SunSize * 0.55f;
            var moon = MeshGo("moon", _follow, Own(Proto3DMeshes.Quad(size, size)), moonMat);
            moon.transform.localPosition = new Vector3(_look.SunOffset.x, _look.SunOffset.y + 18f, 380f);
            NoShadow(Hidden(moon));
            InkTemples();   // near temple strip, birds, mist bands, petals (Arena3DTemple.cs)
        }

        // ------------------------------------------------------------------------------------------------
        // Painting
        // ------------------------------------------------------------------------------------------------

        /// <summary>Hermite edge step: 0 below <paramref name="e0"/>, 1 above <paramref name="e1"/> (Mathf.SmoothStep interpolates values instead).</summary>
        private static float InkSmooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private const int RangeW = 1024, RangeH = 384;

        /// <summary>Periodic ridge height 0..1 at <paramref name="u"/> (0..1, wraps).</summary>
        private static float Ridge(float u, int seed, float peaks)
        {
            var rnd = new System.Random(seed);
            float h = 0.34f;
            int[] ks = { 1, 2, 3, 5, 8, 13, 21 };
            float amp = 0.13f;
            foreach (int k in ks)
            {
                float ph = (float)rnd.NextDouble() * 6.283f;
                h += amp * Mathf.Sin(6.283f * k * u + ph);
                amp *= 0.58f;
            }
            // Karst peaks: tall narrow spires (the scroll's needle mountains).
            int n = 3 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                float pc = (float)rnd.NextDouble();
                float w = 0.018f + (float)rnd.NextDouble() * 0.035f;
                float a = (0.2f + (float)rnd.NextDouble() * 0.22f) * peaks;
                float d = Mathf.Abs(u - pc);
                d = Mathf.Min(d, 1f - d);
                float t = d / w;
                h += a * Mathf.Exp(-t * t) * (1f + 0.07f * Mathf.Sin(u * 90f + i * 1.7f));
            }
            if (h > 0.82f) h = 0.82f + (h - 0.82f) * 0.35f;   // soft ceiling: no peak is cut by the strip's top
            return Mathf.Clamp(h, 0.08f, 0.95f);
        }

        /// <summary>Cheap periodic value noise (u wraps at <paramref name="cellsU"/>).</summary>
        private static float Noise(float u, float v, int cellsU, int seed)
        {
            float x = u * cellsU, y = v;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float H(int xi, int yi)
            {
                xi = ((xi % cellsU) + cellsU) % cellsU;
                uint h = (uint)(xi * 374761393 + yi * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h & 0xffff) / 65535f;
            }
            float a = Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), fx);
            float b = Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        private static Texture2D RangeTexture(InkRange r)
        {
            const int W = RangeW, H = RangeH;
            var ridge = new float[W];
            var inner = new float[W];
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                ridge[x] = Ridge(u, r.Seed, r.Peaks);
                inner[x] = ridge[x] - 0.12f - 0.1f * Ridge(u, r.Seed + 101, 0.6f);   // a second, inner fold line
            }
            var ink = Color.Lerp(InkBlack, InkPaper, r.Value);
            var alpha = new float[W * H];
            float edge = 2.6f / H;                       // dark brushed ridge line
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                for (int y = 0; y < H; y++)
                {
                    float v = (y + 0.5f) / H;            // 0 bottom .. 1 top
                    float top = ridge[x];
                    if (v > top) continue;
                    float depth = top - v;
                    // Wash: dense under the ridge, fading into mist toward the feet of the range.
                    float wash = Mathf.Lerp(0.95f, 0.1f, InkSmooth(0f, 0.42f, depth));
                    // Cun texture: vertical brush streaks and dry-brush breaks.
                    float streak = Noise(u, v * 7f, 180, r.Seed) * 0.6f + Noise(u, v * 2f, 60, r.Seed + 7) * 0.4f;
                    wash *= 0.62f + 0.55f * streak;
                    if (depth < edge * (0.8f + Noise(u, 0.5f, 90, r.Seed + 3))) wash = Mathf.Max(wash, 0.97f);
                    float fold = Mathf.Abs(v - inner[x]);
                    if (v < inner[x] + 0.004f && fold < 0.006f + 0.006f * Noise(u, 1.5f, 70, r.Seed + 5))
                        wash = Mathf.Max(wash, 0.75f * (0.6f + 0.4f * streak));
                    // Feet dissolve completely into the mist band.
                    wash *= InkSmooth(0f, 0.22f, v);
                    alpha[y * W + x] = Mathf.Clamp01(wash * r.Alpha);
                }
            }
            if (r.Waterfall) PaintWaterfall(alpha, ridge, W, H);
            if (r.Pagodas) PaintPagodas(alpha, ridge, W, H, r.Seed);
            if (r.Pines) PaintPines(alpha, ridge, W, H, r.Seed, r.PineScale);
            if (r.Birds) PaintBirds(alpha, ridge, W, H, r.Seed);

            var t = new Texture2D(W, H, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "inkRange" + r.Seed,
            };
            var px = new Color32[W * H];
            var c = (Color32)ink;
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(c.r, c.g, c.b, (byte)(alpha[i] * 255f));
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }

        private static void Dab(float[] a, int W, int H, float cx, float cy, float rx, float ry, float val)
        {
            int x0 = Mathf.FloorToInt(cx - rx - 1), x1 = Mathf.CeilToInt(cx + rx + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry - 1)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + ry + 1));
            for (int y = y0; y <= y1; y++)
            for (int xx = x0; xx <= x1; xx++)
            {
                float dx = (xx - cx) / Mathf.Max(0.5f, rx), dy = (y - cy) / Mathf.Max(0.5f, ry);
                float d = dx * dx + dy * dy;
                if (d > 1f) continue;
                int x = ((xx % W) + W) % W;
                float k = val * Mathf.Clamp01((1f - d) * 3f);
                int i = y * W + x;
                if (k > a[i]) a[i] = k;
            }
        }

        private static void Stroke(float[] a, int W, int H, Vector2 p0, Vector2 p1, float w0, float w1, float val)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt((p1 - p0).magnitude * 1.5f));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                var p = Vector2.Lerp(p0, p1, t);
                float w = Mathf.Lerp(w0, w1, t);
                Dab(a, W, H, p.x, p.y, w, w, val);
            }
        }

        /// <summary>A waterfall down the steepest cliff of the range: paper left unpainted between two ink edges, mist at its foot.</summary>
        private static void PaintWaterfall(float[] a, float[] ridge, int W, int H)
        {
            int best = 0; float steep = 0f;
            for (int x = 2; x < W - 2; x++)
            {
                float d = Mathf.Abs(ridge[x + 2] - ridge[x - 2]);
                if (d > steep && ridge[x] > 0.45f) { steep = d; best = x; }
            }
            int top = Mathf.RoundToInt(ridge[best] * H) - 6, bottom = Mathf.RoundToInt(H * 0.12f);
            for (int y = bottom; y < top; y++)
            {
                float t = (top - y) / (float)(top - bottom);
                float half = 2.5f + t * 4f;
                float wob = Mathf.Sin(y * 0.21f) * 0.8f;
                for (int x = Mathf.FloorToInt(best - half + wob); x <= Mathf.CeilToInt(best + half + wob); x++)
                {
                    int xx = ((x % W) + W) % W;
                    float edge = Mathf.Abs(x - (best + wob)) / half;
                    a[y * W + xx] = edge > 0.82f ? Mathf.Max(a[y * W + xx], 0.8f) : a[y * W + xx] * (0.05f + 0.25f * edge * edge);
                }
            }
            for (int k = 0; k < 14; k++)   // spray / mist at the foot: erase soft blobs
            {
                float cx = best + (k - 7) * 2.2f, cy = bottom + (k % 4) * 2.5f;
                for (int y = Mathf.Max(0, (int)cy - 6); y < Mathf.Min(H, (int)cy + 6); y++)
                for (int x = (int)cx - 7; x <= (int)cx + 7; x++)
                {
                    int xx = ((x % W) + W) % W;
                    float d = ((x - cx) * (x - cx)) / 49f + ((y - cy) * (y - cy)) / 36f;
                    if (d < 1f) a[y * W + xx] *= 0.35f + 0.65f * d;
                }
            }
        }

        /// <summary>Two pagodas (tiered roofs with upturned eaves and a spire) on the highest peak of the range.</summary>
        private static void PaintPagodas(float[] a, float[] ridge, int W, int H, int seed)
        {
            int best = 0;
            for (int x = 0; x < W; x++) if (ridge[x] > ridge[best]) best = x;
            // Sit them a little down the shoulder of the peak, where the slope is gentler.
            int[] at = { (best + W / 2 + 40) % W, (best + W / 2 + 70) % W };
            int[] tiers = { 4, 3 };
            for (int k = 0; k < at.Length; k++)
            {
                int x = at[k];
                float baseY = ridge[x] * H - 2f;
                float s = k == 0 ? 1.5f : 1.2f;
                float y = baseY;
                for (int t = 0; t < tiers[k]; t++)
                {
                    float w = (15f - t * 2.6f) * s, bodyH = 4.2f * s, roofH = 2.6f * s;
                    // body
                    for (float yy = y; yy < y + bodyH; yy += 0.5f) Stroke(a, W, H, new Vector2(x - w * 0.32f, yy), new Vector2(x + w * 0.32f, yy), 0.6f, 0.6f, 0.92f);
                    y += bodyH;
                    // roof: a slab with eaves that flick upward at both ends
                    for (float yy = y; yy < y + roofH; yy += 0.5f)
                    {
                        float k2 = (yy - y) / roofH;
                        float half = w * (0.62f - 0.25f * k2);
                        Stroke(a, W, H, new Vector2(x - half, yy), new Vector2(x + half, yy), 0.7f, 0.7f, 1f);
                    }
                    Stroke(a, W, H, new Vector2(x - w * 0.62f, y + 0.3f), new Vector2(x - w * 0.75f, y + 2.4f * s), 0.8f, 0.4f, 1f);
                    Stroke(a, W, H, new Vector2(x + w * 0.62f, y + 0.3f), new Vector2(x + w * 0.75f, y + 2.4f * s), 0.8f, 0.4f, 1f);
                    y += roofH;
                }
                Stroke(a, W, H, new Vector2(x, y), new Vector2(x, y + 7f * s), 0.7f, 0.3f, 1f);   // spire
                for (int r = 0; r < 3; r++) Dab(a, W, H, x, y + 2f * s + r * 1.6f * s, 1.3f * s, 0.6f, 1f);
            }
        }

        /// <summary>Windswept pines on the ridge: a leaning, kinked trunk and flat layered needle clouds.</summary>
        private static void PaintPines(float[] a, float[] ridge, int W, int H, int seed, float scale)
        {
            var rnd = new System.Random(seed * 17 + 3);
            int n = 7 + rnd.Next(5);
            for (int i = 0; i < n; i++)
            {
                int x = rnd.Next(W);
                float baseY = ridge[x] * H - 1.5f;
                if (baseY < H * 0.15f) continue;
                float hgt = (26f + (float)rnd.NextDouble() * 24f) * scale;
                float lean = ((float)rnd.NextDouble() - 0.5f) * 1.2f;
                var p = new Vector2(x, baseY);
                var mid = p + new Vector2(lean * hgt * 0.45f + ((float)rnd.NextDouble() - 0.5f) * 5f, hgt * 0.55f);
                var topP = mid + new Vector2(lean * hgt * 0.5f, hgt * 0.45f);
                Stroke(a, W, H, p, mid, 1.6f, 1.1f, 1f);
                Stroke(a, W, H, mid, topP, 1.1f, 0.5f, 1f);
                // branches with flat needle pads
                int pads = 3 + rnd.Next(3);
                for (int k = 0; k < pads; k++)
                {
                    float t = 0.45f + k / (float)pads * 0.55f;
                    var on = t < 0.55f ? Vector2.Lerp(p, mid, t / 0.55f) : Vector2.Lerp(mid, topP, (t - 0.55f) / 0.45f);
                    float dir = k % 2 == 0 ? 1f : -1f;
                    var end = on + new Vector2(dir * (6f + (float)rnd.NextDouble() * 8f) * scale, (1.5f + (float)rnd.NextDouble() * 2f) * scale);
                    Stroke(a, W, H, on, end, 0.6f, 0.35f, 1f);
                    float pw = (7f + (float)rnd.NextDouble() * 5f) * scale;
                    Dab(a, W, H, end.x, end.y + 0.8f, pw, 1.9f, 0.95f);
                    Dab(a, W, H, end.x + dir * 2f, end.y + 2.2f, pw * 0.7f, 1.4f, 0.9f);
                }
                Dab(a, W, H, topP.x, topP.y, 4.5f * scale, 1.8f * scale, 0.95f);
            }
        }

        /// <summary>A loose flock of small ink birds above the far range.</summary>
        private static void PaintBirds(float[] a, float[] ridge, int W, int H, int seed)
        {
            var rnd = new System.Random(seed * 29 + 1);
            for (int f = 0; f < 3; f++)
            {
                float cx = rnd.Next(W), cy = H * (0.82f + (float)rnd.NextDouble() * 0.12f);
                int n = 3 + rnd.Next(4);
                for (int i = 0; i < n; i++)
                {
                    float x = cx + ((float)rnd.NextDouble() - 0.5f) * 40f, y = cy + ((float)rnd.NextDouble() - 0.5f) * 14f;
                    if (y < ridge[((int)x % W + W) % W] * H + 6f) continue;
                    float s = 1.5f + (float)rnd.NextDouble();
                    Stroke(a, W, H, new Vector2(x - 2f * s, y + 1f * s), new Vector2(x, y), 0.45f, 0.35f, 0.85f);
                    Stroke(a, W, H, new Vector2(x, y), new Vector2(x + 2f * s, y + 1.2f * s), 0.35f, 0.45f, 0.85f);
                }
            }
        }

        /// <summary>Moon: a near-white disc with a soft grey wash ring so it reads on the paper sky.</summary>
        private static Texture2D InkMoonTexture()
        {
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "inkMoon" };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float disc = 1f - InkSmooth(0.60f, 0.63f, r);
                float ring = Mathf.Exp(-Mathf.Pow((r - 0.72f) / 0.12f, 2f)) * 0.22f;
                float a = Mathf.Max(disc, ring);
                var c = disc > ring ? new Color(1f, 1f, 0.985f, a) : new Color(0.62f, 0.62f, 0.6f, a);
                px[y * N + x] = c;
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }
    }
}
