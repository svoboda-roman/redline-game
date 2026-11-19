using System.Collections.Generic;
using Redline.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Surfaces and grounded structure of <see cref="Arena3DRenderer"/> (visual only; maps and collision untouched):
    /// <list type="bullet">
    /// <item>per-level pixel textures at 48 texels/unit (<see cref="SurfacePatterns"/>): wet floor with puddles,
    /// steel slab tops, a level fascia (Loop01 riveted steel, Loop02 concrete, Loop03 brick) and a grime detail
    /// map for structure and the dressing kit;</item>
    /// <item>support structure under every floating platform: joists under the slab, columns down to whatever is
    /// below (floor or a lower platform), tie beams and X-bracing between columns, all behind the play plane
    /// (z ≥ 0.45) so nothing covers a character;</item>
    /// <item>ground clutter behind the play plane: crate stacks, pallets, drums and rubble at column feet and along
    /// the floor.</item>
    /// </list>
    /// </summary>
    public sealed partial class Arena3DRenderer
    {
        /// <summary>World units covered by one tile of the grime detail texture.</summary>
        private const float GrimeUnits = 2f;

        private struct LevelBox { public int X0, X1, Y0, Y1; public float Depth; }

        private readonly List<LevelBox> _boxes = new List<LevelBox>();
        private float[,] _tileDepth;
        private Material _fasciaMat, _slabTopMat, _structMat, _clutterMat;
        private Mesh _structMesh;
        private float _streetUnits = 8f;
        private Texture2D _streetGloss;

        /// <summary>Triangles in the support structure + clutter mesh, one ring copy.</summary>
        public long StructureTriangles { get; private set; }

        /// <summary>Surface textures created (albedo/normal/metal-gloss maps, puddle mask).</summary>
        public int SurfaceTextureCount { get; private set; }

        /// <summary>GPU bytes of the surface textures, mips included.</summary>
        public long SurfaceTextureBytes { get; private set; }

        /// <summary>
        /// Builds the surface materials: <c>_deckMat</c> (floor top), <c>_wallMat</c> (floor sides, parapet),
        /// <c>_fasciaMat</c> / <c>_slabTopMat</c> (platforms), <c>_structMat</c>, <c>_clutterMat</c>, the
        /// palette's grime detail and the puddle mask on <c>_puddleMat</c>. Colours are divided by each texture's
        /// mean so the level's overall values (tuned with the grade) stay where they were.
        /// </summary>
        private void SurfaceMaterials()
        {
            SurfacePatterns.FloorKind floor;
            SurfacePatterns.FasciaKind fascia;
            switch (_look.Name)
            {
                case "Loop02": floor = SurfacePatterns.FloorKind.Asphalt; fascia = SurfacePatterns.FasciaKind.Concrete; break;
                case "Loop03": floor = SurfacePatterns.FloorKind.Setts; fascia = SurfacePatterns.FasciaKind.Brick; break;
                default: floor = SurfacePatterns.FloorKind.ConcreteDeck; fascia = SurfacePatterns.FasciaKind.RivetedSteel; break;
            }
            int seed = _look.Seed * 7 + 1;
            var street = Surface(SurfaceTextures3D.Build("street", SurfacePatterns.Street(floor, seed, _look.PuddleThreshold), true, true));
            _streetGloss = street.MetalGloss;
            var top = Surface(SurfaceTextures3D.Build("slabTop", SurfacePatterns.SlabTop(seed + 1), true, true));
            var front = Surface(SurfaceTextures3D.Build("fascia", SurfacePatterns.Fascia(fascia, seed + 2), true, true));
            var grime = Surface(SurfaceTextures3D.Build("grime", SurfacePatterns.Grime(seed + 3), false, false));
            _streetUnits = street.Units.x;

            // Old panel albedo averaged ≈ 0.2 × colour × 2.4 (deck) / × 3 (wall); keep those values, a little lifted
            // on platform fronts so they read as built surfaces rather than black slabs.
            _deckMat = Own(Proto3DMaterials.Lit3D("deck", Keep(_look.DeckColor * 0.48f, street.MeanLum), 1f, 1f,
                street.Albedo, street.Normal, 1f, street.MetalGloss));
            _slabTopMat = Own(Proto3DMaterials.Lit3D("slabTop", Keep(_look.DeckColor * 0.5f, top.MeanLum), 1f, 1f,
                top.Albedo, top.Normal, 1f, top.MetalGloss));
            // Fascia tile is 4 × 1 units; the boxes' UV = position / 4, so V is tiled ×4 (one course per unit).
            _fasciaMat = Own(Proto3DMaterials.Lit3D("fascia", Keep(_look.WallColor * 0.8f, front.MeanLum), 1f, 1f,
                front.Albedo, front.Normal, 1f, front.MetalGloss, tiling: new Vector2(4f / front.Units.x, 4f / front.Units.y)));
            _wallMat = Own(Proto3DMaterials.Lit3D("wall", Keep(_look.WallColor * 0.36f, grime.MeanLum), 0.35f, 0.3f,
                grime.Albedo, tiling: new Vector2(4f / GrimeUnits, 4f / GrimeUnits)));
            _structMat = Own(Proto3DMaterials.Lit3D("structure", Keep(_look.WallColor * 0.5f, grime.MeanLum), 0.4f, 0.6f, grime.Albedo));
            _clutterMat = Own(Proto3DMaterials.Lit3D("clutter", Keep(_look.DeckColor * 0.4f, grime.MeanLum), 0.3f, 0f, grime.Albedo));
            _palette.Detail = grime.Albedo;
            _palette.DetailMean = grime.MeanLum;

            var mask = Own(SurfaceTextures3D.PuddleMask(seed + 4));
            SurfaceTextureCount++;
            SurfaceTextureBytes += SurfaceTextures3D.SizeOf(mask);
            _puddleMat.SetTexture("_BaseMap", mask);
            _puddleMat.SetFloat("_Cutoff", 0.5f);
            Debug.Log($"[Surfaces] {_look.Name}: floor {floor}, fascia {fascia}, {SurfaceTextureCount} textures, {SurfaceTextureBytes / 1024} KB");
        }

        private SurfaceTextures3D.Set Surface(SurfaceTextures3D.Set s)
        {
            foreach (var t in new[] { s.Albedo, s.Normal, s.MetalGloss })
                if (t != null) { Own(t); SurfaceTextureCount++; }
            SurfaceTextureBytes += s.Bytes;
            return s;
        }

        private static Color Keep(Color c, float mean)
        {
            var r = c / Mathf.Max(0.3f, mean);
            r.a = 1f;
            return r;
        }

        /// <summary>Remembers a level box and its depth per tile (called while the level mesh is built).</summary>
        private void RecordBox(int x0, int x1, int y0, int y1, float depth)
        {
            if (_tileDepth == null) _tileDepth = new float[_map.Width, _map.Height];
            _boxes.Add(new LevelBox { X0 = x0, X1 = x1, Y0 = y0, Y1 = y1, Depth = depth });
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) _tileDepth[x, y] = depth;
        }

        private void BuildStructure(Transform copy)
        {
            if (_structMesh == null) BuildStructureMesh();
            if (_structMesh.vertexCount > 0) MeshGo("structure", copy, _structMesh, _structMat, _clutterMat);
        }

        private void BuildStructureMesh()
        {
            var b = new BoxBuilder(1f / GrimeUnits);
            var rnd = new System.Random(_look.Seed * 13 + 5);
            float R() => (float)rnd.NextDouble();
            var cols = new List<Vector3>(); // x, land y, z

            foreach (var box in _boxes)
            {
                if (box.Y0 < 2) continue;
                float y0 = box.Y0, width = box.X1 - box.X0;
                bool floating = false;
                for (int x = box.X0; x < box.X1; x++) if (!_map.IsSolid(x, box.Y0 - 1)) { floating = true; break; }
                if (!floating) continue;

                // Joists under the slab, behind the play plane (z ≥ 0.45 so a jumping character is never covered).
                for (float x = box.X0 + 0.5f; x < box.X1; x += 1f)
                    b.Box(0, new Vector3(x - 0.05f, y0 - 0.12f, 0.45f), new Vector3(x + 0.05f, y0, box.Depth - 0.3f));
                // Back edge beam.
                b.Box(0, new Vector3(box.X0 + 0.1f, y0 - 0.22f, box.Depth - 0.5f), new Vector3(box.X1 - 0.1f, y0, box.Depth - 0.25f));

                // Columns: at both ends, and every ≈ 4 units between.
                var xs = new List<float>();
                if (width < 2f) xs.Add(box.X0 + width * 0.5f);
                else
                {
                    // Wider spacing per level (Loop02/03 ≈ 8.5): fewer columns crowding the frame under high slabs.
                    int inner = Mathf.Max(0, Mathf.FloorToInt((width - 0.8f) / Mathf.Max(2f, _look.ColumnSpacing)));
                    for (int i = 0; i <= inner + 1; i++) xs.Add(Mathf.Lerp(box.X0 + 0.4f, box.X1 - 0.4f, i / (float)(inner + 1)));
                }
                cols.Clear();
                foreach (float cx in xs)
                {
                    int ix = Mathf.Clamp(Mathf.FloorToInt(cx), 0, _map.Width - 1);
                    if (_map.IsSolid(ix, box.Y0 - 1)) continue;
                    float land = FloorTop, lower = _look.DeckFar;
                    for (int y = box.Y0 - 1; y >= 1; y--)
                        if (_map.IsSolid(ix, y)) { land = y + 1; lower = _tileDepth != null ? _tileDepth[ix, y] : _look.DeckFar; break; }
                    if (land >= y0 - 0.3f) continue;
                    float z = Mathf.Max(3f, Mathf.Min(box.Depth, lower) - 0.8f);
                    // H-column: two flanges + web, base and cap plates. Columns nearer the camera are slimmer (the
                    // nearest ones read widest on screen); flanges never under 0.1 (≥ 4 texels).
                    float k = Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(3f, 6.5f, z));
                    float fw = 0.11f * k, ww = Mathf.Max(0.03f, 0.04f * k), pw = 0.24f * k;
                    b.Box(0, new Vector3(cx - fw, land, z - 0.15f), new Vector3(cx + fw, y0 - 0.12f, z - 0.09f));
                    b.Box(0, new Vector3(cx - fw, land, z + 0.09f), new Vector3(cx + fw, y0 - 0.12f, z + 0.15f));
                    b.Box(0, new Vector3(cx - ww, land, z - 0.09f), new Vector3(cx + ww, y0 - 0.12f, z + 0.09f));
                    b.Box(0, new Vector3(cx - pw, land, z - 0.26f), new Vector3(cx + pw, land + 0.06f, z + 0.26f));
                    b.Box(0, new Vector3(cx - pw * 0.85f, y0 - 0.22f, z - 0.22f), new Vector3(cx + pw * 0.85f, y0 - 0.12f, z + 0.22f));
                    cols.Add(new Vector3(cx, land, z));
                    if (land <= FloorTop + 0.01f && R() < 0.7f) FootClutter(b, rnd, cx + (R() - 0.5f) * 1.2f, z - 0.75f);
                }

                // Ties and X-bracing between neighbouring columns on the same footing.
                for (int i = 0; !_look.NeonStreet && i + 1 < cols.Count; i++)   // no bracing lattice on the neon street
                {
                    var a = cols[i]; var c = cols[i + 1];
                    if (Mathf.Abs(a.y - c.y) > 0.01f || Mathf.Abs(a.z - c.z) > 0.01f || c.x - a.x < 1.2f) continue;
                    float zb = a.z + 0.2f;
                    float yTop = y0 - 0.35f, step = Mathf.Clamp(c.x - a.x, 2.2f, 3.5f);
                    float prev = yTop;
                    b.Beam(0, new Vector3(a.x, yTop, zb), new Vector3(c.x, yTop, zb), 0.12f, 0.08f);
                    for (float y = yTop - step; y > a.y + 0.6f; y -= step)
                    {
                        b.Beam(0, new Vector3(a.x, y, zb), new Vector3(c.x, y, zb), 0.12f, 0.08f);
                        b.Beam(0, new Vector3(a.x, y, zb), new Vector3(c.x, prev, zb), 0.08f, 0.06f);
                        b.Beam(0, new Vector3(a.x, prev, zb), new Vector3(c.x, y, zb), 0.08f, 0.06f);
                        prev = y;
                    }
                    if (prev - a.y > 1.2f) // last bay down to the footing
                    {
                        b.Beam(0, new Vector3(a.x, a.y + 0.3f, zb), new Vector3(c.x, prev, zb), 0.08f, 0.06f);
                        b.Beam(0, new Vector3(a.x, prev, zb), new Vector3(c.x, a.y + 0.3f, zb), 0.08f, 0.06f);
                    }
                }
            }

            // Floor clutter behind the play plane (z 1.6..3.2), where nothing solid stands.
            for (float x = 1f + R() * 2f; x < _map.Width - 1f; x += 3.5f + R() * 4f)
            {
                int ix = Mathf.FloorToInt(x);
                if (!HasDeck(ix) || _map.IsSolid(ix, 1) || _map.Get(ix, 1) == Tile.Spike) continue;
                FootClutter(b, rnd, x, 1.6f + R() * 1.6f);
            }

            _structMesh = Own(b.Build("structure"));
            StructureTriangles = b.Triangles;
            Debug.Log($"[Structure] {_look.Name}: {_boxes.Count} boxes, {StructureTriangles} tris/copy");
        }

        /// <summary>One clutter group on the floor at (x, z): crate stack, pallet, drum pair or rubble.</summary>
        private static void FootClutter(BoxBuilder b, System.Random rnd, float x, float z)
        {
            float R() => (float)rnd.NextDouble();
            float y = FloorTop;
            double pick = rnd.NextDouble();
            if (pick < 0.3)
            {
                float s = 0.45f + R() * 0.25f;
                b.Oriented(1, new Vector3(x, y + s * 0.5f, z), Quaternion.Euler(0f, R() * 30f - 15f, 0f), Vector3.one * s);
                if (R() < 0.6f) b.Oriented(1, new Vector3(x + s * 0.9f, y + s * 0.4f, z + 0.1f), Quaternion.Euler(0f, R() * 40f, 0f), Vector3.one * s * 0.8f);
                if (R() < 0.4f) b.Oriented(1, new Vector3(x + 0.05f, y + s * 1.35f, z), Quaternion.Euler(0f, R() * 40f - 20f, 0f), Vector3.one * s * 0.75f);
            }
            else if (pick < 0.45)
            {
                // Pallet: deck boards on three runners.
                var rot = Quaternion.Euler(0f, R() * 50f - 25f, 0f);
                for (int i = -1; i <= 1; i++) b.Oriented(1, new Vector3(x, y + 0.04f, z) + rot * new Vector3(0f, 0f, i * 0.38f), rot, new Vector3(1f, 0.08f, 0.1f));
                for (int i = -2; i <= 2; i++) b.Oriented(1, new Vector3(x, y + 0.11f, z) + rot * new Vector3(i * 0.22f, 0f, 0f), rot, new Vector3(0.16f, 0.06f, 0.9f));
            }
            else if (pick < 0.65)
            {
                b.Drum(0, new Vector3(x, y, z), 0.27f, 0.85f);
                if (R() < 0.5f) b.Drum(0, new Vector3(x + 0.6f, y, z + 0.15f), 0.27f, 0.85f);
            }
            else
            {
                // Rubble: broken slab chunks and rebar.
                int n = 3 + rnd.Next(4);
                for (int i = 0; i < n; i++)
                {
                    float s = 0.12f + R() * 0.3f;
                    b.Oriented(1, new Vector3(x + (R() - 0.5f) * 1.2f, y + s * 0.3f, z + (R() - 0.5f) * 0.6f),
                        Quaternion.Euler(R() * 30f - 15f, R() * 360f, R() * 30f - 15f), new Vector3(s * 1.6f, s * 0.7f, s));
                }
                if (R() < 0.5f) b.Beam(0, new Vector3(x - 0.4f, y + 0.05f, z), new Vector3(x + 0.3f, y + 0.5f, z + 0.1f), 0.05f, 0.05f);
            }
        }

        /// <summary>Two-submesh builder of boxes, oriented beams and octagonal drums with box-projected world UVs.</summary>
        private sealed class BoxBuilder
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<Vector2> _u = new List<Vector2>();
            private readonly List<int>[] _t;
            private readonly float _uvScale;

            /// <summary>Builder with <paramref name="subs"/> sub-meshes and world UV = position × <paramref name="uvScale"/>.</summary>
            public BoxBuilder(float uvScale, int subs = 2)
            {
                _uvScale = uvScale;
                _t = new List<int>[subs];
                for (int i = 0; i < subs; i++) _t[i] = new List<int>();
            }

            public long Triangles
            {
                get { long n = 0; foreach (var t in _t) n += t.Count; return n / 3; }
            }

            /// <summary>Flat-shaded tetrahedron of radius <paramref name="r"/> (a leaf clump: 4 tris).</summary>
            public void Tetra(int sub, Vector3 c, Quaternion rot, float r)
            {
                var a = c + rot * new Vector3(0f, r, 0f);
                var b = c + rot * new Vector3(0.943f * r, -0.333f * r, 0f);
                var d = c + rot * new Vector3(-0.471f * r, -0.333f * r, 0.816f * r);
                var e = c + rot * new Vector3(-0.471f * r, -0.333f * r, -0.816f * r);
                TriOut(a, b, d, c, sub); TriOut(a, d, e, c, sub); TriOut(a, e, b, c, sub); TriOut(b, e, d, c, sub);
            }

            private void TriOut(Vector3 a, Vector3 b, Vector3 d, Vector3 centre, int sub)
            {
                var n = Vector3.Cross(b - a, d - a).normalized;
                if (Vector3.Dot(n, (a + b + d) / 3f - centre) < 0f) n = -n;
                Tri(a, b, d, n, sub);
            }

            /// <summary>Octagonal drum of radius <paramref name="r"/> and length <paramref name="h"/> along rot·up from <paramref name="baseC"/>.</summary>
            public void DrumR(int sub, Vector3 baseC, Quaternion rot, float r, float h)
            {
                const int N = 8;
                var top = new Vector3[N]; var bot = new Vector3[N];
                var up = rot * Vector3.up;
                for (int i = 0; i < N; i++)
                {
                    float a0 = (i + 0.5f) / N * Mathf.PI * 2f, a1 = (i + 1.5f) / N * Mathf.PI * 2f;
                    var p0 = baseC + rot * new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
                    var p1 = baseC + rot * new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                    var n = ((p0 + p1) * 0.5f - baseC).normalized;
                    Quad(p0, p0 + up * h, p1 + up * h, p1, n, sub);
                    top[i] = p0 + up * h; bot[i] = p0;
                }
                for (int i = 1; i < N - 1; i++) { Tri(top[0], top[i], top[i + 1], up, sub); Tri(bot[0], bot[i + 1], bot[i], -up, sub); }
            }

            public void Box(int sub, Vector3 min, Vector3 max) =>
                Oriented(sub, (min + max) * 0.5f, Quaternion.identity, max - min);

            /// <summary>Square-section beam from <paramref name="a"/> to <paramref name="b"/>: w across in the XY plane, d in depth.</summary>
            public void Beam(int sub, Vector3 a, Vector3 b, float w, float d)
            {
                var ax = b - a;
                float len = ax.magnitude;
                if (len < 1e-4f) return;
                var x = ax / len;
                var y = Mathf.Abs(x.z) > 0.9f ? Vector3.up : Vector3.Cross(Vector3.forward, x).normalized;
                var z = Vector3.Cross(x, y).normalized;
                Core((a + b) * 0.5f, x, y, z, new Vector3(len, w, d) * 0.5f, sub);
            }

            public void Oriented(int sub, Vector3 c, Quaternion r, Vector3 size) =>
                Core(c, r * Vector3.right, r * Vector3.up, r * Vector3.forward, size * 0.5f, sub);

            /// <summary>Octagonal drum standing on <paramref name="baseC"/>.</summary>
            public void Drum(int sub, Vector3 baseC, float r, float h)
            {
                const int N = 8;
                var top = new Vector3[N];
                for (int i = 0; i < N; i++)
                {
                    float a0 = (i + 0.5f) / N * Mathf.PI * 2f, a1 = (i + 1.5f) / N * Mathf.PI * 2f;
                    var p0 = baseC + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
                    var p1 = baseC + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                    var n = ((p0 + p1) * 0.5f - baseC).normalized;
                    Quad(p0, p0 + Vector3.up * h, p1 + Vector3.up * h, p1, n, sub);
                    top[i] = p0 + Vector3.up * h;
                }
                for (int i = 1; i < N - 1; i++) Tri(top[0], top[i], top[i + 1], Vector3.up, sub);
                // Rim band (2+ texels) a little proud of the side.
                Core(baseC + Vector3.up * (h * 0.5f), Vector3.right, Vector3.up, Vector3.forward, new Vector3(r * 0.92f, 0.03f, r * 0.92f), sub);
            }

            private void Core(Vector3 c, Vector3 x, Vector3 y, Vector3 z, Vector3 h, int sub)
            {
                var ax = new[] { x * h.x, y * h.y, z * h.z };
                for (int i = 0; i < 3; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    var n = ax[i].normalized * s;
                    var u = ax[(i + 1) % 3];
                    var v = ax[(i + 2) % 3];
                    var fc = c + ax[i] * s;
                    Quad(fc - u - v, fc + u - v, fc + u + v, fc - u + v, n, sub);
                }
            }

            /// <summary>Quad with normal <paramref name="n"/>; winding fixed so the face is front-facing along n.</summary>
            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, int sub)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { var t = b; b = d; d = t; }
                int i = _v.Count;
                Add(a, n); Add(b, n); Add(c, n); Add(d, n);
                _t[sub].Add(i); _t[sub].Add(i + 1); _t[sub].Add(i + 2);
                _t[sub].Add(i); _t[sub].Add(i + 2); _t[sub].Add(i + 3);
            }

            private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int sub)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { var t = b; b = c; c = t; }
                int i = _v.Count;
                Add(a, n); Add(b, n); Add(c, n);
                _t[sub].Add(i); _t[sub].Add(i + 1); _t[sub].Add(i + 2);
            }

            private void Add(Vector3 p, Vector3 n)
            {
                _v.Add(p); _n.Add(n);
                var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                var q = p * _uvScale;
                _u.Add(a.y >= a.x && a.y >= a.z ? new Vector2(q.x, q.z) : a.x >= a.z ? new Vector2(q.z, q.y) : new Vector2(q.x, q.y));
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _u);
                m.subMeshCount = _t.Length;
                for (int i = 0; i < _t.Length; i++) m.SetTriangles(_t[i], i);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
