using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// The painted shan-shui landscape of the ink world (<see cref="Arena3DLook.InkWash"/>), after the user's reference
    /// scroll: faceted rock ranges with lit and shaded faces and dark creases, puffy cloud banks drowning the feet of
    /// every range, sparse sky clouds, a dark grey moon crossed by cloud strips, a near row of tall flat-topped rock
    /// pillars (a two-tier temple with stairs, an arched bridge, a waterfall, pines) and, now and then, a gnarled tree on a
    /// dark rock outcrop just behind the play plane - all on warm stained paper.
    /// </summary>
    /// <remarks>
    /// Every layer is painted on an <see cref="InkCanvas"/> (coverage + ink density) and baked into an RGBA texture whose
    /// colour runs from the layer's light tone (density 0) to its dark ink (density 1). Rows tile horizontally like the
    /// old ranges (registered in <c>_cityRows</c>), and their render queues are set far to near so they always layer
    /// correctly regardless of the per-copy sort distance.
    /// </remarks>
    public sealed partial class Arena3DRenderer
    {
        /// <summary>Warm cream of the paper and the clouds.</summary>
        private static readonly Color InkCream = new Color(0.96f, 0.93f, 0.85f);

        /// <summary>A layer being painted: coverage (alpha) and ink density (0 light tone .. 1 dark ink), x wraps.</summary>
        private sealed class InkCanvas
        {
            public readonly int W, H;
            public readonly float[] Cov, Dens;

            public InkCanvas(int w, int h) { W = w; H = h; Cov = new float[w * h]; Dens = new float[w * h]; }

            /// <summary>Paint "over": the new ink covers what is underneath by <paramref name="cov"/>.</summary>
            public void Put(int x, int y, float cov, float dens)
            {
                if (y < 0 || y >= H || cov <= 0f) return;
                int i = y * W + (((x % W) + W) % W);
                Dens[i] = Cov[i] <= 0f ? dens : Mathf.Lerp(Dens[i], dens, cov);
                Cov[i] = Mathf.Max(Cov[i], cov);
            }

            /// <summary>Merges a plain alpha mask (the older brush helpers paint into one) as ink of <paramref name="dens"/>.</summary>
            public void Merge(float[] mask, float dens)
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    float e = Mathf.Clamp01(mask[i]);
                    if (e <= 0f) continue;
                    Dens[i] = Cov[i] <= 0f ? dens : Mathf.Lerp(Dens[i], dens, e);
                    Cov[i] = Mathf.Max(Cov[i], e);
                }
            }

            public void Ellipse(float cx, float cy, float rx, float ry, float dens, float cov = 1f)
            {
                for (int y = Mathf.FloorToInt(cy - ry - 1); y <= Mathf.CeilToInt(cy + ry + 1); y++)
                for (int x = Mathf.FloorToInt(cx - rx - 1); x <= Mathf.CeilToInt(cx + rx + 1); x++)
                {
                    float dx = (x - cx) / Mathf.Max(0.5f, rx), dy = (y - cy) / Mathf.Max(0.5f, ry);
                    float d = dx * dx + dy * dy;
                    if (d <= 1f) Put(x, y, cov * Mathf.Clamp01((1f - d) * 4f), dens);
                }
            }

            public void Line(Vector2 p0, Vector2 p1, float w0, float w1, float dens, float cov = 1f)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt((p1 - p0).magnitude * 1.5f));
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    var p = Vector2.Lerp(p0, p1, t);
                    float w = Mathf.Lerp(w0, w1, t);
                    Ellipse(p.x, p.y, w, w, dens, cov);
                }
            }

            /// <summary>Colours the canvas (pure data: safe off the main thread; <see cref="InkImage.ToTexture"/> makes the texture).</summary>
            public InkImage Bake(string name, Color light, Color dark, bool repeat = false)
            {
                var px = new Color32[W * H];
                for (int i = 0; i < px.Length; i++)
                {
                    var c = Color.Lerp(light, dark, Mathf.Clamp01(Dens[i]));
                    c.a = Mathf.Clamp01(Cov[i]);
                    px[i] = c;
                }
                return new InkImage { Name = name, W = W, H = H, Px = px, Repeat = repeat };
            }
        }

        private static float Hash01(int a, int b)
        {
            uint h = (uint)(a * 374761393 + b * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h & 0xffff) / 65535f;
        }

        private static void DumpInk(string name, Texture2D t)
        {
            string dump = System.Environment.GetEnvironmentVariable("REDLINE_INK_DUMP");
            if (string.IsNullOrEmpty(dump)) return;
            System.IO.Directory.CreateDirectory(dump);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dump, name + ".png"), t.EncodeToPNG());
        }

        /// <summary>Adds a horizontally tiling painted row under the camera follow (three copies, wrapped in Update).</summary>
        private void AddInkRow(string name, Texture2D tex, float z, float period, float height, float baseY, int queue, float alpha = 1f)
        {
            var mat = Own(Proto3DMaterials.FxMat(name, tex, new Color(1f, 1f, 1f, alpha), 1, 0f, 0f, queue));
            var mesh = Own(Proto3DMeshes.Quad(period, height));
            var row = new GameObject(name).transform;
            row.SetParent(_follow, false);
            for (int c = -1; c <= 1; c++)
            {
                var go = MeshGo(name, row, mesh, mat);
                go.transform.localPosition = new Vector3(c * period, baseY + height * 0.5f, z);
                NoShadow(Hidden(go));
            }
            int n = _cityRows == null ? 0 : _cityRows.Length;
            System.Array.Resize(ref _cityRows, n + 1);
            System.Array.Resize(ref _cityPeriod, n + 1);
            _cityRows[n] = row;
            _cityPeriod[n] = period;
        }

        // ------------------------------------------------------------------------------------------------
        // Rock ranges
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// A faceted rock range: the ridge is joined by straight facets, each facet's face is lit (rising to the right,
        /// facing the light from the left) or shaded, creases run down from the ridge vertices, dry-brush streaks break
        /// the faces, and everything pales into the cloud toward the feet.
        /// </summary>
        private static InkImage RockRangeTexture(InkRange r)
        {
            const int W = RangeW, H = RangeH;
            var cv = new InkCanvas(W, H);
            const int Nodes = 72;
            var node = new float[Nodes];
            var rnd = new System.Random(r.Seed * 31 + 5);
            for (int i = 0; i < Nodes; i++) node[i] = Ridge(i / (float)Nodes, r.Seed, r.Peaks) + ((float)rnd.NextDouble() - 0.5f) * 0.04f;
            var ridge = new float[W];
            var facet = new int[W];
            var slope = new float[W];
            for (int x = 0; x < W; x++)
            {
                float f = (x + 0.5f) / W * Nodes;
                int i0 = Mathf.FloorToInt(f) % Nodes, i1 = (i0 + 1) % Nodes;
                ridge[x] = Mathf.Lerp(node[i0], node[i1], f - Mathf.Floor(f));
                facet[x] = i0;
                slope[x] = node[i1] - node[i0];
            }
            float edge = 2.2f / H;
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float bend = (Noise(u, 0.5f, 20, r.Seed + 9) - 0.5f) * 2.4f;
                for (int y = H - 1; y >= 0; y--)
                {
                    float v = (y + 0.5f) / H;
                    if (v > ridge[x]) continue;
                    float d = ridge[x] - v;
                    // The facet under this pixel: creases slant down from the ridge vertices (bent by low noise).
                    int xs = x + Mathf.RoundToInt(d * H * bend);
                    xs = ((xs % W) + W) % W;
                    int xs1 = (((xs + 1) % W) + W) % W;
                    int id = facet[xs];
                    bool lit = slope[xs] > 0f;
                    float dens = lit ? 0.22f : 0.66f;
                    dens += (Hash01(id, r.Seed) - 0.5f) * 0.22f;
                    float streak = Noise(u, v * 6f, 220, r.Seed) * 0.6f + Noise(u, v * 18f, 90, r.Seed + 4) * 0.4f;
                    dens *= 0.7f + 0.55f * streak;
                    // Crease where the facet changes (a dark line running down from each vertex).
                    if (facet[xs1] != id && (slope[xs1] > 0f) != lit) dens = Mathf.Max(dens, 0.85f);
                    else if (facet[xs1] != id) dens = Mathf.Max(dens, 0.5f);
                    // Horizontal ledges: short dry-brush strata on the shaded faces.
                    if (!lit && Noise(u, v * 40f, 160, r.Seed + 13) > 0.8f) dens = Mathf.Max(dens, 0.8f);
                    // Pale with depth below the ridge, into the cloud.
                    dens *= Mathf.Lerp(1f, 0.4f, InkSmooth(0.02f, 0.5f, d));
                    if (d < edge * (0.8f + Noise(u, 0.5f, 90, r.Seed + 3))) dens = 0.95f;   // dark brushed ridge line
                    float cov = InkSmooth(0f, 0.3f, v) * r.Alpha;
                    cv.Put(x, y, cov, Mathf.Clamp01(dens));
                }
            }
            if (r.Waterfall)
            {
                int best = 0; float steep = 0f;
                for (int x = 3; x < W - 3; x++)
                {
                    float s = Mathf.Abs(ridge[x + 3] - ridge[x - 3]);
                    if (s > steep && ridge[x] > 0.45f) { steep = s; best = x; }
                }
                int top = Mathf.RoundToInt(ridge[best] * H) - 4, bottom = Mathf.RoundToInt(H * 0.18f);
                for (int y = bottom; y < top; y++)
                {
                    float t = (top - y) / (float)Mathf.Max(1, top - bottom);
                    float half = 2f + t * 3.5f;
                    float wob = Mathf.Sin(y * 0.19f) * 0.8f;
                    for (int x = Mathf.FloorToInt(best - half + wob); x <= Mathf.CeilToInt(best + half + wob); x++)
                    {
                        float e = Mathf.Abs(x - (best + wob)) / half;
                        cv.Put(x, y, 1f, e > 0.8f ? 0.75f : 0.02f + 0.1f * Noise(x / (float)W, y * 0.3f, 300, 5));
                    }
                }
            }
            var extra = new float[W * H];
            if (r.Pagodas) PaintPagodas(extra, ridge, W, H, r.Seed);
            if (r.Pines) PaintPines(extra, ridge, W, H, r.Seed, r.PineScale);
            if (r.Birds) PaintBirds(extra, ridge, W, H, r.Seed);
            cv.Merge(extra, 0.95f);
            var dark = Color.Lerp(InkBlack, InkPaper, r.Value);
            var light = Color.Lerp(dark, InkCream, 0.86f);
            return cv.Bake("inkRock" + r.Seed, light, dark);
        }

        // ------------------------------------------------------------------------------------------------
        // Clouds, moon, paper
        // ------------------------------------------------------------------------------------------------

        private struct Puff { public float X, Y, R, Floor; }

        /// <summary>
        /// Cloud texture. A bank (<paramref name="sparse"/> false): a row of overlapping puffs along a baseline with solid
        /// cloud below it, shaded underneath and outlined in grey along their tops and seams. Sparse: a few small flat-bottomed
        /// clusters in open sky.
        /// </summary>
        private static InkImage CloudTexture(int seed, bool sparse)
        {
            const int W = 1024, H = 256;
            var rnd = new System.Random(seed);
            var puffs = new System.Collections.Generic.List<Puff>();
            float baseY = H * 0.5f;
            if (!sparse)
            {
                float x = 0f;
                while (x < W)
                {
                    float rr = H * (0.06f + (float)rnd.NextDouble() * 0.1f);
                    puffs.Add(new Puff { X = x, Y = baseY + ((float)rnd.NextDouble() - 0.3f) * H * 0.12f, R = rr, Floor = -1f });
                    if (rnd.NextDouble() < 0.5)   // a second tier on top
                        puffs.Add(new Puff { X = x + rr * 0.4f, Y = baseY + rr * 0.9f, R = rr * 0.6f, Floor = -1f });
                    x += rr * (0.7f + (float)rnd.NextDouble() * 0.5f);
                }
            }
            else
            {
                for (int c = 0; c < 4; c++)
                {
                    float cx = c * W / 4f + (float)rnd.NextDouble() * W * 0.15f;
                    float cy = H * (0.3f + (float)rnd.NextDouble() * 0.4f);
                    int n = 5 + rnd.Next(5);
                    float span = 50f + (float)rnd.NextDouble() * 90f;
                    for (int k = 0; k < n; k++)
                    {
                        float t = k / (float)(n - 1);
                        float rr = H * (0.04f + 0.08f * Mathf.Sin(t * Mathf.PI)) * (0.8f + 0.4f * (float)rnd.NextDouble());
                        puffs.Add(new Puff { X = cx + (t - 0.5f) * span * 2f, Y = cy + rr * 0.35f, R = rr, Floor = cy });
                    }
                }
            }
            var cv = new InkCanvas(W, H);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float best = -1e9f, second = -1e9f; int bi = -1;
                for (int i = 0; i < puffs.Count; i++)
                {
                    var p = puffs[i];
                    float dx = Mathf.Abs(x - p.X); dx = Mathf.Min(dx, W - dx);
                    if (dx > p.R + 3f) continue;
                    float dy = y - p.Y;
                    if (p.Floor >= 0f && y < p.Floor) continue;   // flat cloud bottoms
                    float f = p.R - Mathf.Sqrt(dx * dx + dy * dy);
                    if (f > best) { second = best; best = f; bi = i; }
                    else if (f > second) second = f;
                }
                bool below = !sparse && y < baseY;
                float cov = Mathf.Clamp01(best + 1f);
                if (below) cov = 1f;
                if (cov <= 0f) continue;
                float dens = 0f;
                if (bi >= 0 && best > -1f)
                {
                    var p = puffs[bi];
                    float ny = (y - p.Y) / p.R;
                    dens = 0.2f * InkSmooth(0f, -1f, ny);                                  // shaded underside of each puff
                    if (best < 2.2f && ny > -0.35f) dens = 0.55f;                         // outline along the tops
                    else if (best - second < 1.6f && ny > -0.2f) dens = Mathf.Max(dens, 0.42f);   // seams between puffs
                    if (p.Floor >= 0f && y - p.Floor < 1.5f) dens = Mathf.Max(dens, 0.35f);       // flat bottom line
                }
                else if (below) dens = 0.18f;
                if (below && (bi < 0 || best <= -1f)) dens = 0.16f;
                if (!sparse) cov *= InkSmooth(0f, H * 0.22f, y);
                cv.Put(x, y, cov, dens);
            }
            return cv.Bake(sparse ? "inkSkyCloud" + seed : "inkCloud" + seed, InkCream, new Color(0.36f, 0.35f, 0.33f));
        }

        /// <summary>A dark grey moon, softly mottled, with two strips of cloud drifting across its lower half.</summary>
        private static InkImage InkMoonTexture()
        {
            const int N = 256;
            var cv = new InkCanvas(N, N);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float disc = 1f - InkSmooth(0.55f, 0.58f, r);
                if (disc <= 0f) continue;
                float mott = Noise((x + 0.5f) / N, y / 24f, 10, 3) * 0.6f + Noise((x + 0.5f) / N, y / 9f, 28, 4) * 0.4f;
                cv.Put(x, y, disc, 0.5f + 0.18f * mott + 0.12f * InkSmooth(0.3f, 0.57f, r));
            }
            // Cloud strips: long cream bands with grey edges, tapered at both ends.
            float[] ys = { 0.36f, 0.22f };
            float[] hs = { 0.07f, 0.05f };
            for (int s = 0; s < ys.Length; s++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N;
                float taper = Mathf.Sin(Mathf.Clamp01((u - 0.04f - s * 0.1f) / 0.85f) * Mathf.PI);
                float half = hs[s] * N * 0.5f * taper * (0.75f + 0.5f * Noise(u, s, 12, 7));
                if (half < 0.5f) continue;
                float cy = ys[s] * N + Mathf.Sin(u * 7f + s) * 2f;
                for (int y = Mathf.FloorToInt(cy - half - 1); y <= Mathf.CeilToInt(cy + half + 1); y++)
                {
                    float e = Mathf.Abs(y - cy) / half;
                    if (e > 1f) continue;
                    cv.Put(x, y, 1f, e > 0.75f ? 0.6f : 0.05f + 0.12f * InkSmooth(0f, -1f, (y - cy) / half));
                }
            }
            var t = cv.Bake("inkMoon", InkCream, new Color(0.18f, 0.18f, 0.19f));
            return t;
        }

        /// <summary>Paper stains: faint warm blotches and fibre over the whole sky (tiles).</summary>
        private static InkImage SkyStainTexture()
        {
            const int W = 512, H = 256;
            var cv = new InkCanvas(W, H);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float n = Noise(u, v * 3f, 5, 91) * 0.55f + Noise(u, v * 7f, 12, 92) * 0.3f + Noise(u, v * 20f, 40, 93) * 0.15f;
                float stain = InkSmooth(0.52f, 0.78f, n) * 0.22f;
                float ring = Mathf.Abs(n - 0.6f) < 0.012f ? 0.12f : 0f;              // dried water-stain edges
                float fibre = Noise(u, v * 90f, 260, 94) > 0.86f ? 0.05f : 0f;
                float a = stain + ring + fibre;
                if (a > 0f) cv.Put(x, y, a, 0.5f + n * 0.3f);
            }
            return cv.Bake("inkSkyStain", new Color(0.86f, 0.8f, 0.68f), new Color(0.48f, 0.42f, 0.34f), true);
        }

        // ------------------------------------------------------------------------------------------------
        // Near rock pillars
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// The near row: tall flat-topped rock pillars rising out of the clouds - vertical lit/mid/shaded faces with
        /// crevices and strata, scrub and pines on the tops, a two-tier temple with stairs on the tallest, an arched
        /// bridge between two neighbours and a waterfall pouring down one face into the cloud.
        /// </summary>
        private static InkImage PillarsTexture(int seed)
        {
            const int W = 1024, H = 512;
            var cv = new InkCanvas(W, H);
            var rnd = new System.Random(seed);
            const int N = 6;
            var cx = new float[N]; var hw = new float[N]; var top = new float[N];
            for (int i = 0; i < N; i++)
            {
                cx[i] = (i + 0.5f) * W / N + ((float)rnd.NextDouble() - 0.5f) * 40f;
                hw[i] = 18f + (float)rnd.NextDouble() * 22f;
                top[i] = H * (0.42f + (float)rnd.NextDouble() * 0.4f);
            }
            int temple = 1;                       // the tallest, widest pillar carries the temple
            top[temple] = H * 0.86f; hw[temple] = 50f;
            int bridgeA = 3, bridgeB = 4;         // an arched bridge between these two
            top[bridgeA] = H * 0.66f; top[bridgeB] = H * 0.6f;
            hw[bridgeA] = Mathf.Min(hw[bridgeA], 30f); hw[bridgeB] = Mathf.Min(hw[bridgeB], 30f);
            int fall = 5;                         // a waterfall pours down this one
            hw[fall] = Mathf.Max(hw[fall], 32f);

            for (int i = 0; i < N; i++)
            {
                int ps = seed * 13 + i * 7;
                int y0 = 0, y1 = Mathf.RoundToInt(top[i]);
                for (int y = y0; y <= y1 + 6; y++)
                {
                    float v = y / (float)H;
                    float w = hw[i] * (0.92f + 0.12f * Mathf.Sin(v * 9f + i) + 0.1f * Noise(0.5f, v * 6f, 1, ps + 1));
                    float edgeL = (Noise(0.2f, v * 40f, 1, ps + 2) - 0.5f) * 5f, edgeR = (Noise(0.7f, v * 40f, 1, ps + 3) - 0.5f) * 5f;
                    // Flat top with a rounded shoulder and a little scrub.
                    float shoulder = 0f;
                    if (y > y1 - 8) shoulder = (y - (y1 - 8)) * 1.4f;
                    float L = cx[i] - w - edgeL + shoulder, R = cx[i] + w + edgeR - shoulder;
                    if (y > y1) continue;
                    for (int x = Mathf.FloorToInt(L); x <= Mathf.CeilToInt(R); x++)
                    {
                        float s = (x - cx[i]) / w;                                       // -1 left .. 1 right
                        float wig = (Noise(0.3f, v * 8f, 1, ps + 4) - 0.5f) * 0.3f;
                        float dens = s < -0.5f + wig ? 0.14f : s < 0.15f + wig ? 0.34f : s < 0.62f + wig ? 0.62f : 0.78f;
                        dens += (Noise(x / (float)W, v * 10f, 300, ps + 5) - 0.5f) * 0.2f;
                        // Vertical crevices at the face boundaries, horizontal strata cracks.
                        float b1 = Mathf.Abs(s - (-0.5f + wig)), b2 = Mathf.Abs(s - (0.15f + wig)), b3 = Mathf.Abs(s - (0.62f + wig));
                        float px1 = 1.2f / w;
                        if (b1 < px1 || b3 < px1) dens = Mathf.Max(dens, 0.82f);
                        else if (b2 < px1) dens = Mathf.Max(dens, 0.6f);
                        float strata = Noise(x / (float)W * 0.5f, v * 120f, 40, ps + 6);
                        if (strata > 0.88f && Noise(x / (float)W, v * 2f, 60, ps + 7) > 0.5f) dens = Mathf.Max(dens, 0.55f);
                        if (x - L < 1.6f || R - x < 1.6f || y1 - y < 1.6f) dens = 0.95f;      // outline
                        float cov = InkSmooth(0f, H * 0.2f, y);
                        cv.Put(x, y, cov, Mathf.Clamp01(dens));
                    }
                }
                // Scrub along the top.
                for (int k = 0; k < 6; k++)
                    cv.Ellipse(cx[i] + ((float)rnd.NextDouble() - 0.5f) * hw[i] * 1.6f, top[i] + 1.5f, 3f + (float)rnd.NextDouble() * 4f, 1.6f, 0.92f);
            }

            var extra = new float[W * H];
            // Pines on the tops of the plain pillars.
            for (int i = 0; i < N; i++)
            {
                if (i == temple) continue;
                int count = i == bridgeA || i == bridgeB ? 1 : 2;
                for (int k = 0; k < count; k++)
                    Pine(extra, W, H, cx[i] + ((float)rnd.NextDouble() - 0.5f) * hw[i], top[i], 0.7f + (float)rnd.NextDouble() * 0.5f, rnd);
            }
            // Temple: a two-tier hall on the tallest pillar, with pines beside it.
            Pagoda(extra, W, H, cx[temple], top[temple] + 1f, 2, 2.1f);
            Pine(extra, W, H, cx[temple] - hw[temple] * 0.85f, top[temple], 0.9f, rnd);
            Pine(extra, W, H, cx[temple] + hw[temple] * 0.9f, top[temple], 0.75f, rnd);
            cv.Merge(extra, 0.95f);

            // Stairs zig-zagging down the temple pillar's lit face.
            {
                float sx = cx[temple] - hw[temple] * 0.2f, sy = top[temple];
                for (int k = 0; k < 26; k++)
                {
                    float dir = (k / 9) % 2 == 0 ? -1f : 1f;
                    var a = new Vector2(sx, sy);
                    var b = new Vector2(sx + dir * 3.2f, sy);
                    cv.Line(a, b, 0.6f, 0.6f, 0.9f);
                    cv.Line(b, b - new Vector2(0f, 3f), 0.6f, 0.6f, 0.9f);
                    sx += dir * 3.2f; sy -= 3f;
                    if (sx < cx[temple] - hw[temple] * 0.85f) sx = cx[temple] - hw[temple] * 0.85f;
                }
            }

            // Arched bridge between two pillars: deck, arch underneath, railing with posts.
            {
                float xa = cx[bridgeA] + hw[bridgeA] * 0.9f, xb = cx[bridgeB] - hw[bridgeB] * 0.9f;
                float yb = Mathf.Min(top[bridgeA], top[bridgeB]) - H * 0.07f;
                Vector2 P(float t, float lift, float rise) => new Vector2(Mathf.Lerp(xa, xb, t), yb + lift + 4f * rise * t * (1f - t));
                for (int k = 0; k < 40; k++)
                {
                    float t0 = k / 40f, t1 = (k + 1) / 40f;
                    cv.Line(P(t0, 0f, 10f), P(t1, 0f, 10f), 1.4f, 1.4f, 0.95f);       // deck
                    cv.Line(P(t0, -5f, 14f), P(t1, -5f, 14f), 0.8f, 0.8f, 0.85f);     // arch
                    cv.Line(P(t0, 6f, 10f), P(t1, 6f, 10f), 0.6f, 0.6f, 0.9f);        // rail
                    if (k % 4 == 0) cv.Line(P(t0, 0f, 10f), P(t0, 6f, 10f), 0.5f, 0.5f, 0.9f);
                }
                // Bridge abutments cut into the pillars.
                cv.Ellipse(xa, yb - 2f, 3f, 4f, 0.9f);
                cv.Ellipse(xb, yb - 2f, 3f, 4f, 0.9f);
            }

            // Waterfall: from a notch in the top of one pillar down its face into the cloud.
            {
                float fx = cx[fall] + hw[fall] * 0.25f;
                for (int y = Mathf.RoundToInt(H * 0.08f); y < top[fall] + 1; y++)
                {
                    float t = (top[fall] - y) / top[fall];
                    float half = 2.4f + t * 5f;
                    float wob = Mathf.Sin(y * 0.15f) * 0.7f;
                    for (int x = Mathf.FloorToInt(fx - half + wob); x <= Mathf.CeilToInt(fx + half + wob); x++)
                    {
                        float e = Mathf.Abs(x - (fx + wob)) / half;
                        float streaks = Noise(x / (float)W, y * 0.06f, 400, 17) > 0.7f ? 0.18f : 0.02f;
                        cv.Put(x, y, 1f, e > 0.82f ? 0.7f : streaks);
                    }
                }
            }
            var dark = Color.Lerp(InkBlack, InkPaper, 0.12f);
            return cv.Bake("inkPillars" + seed, Color.Lerp(dark, InkCream, 0.84f), dark);
        }

        // ------------------------------------------------------------------------------------------------
        // Foreground tree
        // ------------------------------------------------------------------------------------------------

        /// <summary>A gnarled pine leaning out from a dark rock outcrop with grass at its foot (one per period).</summary>
        private static InkImage ForeTreeTexture(int seed)
        {
            const int W = 2048, H = 256;
            var cv = new InkCanvas(W, H);
            var rnd = new System.Random(seed);
            // Outcrop: a lumpy faceted rock mound.
            const float Rx0 = 40f, Rx1 = 320f;
            for (int x = (int)Rx0; x <= (int)Rx1; x++)
            {
                float t = (x - Rx0) / (Rx1 - Rx0);
                float h = 70f * Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.7f) * (0.75f + 0.35f * Noise(t, 0.5f, 6, seed));
                for (int y = 0; y < h; y++)
                {
                    float facet = Noise(t * 1.3f + y * 0.004f, 0.5f, 9, seed + 1);
                    float dens = facet > 0.55f ? 0.62f : 0.9f;
                    if (h - y < 2f) dens = 0.98f;
                    cv.Put(x, y, 1f, dens);
                }
            }
            // Grass tufts along the rock and the ground beside it.
            for (int k = 0; k < 90; k++)
            {
                float x = Rx0 - 30f + (float)rnd.NextDouble() * (Rx1 - Rx0 + 80f);
                float t = Mathf.Clamp01((x - Rx0) / (Rx1 - Rx0));
                float baseY = x < Rx0 || x > Rx1 ? 0f : 70f * Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.7f) * (0.75f + 0.35f * Noise(t, 0.5f, 6, seed)) - 2f;
                float len = 6f + (float)rnd.NextDouble() * 12f;
                float lean = ((float)rnd.NextDouble() - 0.3f) * 6f;
                cv.Line(new Vector2(x, baseY), new Vector2(x + lean, baseY + len), 0.9f, 0.3f, 0.95f);
            }
            // Gnarled trunk: twisting segments leaning out to the right.
            var pts = new[] { new Vector2(175f, 55f), new Vector2(158f, 95f), new Vector2(185f, 135f), new Vector2(232f, 160f), new Vector2(300f, 172f), new Vector2(350f, 196f) };
            for (int i = 0; i < pts.Length - 1; i++)
                cv.Line(pts[i], pts[i + 1], 8f - i * 1.2f, 7f - i * 1.2f, 0.97f);
            // Bark highlights: a lighter stroke along the upper edge of the trunk.
            for (int i = 0; i < pts.Length - 2; i++)
                cv.Line(pts[i] + new Vector2(-1.5f, 2f), pts[i + 1] + new Vector2(-1.5f, 2f), 1.2f, 1f, 0.55f);
            // Branches with flat needle pads.
            var branches = new[] { (from: pts[2], to: new Vector2(120f, 168f)), (from: pts[3], to: new Vector2(250f, 205f)), (from: pts[4], to: new Vector2(330f, 140f)), (from: pts[5], to: new Vector2(400f, 205f)), (from: pts[5], to: new Vector2(330f, 226f)) };
            foreach (var b in branches)
            {
                cv.Line(b.from, b.to, 2.4f, 1.2f, 0.97f);
                float pw = 22f + (float)rnd.NextDouble() * 14f;
                cv.Ellipse(b.to.x, b.to.y + 2f, pw, 6f, 0.95f);
                cv.Ellipse(b.to.x + 4f, b.to.y + 7f, pw * 0.7f, 4.5f, 0.92f);
                for (int k = 0; k < 10; k++)   // needle tufts breaking the pad edge
                    cv.Line(new Vector2(b.to.x - pw + k * pw * 0.2f, b.to.y + 1f), new Vector2(b.to.x - pw + k * pw * 0.2f + 2f, b.to.y + 9f + (k % 3) * 2f), 0.8f, 0.3f, 0.95f);
            }
            var dark = Color.Lerp(InkBlack, InkPaper, 0.04f);
            return cv.Bake("inkForeTree" + seed, Color.Lerp(dark, InkCream, 0.55f), dark);
        }
    }
}
