using System.Collections.Generic;
using Redline.Core;
using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Organic and ruined silhouettes for <see cref="Arena3DRenderer"/> (Replaced's decay layer), built procedurally
    /// into one four-sub-mesh mesh per level (masonry, foliage, ash, dark metal) and instanced into every ring copy:
    /// <list type="bullet">
    /// <item>Loop01 (industrial ember): rubble mounds, broken concrete walls, toppled slabs with rebar, cables slung
    /// and dangling under the slabs and across the mid-ground, a few dry creepers, one dead tree;</item>
    /// <item>Loop02 (teal overpass): ruined brick facades with arches and empty windows, heavy overgrowth (creeper
    /// cover, vine curtains under every slab and along the overpass edge, bushes); flood water is in
    /// Arena3DReflections.cs;</item>
    /// <item>Loop03 (oxblood gallery): broken classical columns and fallen drums, debris, a ruined arcade, dead
    /// trees, ash on every ledge.</item>
    /// </list>
    /// Everything sits behind the play plane (z ≥ 0.6) and never inside a platform, so no character is hidden.
    /// </summary>
    /// <remarks>
    /// Leaf clumps are flat-shaded tetrahedra (4 tris, ≥ 0.08 radius = 4+ texels at the play plane), cables and
    /// branches are ≥ 0.07 thick (≥ 2 texels at z 12). Walls are 0.2-unit masonry columns merged into runs, so the
    /// broken tops and arch curves step in whole courses (no single-texel stair noise). Shadows off (mid-ground).
    /// </remarks>
    public sealed partial class Arena3DRenderer
    {
        private const int SubMasonry = 0, SubFoliage = 1, SubAsh = 2, SubMetal = 3;

        private Mesh _ruinMesh;
        private Material[] _ruinMats;

        /// <summary>Triangles in the ruins/overgrowth mesh, one ring copy.</summary>
        public long RuinTriangles { get; private set; }

        private void BuildRuins(Transform copy)
        {
            if (_look.NeonStreet) return;   // no ruins or overgrowth on the neon street
            if (_ruinMats == null) BuildRuinMesh();
            if (_ruinMesh != null && _ruinMesh.vertexCount > 0) NoShadow(MeshGo("ruins", copy, _ruinMesh, _ruinMats));
        }

        private void BuildRuinMesh()
        {
            bool overpass = _look.Name == "Loop02", gallery = _look.Name == "Loop03";
            var kind = overpass || gallery ? SurfacePatterns.FasciaKind.Brick : SurfacePatterns.FasciaKind.Concrete;
            var mas = Surface(SurfaceTextures3D.Build("ruinMasonry", SurfacePatterns.Fascia(kind, _look.Seed * 7 + 9), true, false));
            var masonry = Own(Proto3DMaterials.Lit3D("ruinMasonry", Keep(_look.WallColor * (0.95f * _look.MidValue), mas.MeanLum), 0.15f, 0f,
                mas.Albedo, mas.Normal, 1f, null, tiling: new Vector2(1f / mas.Units.x, 1f / mas.Units.y)));
            var fol = _look.FoliageColor * Mathf.Lerp(1f, _look.MidValue, 0.5f); fol.a = 1f;
            var foliage = Own(Proto3DMaterials.Lit3D("foliage", fol, 0.25f, 0f));
            var ashC = _look.AshColor; ashC.a = 1f;
            var ash = Own(Proto3DMaterials.Lit3D("ash", ashC, 0.1f, 0f));
            var metal = Own(Proto3DMaterials.Lit3D("ruinMetal", new Color(0.045f, 0.042f, 0.045f), 0.4f, 0.5f));
            _ruinMats = new[] { masonry, foliage, ash, metal };

            var b = new BoxBuilder(1f, 4);
            var rnd = new System.Random(_look.Seed * 29 + 11);
            float W = _map.Width;
            float og = _look.Overgrowth;
            bool hasAsh = _look.AshColor.a > 0.01f;
            float R() => (float)rnd.NextDouble();
            float y0 = FloorTop;

            // --- Under every floating slab: vines and cables (z between the play plane and the slab's back).
            foreach (var box in _boxes)
            {
                if (box.Y0 < 2) continue;
                bool floating = false;
                for (int x = box.X0; x < box.X1; x++) if (!_map.IsSolid(x, box.Y0 - 1)) { floating = true; break; }
                if (!floating) continue;
                float zMax = box.Depth - 0.4f;
                int vines = Mathf.RoundToInt(og * (box.X1 - box.X0) / 1.6f * (0.6f + R() * 0.6f));
                for (int i = 0; i < vines; i++)
                {
                    float x = box.X0 + 0.2f + R() * (box.X1 - box.X0 - 0.4f);
                    float free = FreeBelow(x, box.Y0);
                    float len = Mathf.Min(Mathf.Lerp(0.8f, 3.6f, R() * R()) * (0.6f + og * 0.7f), free - 0.6f);
                    if (len > 0.4f) Vine(b, rnd, x, box.Y0 - 0.05f, 0.7f + R() * (zMax - 0.7f), len, og);
                }
                if (!overpass && !gallery && !_look.InkWash)
                {
                    // Industrial: a slung cable under the slab, sometimes a loose dangling end.
                    float zc = 0.8f + R() * (zMax - 0.8f);
                    float sag = 0.4f + R() * 0.9f;
                    if (box.X1 - box.X0 >= 3 && FreeBelowSpan(box.X0 + 0.5f, box.X1 - 0.5f, box.Y0) > sag + 0.6f)
                        Cable(b, new Vector3(box.X0 + 0.4f, box.Y0 - 0.12f, zc), new Vector3(box.X1 - 0.4f, box.Y0 - 0.12f, zc), sag, 0.07f);
                    if (R() < 0.6f)
                    {
                        float x = box.X0 + R() * (box.X1 - box.X0);
                        float len = Mathf.Min(0.9f + R() * 1.8f, FreeBelow(x, box.Y0) - 0.7f);
                        if (len > 0.4f) Dangle(b, rnd, new Vector3(x, box.Y0 - 0.1f, 0.7f + R() * (zMax - 0.7f)), len);
                    }
                }
            }

            // --- Ash on every ledge: ragged thin drifts on slab tops and the deck behind the play plane.
            if (hasAsh)
            {
                foreach (var box in _boxes)
                {
                    float top = box.Y1;
                    for (float x = box.X0; x < box.X1 - 0.05f;)
                    {
                        float w = Mathf.Min(0.3f + R() * 0.5f, box.X1 - x);
                        int ix = Mathf.Clamp(Mathf.FloorToInt(x + w * 0.5f), 0, _map.Width - 1);
                        if (box.Y1 >= _map.Height || !_map.IsSolid(ix, box.Y1))
                            b.Box(SubAsh, new Vector3(x, top, 0.5f + R() * 0.7f), new Vector3(x + w, top + 0.05f + R() * 0.04f, box.Depth - 0.15f));
                        x += w;
                    }
                }
                for (float x = R() * 3f; x < W; x += 1.5f + R() * 3f)
                {
                    float w = 0.6f + R() * 1.8f, z = 0.7f + R() * 4f;
                    if (Clear(x, x + w, y0, y0 + 0.3f, z)) b.Box(SubAsh, new Vector3(x, y0, z), new Vector3(x + w, y0 + 0.045f, z + 0.4f + R() * 1.2f));
                }
            }

            // --- Floor rubble mounds behind the play plane.
            float rubbleStep = gallery ? 7f : overpass ? 12f : 6.5f;
            for (float x = 2f + R() * 3f; x < W - 1f; x += rubbleStep + R() * rubbleStep)
            {
                float z = 1.2f + R() * 2.8f, size = 0.6f + R() * 0.8f;
                if (Clear(x - size * 1.5f, x + size * 1.5f, y0, y0 + 1.5f, z)) Rubble(b, rnd, x, z, size, hasAsh);
            }

            if (overpass) Loop02Ruins(b, rnd);
            else if (gallery) Loop03Ruins(b, rnd);
            else Loop01Ruins(b, rnd);

            RuinTriangles = b.Triangles;
            _ruinMesh = Own(b.Build("ruins"));
            Debug.Log($"[Ruins] {_look.Name}: {RuinTriangles} tris/copy, 4 sub-meshes/copy");
        }

        private void Loop01Ruins(BoxBuilder b, System.Random rnd)
        {
            float R() => (float)rnd.NextDouble();
            float W = _map.Width, y0 = FloorTop;
            // Broken concrete walls and toppled slabs in the near mid-ground.
            for (float x = 4f + R() * 6f; x < W - 4f; x += 17f + R() * 9f)
            {
                float len = 4f + R() * 4f, h = 2.6f + R() * 2.6f;
                // As near as the platforms allow (z 5.5..8.6), so the broken walls read in front of the ribs.
                float z = 5.5f + R() * 1.5f;
                if (!Clear(x, x + len, y0, y0 + h, z)) z = 8.6f + R() * 1.2f;
                Wall(b, rnd, x, x + len, y0, h, z, 0.35f, 0, 0f, 0f, false, 0.55f, _look.Overgrowth * 0.6f, false);
                Rubble(b, rnd, x + len + 0.8f, z - 0.6f, 1.4f + R() * 0.8f, false);
            }
            for (float x = 12f + R() * 6f; x < W - 4f; x += 21f + R() * 10f)
            {
                float z = 5.5f + R() * 2.5f;
                if (!Clear(x - 2f, x + 2f, y0, y0 + 3f, z - 1f)) continue;
                // Toppled floor slab leaning on its own rubble, rebar sticking out of the broken edge.
                var rot = Quaternion.Euler(0f, R() * 30f - 15f, 18f + R() * 22f);
                var c = new Vector3(x, y0 + 0.9f, z);
                b.Oriented(SubMasonry, c, rot, new Vector3(3.4f, 0.28f, 2f));
                for (int i = 0; i < 5; i++)
                {
                    var a = c + rot * new Vector3(1.7f, 0f, -0.8f + i * 0.4f);
                    b.Beam(SubMetal, a, a + rot * new Vector3(0.5f + R() * 0.6f, (R() - 0.5f) * 0.5f, 0f), 0.07f, 0.07f);
                }
                Rubble(b, rnd, x - 1.2f, z, 0.9f, false);
            }
            // Cables slung across the mid-ground between the ribs.
            for (float x = R() * 6f; !_look.InkWash && x < W; x += 9f + R() * 7f)
            {
                float span = 7f + R() * 6f, h = y0 + 5.5f + R() * 4f, z = 9.5f + R() * 2.5f;
                Cable(b, new Vector3(x, h, z), new Vector3(x + span, h + (R() - 0.5f) * 2f, z), 0.8f + R() * 1.4f, 0.09f);
            }
            // One dead tree per ~50 units, growing out of the rubble.
            for (float x = 20f + R() * 10f; x < W - 6f; x += 45f + R() * 15f)
            {
                float z = 6.5f + R() * 2f;
                if (Clear(x - 2f, x + 2f, y0, y0 + 5f, z)) DeadTree(b, rnd, new Vector3(x, y0, z), 4.5f + R() * 1.5f, false);
            }
        }

        private void Loop02Ruins(BoxBuilder b, System.Random rnd)
        {
            float R() => (float)rnd.NextDouble();
            float W = _map.Width, y0 = FloorTop, og = _look.Overgrowth;
            // Ruined brick facades with arches and empty windows, overgrown.
            for (float x = 3f + R() * 6f; x < W - 6f; x += 24f + R() * 10f)
            {
                float len = 9f + R() * 5f, z = 8.6f + R() * 1.2f;
                int arches = len > 12f ? 3 : 2;
                Wall(b, rnd, x, x + len, y0, 5.5f + R() * 3f, z, 0.45f, arches, 2.2f + R() * 0.8f, 1.6f + R() * 0.8f, true, 0.6f, og, false);
            }
            // Vine curtain along the overpass deck's front edge (the overpass_span kit piece: deck underside at
            // baseY + 7.8, front edge at z ≈ 9.1).
            for (float x = R(); x < W; x += 0.5f + R() * 0.9f)
            {
                if (R() < 0.3f) { x += 1.5f; continue; }
                Vine(b, rnd, x, y0 + 8.3f, 8.95f, Mathf.Lerp(0.8f, 4.2f, R() * R()), og);
            }
            // Bushes and creeper mounds on the deck behind the play plane.
            for (float x = 1f + R() * 3f; x < W - 1f; x += 3.5f + R() * 4f)
            {
                float z = 1f + R() * 3f, s = 0.4f + R() * 0.6f;
                if (Clear(x - s, x + s, y0, y0 + s, z)) Bush(b, rnd, x, z, s);
            }
            // Creeper cover climbing the near structure: low leafy patches against the slab backs are hidden, so
            // they go on the deck's far parapet instead (z ≈ DeckFar - 0.6), a continuous green sill.
            for (float x = R() * 2f; x < W; x += 1.2f + R() * 2f)
                LeafPatch(b, rnd, x, y0 + 0.5f, _look.DeckFar - 0.68f, 1.2f + R(), 0.9f, 6 + (int)(R() * 6));
        }

        private void Loop03Ruins(BoxBuilder b, System.Random rnd)
        {
            float R() => (float)rnd.NextDouble();
            float W = _map.Width, y0 = FloorTop;
            // Broken columns (mostly short stumps, so they never crowd the top of the frame) and fallen drums.
            for (float x = 3f + R() * 4f; x < W - 2f; x += 8f + R() * 6f)
            {
                float z = 3.6f + R() * 3.2f, r = 0.3f + R() * 0.12f;
                float h = R() < 0.25f ? 3.5f + R() * 2f : 0.8f + R() * 2.2f;
                if (!Clear(x - 0.8f, x + 0.8f, y0, y0 + h + 0.5f, z - 0.6f)) continue;
                BrokenColumn(b, rnd, x, z, r, h);
                if (R() < 0.6f)
                {
                    var rot = Quaternion.Euler(0f, R() * 60f - 30f, 90f);
                    float fx = x + (R() < 0.5f ? -1f : 1f) * (1f + R());
                    if (Clear(fx - 0.6f, fx + 0.6f, y0, y0 + 1f, z - 0.5f))
                    {
                        b.DrumR(SubMasonry, new Vector3(fx, y0 + r * 0.92f, z), rot, r, 0.7f + R() * 0.4f);
                        b.Box(SubAsh, new Vector3(fx - 0.3f, y0 + r * 1.84f, z - 0.15f), new Vector3(fx + 0.35f, y0 + r * 1.84f + 0.05f, z + 0.15f));
                    }
                }
            }
            // Ruined arcade in the mid-ground, ash on its broken top.
            for (float x = 8f + R() * 8f; x < W - 6f; x += 34f + R() * 12f)
            {
                float len = 9f + R() * 4f;
                Wall(b, rnd, x, x + len, y0, 4.5f + R() * 2.5f, 8.8f + R() * 1f, 0.45f, 3, 2f + R() * 0.5f, 1.8f + R() * 0.6f, false, 0.65f, _look.Overgrowth, true);
            }
            // Dead trees.
            for (float x = 14f + R() * 8f; x < W - 4f; x += 28f + R() * 14f)
            {
                float z = 5.5f + R() * 2.5f;
                if (Clear(x - 2f, x + 2f, y0, y0 + 6f, z)) DeadTree(b, rnd, new Vector3(x, y0, z), 4f + R() * 2f, true);
            }
        }

        // ------------------------------------------------------------------------------------------ placement tests

        /// <summary>True if no platform tile in the rectangle reaches back to depth <paramref name="z"/> (the floor row is ignored).</summary>
        private bool Clear(float x0, float x1, float y0, float y1, float z)
        {
            int ya = Mathf.Max(1, Mathf.FloorToInt(y0)), yb = Mathf.Min(_map.Height - 1, Mathf.CeilToInt(y1) - 1);
            for (int y = ya; y <= yb; y++)
            for (int x = Mathf.FloorToInt(x0); x <= Mathf.FloorToInt(x1); x++)
            {
                int wx = Wrap(x);
                if (!_map.IsSolid(wx, y)) continue;
                if (_tileDepth == null || _tileDepth[wx, y] + 0.4f > z) return false;
            }
            return true;
        }

        /// <summary>Open height under a slab bottom at <paramref name="x"/> (to the next solid tile or the floor).</summary>
        private float FreeBelow(float x, int slabBottom)
        {
            int ix = Wrap(Mathf.FloorToInt(x));
            for (int y = slabBottom - 1; y >= 1; y--) if (_map.IsSolid(ix, y)) return slabBottom - (y + 1);
            return slabBottom - FloorTop;
        }

        private float FreeBelowSpan(float x0, float x1, int slabBottom)
        {
            float m = float.MaxValue;
            for (float x = x0; x <= x1; x += 1f) m = Mathf.Min(m, FreeBelow(x, slabBottom));
            return m;
        }

        // ------------------------------------------------------------------------------------------ primitives

        private static Quaternion RandRot(System.Random rnd) =>
            Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f);

        /// <summary>Hanging vine: leaf clumps down a gently swaying line, thinning toward the tip.</summary>
        private static void Vine(BoxBuilder b, System.Random rnd, float x, float yTop, float z, float len, float density)
        {
            float R() => (float)rnd.NextDouble();
            float ph = R() * 6f;
            for (float d = 0f; d < len; d += 0.15f + R() * 0.07f)
            {
                float t = d / len;
                float sz = Mathf.Lerp(0.17f, 0.085f, t) * (0.85f + R() * 0.4f);
                float xx = x + Mathf.Sin(d * 1.3f + ph) * 0.16f;
                b.Tetra(SubFoliage, new Vector3(xx + (R() - 0.5f) * 0.1f, yTop - d, z + (R() - 0.5f) * 0.14f), RandRot(rnd), sz);
                if (R() < 0.35f + 0.4f * density)
                    b.Tetra(SubFoliage, new Vector3(xx + (R() - 0.5f) * 0.32f, yTop - d - 0.07f, z + (R() - 0.5f) * 0.2f), RandRot(rnd), sz * 0.8f);
            }
        }

        /// <summary>Flat creeper patch on a vertical face at depth <paramref name="z"/> (just in front of it).</summary>
        private static void LeafPatch(BoxBuilder b, System.Random rnd, float cx, float cy, float z, float w, float h, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rnd.NextDouble());
                var p = new Vector3(cx + Mathf.Cos(a) * r * w * 0.5f, cy + Mathf.Sin(a) * r * h * 0.5f, z - 0.06f - (float)rnd.NextDouble() * 0.1f);
                b.Tetra(SubFoliage, p, RandRot(rnd), 0.1f + (float)rnd.NextDouble() * 0.09f);
            }
        }

        /// <summary>Bush: a half-dome of leaf clumps on the floor.</summary>
        private static void Bush(BoxBuilder b, System.Random rnd, float x, float z, float s)
        {
            int n = 8 + (int)(s * 22f);
            for (int i = 0; i < n; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = (float)rnd.NextDouble();
                float hy = Mathf.Sqrt(Mathf.Max(0f, 1f - r * r)) * s * 0.9f * (float)rnd.NextDouble();
                b.Tetra(SubFoliage, new Vector3(x + Mathf.Cos(a) * r * s * 1.3f, FloorTop + 0.06f + hy, z + Mathf.Sin(a) * r * s * 0.7f),
                    RandRot(rnd), 0.12f + (float)rnd.NextDouble() * 0.1f);
            }
        }

        /// <summary>Rubble mound: broken chunks heaped high in the middle, a few rebar rods, ash on top if any.</summary>
        private static void Rubble(BoxBuilder b, System.Random rnd, float x, float z, float size, bool ash)
        {
            float R() => (float)rnd.NextDouble();
            int n = 5 + (int)(size * 9f);
            for (int i = 0; i < n; i++)
            {
                float a = R() * Mathf.PI * 2f, r = Mathf.Pow(R(), 0.8f);
                float px = x + Mathf.Cos(a) * r * size * 1.4f, pz = z + Mathf.Sin(a) * r * size * 0.6f;
                float s = (0.14f + R() * 0.26f) * Mathf.Lerp(1.3f, 0.7f, r) * Mathf.Max(0.6f, size);
                float py = FloorTop + (1f - r) * size * 0.45f + s * 0.25f;
                var rot = Quaternion.Euler(R() * 40f - 20f, R() * 360f, R() * 40f - 20f);
                b.Oriented(SubMasonry, new Vector3(px, py, pz), rot, new Vector3(s * 1.6f, s * 0.7f, s));
                if (ash && r < 0.6f && R() < 0.5f)
                    b.Oriented(SubAsh, new Vector3(px, py + s * 0.37f, pz), rot, new Vector3(s * 1.3f, 0.05f, s * 0.8f));
            }
            int bars = (int)(R() * 3f);
            for (int i = 0; i < bars; i++)
            {
                var a = new Vector3(x + (R() - 0.5f) * size, FloorTop + size * 0.3f, z + (R() - 0.5f) * 0.4f);
                b.Beam(SubMetal, a, a + new Vector3((R() - 0.5f) * 1.2f, 0.4f + R() * 0.6f, (R() - 0.5f) * 0.3f), 0.07f, 0.07f);
            }
        }

        /// <summary>Catenary-ish cable from <paramref name="a"/> to <paramref name="c"/> sagging by <paramref name="sag"/>.</summary>
        private static void Cable(BoxBuilder b, Vector3 a, Vector3 c, float sag, float w)
        {
            const int N = 10;
            var prev = a;
            for (int i = 1; i <= N; i++)
            {
                float t = i / (float)N;
                var p = Vector3.Lerp(a, c, t) + Vector3.down * (sag * 4f * t * (1f - t));
                b.Beam(SubMetal, prev, p, w, w);
                prev = p;
            }
        }

        /// <summary>Loose cable end hanging from <paramref name="top"/>, curling at the tip.</summary>
        private static void Dangle(BoxBuilder b, System.Random rnd, Vector3 top, float len)
        {
            var prev = top;
            float side = (float)rnd.NextDouble() < 0.5f ? -1f : 1f;
            for (int i = 1; i <= 6; i++)
            {
                float t = i / 6f;
                var p = top + new Vector3(side * 0.35f * t * t, -len * t, 0f);
                b.Beam(SubMetal, prev, p, 0.07f, 0.07f);
                prev = p;
            }
        }

        /// <summary>Dead tree: tapered trunk and recursive bare branches in the XY plane (silhouette), optional ash.</summary>
        private static void DeadTree(BoxBuilder b, System.Random rnd, Vector3 root, float height, bool ash)
        {
            Branch(b, rnd, root, Vector3.up, height * 0.42f, 0.26f, 0, ash);
        }

        private static void Branch(BoxBuilder b, System.Random rnd, Vector3 p, Vector3 dir, float len, float w, int depth, bool ash)
        {
            float R() => (float)rnd.NextDouble();
            var end = p + dir * len;
            b.Beam(SubMetal, p, end, w, w * 0.9f);
            if (ash && depth >= 1 && dir.y < 0.85f) b.Beam(SubAsh, p + Vector3.up * (w * 0.55f), end + Vector3.up * (w * 0.55f), w * 0.5f, w * 0.7f);
            if (depth >= 4 || w < 0.1f) return;
            int kids = R() < 0.35f ? 3 : 2;
            for (int k = 0; k < kids; k++)
            {
                float ang = (k == 0 ? (R() - 0.5f) * 20f : (k == 1 ? 1f : -1f) * (22f + R() * 30f)) + (R() - 0.5f) * 10f;
                var nd = Quaternion.Euler(0f, 0f, ang) * dir;
                nd = (nd + new Vector3(0f, 0f, (R() - 0.5f) * 0.25f)).normalized;
                Branch(b, rnd, end, nd, len * (0.62f + R() * 0.18f), Mathf.Max(0.07f, w * 0.62f), depth + 1, ash);
            }
        }

        /// <summary>Broken classical column: plinth, stacked drums, a tilted broken top (or a capital when tall), ash caps.</summary>
        private static void BrokenColumn(BoxBuilder b, System.Random rnd, float x, float z, float r, float h)
        {
            float R() => (float)rnd.NextDouble();
            float y = FloorTop;
            b.Box(SubMasonry, new Vector3(x - r * 1.35f, y, z - r * 1.35f), new Vector3(x + r * 1.35f, y + 0.32f, z + r * 1.35f));
            b.Box(SubAsh, new Vector3(x - r * 1.35f, y + 0.32f, z - r * 1.3f), new Vector3(x + r * 0.2f, y + 0.37f, z + r * 1.3f));
            y += 0.32f;
            float top = FloorTop + h;
            float ox = 0f;
            while (y < top - 0.2f)
            {
                float sh = Mathf.Min(0.75f + R() * 0.2f, top - y);
                b.DrumR(SubMasonry, new Vector3(x + ox, y, z), Quaternion.Euler(0f, R() * 45f, 0f), r * (0.97f + R() * 0.05f), sh);
                ox += (R() - 0.5f) * 0.04f;
                y += sh;
            }
            if (h > 3.4f)
            {
                b.Box(SubMasonry, new Vector3(x - r * 1.3f, y, z - r * 1.3f), new Vector3(x + r * 1.3f, y + 0.22f, z + r * 1.3f));
                b.Box(SubAsh, new Vector3(x - r * 1.3f, y + 0.22f, z - r * 1.2f), new Vector3(x + r * 1.3f, y + 0.28f, z + r * 1.2f));
            }
            else
            {
                // Sheared top: a tilted half-drum chunk.
                var rot = Quaternion.Euler(R() * 10f - 5f, R() * 40f, (R() < 0.5f ? 1f : -1f) * (14f + R() * 14f));
                b.Oriented(SubMasonry, new Vector3(x + ox, y + 0.12f, z), rot, new Vector3(r * 1.7f, 0.3f, r * 1.7f));
                b.Oriented(SubAsh, new Vector3(x + ox, y + 0.29f, z), rot, new Vector3(r * 1.4f, 0.05f, r * 1.4f));
            }
        }

        /// <summary>
        /// Ruined masonry wall from x0 to x1 standing on <paramref name="yb"/> at depth z (front face), thickness t:
        /// optional arches (count, opening width, springing height) with voussoir rings, an upper row of empty
        /// windows, a broken stepped top, creeper cover and vines by <paramref name="overgrowth"/>, ash caps.
        /// Built from 0.2-wide masonry columns; equal neighbouring columns merge into one box.
        /// </summary>
        private static void Wall(BoxBuilder b, System.Random rnd, float x0, float x1, float yb, float h, float z, float t,
            int arches, float aw, float spring, bool windows, float broken, float overgrowth, bool ash)
        {
            float R() => (float)rnd.NextDouble();
            const float Cw = 0.2f, Course = 0.25f;
            int n = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / Cw));
            float seed = R() * 50f;
            float ar = aw * 0.5f;
            var tops = new float[n];
            var segs = new List<(float lo, float hi)>[n];
            for (int i = 0; i < n; i++)
            {
                float u = (i + 0.5f) / n;
                float edge = Mathf.Min(u, 1f - u);
                float noise = Mathf.PerlinNoise(seed + i * 0.07f, seed * 0.3f);
                float drop = broken * Mathf.Clamp01(noise * 1.5f - 0.35f) + (edge < 0.12f ? (0.12f - edge) * 4f : 0f);
                float top = Mathf.Max(h * 0.25f, h * (1f - drop));
                top = Mathf.Round(top / Course) * Course;
                tops[i] = top;
                float cx = x0 + (i + 0.5f) * Cw;
                var list = new List<(float, float)>();
                float lo = 0f;
                // Openings in this column, bottom-up: the arch, then the window above it.
                for (int k = 0; k < arches; k++)
                {
                    float oc = x0 + (k + 0.5f) * (x1 - x0) / arches;
                    float dx = cx - oc;
                    if (Mathf.Abs(dx) >= ar) continue;
                    float curve = spring + Mathf.Sqrt(ar * ar - dx * dx);
                    curve = Mathf.Round(curve / (Course * 0.5f)) * (Course * 0.5f);
                    lo = Mathf.Min(curve, top);
                    if (windows && Mathf.Abs(dx) < 0.55f)
                    {
                        float w0 = spring + ar + 1f, w1 = w0 + 1.3f;
                        if (top > w1 + 0.5f) { list.Add((lo, w0)); lo = w1; }
                    }
                }
                if (top > lo + 0.01f) list.Add((lo, top));
                segs[i] = list;
            }
            // Merge equal neighbouring columns.
            int s0 = 0;
            for (int i = 1; i <= n; i++)
            {
                if (i < n && Same(segs[i], segs[s0])) continue;
                float xa = x0 + s0 * Cw, xb = x0 + i * Cw;
                foreach (var sg in segs[s0])
                    b.Box(SubMasonry, new Vector3(xa, yb + sg.lo, z), new Vector3(xb, yb + sg.hi, z + t));
                if (ash && segs[s0].Count > 0)
                {
                    float hi = segs[s0][segs[s0].Count - 1].hi;
                    b.Box(SubAsh, new Vector3(xa, yb + hi, z + 0.02f), new Vector3(xb, yb + hi + 0.06f, z + t - 0.02f));
                }
                s0 = i;
            }
            // Voussoir rings proud of the face where the wall still stands above them.
            for (int k = 0; k < arches; k++)
            {
                float oc = x0 + (k + 0.5f) * (x1 - x0) / arches;
                const int V = 9;
                for (int v = 0; v < V; v++)
                {
                    float a = Mathf.PI * (v + 0.5f) / V;
                    var c = new Vector3(oc + Mathf.Cos(a) * (ar + 0.12f), yb + spring + Mathf.Sin(a) * (ar + 0.12f), z - 0.04f);
                    int col = Mathf.Clamp(Mathf.FloorToInt((c.x - x0) / Cw), 0, n - 1);
                    if (tops[col] + yb < c.y + 0.2f) continue; // broken away
                    b.Oriented(SubMasonry, c, Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f), new Vector3(Mathf.PI * ar / V * 1.05f, 0.24f, 0.1f));
                }
                // Impost blocks at the springing line.
                b.Box(SubMasonry, new Vector3(oc - ar - 0.3f, yb + spring - 0.15f, z - 0.06f), new Vector3(oc - ar + 0.05f, yb + spring, z + t));
                b.Box(SubMasonry, new Vector3(oc + ar - 0.05f, yb + spring - 0.15f, z - 0.06f), new Vector3(oc + ar + 0.3f, yb + spring, z + t));
            }
            // Overgrowth: creeper patches on the face, vines off the broken top and from the arch crowns, a bush at the foot.
            if (overgrowth > 0.01f)
            {
                int patches = Mathf.RoundToInt((x1 - x0) * overgrowth * 0.9f);
                for (int i = 0; i < patches; i++)
                {
                    float px = x0 + R() * (x1 - x0);
                    int col = Mathf.Clamp(Mathf.FloorToInt((px - x0) / Cw), 0, n - 1);
                    float py = yb + R() * tops[col] * (R() < 0.6f ? 0.45f : 1f);
                    LeafPatch(b, rnd, px, py, z, 0.8f + R() * 1.2f, 0.6f + R() * 1.4f, 5 + (int)(R() * 8f * overgrowth));
                }
                int vines = Mathf.RoundToInt((x1 - x0) * overgrowth * 0.8f);
                for (int i = 0; i < vines; i++)
                {
                    int col = (int)(R() * n);
                    Vine(b, rnd, x0 + (col + 0.5f) * Cw, yb + tops[col] + 0.05f, z - 0.12f, Mathf.Min(tops[col] - 0.3f, 0.8f + R() * 3f * overgrowth), overgrowth);
                }
                for (int k = 0; k < arches; k++)
                {
                    float oc = x0 + (k + 0.5f) * (x1 - x0) / arches;
                    if (R() < overgrowth) Vine(b, rnd, oc + (R() - 0.5f) * ar, yb + spring + ar - 0.1f, z + t * 0.5f, 0.8f + R() * 1.6f, overgrowth);
                }
                if (R() < overgrowth) Bush(b, rnd, x0 + R() * (x1 - x0), z - 0.5f, 0.6f + R() * 0.6f);
            }
        }

        private static bool Same(List<(float lo, float hi)> a, List<(float lo, float hi)> c)
        {
            if (a.Count != c.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (Mathf.Abs(a[i].lo - c[i].lo) > 0.01f || Mathf.Abs(a[i].hi - c[i].hi) > 0.01f) return false;
            return true;
        }
    }
}
