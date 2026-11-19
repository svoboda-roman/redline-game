using UnityEngine;
using UnityEngine.Rendering;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Runtime material factory for the 2.5D prototype: URP Lit (opaque, alpha-clipped, emissive) set up with the
    /// keywords the Lit shader expects, and the Proto3D/Fx effect shader in additive, alpha-blend and opaque modes.
    /// </summary>
    /// <remarks>Prototype-only. Shader property names verified against the installed URP 17 Lit.shader.</remarks>
    public static class Proto3DMaterials
    {
        private static Shader _lit, _fx;
        private static Shader Lit => _lit != null ? _lit : (_lit = Shader.Find("Universal Render Pipeline/Lit"));
        private static Shader Fx => _fx != null ? _fx : (_fx = Shader.Find("Proto3D/Fx"));

        /// <summary>Opaque URP Lit. Any texture may be null. <paramref name="tiling"/> applies to all maps.</summary>
        public static Material Lit3D(string name, Color color, float smoothness, float metallic,
            Texture albedo = null, Texture normal = null, float normalScale = 1f, Texture metalGloss = null,
            Texture emission = null, Color emissionColor = default, Vector2? tiling = null)
        {
            var m = new Material(Lit) { name = name };
            m.SetColor("_BaseColor", color);
            if (albedo != null) m.SetTexture("_BaseMap", albedo);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", normalScale);
                m.EnableKeyword("_NORMALMAP");
            }
            if (metalGloss != null)
            {
                m.SetTexture("_MetallicGlossMap", metalGloss);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (emission != null || emissionColor.maxColorComponent > 0f)
            {
                if (emission != null) m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", emissionColor);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (tiling.HasValue)
            {
                m.SetTextureScale("_BaseMap", tiling.Value);
            }
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Alpha-clipped, double-sided URP Lit for sprite sheets (point-filtered atlas, normal sheet).</summary>
        public static Material Sprite(string name, Texture color, Texture normal, float smoothness = 0.25f)
        {
            var m = new Material(Lit) { name = name };
            m.SetTexture("_BaseMap", color);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            return m;
        }

        /// <summary>Effect material. <paramref name="mode"/>: 0 additive, 1 alpha blend, 2 opaque (sky).</summary>
        public static Material FxMat(string name, Texture tex, Color color, int mode, float fogAmount = 1f,
            float edgePow = 0f, int queueOffset = 0)
        {
            var m = new Material(Fx) { name = name };
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", color);
            m.SetFloat("_FogAmount", fogAmount);
            m.SetFloat("_EdgePow", edgePow);
            switch (mode)
            {
                case 0:
                    m.SetFloat("_SrcBlend", (float)BlendMode.One);
                    m.SetFloat("_DstBlend", (float)BlendMode.One);
                    m.SetFloat("_ZWrite", 0f);
                    m.renderQueue = (int)RenderQueue.Transparent + queueOffset;
                    break;
                case 1:
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f);
                    m.renderQueue = (int)RenderQueue.Transparent + queueOffset;
                    break;
                default:
                    m.SetFloat("_SrcBlend", (float)BlendMode.One);
                    m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    m.SetFloat("_ZWrite", 1f);
                    m.renderQueue = (int)RenderQueue.Geometry + queueOffset;
                    break;
            }
            return m;
        }

        /// <summary>Additive emissive-mask overlay for a sprite sheet (R/G/B mask channels tinted).</summary>
        public static Material EmissiveMask(string name, Texture mask, Color r, Color g, Color b)
        {
            var m = FxMat(name, mask, Color.white, 0, 1f, 0f, 10);
            m.SetFloat("_EmMask", 1f);
            m.SetColor("_TintR", r);
            m.SetColor("_TintG", g);
            m.SetColor("_TintB", b);
            return m;
        }
    }
}
