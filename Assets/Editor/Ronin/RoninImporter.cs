using System.IO;
using System.Linq;
using Redline.Game.Ronin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Redline.RoninEditor
{
    /// <summary>
    /// Import rules for the Tech Ronin under <c>Assets/Art3D/Ronin/</c> (built by <c>tools/art/ronin/build_ronin.py</c>).
    /// <list type="bullet">
    /// <item><c>ronin.fbx</c>: Humanoid, avatar created from the model (X Bot T-pose rest), no animation, no cameras
    /// or lights, transforms kept (the spring bones and the katana are found by name).</item>
    /// <item><c>Animations/*.fbx</c>: Humanoid, avatar created from the clip's own armature (identical to ronin.fbx),
    /// one clip named after the file; idle, run, fall and hang loop. Root rotation, height and XZ are baked into the
    /// pose (based on the original), so with root motion off every clip plays in place. The forward travel of the
    /// Mixamo run, slide, jump, jump attack, spin and attack_3 was already subtracted in Blender.</item>
    /// </list>
    /// </summary>
    public sealed class RoninImporter : AssetPostprocessor
    {
        /// <summary>Folder handled by this importer.</summary>
        public const string Root = "Assets/Art3D/Ronin/";
        /// <summary>The character model.</summary>
        public const string ModelPath = Root + "ronin.fbx";
        private static readonly string[] Loops = { "idle", "run", "sprint", "fall", "hang" };

        /// <summary>Bump to reimport every ronin FBX when the rules change.</summary>
        public override uint GetVersion() => 4;

        /// <summary>Palette textures written by build_ronin.py: point-sampled, no mips, uncompressed.</summary>
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root + "Materials/Ronin_Palette_")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = !assetPath.EndsWith("_MS.png");
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root) || !assetPath.EndsWith(".fbx")) return;
            var mi = (ModelImporter)assetImporter;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.optimizeGameObjects = false;
            mi.animationType = ModelImporterAnimationType.Human;
            if (assetPath == ModelPath)
            {
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                return;
            }
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            // Own avatar per clip: the clips carry the exact ronin armature in the same X Bot T-pose rest, so muscle-space
            // retargeting is 1:1. (CopyFromOther fails: the armature-only FBX loses the "Armature" root node that
            // ronin.fbx keeps, so Unity reports a transform-hierarchy mismatch.)
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Root + "Animations/")) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips.Length == 0) return;
            var c = clips[0];
            c.name = Path.GetFileNameWithoutExtension(assetPath);
            c.loopTime = Loops.Contains(c.name);
            c.lockRootRotation = true;
            c.lockRootHeightY = true;
            c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = true;
            c.keepOriginalPositionY = true;
            c.keepOriginalPositionXZ = true;
            mi.clipAnimations = new[] { c };
        }
    }

    /// <summary>
    /// Generates the ronin's URP materials and the build-safe <see cref="RoninAssets"/> bundle, and renders the
    /// evidence captures in batch mode (edit mode, own scene, nothing saved).
    /// </summary>
    /// <example>
    /// REDLINE_RONIN_CAPTURE=/abs/dir Unity -batchmode -projectPath . -executeMethod Redline.RoninEditor.RoninSetup.CaptureFromCommandLine
    /// </example>
    public static class RoninSetup
    {
        private const string MatDir = RoninImporter.Root + "Materials/";
        private const string AssetsPath = RoninImporter.Root + "Resources/Ronin/RoninAssets.asset";
        private const string CaptureDir = RoninImporter.Root + "Capture/";

        /// <summary>Ronin_Atlas emission multiplier (eyes, cyber lines); equals RoninView.AtlasEmissionScale. build_ronin.py
        /// still writes the palette for EMIT_SCALE 7: the cyber-line cell of Ronin_Palette_Emit.png was halved by hand
        /// when this went to 14, so re-running the Blender export needs EMIT_SCALE 14 and the cyber emission halved.</summary>
        public const float EmitScale = Redline.Game.Ronin.RoninView.AtlasEmissionScale;
        private const string PaletteBase = MatDir + "Ronin_Palette_Base.png";
        private const string PaletteMS = MatDir + "Ronin_Palette_MS.png";
        private const string PaletteEmit = MatDir + "Ronin_Palette_Emit.png";

        // Three materials, three draw calls: the body atlas (all flat colours from the palette textures),
        // the hat rim band and the katana (both tinted at runtime by RoninView.SetTierColor).
        // name, base colour (sRGB), metallic, smoothness, emission (linear HDR), uses the palette textures
        private static readonly (string name, Color baseColor, float metal, float smooth, Color emission, bool palette)[] Table =
        {
            ("Ronin_Atlas", Color.white,                     0f,   1f,   new Color(EmitScale, EmitScale, EmitScale), true),
            ("Ronin_Rim",   new Color32(30, 60, 70, 255),    0f,   0.5f, new Color(0.64f, 4f, 4.8f), false),
            ("Ronin_Blade", new Color32(190, 205, 215, 255), 1f,   1f,   new Color(0.4f, 2.5f, 3f), true),
        };

        private static readonly (RoninClip clip, string file)[] ClipFiles =
        {
            (RoninClip.Idle, "idle"), (RoninClip.Run, "run"), (RoninClip.SlideAttack, "slide_attack"), (RoninClip.Jump, "jump"),
            (RoninClip.Fall, "fall"), (RoninClip.JumpAttack, "jump_attack"), (RoninClip.Attack1, "attack_1"),
            (RoninClip.Attack2, "attack_2"), (RoninClip.Attack3, "attack_3"), (RoninClip.SpinAttack, "spin_attack"),
            (RoninClip.Hurt, "hurt"), (RoninClip.Death, "death"), (RoninClip.Hang, "hang"),
            (RoninClip.Sprint, "sprint"), (RoninClip.Land, "land"), (RoninClip.RunAttack1, "run_attack_1"), (RoninClip.RunAttack2, "run_attack_2"),
        };

        /// <summary>Creates or updates the materials and <c>RoninAssets.asset</c>; returns the bundle.</summary>
        [MenuItem("Redline/Ronin/Rebuild Assets")]
        public static RoninAssets EnsureAssets()
        {
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(Path.GetDirectoryName(AssetsPath));
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mats = new Material[Table.Length];
            for (int i = 0; i < Table.Length; i++)
            {
                var e = Table[i];
                string path = MatDir + e.name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(shader) { name = e.name }; AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_BaseColor", e.baseColor);
                m.SetFloat("_Metallic", e.metal);
                m.SetFloat("_Smoothness", e.smooth);
                if (e.palette)
                {
                    m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PaletteBase));
                    m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PaletteMS));
                    m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PaletteEmit));
                    m.SetFloat("_SmoothnessTextureChannel", 0f);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                bool emissive = e.emission.maxColorComponent > 0f;
                m.SetColor("_EmissionColor", e.emission);
                if (emissive) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
                // URP's validation derives _EMISSION from AnyEmissive in these flags: None would strip the keyword on import.
                m.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                EditorUtility.SetDirty(m);
                mats[i] = m;
            }
            // A freshly created .mat is re-imported on save, which can drop keywords set before the import; apply
            // the emission keyword again on the saved assets.
            AssetDatabase.SaveAssets();
            for (int i = 0; i < Table.Length; i++)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + Table[i].name + ".mat");
                if (m == null) continue;
                if (Table[i].emission.maxColorComponent > 0f) m.EnableKeyword("_EMISSION");
                if (Table[i].palette) m.EnableKeyword("_METALLICSPECGLOSSMAP");
                EditorUtility.SetDirty(m);
                mats[i] = m;
            }
            var bundle = AssetDatabase.LoadAssetAtPath<RoninAssets>(AssetsPath);
            if (bundle == null) { bundle = ScriptableObject.CreateInstance<RoninAssets>(); AssetDatabase.CreateAsset(bundle, AssetsPath); }
            bundle.Model = AssetDatabase.LoadAssetAtPath<GameObject>(RoninImporter.ModelPath);
            bundle.Materials = mats;
            bundle.Clips = new AnimationClip[ClipFiles.Length];
            foreach (var (clip, file) in ClipFiles)
            {
                bundle.Clips[(int)clip] = AssetDatabase.LoadAllAssetsAtPath(RoninImporter.Root + "Animations/" + file + ".fbx")
                    .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (bundle.Clips[(int)clip] == null) Debug.LogWarning("RONIN missing clip " + file);
            }
            EditorUtility.SetDirty(bundle);
            AssetDatabase.SaveAssets();
            return bundle;
        }

        private static UniversalRenderPipelineAsset EnsurePipeline()
        {
            string path = CaptureDir + "RoninCapture_URP.asset";
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (existing != null) return existing;
            Directory.CreateDirectory(CaptureDir);
            var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
            rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            rd.renderingMode = RenderingMode.ForwardPlus;
            AssetDatabase.CreateAsset(rd, CaptureDir + "RoninCapture_Renderer.asset");
            ResourceReloader.ReloadAllNullIn(rd, "Packages/com.unity.render-pipelines.universal");
            var asset = UniversalRenderPipelineAsset.Create(rd);
            asset.supportsHDR = true;
            asset.msaaSampleCount = 1;
            asset.shadowDistance = 40f;
            AssetDatabase.CreateAsset(asset, path);
            var so = new SerializedObject(asset);
            so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>
        /// Batch entry for the in-world evidence: builds assets, then enters Play Mode where
        /// <see cref="RoninWorldCapture"/> renders ronin-v2-*.png into $REDLINE_RONIN_WORLD inside a Loop01 arena.
        /// </summary>
        public static void CaptureWorldFromCommandLine()
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(RoninWorldCapture.EnvVar)))
            {
                Debug.LogError("RONIN_FAIL set " + RoninWorldCapture.EnvVar);
                EditorApplication.Exit(2);
                return;
            }
            AssetDatabase.Refresh();
            if (System.Environment.GetEnvironmentVariable("REDLINE_RONIN_REIMPORT") == "1")
            {
                AssetDatabase.ImportAsset(RoninImporter.ModelPath, ImportAssetOptions.ForceUpdate);
                foreach (var f in Directory.GetFiles(RoninImporter.Root + "Animations", "*.fbx"))
                    AssetDatabase.ImportAsset(f.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
            }
            EnsureAssets();
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Batch entry: builds assets, renders ronin-*.png into $REDLINE_RONIN_CAPTURE, exits.</summary>
        public static void CaptureFromCommandLine()
        {
            string dir = System.Environment.GetEnvironmentVariable("REDLINE_RONIN_CAPTURE");
            int code = 0;
            var prev = QualitySettings.renderPipeline;
            try
            {
                if (string.IsNullOrEmpty(dir)) throw new System.Exception("set REDLINE_RONIN_CAPTURE");
                Directory.CreateDirectory(dir);
                if (System.Environment.GetEnvironmentVariable("REDLINE_RONIN_REIMPORT") == "1")
                {
                    AssetDatabase.ImportAsset(RoninImporter.ModelPath, ImportAssetOptions.ForceUpdate);
                    foreach (var f in Directory.GetFiles(RoninImporter.Root + "Animations", "*.fbx"))
                        AssetDatabase.ImportAsset(f.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
                }
                var bundle = EnsureAssets();
                var avatar = AssetDatabase.LoadAllAssetsAtPath(RoninImporter.ModelPath).OfType<Avatar>().FirstOrDefault();
                Debug.Log("RONIN avatar " + (avatar != null ? avatar.isHuman + "/" + avatar.isValid : "null"));
                foreach (var c in bundle.Clips) if (c != null) Debug.Log($"RONIN clip {c.name} len={c.length:F2} loop={c.isLooping} human={c.humanMotion}");
                QualitySettings.renderPipeline = EnsurePipeline();
                RoninCaptureScene.Run(bundle, dir);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("RONIN_FAIL " + ex);
                code = 1;
            }
            finally
            {
                QualitySettings.renderPipeline = prev;
            }
            EditorApplication.Exit(code);
        }
    }

    /// <summary>Evidence scene: dark slate backdrop, warm key, cool rim, fog, 640x360 RT point-upscaled 2x.</summary>
    internal static class RoninCaptureScene
    {
        private const int W = 640, H = 360;
        private const float Dt = 1f / 60f;

        public static void Run(RoninAssets bundle, string dir)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var slate = new Color(0.018f, 0.02f, 0.026f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.08f, 0.1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.018f;
            RenderSettings.fogColor = slate;

            var key = new GameObject("key").AddComponent<Light>();
            key.type = LightType.Directional; key.color = new Color(1f, 0.78f, 0.55f); key.intensity = 1.6f;
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            var rim = new GameObject("rim").AddComponent<Light>();
            rim.type = LightType.Directional; rim.color = new Color(0.45f, 0.75f, 1f); rim.intensity = 2.2f;
            rim.transform.rotation = Quaternion.Euler(20f, 160f, 0f);

            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.006f, 0.0065f, 0.008f));   // dark wet deck
            floorMat.SetFloat("_Metallic", 0.2f);
            floorMat.SetFloat("_Smoothness", 0.82f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(200f, 1f, 60f);
            floor.transform.position = new Vector3(0f, -0.5f, 20f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.9f); bloom.threshold.Override(0.9f); bloom.scatter.Override(0.6f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var vol = new GameObject("volume").AddComponent<Volume>();
            vol.isGlobal = true; vol.sharedProfile = profile;

            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = slate;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            cam.allowHDR = true;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // Scale check.
            var probe = RoninView.Create(bundle, null);
            probe.Tick(Dt);
            var b = new Bounds(probe.transform.position, Vector3.zero);
            foreach (var r in probe.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            Debug.Log("RONIN bounds " + b.size.ToString("F2") + " center " + b.center.ToString("F2"));
            Object.DestroyImmediate(probe.gameObject);

            // Turnaround: front, right side (game facing), back, 3/4.
            float[] yaws = { 180f, 90f, 0f, 145f };
            var views = new RoninView[4];
            for (int i = 0; i < 4; i++)
            {
                views[i] = Spawn(bundle, new Vector3(-3.3f + i * 2.2f, 0f, 0f));
                views[i].CameraTurn = 0f;
            }
            Simulate(views, 1.5f, (v, i, t) => v.Input = new RoninInput { Grounded = true, Facing = 1 });
            for (int i = 0; i < 4; i++) views[i].Model.localRotation = Quaternion.Euler(0f, yaws[i], 0f);
            Shot(cam, dir, "ronin-turnaround.png", new Vector3(0f, 1.05f, 0f), 11.5f);
            Clear(views);

            // Closeup idle, game facing.
            var c = Spawn(bundle, Vector3.zero);
            Simulate(new[] { c }, 1.2f, (v, i, t) => v.Input = new RoninInput { Grounded = true, Facing = 1 });
            Shot(cam, dir, "ronin-closeup.png", new Vector3(0f, 1.25f, 0f), 3.6f);
            Clear(new[] { c });

            // Poses, each mid-motion and moving at its gameplay speed so the coat reacts.
            // idle, run, slide attack, slash, jump attack, hang
            var poses = new RoninView[6];
            float[] speed = { 0f, 6.5f, 9f, 0f, 4f, 0f };
            float[] dur = { 1.2f, 1.25f, 0.55f, 0.55f, 0.75f, 1.0f };
            for (int i = 0; i < 6; i++) poses[i] = Spawn(bundle, Vector3.zero);
            for (int i = 0; i < 6; i++)
            {
                var slot = new Vector3(-5.5f + i * 2.2f, i == 4 ? 0.6f : i == 5 ? 1.2f : 0f, 0f);
                var start = slot - new Vector3(speed[i] * dur[i], 0f, 0f);
                poses[i].transform.position = start;
                poses[i].ResetSprings();
                int steps = Mathf.RoundToInt(dur[i] / Dt);
                for (int s = 1; s <= steps; s++)
                {
                    float t = s * Dt;
                    var input = new RoninInput { Grounded = true, Facing = 1, Speed = speed[i] };
                    if (i == 2) input.Sliding = true;
                    if (i == 3) { input.Attacking = true; input.StrikeId = 0; }
                    if (i == 4) { input.Attacking = true; input.Grounded = false; }
                    if (i == 5) { input.Hooked = true; input.Grounded = false; }
                    poses[i].Input = input;
                    var p = Vector3.Lerp(start, slot, t / dur[i]);
                    if (i == 5) p = slot + new Vector3(Mathf.Sin(t * 3f) * 0.6f, -Mathf.Abs(Mathf.Sin(t * 3f)) * 0.1f, 0f);
                    poses[i].transform.position = p;
                    poses[i].Tick(Dt);
                }
            }
            for (int i = 0; i < 6; i++) Debug.Log("RONIN pose " + i + " state " + poses[i].Current);
            Shot(cam, dir, "ronin-poses.png", new Vector3(0f, 1.0f, 0f), 16f);
            // Game-scale read check (~55 px tall).
            Shot(cam, dir, "ronin-poses-gamescale.png", new Vector3(0f, 1.0f, 0f), 23f);
            Clear(poses);
        }

        private static RoninView Spawn(RoninAssets bundle, Vector3 pos)
        {
            var v = RoninView.Create(bundle, null);
            v.AutoTick = false;
            v.transform.position = pos;
            v.ResetSprings();
            return v;
        }

        private static void Simulate(RoninView[] views, float seconds, System.Action<RoninView, int, float> input)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int s = 0; s < steps; s++)
                for (int i = 0; i < views.Length; i++) { input(views[i], i, s * Dt); views[i].Tick(Dt); }
        }

        private static void Clear(RoninView[] views)
        {
            foreach (var v in views) if (v != null) Object.DestroyImmediate(v.gameObject);
        }

        private static void Shot(Camera cam, string dir, string file, Vector3 target, float distance)
        {
            cam.transform.position = target + new Vector3(0f, 0.12f * distance, -distance);
            cam.transform.LookAt(target);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Point, antiAliasing = 1 };
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            var src = tex.GetPixels32();
            var dst = new Color32[W * H * 4];
            for (int y = 0; y < H * 2; y++)
                for (int x = 0; x < W * 2; x++)
                    dst[y * W * 2 + x] = src[(y / 2) * W + x / 2];
            var big = new Texture2D(W * 2, H * 2, TextureFormat.RGBA32, false);
            big.SetPixels32(dst);
            big.Apply();
            File.WriteAllBytes(Path.Combine(dir, file), big.EncodeToPNG());
            Object.DestroyImmediate(tex); Object.DestroyImmediate(big);
            rt.Release(); Object.DestroyImmediate(rt);
            Debug.Log("RONIN shot " + file);
        }
    }
}
