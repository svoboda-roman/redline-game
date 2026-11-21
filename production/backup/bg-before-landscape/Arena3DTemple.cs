using System.Collections.Generic;
using Redline.Core;
using Redline.Game.Atmosphere;
using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Japanese-gothic dressing of the ink world (<see cref="Arena3DLook.InkWash"/>), visual only:
    /// <list type="bullet">
    /// <item>temple eaves: a dark tiled cornice along the top front edge of every platform and wall cap, with tile ribs and
    /// upturned corner horns (kept below the walking surface, in front of the fascia);</item>
    /// <item>a near temple layer (z 48): a five-storey pagoda on a rock outcrop, torii gates, stone lanterns and pine
    /// clusters, painted like the ranges and tiled with the same follow logic;</item>
    /// <item>life: a few flocks of ink birds crossing the sky, slowly drifting mist bands and falling red petals.</item>
    /// </list>
    /// </summary>
    public sealed partial class Arena3DRenderer
    {
        private Mesh _eaveMesh;
        private Material _eaveMat, _eaveRibMat;
        private readonly List<Transform> _birds = new List<Transform>();
        private readonly List<float> _birdSpeed = new List<float>();
        private readonly List<Material> _mistMats = new List<Material>();
        private Material _birdMatA, _birdMatB;
        private MeshRenderer[] _birdRenderers = new MeshRenderer[0];

        // ------------------------------------------------------------------------------------------------
        // Eaves
        // ------------------------------------------------------------------------------------------------

        /// <summary>Builds the shared eave mesh (one per level, instanced into every ring copy).</summary>
        private void BuildEaves()
        {
            _eaveMat = Own(Proto3DMaterials.Lit3D("eaveTile", new Color(0.075f, 0.072f, 0.08f), 0.55f, 0.2f));
            _eaveRibMat = Own(Proto3DMaterials.Lit3D("eaveRib", new Color(0.13f, 0.125f, 0.135f), 0.45f, 0.2f));
            var b = new BoxBuilder(1f / GrimeUnits, 2);
            foreach (var box in _boxes)
            {
                bool open = true;
                for (int x = box.X0; x < box.X1 && open; x++) if (_map.IsSolid(x, box.Y1)) open = false;
                if (!open) continue;
                float x0 = box.X0 - 0.22f, x1 = box.X1 + 0.22f, y = box.Y1;
                // Tile slab: slopes down and out (front lower than back), its top just under the walking surface.
                b.Box(0, new Vector3(x0, y - 0.2f, -0.86f), new Vector3(x1, y - 0.07f, -0.48f));
                b.Box(0, new Vector3(x0 + 0.05f, y - 0.26f, -0.92f), new Vector3(x1 - 0.05f, y - 0.18f, -0.84f));   // drip edge
                // Rounded tile ribs (lighter), every quarter unit.
                for (float x = x0 + 0.1f; x < x1 - 0.05f; x += 0.25f)
                    b.Box(1, new Vector3(x, y - 0.08f, -0.88f), new Vector3(x + 0.08f, y - 0.03f, -0.5f));
                // Upturned corner horns (the gothic flick of a temple roof).
                foreach (float sx in new[] { -1f, 1f })
                {
                    float xe = sx < 0 ? x0 : x1;
                    b.Oriented(0, new Vector3(xe + sx * 0.06f, y - 0.06f, -0.8f), Quaternion.Euler(0f, 0f, sx * -38f), new Vector3(0.34f, 0.09f, 0.16f));
                    b.Oriented(0, new Vector3(xe + sx * 0.17f, y + 0.07f, -0.8f), Quaternion.Euler(0f, 0f, sx * -62f), new Vector3(0.18f, 0.06f, 0.1f));
                }
                // Stone brackets under the cornice every ~1.5 units (shadowed corbels).
                for (float x = box.X0 + 0.5f; x < box.X1 - 0.3f; x += 1.5f)
                    b.Box(0, new Vector3(x - 0.07f, y - 0.42f, -0.62f), new Vector3(x + 0.07f, y - 0.2f, -0.5f));
            }
            _eaveMesh = Own(b.Build("eaves"));
        }

        // ------------------------------------------------------------------------------------------------
        // Kerosene lanterns (replace the street lamps in the ink world)
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Hurricane (kerosene) lanterns at the lamp spots: a timber post with an iron arm (floor spots), or a chain under
        /// the platform (ceiling spots); each lantern has a base, four frame wires round a glowing glass globe, a cap with a
        /// chimney and a wire bail. The globe and flame go in the emissive head mesh, iron and timber in the post mesh.
        /// </summary>
        private void BuildLanternMeshes()
        {
            var posts = new List<Mesh>();
            var heads = new List<Mesh>();
            void Box(List<Mesh> list, Vector3 min, Vector3 max) => list.Add(Proto3DMeshes.ChamferBox(min, max, 0f, 4f));
            void Lantern(Vector3 c, float s)
            {
                Box(posts, c + new Vector3(-0.17f, -0.3f, -0.17f) * s, c + new Vector3(0.17f, -0.21f, 0.17f) * s);      // base
                Box(heads, c + new Vector3(-0.12f, -0.21f, -0.12f) * s, c + new Vector3(0.12f, 0.15f, 0.12f) * s);      // glass globe
                Box(heads, c + new Vector3(-0.045f, -0.16f, -0.045f) * s, c + new Vector3(0.045f, 0.02f, 0.045f) * s);  // flame
                foreach (var dx in new[] { -1f, 1f })
                foreach (var dz in new[] { -1f, 1f })
                    Box(posts, c + new Vector3(dx * 0.15f - 0.015f, -0.22f, dz * 0.15f - 0.015f) * s, c + new Vector3(dx * 0.15f + 0.015f, 0.17f, dz * 0.15f + 0.015f) * s);
                Box(posts, c + new Vector3(-0.16f, 0.15f, -0.16f) * s, c + new Vector3(0.16f, 0.24f, 0.16f) * s);       // cap
                Box(posts, c + new Vector3(-0.07f, 0.24f, -0.07f) * s, c + new Vector3(0.07f, 0.33f, 0.07f) * s);       // chimney
                Box(posts, c + new Vector3(-0.15f, 0.24f, -0.015f) * s, c + new Vector3(-0.12f, 0.44f, 0.015f) * s);    // bail sides
                Box(posts, c + new Vector3(0.12f, 0.24f, -0.015f) * s, c + new Vector3(0.15f, 0.44f, 0.015f) * s);
                Box(posts, c + new Vector3(-0.15f, 0.42f, -0.015f) * s, c + new Vector3(0.15f, 0.45f, 0.015f) * s);     // bail top
            }
            foreach (var spot in _spots)
            {
                var head = LampHead(spot);
                float x = head.x;
                switch (spot.Kind)
                {
                    case LampField.Kind.Post:
                    {
                        // Timber post behind the play plane, an iron arm forward over it, the lantern hanging from the arm.
                        float baseY = spot.Y + 1f, z = 1.4f, top = head.y + 0.75f;
                        Box(posts, new Vector3(x - 0.08f, baseY, z - 0.08f), new Vector3(x + 0.08f, top, z + 0.08f));
                        Box(posts, new Vector3(x - 0.035f, top - 0.1f, head.z), new Vector3(x + 0.035f, top - 0.03f, z));
                        Box(posts, new Vector3(x - 0.015f, head.y + 0.45f, head.z - 0.015f), new Vector3(x + 0.015f, top - 0.1f, head.z + 0.015f));   // hook wire
                        Lantern(head, 1.15f);
                        break;
                    }
                    case LampField.Kind.Ceiling:
                    {
                        float y0 = spot.Y;
                        Box(posts, new Vector3(x - 0.04f, y0 - 0.08f, head.z - 0.04f), new Vector3(x + 0.04f, y0, -0.5f));
                        for (float y = head.y + 0.45f; y < y0 - 0.08f; y += 0.14f)   // chain links
                            Box(posts, new Vector3(x - 0.025f, y, head.z - 0.025f), new Vector3(x + 0.025f, y + 0.09f, head.z + 0.025f));
                        Lantern(head, 1.1f);
                        break;
                    }
                    default:
                    {
                        // Signal spot: a small lantern on a short chain (its light flickers in Update).
                        float y0 = spot.Y;
                        for (float y = head.y + 0.3f; y < y0; y += 0.12f)
                            Box(posts, new Vector3(head.x - 0.02f, y, head.z - 0.02f), new Vector3(head.x + 0.02f, y + 0.08f, head.z + 0.02f));
                        Lantern(head, 0.75f);
                        break;
                    }
                }
            }
            _postMesh = Own(posts.Count > 0 ? Proto3DMeshes.MergeTwoSub(posts, "lanternIron") : new Mesh());
            _headMesh = Own(heads.Count > 0 ? Proto3DMeshes.MergeTwoSub(heads, "lanternGlass") : new Mesh());
        }

        // ------------------------------------------------------------------------------------------------
        // Near temple layer + life
        // ------------------------------------------------------------------------------------------------

        /// <summary>The near temple strip, birds, mist bands and petals (after <see cref="InkLandscape"/>).</summary>
        private void InkTemples()
        {
            // Temple strip: append to the parallax rows (same wrap/follow as the ranges).
            const float Z = 48f, Period = 190f, Height = 46f, Base = -6f;
            var tex = Own(TempleTexture(31));
            var mat = Own(Proto3DMaterials.FxMat("inkTemples", tex, Color.white, 1, 0f, 0f, -90));
            var mesh = Own(Proto3DMeshes.Quad(Period, Height));
            var row = new GameObject("InkTemples").transform;
            row.SetParent(_follow, false);
            for (int c = -1; c <= 1; c++)
            {
                var go = MeshGo("temples", row, mesh, mat);
                go.transform.localPosition = new Vector3(c * Period, FloorTop + Base + Height * 0.5f, Z);
                NoShadow(Hidden(go));
            }
            int n = _cityRows.Length;
            System.Array.Resize(ref _cityRows, n + 1);
            System.Array.Resize(ref _cityPeriod, n + 1);
            _cityRows[n] = row;
            _cityPeriod[n] = Period;

            // Drifting mist bands (scrolling texture) between the layers.
            var mistTex = Own(MistBandTexture());
            float[] zs = { 58f, 90f, 140f };   // behind the temple strip (z 48), between the ranges
            for (int i = 0; i < zs.Length; i++)
            {
                var col = InkPaper; col.a = 0.32f - i * 0.06f;
                var mm = Own(Proto3DMaterials.FxMat("inkMist" + i, mistTex, col, 1, 0f, 0f, -80 + i * 4));
                mm.mainTextureScale = new Vector2(3f, 1f);
                _mistMats.Add(mm);
                var g = MeshGo("mist", _follow, Own(Proto3DMeshes.Quad(260f + zs[i] * 3f, 9f + i * 4f)), mm);
                g.transform.localPosition = new Vector3(0f, FloorTop + 1.5f + i * 3.5f, zs[i]);
                NoShadow(Hidden(g));
            }

            // Ink birds: two flap frames swapped on the material, flocks crossing at different heights and depths.
            _birdMatA = Own(Proto3DMaterials.FxMat("inkBirdA", Own(BirdTexture(false)), Color.white, 1, 0f, 0f, -85));
            _birdMatB = Own(Proto3DMaterials.FxMat("inkBirdB", Own(BirdTexture(true)), Color.white, 1, 0f, 0f, -85));
            var quad = Own(Proto3DMeshes.Quad(1f, 0.5f));
            var rnd = new System.Random(_look.Seed * 7 + 3);
            var rends = new List<MeshRenderer>();
            for (int f = 0; f < 3; f++)
            {
                float z = 60f + f * 35f, y = FloorTop + 14f + f * 6f + (float)rnd.NextDouble() * 4f;
                float x0 = -80f + (float)rnd.NextDouble() * 160f;
                int count = 4 + rnd.Next(4);
                for (int k = 0; k < count; k++)
                {
                    var go = MeshGo("bird", _follow, quad, _birdMatA);
                    float s = 1.6f + z * 0.02f;
                    go.transform.localScale = new Vector3(s, s, 1f);
                    go.transform.localPosition = new Vector3(x0 - k * 3.2f * (1 + (float)rnd.NextDouble()), y + ((k % 2) - 0.5f) * 2f * (k / 2), z);
                    NoShadow(Hidden(go));
                    _birds.Add(go.transform);
                    _birdSpeed.Add(2.2f + f * 0.6f);
                    rends.Add(go.GetComponent<MeshRenderer>());
                }
            }
            _birdRenderers = rends.ToArray();

            // Falling red petals around the play area (a small accent of colour in the ink).
            Petals();
        }

        /// <summary>Per frame: birds drift across and flap, mist bands scroll.</summary>
        private void InkUpdate(float time, float dt)
        {
            if (!_look.InkWash) return;
            for (int i = 0; i < _birds.Count; i++)
            {
                var t = _birds[i];
                var p = t.localPosition;
                p.x += _birdSpeed[i] * dt;
                if (p.x > 120f) p.x -= 240f;
                t.localPosition = p;
            }
            bool up = Mathf.Repeat(time * 3.2f, 1f) < 0.5f;
            for (int i = 0; i < _birdRenderers.Length; i++)
            {
                bool flap = up ^ (i % 3 == 0);
                var m = flap ? _birdMatA : _birdMatB;
                if (_birdRenderers[i].sharedMaterial != m) _birdRenderers[i].sharedMaterial = m;
            }
            for (int i = 0; i < _mistMats.Count; i++)
                _mistMats[i].mainTextureOffset = new Vector2(time * (0.004f + i * 0.002f), 0f);
        }

        private void Petals()
        {
            var go = new GameObject("InkPetals");
            go.transform.SetParent(_follow, false);
            go.transform.localPosition = new Vector3(0f, FloorTop + 14f, 4f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = 0.4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.08f, 0.12f, 0.9f), new Color(0.95f, 0.3f, 0.35f, 0.9f));
            main.gravityModifier = 0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.prewarm = true;
            var em = ps.emission; em.rateOverTime = 14f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(46f, 1f, 12f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.35f, 0.9f); vel.y = new ParticleSystem.MinMaxCurve(-0.9f, -0.55f); vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.35f; noise.scrollSpeed = 0.2f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = Own(Proto3DMaterials.FxMat("inkPetal", PetalTexture(), Color.white, 1, 0.3f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.layer = FloorReflection3D.HiddenLayer;
            ps.Play();
        }

        // ------------------------------------------------------------------------------------------------
        // Painting
        // ------------------------------------------------------------------------------------------------

        private Texture2D PetalTexture()
        {
            const int N = 8;
            var t = Own(new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "petal" });
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                bool inside = dx * dx * 1.8f + dy * dy < 0.8f && !(dy > 0.55f && Mathf.Abs(dx) < 0.18f);   // petal with a notch
                t.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
            t.Apply(false);
            return t;
        }

        private static Texture2D BirdTexture(bool wingsDown)
        {
            const int W = 32, H = 16;
            var a = new float[W * H];
            var p = new Vector2(16f, 7f);
            if (!wingsDown)
            {
                Stroke(a, W, H, new Vector2(3f, 12f), new Vector2(10f, 9f), 0.6f, 0.9f, 1f);
                Stroke(a, W, H, new Vector2(10f, 9f), p, 0.9f, 1.1f, 1f);
                Stroke(a, W, H, p, new Vector2(22f, 9f), 1.1f, 0.9f, 1f);
                Stroke(a, W, H, new Vector2(22f, 9f), new Vector2(29f, 12f), 0.9f, 0.6f, 1f);
            }
            else
            {
                Stroke(a, W, H, new Vector2(4f, 4f), new Vector2(10f, 6f), 0.6f, 0.9f, 1f);
                Stroke(a, W, H, new Vector2(10f, 6f), p, 0.9f, 1.1f, 1f);
                Stroke(a, W, H, p, new Vector2(22f, 6f), 1.1f, 0.9f, 1f);
                Stroke(a, W, H, new Vector2(22f, 6f), new Vector2(28f, 4f), 0.9f, 0.6f, 1f);
            }
            Dab(a, W, H, 16.5f, 7f, 1.6f, 1.1f, 1f);
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "inkBird" };
            var px = new Color32[W * H];
            var ink = (Color32)Color.Lerp(InkBlack, InkPaper, 0.25f);
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(ink.r, ink.g, ink.b, (byte)(Mathf.Clamp01(a[i]) * 255f));
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }

        private static Texture2D MistBandTexture()
        {
            const int W = 256, H = 64;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, name = "inkMistBand" };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float n = Noise(u, v * 3f, 12, 77) * 0.6f + Noise(u, v * 6f, 32, 78) * 0.4f;
                float band = Mathf.Sin(v * Mathf.PI);                                     // soft top and bottom
                float a = InkSmooth(0.35f, 0.75f, n) * band * band;
                px[y * W + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        /// <summary>The near temple strip: a rocky shore with a pagoda, torii gates, stone lanterns and pines.</summary>
        private static Texture2D TempleTexture(int seed)
        {
            const int W = RangeW, H = RangeH;
            var a = new float[W * H];
            var ground = new float[W];
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                ground[x] = 0.16f + 0.05f * Mathf.Sin(u * 6.283f * 3f + 1f) + 0.03f * Mathf.Sin(u * 6.283f * 11f) + 0.02f * Noise(u, 0.3f, 64, seed);
            }
            // Rock outcrop under the pagoda (a rounded crag).
            int crag = W / 4;
            for (int x = 0; x < W; x++)
            {
                float d = Mathf.Abs(x - crag) / 70f;
                if (d < 1f) ground[x] = Mathf.Max(ground[x], 0.16f + 0.2f * Mathf.Sqrt(1f - d * d));
            }
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                for (int y = 0; y < H; y++)
                {
                    float v = (y + 0.5f) / H;
                    if (v > ground[x]) continue;
                    float depth = ground[x] - v;
                    float wash = Mathf.Lerp(0.95f, 0.25f, InkSmooth(0f, 0.12f, depth));
                    wash *= 0.65f + 0.45f * Noise(u, v * 9f, 200, seed + 1);
                    if (depth < 0.006f) wash = 0.97f;
                    wash *= InkSmooth(0f, 0.06f, v);
                    a[y * W + x] = Mathf.Max(a[y * W + x], wash);
                }
            }
            float G(int x) => ground[((x % W) + W) % W] * H;
            // Five-storey pagoda on the crag.
            Pagoda(a, W, H, crag, G(crag) - 1f, 5, 2.4f);
            // Torii gates and stone lanterns along the shore.
            Torii(a, W, H, W / 2 + 40, G(W / 2 + 40), 1.6f);
            Torii(a, W, H, (W * 7) / 8, G((W * 7) / 8), 1.2f);
            for (int i = 0; i < 6; i++)
            {
                int x = (W / 2 - 60 + i * 120) % W;
                Lantern(a, W, H, x, G(x), 1.1f);
            }
            // Pine clusters.
            var rnd = new System.Random(seed);
            for (int i = 0; i < 9; i++)
            {
                int x = rnd.Next(W);
                if (Mathf.Abs(x - crag) < 40) continue;
                Pine(a, W, H, x, G(x), 0.7f + (float)rnd.NextDouble() * 0.6f, rnd);
            }
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "inkTemples" };
            var px = new Color32[W * H];
            var c = (Color32)Color.Lerp(InkBlack, InkPaper, 0.1f);
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(c.r, c.g, c.b, (byte)(Mathf.Clamp01(a[i]) * 255f));
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }

        private static void Pagoda(float[] a, int W, int H, float x, float baseY, int tiers, float s)
        {
            float y = baseY;
            for (int t = 0; t < tiers; t++)
            {
                float w = (22f - t * 3.2f) * s, bodyH = 6f * s * (t == 0 ? 1.3f : 1f), roofH = 3f * s;
                for (float yy = y; yy < y + bodyH; yy += 0.5f) Stroke(a, W, H, new Vector2(x - w * 0.3f, yy), new Vector2(x + w * 0.3f, yy), 0.6f, 0.6f, 0.95f);
                // windows: paper-light slits in the body
                for (int k = -1; k <= 1; k++) Dab(a, W, H, x + k * w * 0.16f, y + bodyH * 0.55f, 0.8f * s, bodyH * 0.22f, 0f, true);
                y += bodyH;
                for (float yy = y; yy < y + roofH; yy += 0.5f)
                {
                    float k = (yy - y) / roofH;
                    float half = w * (0.66f - 0.3f * k);
                    Stroke(a, W, H, new Vector2(x - half, yy), new Vector2(x + half, yy), 0.7f, 0.7f, 1f);
                }
                Stroke(a, W, H, new Vector2(x - w * 0.66f, y + 0.4f), new Vector2(x - w * 0.82f, y + 3.2f * s), 1f, 0.4f, 1f);
                Stroke(a, W, H, new Vector2(x + w * 0.66f, y + 0.4f), new Vector2(x + w * 0.82f, y + 3.2f * s), 1f, 0.4f, 1f);
                y += roofH;
            }
            Stroke(a, W, H, new Vector2(x, y), new Vector2(x, y + 14f * s), 1f, 0.4f, 1f);   // sorin spire
            for (int r = 0; r < 5; r++) Dab(a, W, H, x, y + 3f * s + r * 2f * s, 1.6f * s, 0.5f * s, 1f);
        }

        private static void Torii(float[] a, int W, int H, float x, float baseY, float s)
        {
            float h = 26f * s, w = 30f * s;
            Stroke(a, W, H, new Vector2(x - w * 0.32f, baseY), new Vector2(x - w * 0.3f, baseY + h), 1.5f * s, 1.3f * s, 1f);
            Stroke(a, W, H, new Vector2(x + w * 0.32f, baseY), new Vector2(x + w * 0.3f, baseY + h), 1.5f * s, 1.3f * s, 1f);
            Stroke(a, W, H, new Vector2(x - w * 0.42f, baseY + h * 0.78f), new Vector2(x + w * 0.42f, baseY + h * 0.78f), 1f * s, 1f * s, 1f);   // nuki
            // kasagi: curved top beam with flicked ends
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f - 0.5f;
                float yy = baseY + h + 1.5f * s + t * t * 7f * s;
                Dab(a, W, H, x + t * w * 1.1f, yy, 2.4f * s, 1.5f * s, 1f);
            }
        }

        private static void Lantern(float[] a, int W, int H, float x, float baseY, float s)
        {
            Stroke(a, W, H, new Vector2(x - 3f * s, baseY), new Vector2(x + 3f * s, baseY), 1f, 1f, 1f);
            Stroke(a, W, H, new Vector2(x, baseY), new Vector2(x, baseY + 7f * s), 1f * s, 1f * s, 1f);
            Dab(a, W, H, x, baseY + 9f * s, 2.6f * s, 2f * s, 1f);
            Dab(a, W, H, x, baseY + 9f * s, 1f * s, 0.9f * s, 0f, true);                     // light box
            Stroke(a, W, H, new Vector2(x - 4.2f * s, baseY + 11.5f * s), new Vector2(x + 4.2f * s, baseY + 11.5f * s), 1.1f * s, 1.1f * s, 1f);
            Dab(a, W, H, x, baseY + 13.2f * s, 0.9f * s, 0.9f * s, 1f);
        }

        private static void Pine(float[] a, int W, int H, float x, float baseY, float s, System.Random rnd)
        {
            float hgt = 30f * s, lean = ((float)rnd.NextDouble() - 0.5f) * 0.9f;
            var p = new Vector2(x, baseY);
            var top = p + new Vector2(lean * hgt * 0.5f, hgt);
            Stroke(a, W, H, p, top, 1.6f * s, 0.6f * s, 1f);
            for (int k = 0; k < 4; k++)
            {
                var on = Vector2.Lerp(p, top, 0.45f + k * 0.17f);
                float dir = k % 2 == 0 ? 1f : -1f;
                var end = on + new Vector2(dir * (7f + (float)rnd.NextDouble() * 6f) * s, 1.5f * s);
                Stroke(a, W, H, on, end, 0.6f * s, 0.4f * s, 1f);
                Dab(a, W, H, end.x, end.y + 0.8f * s, (6f + (float)rnd.NextDouble() * 3f) * s, 1.8f * s, 0.95f);
            }
            Dab(a, W, H, top.x, top.y, 4.5f * s, 1.8f * s, 0.95f);
        }

        /// <summary>Dab that erases ink (paper showing through) when <paramref name="erase"/> is set.</summary>
        private static void Dab(float[] a, int W, int H, float cx, float cy, float rx, float ry, float val, bool erase)
        {
            if (!erase) { Dab(a, W, H, cx, cy, rx, ry, val); return; }
            int x0 = Mathf.FloorToInt(cx - rx - 1), x1 = Mathf.CeilToInt(cx + rx + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry - 1)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + ry + 1));
            for (int y = y0; y <= y1; y++)
            for (int xx = x0; xx <= x1; xx++)
            {
                float dx = (xx - cx) / Mathf.Max(0.5f, rx), dy = (y - cy) / Mathf.Max(0.5f, ry);
                if (dx * dx + dy * dy > 1f) continue;
                int x = ((xx % W) + W) % W;
                a[y * W + x] = Mathf.Min(a[y * W + x], 0.25f);
            }
        }
    }
}
