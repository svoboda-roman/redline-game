using UnityEngine;
using UnityEngine.Rendering;

namespace Redline.Game.Render3D.Actors3D
{
    /// <summary>
    /// Shared GPU resources of the 3D actor renderers (<see cref="Enemy3DRenderer"/>, <see cref="Effects3DRenderer"/>,
    /// <see cref="Orb3DRenderer"/>): the unit quad mesh, the two custom shaders (loaded from
    /// <c>Assets/Render3D/Actors3D/Resources/Actors3D/</c>, so builds include them), the effect materials and the
    /// URP Lit sprite materials. Everything is created once and cached; nothing here allocates per frame.
    /// </summary>
    /// <remarks>
    /// Main-thread only. Statics are cleared before each Play (the project skips domain reloads).
    /// Sim (x, y) is world (x, y) on the play plane z = 0; the camera looks down +z, so "toward the camera" is -z.
    /// </remarks>
    public static class Actors3DAssets
    {
        /// <summary>Rendering layer the world renderer's character back-rim light affects (bit 1, "Characters").</summary>
        public const uint CharacterLayer = 1u << 1;

        private static Mesh _quad;
        private static Shader _lit, _overlay, _fx, _dither;
        private static Material _fxAdd, _fxAlpha, _fxAddTop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _quad = null;
            _lit = _overlay = _fx = _dither = null;
            _fxAdd = _fxAlpha = _fxAddTop = null;
        }

        /// <summary>
        /// Unit quad centred on the origin in the XY plane, facing the camera (normal -z), UV (0,0) bottom-left.
        /// Tangent (1,0,0) with w = -1 so URP's bitangent (cross(n, t) * w) is +y: the sprite normal sheets'
        /// R = +X right, G = +Y up convention then lights correctly, also when the quad is mirrored by a negative
        /// x scale (URP folds the odd-negative-scale sign into the bitangent).
        /// </summary>
        public static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "Actors3DQuad" };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
                _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
                var n = new Vector3(0f, 0f, -1f);
                _quad.normals = new[] { n, n, n, n };
                var t = new Vector4(1f, 0f, 0f, -1f);
                _quad.tangents = new[] { t, t, t, t };
                _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                _quad.RecalculateBounds();
                _quad.UploadMeshData(true);
                return _quad;
            }
        }

        private static Shader Lit => _lit != null ? _lit : (_lit = Shader.Find("Universal Render Pipeline/Lit"));

        /// <summary>The character overlay shader (emissive, fill, rim, flash).</summary>
        public static Shader Overlay => _overlay != null ? _overlay : (_overlay = Load("Actors3D/SpriteOverlay"));

        private static Shader Fx => _fx != null ? _fx : (_fx = Load("Actors3D/Fx"));

        /// <summary>URP Lit copy whose fade is an ordered dither (corpses); falls back to Lit if missing.</summary>
        private static Shader Dither
        {
            get
            {
                if (_dither != null) return _dither;
                _dither = Load("Actors3D/SpriteLitDither");
                return _dither != null ? _dither : (_dither = Lit);
            }
        }

        /// <summary>Texture-alpha clip threshold of the sprite bodies (corpses scale it by their fade alpha).</summary>
        public const float DitherCutoff = 0.5f;

        private static Shader Load(string path)
        {
            var s = Resources.Load<Shader>(path);
            if (s == null) s = Shader.Find(path);
            if (s == null) Debug.LogError("Actors3D: shader " + path + " not found (Assets/Render3D/Actors3D/Resources/" + path + ".shader)");
            return s;
        }

        /// <summary>Additive (One, One), depth-tested, fogged effect material (sparks, glows, embers). GPU-instanced.</summary>
        public static Material FxAdditive => _fxAdd != null ? _fxAdd : (_fxAdd = FxMaterial("Actors3DFxAdd", true, false));

        /// <summary>Alpha-blended effect material (smoke, rain spray, orb tokens). GPU-instanced.</summary>
        public static Material FxAlpha => _fxAlpha != null ? _fxAlpha : (_fxAlpha = FxMaterial("Actors3DFxAlpha", false, false));

        /// <summary>Additive, ZTest Always, drawn last: the full-view flash quad in front of the camera.</summary>
        public static Material FxAdditiveTop => _fxAddTop != null ? _fxAddTop : (_fxAddTop = FxMaterial("Actors3DFxTop", true, true));

        private static Material FxMaterial(string name, bool additive, bool top)
        {
            var m = new Material(Fx) { name = name, enableInstancing = true };
            m.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZTest", (float)(top ? CompareFunction.Always : CompareFunction.LessEqual));
            m.renderQueue = (int)RenderQueue.Transparent + (top ? 500 : additive ? 20 : 10);
            return m;
        }

        /// <summary>
        /// URP Lit material for a sprite sheet: double-sided, point-sampled atlas with its normal sheet.
        /// <paramref name="fade"/> false: opaque alpha-clipped (cutoff 0.5), casts shadows, writes depth (live bodies).
        /// <paramref name="fade"/> true: the same opaque, depth-writing cutout on <c>Actors3D/SpriteLitDither</c> (corpses):
        /// <c>_BaseColor.a</c> set per renderer is a 4x4 ordered screen-door fade, so the body keeps its real depth for
        /// the depth of field while it fades. Per renderer also set <c>_Cutoff</c> = <see cref="DitherCutoff"/> * alpha
        /// (Lit multiplies the clip alpha by <c>_BaseColor.a</c>).
        /// </summary>
        /// <remarks>Property and keyword names checked against the installed URP 17 Lit.shader (as Proto3DMaterials does).</remarks>
        public static Material SpriteLit(string name, Texture color, Texture normal, bool fade)
        {
            var m = new Material(fade ? Dither : Lit) { name = name };
            m.SetTexture("_BaseMap", color);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.22f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_AlphaClip", 1f);
            m.EnableKeyword("_ALPHATEST_ON");
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            m.SetFloat("_Cutoff", DitherCutoff);
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            return m;
        }

        /// <summary>Creates a child quad renderer. <paramref name="character"/> adds the rim-light rendering layer.</summary>
        public static MeshRenderer CreateQuad(string name, Transform parent, Material material, bool shadows, bool character)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            if (character) r.renderingLayerMask |= CharacterLayer;
            r.enabled = false;
            return r;
        }
    }
}
