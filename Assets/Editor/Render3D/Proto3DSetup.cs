using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Redline.Proto3DEditor
{
    /// <summary>
    /// Editor entry for the 2.5D render prototype. Creates (once) a separate URP pipeline asset with a Universal
    /// (forward+) renderer under Assets/Render3D, leaving Assets/Settings/UniversalRP.asset and the 2D renderer
    /// untouched, then enters Play Mode where <c>Redline.Game.Render3D.Proto3DCapture</c> builds and captures.
    /// </summary>
    /// <example>
    /// REDLINE_PROTO3D_CAPTURE=/abs/dir Unity -batchmode -projectPath . -executeMethod Redline.Proto3DEditor.Proto3DSetup.RunFromCommandLine
    /// </example>
    public static class Proto3DSetup
    {
        private const string Dir = "Assets/Render3D";
        private const string RendererPath = Dir + "/Proto3D_Renderer.asset";
        private const string PipelinePath = Dir + "/Resources/Proto3D_URP.asset";

        /// <summary>Creates the prototype renderer and pipeline assets if missing; returns the pipeline asset.</summary>
        public static UniversalRenderPipelineAsset EnsureAssets()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (existing != null) return existing;
            Directory.CreateDirectory(Dir + "/Resources");
            var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
            rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            rd.renderingMode = RenderingMode.ForwardPlus;
            AssetDatabase.CreateAsset(rd, RendererPath);
            UnityEngine.Rendering.ResourceReloader.ReloadAllNullIn(rd, "Packages/com.unity.render-pipelines.universal");
            var asset = UniversalRenderPipelineAsset.Create(rd);
            asset.supportsHDR = true;
            asset.msaaSampleCount = 1;
            asset.renderScale = 1f;
            asset.mainLightShadowmapResolution = 2048;
            asset.additionalLightsShadowmapResolution = 4096;
            asset.shadowDistance = 60f;
            AssetDatabase.CreateAsset(asset, PipelinePath);
            // Read-only in the 6.3 API: set the serialized fields (names from the URP asset YAML).
            var so = new SerializedObject(asset);
            so.FindProperty("m_MainLightRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("PROTO3D created " + PipelinePath);
            return asset;
        }

        /// <summary>Command-line entry: ensures assets, then enters Play Mode (the capture boots itself).</summary>
        public static void RunFromCommandLine()
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("REDLINE_PROTO3D_CAPTURE")))
            {
                Debug.LogError("PROTO3D: set REDLINE_PROTO3D_CAPTURE");
                EditorApplication.Exit(2);
                return;
            }
            AssetDatabase.Refresh();
            EnsureAssets();
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// Command-line entry for the 3D level renderer evidence: ensures assets, then enters Play Mode where
        /// <c>Redline.Game.Render3D.World3DCapture</c> captures world3d-*.png into REDLINE_WORLD3D_CAPTURE.
        /// </summary>
        /// <example>
        /// REDLINE_WORLD3D_CAPTURE=/abs/dir Unity -batchmode -projectPath . -executeMethod Redline.Proto3DEditor.Proto3DSetup.RunWorld3DFromCommandLine
        /// </example>
        public static void RunWorld3DFromCommandLine()
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("REDLINE_WORLD3D_CAPTURE")))
            {
                Debug.LogError("WORLD3D: set REDLINE_WORLD3D_CAPTURE");
                EditorApplication.Exit(2);
                return;
            }
            AssetDatabase.Refresh();
            EnsureAssets();
            EditorApplication.EnterPlaymode();
        }
    }
}
