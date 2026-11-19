#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Redline.Game.Render3D.Integration
{
    /// <summary>
    /// Editor tool: saves one URP Lit material asset per keyword combination the 3D renderers create at runtime
    /// (Proto3DMaterials.Lit3D / Sprite, Actors3DAssets.SpriteLit) under Assets/Render3D/Resources/ShaderVariants, so
    /// the player build keeps those shader_feature variants (_NORMALMAP, _EMISSION, _METALLICSPECGLOSSMAP,
    /// _ALPHATEST_ON, _SURFACE_TYPE_TRANSPARENT) instead of stripping them. Also keeps the corpse dither copy of Lit
    /// (<c>Actors3D/SpriteLitDither</c>, alpha-clipped with and without a normal map) and the live 3D ronin's
    /// <c>Redline/Ronin Lit Rim</c> (one keeper per Ronin_*.mat, with that material's keywords, as RoninView swaps them
    /// at runtime). The baked sprite is the default player view, but the live model stays selectable
    /// (Actors3DView.UseBakedRonin), so its variants must still ship. Re-run after adding a new combination.
    /// </summary>
    /// <example>Unity -batchmode -quit -projectPath . -executeMethod Redline.Game.Render3D.Integration.ShaderVariantKeeper.Create</example>
    public static class ShaderVariantKeeper
    {
        private const string Dir = "Assets/Render3D/Resources/ShaderVariants";
        // URP's material validation (run on import) re-derives the shader_feature keywords from the properties:
        // _NORMALMAP from a _BumpMap texture, _METALLICSPECGLOSSMAP from a _MetallicGlossMap texture, and _EMISSION from
        // globalIlluminationFlags containing AnyEmissive. So each keeper carries the property that implies its
        // keyword (textures that ship anyway), otherwise the saved asset loses the keyword and the variant is stripped.
        private const string NormalTex = "Assets/Resources/Art/Props/props_normal.png";
        private const string MetalTex = "Assets/Art3D/Ronin/Materials/Ronin_Palette_MS.png";
        private const string DitherShader = "Assets/Render3D/Actors3D/Resources/Actors3D/SpriteLitDither.shader";
        private const string RimShader = "Assets/Art3D/Ronin/Resources/Ronin/RoninLitRim.shader";
        private const string RoninMaterials = "Assets/Art3D/Ronin/Materials";

        /// <summary>Creates (or overwrites) the keeper materials.</summary>
        public static void Create()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) { Debug.LogError("VARIANTS: URP Lit not found"); return; }
            Directory.CreateDirectory(Dir);
            int n = 0;
            // Opaque Lit3D: every combination of normal map, metal-gloss map and emission.
            for (int mask = 0; mask < 8; mask++)
            {
                var m = new Material(lit);
                if ((mask & 1) != 0) Normal(m);
                if ((mask & 2) != 0) { m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MetalTex)); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if ((mask & 4) != 0)
                {
                    m.SetColor("_EmissionColor", Color.white);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    m.EnableKeyword("_EMISSION");
                }
                else m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                Save(m, "Lit_" + mask, ref n);
            }
            // Alpha-clipped sprites (opaque queue) and fading sprites (transparent), with and without a normal map.
            for (int normal = 0; normal < 2; normal++)
            {
                var cut = new Material(lit);
                cut.SetFloat("_AlphaClip", 1f);
                cut.SetFloat("_Cull", (float)CullMode.Off);
                cut.EnableKeyword("_ALPHATEST_ON");
                if (normal == 1) Normal(cut);
                cut.renderQueue = (int)RenderQueue.AlphaTest;
                Save(cut, "Cutout_" + normal, ref n);

                var fade = new Material(lit);
                fade.SetFloat("_AlphaClip", 1f);
                fade.SetFloat("_Surface", 1f);
                fade.SetFloat("_Cull", (float)CullMode.Off);
                fade.EnableKeyword("_ALPHATEST_ON");
                fade.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (normal == 1) Normal(fade);
                fade.SetOverrideTag("RenderType", "Transparent");
                fade.renderQueue = (int)RenderQueue.Transparent;
                Save(fade, "Fade_" + normal, ref n);
            }
            // Corpse sprites: Actors3DAssets.SpriteLit(fade: true) on the dither copy of Lit.
            var dither = AssetDatabase.LoadAssetAtPath<Shader>(DitherShader);
            if (dither == null) Debug.LogError("VARIANTS: " + DitherShader + " not found");
            else
                for (int normal = 0; normal < 2; normal++)
                {
                    var m = new Material(dither);
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cull", (float)CullMode.Off);
                    m.EnableKeyword("_ALPHATEST_ON");
                    if (normal == 1) Normal(m);
                    m.renderQueue = (int)RenderQueue.AlphaTest;
                    Save(m, "Dither_" + normal, ref n);
                }
            // Live ronin: the rim copy of each model material, keywords included (RoninView.RimShaderPath).
            var rim = AssetDatabase.LoadAssetAtPath<Shader>(RimShader);
            if (rim == null) Debug.LogError("VARIANTS: " + RimShader + " not found");
            else
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { RoninMaterials }))
                {
                    var src = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (src == null) continue;
                    var m = new Material(src) { shader = rim };
                    foreach (var k in src.shaderKeywords) m.EnableKeyword(k);
                    Save(m, "Rim_" + src.name, ref n);
                }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Dir }))
            {
                var saved = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                Debug.Log("VARIANTS " + saved.name + " = [" + string.Join(" ", saved.shaderKeywords) + "]");
            }
            Debug.Log("VARIANTS created " + n + " materials in " + Dir);
        }

        private static void Normal(Material m)
        {
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalTex));
            m.EnableKeyword("_NORMALMAP");
        }

        private static void Save(Material m, string name, ref int n)
        {
            string path = Dir + "/Keep_" + name + ".mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            n++;
        }
    }
}
#endif
