using UnityEngine;
using UnityEngine.Rendering;

namespace Redline.Game.Ronin
{
    /// <summary>
    /// Swing ribbon of the ronin's katana: a camera-independent strip swept between a point near the guard and the
    /// blade tip over the last <see cref="Lifetime"/> seconds, Catmull-Rom smoothed, drawn with an unlit additive
    /// material in the tier colour (HDR, so it blooms). Emits only while <see cref="Tick"/> is told to; the existing
    /// samples then age out.
    /// </summary>
    /// <remarks>
    /// One pooled mesh with fixed-size vertex, UV and index arrays: no per-frame allocation. The colour comes from a
    /// gradient texture (u = age, head bright, tail transparent; v = guard to tip, tip brightest) through the
    /// material's texture shape (Actors3D/Fx shape 3), tinted by the per-renderer <c>_Color</c>.
    /// Budget: one draw call, at most <see cref="MaxSamples"/> × <see cref="Subdivisions"/> × 2 vertices.
    /// </remarks>
    public sealed class BladeTrail : MonoBehaviour
    {
        /// <summary>Recorded swing samples kept (ring buffer).</summary>
        public const int MaxSamples = 16;
        /// <summary>Spline points per recorded segment.</summary>
        public const int Subdivisions = 3;
        private const int MaxPoints = (MaxSamples - 1) * Subdivisions + 1;

        /// <summary>Seconds a swept sample stays visible.</summary>
        public float Lifetime = 0.1f;
        /// <summary>Fraction of the guard-to-tip span where the ribbon's inner edge starts.</summary>
        public float InnerStart = 0.18f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private static Texture2D _gradient;

        private Transform _blade;
        private Vector3 _tipLocal, _innerLocal;
        private readonly Vector3[] _tip = new Vector3[MaxSamples];
        private readonly Vector3[] _inner = new Vector3[MaxSamples];
        private readonly float[] _born = new float[MaxSamples];
        private int _head, _count;
        private float _time;
        private Mesh _mesh;
        private Vector3[] _verts;
        private Vector2[] _uvs;
        private int[] _indices;
        private MeshRenderer _renderer;
        private Material _material;
        private MaterialPropertyBlock _mpb;
        private Color _color = Color.white;

        /// <summary>
        /// Creates the trail under <paramref name="parent"/> (any transform; the ribbon is rebuilt in its space each
        /// frame) for <paramref name="katana"/>. <paramref name="additiveTemplate"/> is copied (Actors3D/Fx additive).
        /// Returns null if the katana has no mesh.
        /// </summary>
        public static BladeTrail Create(Transform parent, Transform katana, Material additiveTemplate)
        {
            if (katana == null || additiveTemplate == null) return null;
            var go = new GameObject("BladeTrail");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<BladeTrail>();
            if (!t.Init(katana, additiveTemplate)) { Destroy(go); return null; }
            return t;
        }

        /// <summary>Tier colour (HDR) of the ribbon.</summary>
        public void SetColor(Color hdr)
        {
            _color = hdr;
            _color.a = 1f;
        }

        /// <summary>Samples the blade (when <paramref name="emit"/>), ages samples, rebuilds the ribbon. No allocations.</summary>
        public void Tick(float dt, bool emit)
        {
            _time += dt;
            while (_count > 0 && _time - _born[Index(_count - 1)] > Lifetime) _count--;
            if (emit)
            {
                _head = (_head + MaxSamples - 1) % MaxSamples;
                _tip[_head] = _blade.TransformPoint(_tipLocal);
                _inner[_head] = _blade.TransformPoint(_innerLocal);
                _born[_head] = _time;
                if (_count < MaxSamples) _count++;
            }
            Rebuild();
        }

        /// <summary>Drops all samples (teleport, respawn).</summary>
        public void Clear()
        {
            _count = 0;
            if (_renderer != null) _renderer.enabled = false;
        }

        private int Index(int age) => (_head + age) % MaxSamples;

