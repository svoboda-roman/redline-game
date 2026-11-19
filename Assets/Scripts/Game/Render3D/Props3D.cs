using System;
using System.Collections.Generic;
using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Loads the rooftop prop models (FBX under <c>Assets/Render3D/Resources/Props3D/</c>, so they ship in builds)
    /// and instantiates them with runtime materials resolved by the model's material slot name
    /// (metal, rust, neon, warm, ...). See <see cref="Palette"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var palette = new Props3D.Palette(look);
    /// var tank = Props3D.Spawn("water_tank", parent, palette.For);
    /// palette.Dispose(); // when the level goes away (instances are destroyed with their parent)
    /// </code>
    /// </example>
    /// <remarks>Main thread only. Spawning allocates (build time only).</remarks>
    public static class Props3D
    {
        /// <summary>Every prop model name available.</summary>
        public static readonly string[] Names =
        {
            "ac_unit", "antenna_mast", "barrels", "billboard", "cable_posts", "container", "crates", "debris",
            "exhaust_elbow", "fan_tower", "fence", "neon_sign", "railing", "sat_dish", "stair_hut", "vent_stack", "water_tank",
            // set-dressing kit (tools/art/props3d/build_dressing.py)
            "girder_col", "girder_x", "truss_span", "crane", "pillar", "overpass_span", "jersey_barrier", "guard_rail",
            "facade_block", "gallery_arcade", "statue", "banner", "chimney", "pipe_run", "cable_sag",
            "fg_girder", "fg_pipe", "fg_fence", "fg_cables",
        };

        /// <summary>
        /// Collapses model slot names into few material groups so merged dressing layers stay at a handful of
        /// draw calls (metal, gunmetal and steel share one; everything else keeps its own).
        /// </summary>
        public static string GroupOf(string slot)
        {
            slot = slot.Replace(" (Instance)", "").Trim();
            int dot = slot.IndexOf('.');
            if (dot > 0) slot = slot.Substring(0, dot);
            return slot == "gunmetal" || slot == "steel" ? "metal" : slot;
        }

        /// <summary>
        /// Merges many placed instances of kit models into one mesh with one sub-mesh per material group
        /// (<see cref="GroupOf"/>). Needs the FBX to be read/write enabled (the kit's import settings are).
        /// </summary>
        public sealed class Merger
        {
            private readonly Dictionary<string, List<CombineInstance>> _groups = new Dictionary<string, List<CombineInstance>>();
            private readonly List<Mesh> _temp = new List<Mesh>();

            /// <summary>Number of instances added.</summary>
            public int Count { get; private set; }

            /// <summary>Triangle count of everything added.</summary>
            public long Triangles { get; private set; }

            /// <summary>Adds model <paramref name="name"/> placed by <paramref name="world"/> (relative to the layer root).</summary>
            public bool Add(string name, Matrix4x4 world)
            {
                var src = Load(name);
                if (src == null) return false;
                foreach (var mf in src.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mesh == null || mr == null) continue;
                    var local = src.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var mats = mr.sharedMaterials;
                    for (int i = 0; i < mesh.subMeshCount; i++)
                    {
                        string g = GroupOf(i < mats.Length && mats[i] != null ? mats[i].name : "metal");
                        if (!_groups.TryGetValue(g, out var list)) _groups[g] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = world * local });
                        Triangles += mesh.GetIndexCount(i) / 3;
                    }
                }
                Count++;
                return true;
            }

            /// <summary>
            /// Builds the merged mesh and its material list (same order as the sub-meshes). Null if empty.
            /// <paramref name="boxUvUnits"/> &gt; 0 replaces the models' UVs with world-aligned box projection, one
            /// texture tile per <paramref name="boxUvUnits"/> world units, so detail textures keep the scene's texel density.
            /// </summary>
            public Mesh Build(string name, Func<string, Material> materialFor, out Material[] mats, float boxUvUnits = 0f)
            {
                var parts = new List<CombineInstance>();
                var ml = new List<Material>();
                foreach (var kv in _groups)
                {
                    var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    m.CombineMeshes(kv.Value.ToArray(), true, true);
                    _temp.Add(m);
                    parts.Add(new CombineInstance { mesh = m, subMeshIndex = 0, transform = Matrix4x4.identity });
                    ml.Add(materialFor(kv.Key));
                }
                mats = ml.ToArray();
                if (parts.Count == 0) return null;
                var res = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                res.CombineMeshes(parts.ToArray(), false, false);
                foreach (var t in _temp) UnityEngine.Object.Destroy(t);
                _temp.Clear();
                if (boxUvUnits > 0f) BoxProjectUv(res, 1f / boxUvUnits);
                return res;
            }
        }

        /// <summary>
        /// Sets UV0 of a readable mesh to a box projection of its vertex positions: the face's dominant normal axis
        /// picks the plane (top/bottom XZ, sides ZY, front/back XY); UV = position × <paramref name="scale"/>.
        /// </summary>
        public static void BoxProjectUv(Mesh mesh, float scale)
        {
            if (mesh == null || !mesh.isReadable) return;
            var v = mesh.vertices;
            var n = mesh.normals;
            if (n == null || n.Length != v.Length) { mesh.RecalculateNormals(); n = mesh.normals; }
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var a = new Vector3(Mathf.Abs(n[i].x), Mathf.Abs(n[i].y), Mathf.Abs(n[i].z));
                var p = v[i] * scale;
                uv[i] = a.y >= a.x && a.y >= a.z ? new Vector2(p.x, p.z) : a.x >= a.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
            }
            mesh.uv = uv;
        }

        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

        /// <summary>Loads (cached) a prop model by name; null if missing.</summary>
        public static GameObject Load(string name)
        {
            if (Cache.TryGetValue(name, out var go) && go != null) return go;
            go = Resources.Load<GameObject>("Props3D/" + name);
            if (go == null) Debug.LogWarning("Props3D: missing model " + name);
            Cache[name] = go;
            return go;
        }

        /// <summary>
        /// Instantiates prop <paramref name="name"/> under <paramref name="parent"/>, replacing each material slot
        /// with <paramref name="materialFor"/>(slot name) or <paramref name="overrideMat"/>. Returns null if missing.
        /// </summary>
        public static GameObject Spawn(string name, Transform parent, Func<string, Material> materialFor, Material overrideMat = null)
        {
            var src = Load(name);
            if (src == null) return null;
            var go = UnityEngine.Object.Instantiate(src, parent);
            go.name = name;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = overrideMat != null ? overrideMat : materialFor(mats[i] != null ? mats[i].name : "x");
                r.sharedMaterials = mats;
            }
            return go;
        }

        /// <summary>Runtime materials for the prop slot names, tinted by a look. Owns and destroys them.</summary>
        public sealed class Palette : IDisposable
        {
            private readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
            private readonly Dictionary<string, Material> _textured = new Dictionary<string, Material>();
            private readonly Arena3DLook _look;

            /// <summary>Emission multiplier for emissive slots on <see cref="Textured"/> (merged dressing) materials.</summary>
            public const float DressingEmission = 0.3f;

            /// <summary>
            /// Greyscale detail (grime) albedo for <see cref="Textured"/>; not owned by the palette. Set it before
            /// the first <see cref="Textured"/> call; null makes <see cref="Textured"/> equal <see cref="For"/>.
            /// </summary>
            public Texture2D Detail;

            /// <summary>Mean luminance of <see cref="Detail"/>; slot colours are divided by it to keep their value.</summary>
            public float DetailMean = 1f;

            /// <summary>Creates an empty palette for <paramref name="look"/>; materials are made on first use.</summary>
            public Palette(Arena3DLook look) { _look = look; }

            /// <summary>Material for a model slot name (Unity's " (Instance)" and ".001" suffixes are ignored).</summary>
            public Material For(string slot) => Make(slot, false);

            /// <summary>
            /// Like <see cref="For"/> but with the <see cref="Detail"/> grime texture on non-emissive slots. Only for
            /// meshes with world-scaled UVs (<see cref="Merger.Build"/> with box UVs): the kit models' own UVs are 0..1
            /// per face and would squeeze the texels.
            /// </summary>
            public Material Textured(string slot) => Make(slot, Detail != null);

            private Material Make(string slot, bool textured)
            {
                slot = slot.Replace(" (Instance)", "").Trim();
                int dot = slot.IndexOf('.');
                if (dot > 0) slot = slot.Substring(0, dot);
                var cache = textured ? _textured : _mats;
                if (cache.TryGetValue(slot, out var m)) return m;
                Color c; float sm = 0.55f, me = 0.6f; Color em = Color.black;
                switch (slot)
                {
                    case "metal": c = new Color(0.2f, 0.23f, 0.27f); break;
                    case "gunmetal": c = new Color(0.14f, 0.15f, 0.17f); break;
                    case "steel": c = new Color(0.32f, 0.34f, 0.36f); sm = 0.7f; break;
                    case "dark": c = new Color(0.05f, 0.055f, 0.065f); break;
                    case "rust": c = new Color(0.25f, 0.11f, 0.06f); sm = 0.3f; me = 0.3f; break;
                    case "wood": c = new Color(0.17f, 0.11f, 0.07f); sm = 0.3f; me = 0f; break;
                    case "brick": c = new Color(0.15f, 0.12f, 0.115f); sm = 0.25f; me = 0f; break;
                    case "concrete": c = new Color(0.2f, 0.2f, 0.21f); sm = 0.4f; me = 0f; break;
                    case "teal": c = new Color(0.06f, 0.2f, 0.22f); break;
                    case "hazard": c = new Color(0.45f, 0.33f, 0.05f); sm = 0.45f; me = 0.2f; break;
                    case "paintred": c = new Color(0.35f, 0.05f, 0.05f); me = 0.2f; break;
                    case "wire": case "cable": c = new Color(0.04f, 0.04f, 0.045f); sm = 0.4f; break;
                    case "red": c = Color.black; em = _look.Signal * 2f; break;
                    case "warm": c = Color.black; em = _look.Sodium * 1.5f; break;
                    case "neon": c = Color.black; em = _look.Neon * 1.2f; break;
                    case "panel": c = Color.black; em = _look.Neon * 0.3f; break;
                    default: c = new Color(0.12f, 0.13f, 0.15f); break;
                }
                if (textured && em.maxColorComponent <= 0f)
                {
                    // Textured = the merged mid-ground dressing: MidValue pushes it toward silhouette per level.
                    var tc = c * _look.MidValue / Mathf.Max(0.3f, DetailMean); tc.a = 1f;
                    m = Proto3DMaterials.Lit3D("propT_" + slot, tc, sm, me, Detail);
                }
                else
                {
                    // Emissive panes on the merged dressing (gallery arcade doorways and windows, facade windows) are
                    // big flat quads 15 units back: at full strength they bloomed into flat cream rectangles. Keep
                    // them a dim lit glow there; standalone props (lamps, signs) keep their full emission.
                    if (textured) em *= DressingEmission * _look.MidValue;
                    m = Proto3DMaterials.Lit3D((textured ? "propE_" : "prop_") + slot, c, sm, me, emissionColor: em);
                }
                cache[slot] = m;
                return m;
            }

            /// <summary>Destroys the materials this palette created.</summary>
            public void Dispose()
            {
                foreach (var m in _mats.Values) if (m != null) UnityEngine.Object.Destroy(m);
                foreach (var m in _textured.Values) if (m != null) UnityEngine.Object.Destroy(m);
                _mats.Clear();
                _textured.Clear();
            }
        }
    }
}
