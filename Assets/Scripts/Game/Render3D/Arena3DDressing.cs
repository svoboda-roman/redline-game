using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Set-dressing layers of <see cref="Arena3DRenderer"/>: the dense sharp mid-ground (Z +3..+14: columns, trusses,
    /// pipes, cranes, overpass spans, facades), the backdrop wall behind it, and the foreground occluders
    /// (Z -4..-1.5, vertical girders only). Each layer is merged once per level into a few sub-meshes (one per material group) and the
    /// same mesh is instanced into every ring copy, so the dressing tiles seamlessly: pieces are owned by the copy
    /// holding their centre and wall runs are divided exactly into the ring width.
    /// </summary>
    /// <remarks>Per-level kit: Loop01 rooftop plant, Loop02 overpass, Loop03 gallery. Anything else uses the plant.</remarks>
    public sealed partial class Arena3DRenderer
    {
        private Mesh _dressMid, _dressFg;
        private Material[] _dressMidMats, _dressFgMats;

        /// <summary>Triangles in the merged dressing layers (mid-ground + foreground), one ring copy.</summary>
        public long DressingTriangles { get; private set; }

        /// <summary>Number of kit pieces placed in one ring copy.</summary>
        public int DressingPieces { get; private set; }

        private void BuildDressing(Transform copy)
        {
            if (_dressMid == null && _dressFg == null) BuildDressingMeshes();
            if (_dressMid != null) NoShadow(MeshGo("dressMid", copy, _dressMid, _dressMidMats));
            if (_dressFg != null) NoShadow(MeshGo("dressFg", copy, _dressFg, _dressFgMats));
        }

        private bool ColumnFree(float x, int yTo, int halfWidth)
        {
            int ix = Mathf.FloorToInt(x);
            for (int dx = -halfWidth; dx <= halfWidth; dx++)
            for (int y = 1; y <= yTo; y++)
                if (_map.IsSolid(((ix + dx) % _map.Width + _map.Width) % _map.Width, y)) return false;
            return true;
        }

        private void BuildDressingMeshes()
        {
            var rnd = new System.Random(_look.Seed * 17 + 3);
            float W = _map.Width;
            string lvl = _look.Name;
            bool overpass = lvl == "Loop02", gallery = lvl == "Loop03";
            var mid = new Props3D.Merger();
            var fg = new Props3D.Merger();
            float R() => (float)rnd.NextDouble();
            Matrix4x4 T(float x, float y, float z, float yaw, Vector3 s) => Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f), s);
            Vector3 S(float v) => new Vector3(v, v, v);
            float baseY = FloorTop;

            // 1. Backdrop wall (z 14.8..16): ONE low, gapped row, so the top of the frame shows the far skyline
            //    silhouetted against the bright sky (Replaced: light behind, dark in front). Divided exactly into W.
            float pieceW = gallery ? 12f : 9f;
            int n = Mathf.Max(1, Mathf.RoundToInt(W / pieceW));
            float sx = W / n / pieceW;
            for (int i = 0; i < n; i++)
            {
                float gap = R();
                if (!gallery && gap < 0.3f) continue; // open lots between blocks (the gallery stays continuous: lit wall)
                string name = gallery ? "gallery_arcade" : "facade_block";
                float h = gallery ? 0.62f + R() * 0.12f : 0.45f + R() * 0.35f;
                float xx = (i + 0.5f) * W / n;
                mid.Add(name, T(xx, baseY - 0.6f, 14.8f + R() * 0.8f, 0f, new Vector3(sx, h, 1f)));
            }

            // 2. Structural ribs (z 9..13.5): column + truss pairs in a jittered grid.
            float cell = Mathf.Max(4f, _look.RibSpacing); // wider in Loop02/03: fewer ribs crowding under high slabs
            int cells = Mathf.Max(1, Mathf.RoundToInt(W / cell));
            for (int i = 0; i < cells; i++)
            {
                float x = (i + 0.25f + R() * 0.5f) * W / cells;
                float z = 9f + R() * 4.5f;
                int kind = i % 4;
                float ys = 0.85f + R() * 0.9f;
                if (overpass)
                {
                    if (kind == 0 || kind == 2) mid.Add("pillar", T(x, baseY, z, 0f, new Vector3(0.7f + R() * 0.5f, 0.9f + R() * 0.6f, 0.7f + R() * 0.5f)));
                    else mid.Add("girder_col", T(x, baseY, z, 0f, new Vector3(0.75f, ys, 0.75f)));
                }
                else if (gallery)
                {
                    if (kind == 0) mid.Add("pillar", T(x, baseY, z, 0f, new Vector3(0.55f, 1.3f, 0.55f)));
                    else if (kind == 1) mid.Add("banner", T(x, baseY + 6.5f + R() * 2f, z, 0f, S(1.2f)));
                    else mid.Add("girder_col", T(x, baseY, z + 1f, 0f, new Vector3(0.7f, ys, 0.7f)));
                }
                else
                {
                    if (kind == 3) mid.Add("girder_x", T(x, baseY, z, 0f, new Vector3(1f, ys, 1f)));
                    else mid.Add("girder_col", T(x, baseY, z, 0f, new Vector3(1f, ys, 1f)));
                }
                if (kind == 1 || kind == 3)
                    mid.Add("truss_span", T(x + 3.1f, baseY + 4.2f + R() * 6f, z, 0f, new Vector3(0.7f + R() * 0.4f, 1f, 1f)));
                if (kind == 0 || kind == 2)
                    mid.Add("pipe_run", T(x + 1.5f, baseY + (kind == 0 ? 0.2f : 6.3f), 13.6f - R() * 1.5f, 0f, new Vector3(0.8f + R() * 0.4f, 1f, 1f)));
                if (kind == 2)
                    mid.Add("cable_sag", T(x + 3f, baseY + 6f + R() * 3f, z - 1f, 0f, new Vector3(0.9f + R() * 0.3f, 1f, 1f)));
            }

            // 3. Level signature pieces.
            if (overpass)
            {
                int spans = Mathf.Max(1, Mathf.RoundToInt(W / 12f));
                float spx = W / spans / 12f;
                for (int i = 0; i < spans; i++)
                {
                    float x = (i + 0.5f) * W / spans;
                    mid.Add("overpass_span", T(x, baseY + 7.8f, 12.6f, 0f, new Vector3(spx, 1f, 1f)));
                    mid.Add("pillar", T(x + W / spans * 0.5f, baseY, 12.6f, 0f, new Vector3(0.8f, 0.78f, 0.8f)));
                }
            }
            else if (gallery)
            {
                for (float x = 6f + R() * 3f; x < W - 4f; x += 11f + R() * 5f)
                {
                    if (!ColumnFree(x, 5, 1)) continue;
                    mid.Add("statue", T(x, baseY, 3.6f + R() * 4f, 0f, S(1.25f)));
                }
            }
            else
            {
                for (float x = 8f + R() * 4f; x < W - 8f; x += 24f + R() * 10f)
                    mid.Add("crane", T(x, baseY - 1f, 15.5f, R() > 0.5f ? 0f : 180f, S(0.95f + R() * 0.25f)));
                for (float x = 3f + R() * 5f; x < W - 3f; x += 13f + R() * 7f)
                    mid.Add("chimney", T(x, baseY, 10f + R() * 3.5f, 0f, new Vector3(1f, 1f + R() * 0.8f, 1f)));
            }

            // 4. Low deck furniture behind the play plane, only where nothing solid stands.
            for (float x = 4f + R() * 3f; x < W - 3f; x += 9f + R() * 5f)
            {
                if (!ColumnFree(x, 3, 2)) continue;
                mid.Add(x % 2f < 1f ? "jersey_barrier" : "guard_rail", T(x, baseY, 3.2f + R() * 1.4f, 0f, S(1f)));
            }

            // 5. No vertical foreground occluders. The camera sits only ≈ 12.4 units in front of the play plane
            //    (FramingHeight 6.67, FOV 30; 9.3 and ×1.6 at the old 5.0), so an fg_girder at z -3.6 is magnified ×1.4: a 1-unit beam with 1.5-unit
            //    collars read as a black pillar over 16-25 % of the frame and swept across the characters whenever
            //    the camera scrolled past it. Foreground framing is the low kerb line in BuildFramers, which projects
            //    below the feet sight-line by construction.

            DressingPieces = mid.Count + fg.Count;
            DressingTriangles = mid.Triangles + fg.Triangles;
            // World box-projected UVs + the grime detail texture (same texel density as the play plane).
            _dressMid = mid.Count > 0 ? Own(mid.Build("dressMid", _palette.Textured, out _dressMidMats, GrimeUnits)) : null;
            _dressFg = fg.Count > 0 ? Own(fg.Build("dressFg", _palette.Textured, out _dressFgMats, GrimeUnits)) : null;
            Debug.Log($"[Dressing] {lvl}: {DressingPieces} pieces, {DressingTriangles} tris/copy, mid {_dressMidMats?.Length ?? 0} + fg {_dressFgMats?.Length ?? 0} sub-meshes/copy");
        }
    }
}