        private bool Init(Transform katana, Material template)
        {
            Mesh src = null;
            var mf = katana.GetComponent<MeshFilter>();
            if (mf != null) src = mf.sharedMesh;
            if (src == null) return false;
            // Blade axis = longest local bounds axis. The end nearer the hand (the katana's parent) is the pommel;
            // the guard sits ~29% of the length from it (grip 33 : blade 82 in build_ronin.py).
            Bounds b = src.bounds;
            Vector3 e = b.extents;
            Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
            float half = Vector3.Dot(e, axis);
            Vector3 a = b.center - axis * half, c = b.center + axis * half;
            Vector3 hand = katana.parent != null ? katana.InverseTransformPoint(katana.parent.position) : Vector3.zero;
            Vector3 pommel = (a - hand).sqrMagnitude < (c - hand).sqrMagnitude ? a : c;
            Vector3 tip = pommel == a ? c : a;
            Vector3 guard = Vector3.Lerp(pommel, tip, 0.29f);
            _blade = katana;
            _tipLocal = tip;
            _innerLocal = Vector3.Lerp(guard, tip, InnerStart);

            _verts = new Vector3[MaxPoints * 2];
            _uvs = new Vector2[MaxPoints * 2];
            _indices = new int[(MaxPoints - 1) * 6];
            for (int i = 0; i < MaxPoints - 1; i++)
            {
                int v = i * 2, k = i * 6;
                _indices[k] = v; _indices[k + 1] = v + 1; _indices[k + 2] = v + 3;
                _indices[k + 3] = v; _indices[k + 4] = v + 3; _indices[k + 5] = v + 2;
            }
            _mesh = new Mesh { name = "BladeTrail" };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.SetIndices(_indices, MeshTopology.Triangles, 0, false);
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _material = new Material(template) { name = "BladeTrail" };
            _material.SetTexture("_MainTex", Gradient);
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.enabled = false;
            _mpb = new MaterialPropertyBlock();
            return true;
        }

        private void Rebuild()
        {
            if (_count < 2) { _renderer.enabled = false; return; }
            var tr = transform;
            int n = 0;
            for (int s = 0; s < _count - 1; s++)
            {
                int i0 = Index(Mathf.Max(s - 1, 0)), i1 = Index(s), i2 = Index(s + 1), i3 = Index(Mathf.Min(s + 2, _count - 1));
                int steps = s == _count - 2 ? Subdivisions + 1 : Subdivisions;
                for (int k = 0; k < steps; k++)
                {
                    float f = (float)k / Subdivisions;
                    Vector3 tip = CatmullRom(_tip[i0], _tip[i1], _tip[i2], _tip[i3], f);
                    Vector3 inner = CatmullRom(_inner[i0], _inner[i1], _inner[i2], _inner[i3], f);
                    float age = Mathf.Lerp(_time - _born[i1], _time - _born[i2], f);
                    float u = Mathf.Clamp01(age / Lifetime);
                    _verts[n * 2] = tr.InverseTransformPoint(inner);
                    _verts[n * 2 + 1] = tr.InverseTransformPoint(tip);
                    _uvs[n * 2] = new Vector2(u, 0f);
                    _uvs[n * 2 + 1] = new Vector2(u, 1f);
                    n++;
                }
            }
            // Collapse unused points onto the last one (degenerate triangles; the index buffer stays fixed).
            for (int i = n; i < MaxPoints; i++)
            {
                _verts[i * 2] = _verts[n * 2 - 2];
                _verts[i * 2 + 1] = _verts[n * 2 - 1];
                _uvs[i * 2] = _uvs[i * 2 + 1] = new Vector2(1f, 0f);
            }
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.RecalculateBounds();
            _mpb.SetColor(ColorId, _color);
            _mpb.SetVector(ParamsId, new Vector4(3f, 0.15f, 0f, 0f));
            _renderer.SetPropertyBlock(_mpb);
            _renderer.enabled = true;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>u = age (0 head, 1 tail), v = inner edge (0) to tip (1). White RGB, alpha carries the shape.</summary>
        private static Texture2D Gradient
        {
            get
            {
                if (_gradient != null) return _gradient;
                const int W = 32, H = 8;
                _gradient = new Texture2D(W, H, TextureFormat.RGBA32, false, true)
                {
                    name = "BladeTrailGradient", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                };
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (W - 1f), v = y / (H - 1f);
                    float age = (1f - u) * (1f - u);
                    float across = Mathf.Lerp(0.25f, 1f, v * v);
                    // A hot white edge along the tip line.
                    float hot = v > 0.8f ? 1f : 0f;
                    float a = age * across;
                    byte w = (byte)(255 * Mathf.Lerp(0.85f, 1f, hot));
                    px[y * W + x] = new Color32(w, w, w, (byte)(255 * Mathf.Clamp01(a)));
                }
                _gradient.SetPixels32(px);
                _gradient.Apply(false, true);
                return _gradient;
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }
    }
}
