using System.Collections.Generic;
using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Procedural meshes for the 2.5D prototype: chamfered boxes with world-space UVs (so panel textures
    /// tile at a constant density across merged tile runs), open cones for light shafts, spikes.
    /// </summary>
    /// <remarks>Prototype-only; builds meshes once.</remarks>
    public static class Proto3DMeshes
    {
        private sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<int>[] T = { new List<int>(), new List<int>() };

            /// <summary>Quad a,b,c,d counter-clockwise seen from the side the normal faces (Unity: clockwise = front).</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, System.Func<Vector3, Vector2> uv, int sub)
            {
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                for (int k = 0; k < 4; k++) N.Add(n);
                U.Add(uv(a)); U.Add(uv(b)); U.Add(uv(c)); U.Add(uv(d));
                // Unity front faces are clockwise when viewed from the front.
                T[sub].Add(i); T[sub].Add(i + 1); T[sub].Add(i + 2);
                T[sub].Add(i); T[sub].Add(i + 2); T[sub].Add(i + 3);
            }

            public void Poly(Vector3[] p, Vector3 n, System.Func<Vector3, Vector2> uv, int sub)
            {
                int i = V.Count;
                foreach (var q in p) { V.Add(q); N.Add(n); U.Add(uv(q)); }
                for (int k = 1; k < p.Length - 1; k++) { T[sub].Add(i); T[sub].Add(i + k); T[sub].Add(i + k + 1); }
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetUVs(0, U);
                m.subMeshCount = 2;
                m.SetTriangles(T[0], 0);
                m.SetTriangles(T[1], 1);
                m.RecalculateTangents();
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>
        /// Axis-aligned box from <paramref name="min"/> to <paramref name="max"/> (world units; the mesh is built in
        /// world space, place it at the origin). The top-front and top-back edges are chamfered by
        /// <paramref name="chamfer"/>. Submesh 0 = sides/front/back/bottom, submesh 1 = top + chamfers (the wet
        /// surface). UV = world position / <paramref name="uvScale"/> on the face's two axes.
        /// </summary>
        public static Mesh ChamferBox(Vector3 min, Vector3 max, float chamfer, float uvScale, string name = "box")
        {
            var b = new Builder();
            float x0 = min.x, x1 = max.x, y0 = min.y, y1 = max.y, z0 = min.z, z1 = max.z;
            float c = Mathf.Min(chamfer, (y1 - y0) * 0.45f, (z1 - z0) * 0.45f);
            float s = 1f / uvScale;
            System.Func<Vector3, Vector2> uvXY = p => new Vector2(p.x * s, p.y * s);
            System.Func<Vector3, Vector2> uvXZ = p => new Vector2(p.x * s, p.z * s);
            System.Func<Vector3, Vector2> uvZY = p => new Vector2(p.z * s, p.y * s);
            // Front (facing -z): seen from -z, clockwise = (x0,y0) -> (x0,yt) -> (x1,yt) -> (x1,y0).
            float yt = y1 - c;
            b.Quad(new Vector3(x0, y0, z0), new Vector3(x0, yt, z0), new Vector3(x1, yt, z0), new Vector3(x1, y0, z0), Vector3.back, uvXY, 0);
            // Front chamfer (normal up-and-toward-camera).
            var nfc = new Vector3(0, 1, -1).normalized;
            b.Quad(new Vector3(x0, yt, z0), new Vector3(x0, y1, z0 + c), new Vector3(x1, y1, z0 + c), new Vector3(x1, yt, z0), nfc, uvXZ, 1);
            // Top.
            b.Quad(new Vector3(x0, y1, z0 + c), new Vector3(x0, y1, z1 - c), new Vector3(x1, y1, z1 - c), new Vector3(x1, y1, z0 + c), Vector3.up, uvXZ, 1);
            // Back chamfer.
            var nbc = new Vector3(0, 1, 1).normalized;
            b.Quad(new Vector3(x0, y1, z1 - c), new Vector3(x0, yt, z1), new Vector3(x1, yt, z1), new Vector3(x1, y1, z1 - c), nbc, uvXZ, 1);
            // Back (facing +z).
            b.Quad(new Vector3(x1, y0, z1), new Vector3(x1, yt, z1), new Vector3(x0, yt, z1), new Vector3(x0, y0, z1), Vector3.forward, uvXY, 0);
            // Bottom.
            b.Quad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), Vector3.down, uvXZ, 0);
            // Left (facing -x), pentagon/hexagon outline, seen from -x clockwise.
            b.Poly(new[] { new Vector3(x0, y0, z1), new Vector3(x0, yt, z1), new Vector3(x0, y1, z1 - c), new Vector3(x0, y1, z0 + c), new Vector3(x0, yt, z0), new Vector3(x0, y0, z0) },
                Vector3.left, uvZY, 0);
            // Right (facing +x).
            b.Poly(new[] { new Vector3(x1, y0, z0), new Vector3(x1, yt, z0), new Vector3(x1, y1, z0 + c), new Vector3(x1, y1, z1 - c), new Vector3(x1, yt, z1), new Vector3(x1, y0, z1) },
                Vector3.right, uvZY, 0);
            return b.Build(name);
        }

        /// <summary>
        /// Open cone (no caps) with its apex at the origin pointing up and the base of <paramref name="radius"/> at
        /// y = -<paramref name="length"/>. UV.y = 1 at the apex, 0 at the base. Normals point outward.
        /// </summary>
        public static Mesh Cone(float radius, float length, float apexRadius = 0.12f, int seg = 20)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var nn = (dir * length + Vector3.up * (radius - apexRadius)).normalized;
                v.Add(dir * apexRadius); n.Add(nn); u.Add(new Vector2(i / (float)seg, 1f));
                v.Add(dir * radius + Vector3.down * length); n.Add(nn); u.Add(new Vector2(i / (float)seg, 0f));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                t.Add(a); t.Add(c); t.Add(b);
                t.Add(b); t.Add(c); t.Add(d);
            }
            var m = new Mesh { name = "cone" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, u); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Row of four-sided spikes across a tile (x0..x0+1), standing on y, spanning z0..z1.</summary>
        public static Mesh Spikes(float x0, float y, float z0, float z1, float height, int perRow = 3)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            int rows = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / 0.5f));
            for (int r = 0; r < rows; r++)
            for (int k = 0; k < perRow; k++)
            {
                float w = 1f / perRow;
                float cx = x0 + (k + 0.5f) * w, cz = z0 + (r + 0.5f) * (z1 - z0) / rows;
                float h = height * (0.75f + 0.25f * ((k * 7 + r * 3) % 5) / 4f);
                var apex = new Vector3(cx, y + h, cz);
                var p = new[]
                {
                    new Vector3(cx - w * 0.45f, y, cz - w * 0.45f), new Vector3(cx + w * 0.45f, y, cz - w * 0.45f),
                    new Vector3(cx + w * 0.45f, y, cz + w * 0.45f), new Vector3(cx - w * 0.45f, y, cz + w * 0.45f)
                };
                for (int f = 0; f < 4; f++)
                {
                    int i = v.Count;
                    v.Add(p[f]); v.Add(apex); v.Add(p[(f + 1) % 4]);
                    t.Add(i); t.Add(i + 1); t.Add(i + 2);
                }
            }
            var m = new Mesh { name = "spikes" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Plain quad facing -z, centred, size w×h, UV 0..1.</summary>
        public static Mesh Quad(float w, float h)
        {
            var m = new Mesh { name = "quad" };
            m.SetVertices(new List<Vector3> { new Vector3(-w / 2, -h / 2, 0), new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(w / 2, -h / 2, 0) });
            m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Flat strip lying on y = 0 (normal +y), <paramref name="w"/> wide in x, running from z = 0 to
        /// z = -<paramref name="len"/> (toward the camera). UV x across 0..1, UV y along 0 (z = 0) .. 1 (near end).
        /// Used by the wet-reflection streaks (Proto3D/Fx with _Wet = 1; the material is double-sided).
        /// </summary>
        public static Mesh FloorStrip(float w, float len)
        {
            var m = new Mesh { name = "floorStrip" };
            m.SetVertices(new List<Vector3> { new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(w / 2, 0, -len), new Vector3(-w / 2, 0, -len) });
            m.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Merges two-submesh meshes (as built by <see cref="ChamferBox"/>) into one two-submesh mesh, keeping
        /// submesh 0 (sides) and submesh 1 (tops) separate. Destroys the inputs when <paramref name="destroyInputs"/>.
        /// </summary>
        public static Mesh MergeTwoSub(List<Mesh> parts, string name, bool destroyInputs = true)
        {
            var a = new CombineInstance[parts.Count];
            var b = new CombineInstance[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                a[i] = new CombineInstance { mesh = parts[i], subMeshIndex = 0, transform = Matrix4x4.identity };
                b[i] = new CombineInstance { mesh = parts[i], subMeshIndex = 1, transform = Matrix4x4.identity };
            }
            var sides = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            sides.CombineMeshes(a, true, false);
            var tops = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            tops.CombineMeshes(b, true, false);
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.CombineMeshes(new[]
            {
                new CombineInstance { mesh = sides, transform = Matrix4x4.identity },
                new CombineInstance { mesh = tops, transform = Matrix4x4.identity },
            }, false, false);
            m.RecalculateBounds();
            Object.Destroy(sides); Object.Destroy(tops);
            if (destroyInputs) foreach (var p in parts) Object.Destroy(p);
            return m;
        }
    }
}
