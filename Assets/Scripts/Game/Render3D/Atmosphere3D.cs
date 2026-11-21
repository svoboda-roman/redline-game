using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Replaced-style atmosphere layers for <see cref="Arena3DRenderer"/>, built as a handful of big cards plus
    /// one small dust system (art bible 2 and 8): a bright far glow band behind the near city rows (the far
    /// layer is the brightest large area, far bright to near dark), streaked god-ray cards leaning along the
    /// key light through the mid-ground structure, scrolling ground mist hugging the deck, a dark shade over
    /// the near deck, and slow dust or ash motes drifting in the key light.
    /// </summary>
    /// <remarks>
    /// Cost at 640×320: 5 draw calls (far band, rays, mist, near shade, dust), 14 quads, at most 140 particles,
    /// about 3 full-screen layers of transparent fill (well under the 3× overdraw budget). No post-process pass,
    /// so nothing is taken from the 1.5 ms post budget. Motes are 3-5 texels at the play plane (never under 2),
    /// fade in and out, and move at most ~8 texels per second, so they do not sparkle.
    /// Main thread only; everything created is appended to the caller's owned list for disposal.
    /// </remarks>
    /// <example>
    /// <code>
    /// Atmosphere3D.Build(followRoot, look, floorTop: 1f, mapHeight: map.Height, owned);
    /// </code>
    /// </example>
    public static class Atmosphere3D
    {
        /// <summary>Mist texture repeat length in world units (world-anchored scroll).</summary>
        public const float MistTile = 14f;

        /// <summary>Builds every layer under <paramref name="follow"/> (a root that follows the camera in X).</summary>
        public static void Build(Transform follow, Arena3DLook look, float floorTop, float mapHeight, List<Object> owned)
        {
            var root = new GameObject("Atmosphere").transform;
            root.SetParent(follow, false);
            var drift = root.gameObject.AddComponent<Atmosphere3DDrift>();
            T Own<T>(T o) where T : Object { owned.Add(o); return o; }

            // 1. Bright far layer: an unfogged additive band between city rows 64 and 92. The near rows silhouette
            //    against it; rows behind it wash out toward it (aerial perspective, the far step is the brightest).
            if (look.FarLift > 0.01f)
            {
                var bandTex = Own(GlowBand());
                var bandMat = Own(Proto3DMaterials.FxMat("farBand", bandTex, look.FarGlow * look.FarLift, 0, 0f, 0f, -25));
                var q = new Quads();
                q.Add(new Vector3(0f, floorTop + 14f, 90f), 700f, 120f, Color.white, 0f, 1f);
                q.Add(new Vector3(0f, floorTop + 4f, 62f), 400f, 40f, new Color(1f, 1f, 1f) * 0.35f, 0f, 1f);
                Go("farBand", root, Own(q.Build("farBand")), bandMat);
            }

            // 2. God rays: streaked additive cards leaning along the key direction, between the play plane and the
            //    z 14 veil, tall enough to run off the top of any level.
            if (look.ShaftStrength > 0.01f)
            {
                var rayTex = Own(RayTexture(look.Seed));
                var rayMat = Own(Proto3DMaterials.FxMat("godRays", rayTex, look.Moon * (0.32f * look.ShaftStrength), 0, 0.45f, 0f, -10));
                float lean = Mathf.Atan2(look.KeyDir.x, -look.KeyDir.y);
                var rot = Quaternion.Euler(0f, 0f, -lean * Mathf.Rad2Deg);
                float len = mapHeight + 24f, cy = mapHeight * 0.5f + 6f;
                float[] xs = { -11f, -3.5f, 3f, 9.5f, 16f }, zs = { 9f, 5f, 11f, 6.5f, 12.5f }, ws = { 5f, 3.2f, 6.5f, 2.6f, 4.4f };
                float[] bright = { 0.8f, 1f, 0.65f, 0.9f, 0.55f };
                var q = new Quads();
                for (int i = 0; i < xs.Length; i++)
                    q.AddRotated(new Vector3(xs[i], cy, zs[i]), ws[i], len, rot, Color.white * bright[i], i * 0.2f, 1f);
                Go("godRays", root, Own(q.Build("godRays")), rayMat);
                drift.Rays = rayMat;
                drift.RayColor = rayMat.GetColor("_Color");
            }

            // 3. Ground mist: three alpha cards hugging the deck (one in front of the play plane, two behind),
            //    world-anchored and drifting slowly sideways.
            if (look.MistStrength > 0.01f)
            {
                var mistTex = Own(MistTexture(look.Seed + 7));
                var tint = look.HazeTint * 1.1f; tint.a = 1f;
                var mistMat = Own(Proto3DMaterials.FxMat("groundMist", mistTex, tint, 1, 0.6f, 0f, -4));
                var q = new Quads();
                float s = look.MistStrength;
                q.Add(new Vector3(0f, floorTop + 0.9f, -1.6f), 80f, 2.2f, new Color(1f, 1f, 1f, 0.16f * s), 0.37f, 80f / MistTile);
                q.Add(new Vector3(0f, floorTop + 1.3f, 3.5f), 100f, 3.2f, new Color(1f, 1f, 1f, 0.32f * s), 0.71f, 100f / MistTile);
                q.Add(new Vector3(0f, floorTop + 1.8f, 10f), 140f, 4.5f, new Color(1f, 1f, 1f, 0.42f * s), 0.13f, 140f / MistTile);
                Go("groundMist", root, Own(q.Build("groundMist")), mistMat);
                drift.Mist = mistMat;
            }

            // 4. Near step: darken the deck in front of the play plane (bottom of the frame), below the floor line.
            if (look.NearShade > 0.01f)
            {
                var shadeTex = Own(ShadeTexture());
                var shadeMat = Own(Proto3DMaterials.FxMat("nearShade", shadeTex, new Color(0f, 0f, 0f, look.NearShade), 1, 0f, 0f, 20));
                var q = new Quads();
                q.Add(new Vector3(0f, floorTop - 6f, -7f), 90f, 12f, Color.white, 0f, 1f);
                Go("nearShade", root, Own(q.Build("nearShade")), shadeMat).layer = FloorReflection3D.HiddenLayer; // under the floor: not mirrored
            }

            // 5. Dust / ash motes in the key light: world-space particles, emitter kept at the camera's height.
            if (look.DustAmount > 0.01f)
            {
                var dustTex = Own(DotTexture());
                var dustMat = Own(Proto3DMaterials.FxMat("dust", dustTex, look.Moon * (0.9f * look.DustAmount), 0, 0.4f));
                var go = new GameObject("dust") { layer = FloorReflection3D.HiddenLayer };
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(0f, floorTop + 4f, 2.5f);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var m = ps.main;
                m.loop = true; m.prewarm = true;
                m.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f);
                m.startSpeed = 0f;
                m.startSize = new ParticleSystem.MinMaxCurve(0.075f, 0.11f);
                m.maxParticles = 140;
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission; em.rateOverTime = 12f * look.DustAmount;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(30f, 12f, 9f);
                var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
                Vector3 kd = look.KeyDir.normalized;
                v.x = new ParticleSystem.MinMaxCurve(kd.x * 0.08f + 0.03f, kd.x * 0.08f + 0.1f);
                v.y = new ParticleSystem.MinMaxCurve(-0.06f, 0.02f);
                v.z = new ParticleSystem.MinMaxCurve(0f, 0f); // all three axes must share the TwoConstants mode
                var n = ps.noise; n.enabled = true; n.strength = 0.06f; n.frequency = 0.25f; n.scrollSpeed = 0.05f; n.quality = ParticleSystemNoiseQuality.Low;
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.sharedMaterial = dustMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                ps.Play();
                drift.Dust = go.transform;
                drift.DustBaseY = floorTop + 4f;
            }
        }

        // ------------------------------------------------------------------ textures (additive ones carry the value in rgb)

        /// <summary>Soft horizontal glow band: brightest a third of the way up, side-faded. rgb = value.</summary>
        private static Texture2D GlowBand()
        {
            const int W = 64, H = 64;
            var t = NewTex(W, H, TextureWrapMode.Clamp);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float d = (v - 0.3f) / (v < 0.3f ? 0.14f : 0.32f);
                float val = Mathf.Exp(-d * d) * Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 6f);
                px[y * W + x] = new Color(val, val, val, val);
            }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        /// <summary>God-ray card: several soft streaks across U of varied width, bright at the top (v = 1), fading down. rgb = value.</summary>
        private static Texture2D RayTexture(int seed)
        {
            const int W = 64, H = 128;
            var rnd = new System.Random(seed * 31 + 5);
            var c = new float[6]; var w = new float[6]; var a = new float[6];
            for (int i = 0; i < 6; i++)
            {
                c[i] = 0.12f + 0.76f * (float)rnd.NextDouble();
                w[i] = 0.03f + 0.09f * (float)rnd.NextDouble();
                a[i] = 0.45f + 0.55f * (float)rnd.NextDouble();
            }
            var t = NewTex(W, H, TextureWrapMode.Clamp);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float across = 0.25f;
                for (int i = 0; i < 6; i++) { float d = (u - c[i]) / w[i]; across += a[i] * Mathf.Exp(-d * d); }
                across = Mathf.Min(1f, across) * Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 4f);
                float along = Mathf.Pow(v, 1.3f) * Mathf.SmoothStep(0f, 1f, (1f - v) * 8f);
                float val = across * along;
                px[y * W + x] = new Color(val, val, val, val);
            }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        /// <summary>Mist: horizontally tileable soft cloud, dense at the bottom, fading up. White, alpha = density.</summary>
        private static Texture2D MistTexture(int seed)
        {
            const int W = 128, H = 32;
            var t = NewTex(W, H, TextureWrapMode.Repeat);
            t.wrapModeV = TextureWrapMode.Clamp;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float ang = u * Mathf.PI * 2f;
                float n1 = Mathf.PerlinNoise(Mathf.Cos(ang) * 1.6f + seed + v * 1.5f, Mathf.Sin(ang) * 1.6f + seed * 0.37f);
                float n2 = Mathf.PerlinNoise(Mathf.Cos(ang) * 4f + seed * 1.7f, Mathf.Sin(ang) * 4f + v * 3f);
                float noise = Mathf.Clamp01(n1 * 0.7f + n2 * 0.45f - 0.1f);
                float vert = Mathf.Pow(1f - v, 1.4f) * Mathf.SmoothStep(0f, 1f, v * 5f + 0.35f);
                px[y * W + x] = new Color(1f, 1f, 1f, vert * (0.35f + 0.65f * noise));
            }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        /// <summary>Near shade: opaque at the bottom, fading to nothing at the top. White, alpha = coverage.</summary>
        private static Texture2D ShadeTexture()
        {
            const int H = 32;
            var t = NewTex(1, H, TextureWrapMode.Clamp);
            var px = new Color[H];
            for (int y = 0; y < H; y++) { float v = (y + 0.5f) / H; px[y] = new Color(1f, 1f, 1f, Mathf.Pow(1f - v, 1.2f)); }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        /// <summary>Mote: an 8×8 soft square (reads as a 2-4 texel speck at the low-res target). rgb = value.</summary>
        private static Texture2D DotTexture()
        {
            const int S = 8;
            var t = NewTex(S, S, TextureWrapMode.Clamp);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / S * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / S * 2f - 1f);
                float val = Mathf.Clamp01((1f - Mathf.Max(dx, dy)) * 2.2f);
                px[y * S + x] = new Color(val, val, val, val);
            }
            t.SetPixels(px); t.Apply(false);
            return t;
        }

        private static Texture2D NewTex(int w, int h, TextureWrapMode wrap) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false, true) { wrapMode = wrap, filterMode = FilterMode.Bilinear, name = "atmos" };

        private static GameObject Go(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>Camera-facing quads (normal -z) with per-quad vertex colour and a U offset/scale, merged into one mesh.</summary>
        private sealed class Quads
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector2> _u = new List<Vector2>();
            private readonly List<Color> _c = new List<Color>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<int> _t = new List<int>();

            public void Add(Vector3 c, float w, float h, Color col, float u0, float uScale) =>
                AddRotated(c, w, h, Quaternion.identity, col, u0, uScale);

            public void AddRotated(Vector3 c, float w, float h, Quaternion rot, Color col, float u0, float uScale)
            {
                int i = _v.Count;
                _v.Add(c + rot * new Vector3(-w / 2, -h / 2, 0)); _u.Add(new Vector2(u0, 0));
                _v.Add(c + rot * new Vector3(-w / 2, h / 2, 0)); _u.Add(new Vector2(u0, 1));
                _v.Add(c + rot * new Vector3(w / 2, h / 2, 0)); _u.Add(new Vector2(u0 + uScale, 1));
                _v.Add(c + rot * new Vector3(w / 2, -h / 2, 0)); _u.Add(new Vector2(u0 + uScale, 0));
                for (int k = 0; k < 4; k++) { _c.Add(col); _n.Add(Vector3.back); }
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _u); m.SetColors(_c);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }

    /// <summary>
    /// Per-frame motion of the <see cref="Atmosphere3D"/> layers: world-anchored, slowly drifting mist; a slow
    /// breathing of the god-ray intensity (±12 % over ~9 s); the dust emitter kept at the camera's height.
    /// No allocation per frame.
    /// </summary>
    public sealed class Atmosphere3DDrift : MonoBehaviour
    {
        /// <summary>Mist material (texture offset scrolled).</summary>
        public Material Mist;
        /// <summary>God-ray material (colour breathed) and its base colour.</summary>
        public Material Rays;
        /// <summary>Base god-ray colour.</summary>
        public Color RayColor;
        /// <summary>Dust emitter (follows the camera's height).</summary>
        public Transform Dust;
        /// <summary>Lowest emitter height (the deck).</summary>
        public float DustBaseY;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private Camera _cam;

        private void LateUpdate()
        {
            float t = Time.time;
            float camX = transform.position.x;
            if (Mist != null) Mist.mainTextureOffset = new Vector2(camX / Atmosphere3D.MistTile + t * 0.012f, 0f);
            if (Rays != null) Rays.SetColor(ColorId, RayColor * (0.94f + 0.12f * Mathf.Sin(t * 0.7f) * Mathf.Sin(t * 0.23f + 1.3f)));
            if (Dust != null)
            {
                if (_cam == null) _cam = FindWorldCamera();
                if (_cam != null)
                {
                    var p = Dust.position;
                    p.y = Mathf.Max(DustBaseY, _cam.transform.position.y);
                    Dust.position = p;
                }
            }
        }

        /// <summary>The perspective camera rendering into a texture (the Pixel3DCamera world camera).</summary>
        private static Camera FindWorldCamera()
        {
            var rig = Pixel3DCamera.Latest;
            if (rig != null && rig.WorldCamera != null) return rig.WorldCamera;
            foreach (var c in Camera.allCameras)
                if (!c.orthographic && c.targetTexture != null) return c;
            return null;
        }
    }
}
