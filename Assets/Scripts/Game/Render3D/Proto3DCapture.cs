using System.Collections;
using System.IO;
using Redline.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Dev-only capture of the 2.5D (3D-pixelated) render prototype. Active only when the environment variable
    /// <c>REDLINE_PROTO3D_CAPTURE</c> names an output directory. Replaces the running game with the prototype scene
    /// of Loop01, swaps in the 3D pipeline asset (Assets/Render3D/Resources/Proto3D_URP.asset) for the session only,
    /// renders a perspective camera into a low-resolution point-filtered RenderTexture, upscales it with nearest
    /// sampling to 1920×1080 and writes proto3d-*.png, then restores the pipeline and exits.
    /// </summary>
    /// <remarks>Never runs in normal play. Entry: Redline.Proto3DEditor.Proto3DSetup.RunFromCommandLine.</remarks>
    public sealed class Proto3DCapture : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_PROTO3D_CAPTURE";
        private const int OutW = 1920, OutH = 1080;
        private static string Directory => System.Environment.GetEnvironmentVariable(EnvVar);

        private RenderPipelineAsset _prevPipeline;
        private Volume _volume;
        private DepthOfField _dof;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (string.IsNullOrEmpty(Directory)) return;
            var go = new GameObject("Proto3DCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<Proto3DCapture>();
        }

        private IEnumerator Start()
        {
            System.IO.Directory.CreateDirectory(Directory);
            yield return null;
            yield return null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Destroy(root);
            yield return null;
            var asset = Resources.Load<RenderPipelineAsset>(Render3DMode.PipelineResource);
            _prevPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = asset;
            Debug.Log("PROTO3D pipeline " + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "null"));
            yield return null;
            try
            {
                var map = TileMap.Parse(Resources.Load<TextAsset>("Loop01").text);
                var look = new Proto3DScene.Look();
                var scene = new Proto3DScene(map, 0f, 96f, look);
                scene.Foreground(26.6f, -9f, 13.4f);
                string art = Path.Combine(Application.dataPath, "Resources/Art/Characters");
                var cyan = new Color(0.4f, 2.5f, 3f);
                var samurai = new Proto3DSpriteActor(scene.Root, art, "Samurai", "samurai_idle_00", new Color(3f, 0.3f, 0.35f), cyan, cyan);
                samurai.Place(new Vector2(29.2f, 1f), 1);
                var grunt = new Proto3DSpriteActor(scene.Root, art, "Grunt", "grunt_idle_00", new Color(3f, 0.2f, 0.2f), new Color(3f, 0.3f, 0.2f), new Color(2.5f, 0.3f, 0.2f));
                grunt.Place(new Vector2(34.5f, 1f), -1);
                var shield = new Proto3DSpriteActor(scene.Root, art, "Shield", "shield_idle_00", new Color(3f, 0.2f, 0.2f), new Color(3f, 0.3f, 0.2f), new Color(2.5f, 0.3f, 0.2f));
                shield.Place(new Vector2(38.5f, 1f), -1);
                var brute = new Proto3DSpriteActor(scene.Root, art, "Brute", "brute_idle_00", new Color(3f, 0.2f, 0.2f), new Color(3f, 0.3f, 0.2f), new Color(2.5f, 0.3f, 0.2f));
                brute.Place(new Vector2(43f, 1f), -1);
                // Rim light on the samurai (cool, from his front-right).
                var rim = new GameObject("rim").AddComponent<Light>();
                rim.transform.SetParent(scene.Root, false);
                rim.transform.position = new Vector3(30.3f, 2.6f, -0.4f);
                rim.type = LightType.Point; rim.color = new Color(0.45f, 0.8f, 1f); rim.intensity = 4f; rim.range = 3f;
                scene.ReflectionProbe(new Vector3(32f, 4f, 0f));
                BuildVolume();
                StartCoroutine(Shots(scene));
            }
            catch (System.Exception ex)
            {
                Debug.LogError("PROTO3D_FAIL " + ex);
                Finish(1);
            }
        }

        private IEnumerator Shots(Proto3DScene scene)
        {
            // Let particles and the probe settle.
            for (int i = 0; i < 30; i++) yield return null;
            var wide = new Vector3(34f, 6.2f, 0f);
            yield return Shot("proto3d-640.png", 640, 360, wide, 30f, 6f, 13.5f);
            yield return Shot("proto3d-480.png", 480, 270, wide, 30f, 6f, 13.5f);
            File.Copy(Path.Combine(Directory, "proto3d-640.png"), Path.Combine(Directory, "proto3d-loop01.png"), true);
            yield return Shot("proto3d-closeup.png", 640, 360, new Vector3(28.6f, 2.9f, 0f), 30f, 5f, 6.2f);
            Compare();
            Finish(0);
        }

        private void BuildVolume()
        {
            var go = new GameObject("Proto3DVolume");
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(0.85f); bloom.intensity.Override(1.4f); bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(1f, 0.85f, 0.75f));
            _dof = p.Add<DepthOfField>(true);
            _dof.mode.Override(DepthOfFieldMode.Bokeh);
            _dof.focalLength.Override(150f); _dof.aperture.Override(2.2f);
            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.6f); ca.contrast.Override(18f); ca.saturation.Override(-5f);
            var smh = p.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.85f, 0.95f, 1.2f, -0.05f));
            smh.highlights.Override(new Vector4(1.15f, 1.0f, 0.85f, 0f));
            var vg = p.Add<Vignette>(true); vg.intensity.Override(0.38f); vg.smoothness.Override(0.45f);
            var fg = p.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(0.22f);
            _volume.sharedProfile = p;
        }

        private IEnumerator Shot(string file, int w, int h, Vector3 target, float fov, float pitch, float frameHeight)
        {
            var camGo = new GameObject("Proto3DCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 700f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            float dist = frameHeight * 0.5f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            var rot = Quaternion.Euler(pitch, 0f, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = target - rot * Vector3.forward * dist;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;
            _dof.focusDistance.Override(dist);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            for (int i = 0; i < 10; i++) yield return null;
            // Rough GPU+CPU timing: N renders then a 1-px readback sync.
            var tex1 = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int N = 20;
            for (int i = 0; i < N; i++) cam.Render();
            RenderTexture.active = rt;
            tex1.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            RenderTexture.active = null;
            sw.Stop();
            cam.Render();
            RenderTexture.active = rt;
            var low = new Texture2D(w, h, TextureFormat.RGB24, false);
            low.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            low.Apply();
            RenderTexture.active = null;
            var big = Upscale(low, OutW, OutH);
            File.WriteAllBytes(Path.Combine(Directory, file), big.EncodeToPNG());
            Debug.Log("PROTO3D_SHOT " + file + " " + w + "x" + h + " camDist=" + dist.ToString("F1") + " camPos=" + cam.transform.position
                      + " msPerFrame=" + (sw.Elapsed.TotalMilliseconds / N).ToString("F2"));
            cam.targetTexture = null;
            Destroy(camGo); Destroy(low); Destroy(big); Destroy(tex1);
            rt.Release(); Destroy(rt);
            yield return null;
        }

        private static Texture2D Upscale(Texture2D src, int w, int h)
        {
            var s = src.GetPixels32();
            var d = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int sy = y * src.height / h;
                for (int x = 0; x < w; x++) d[y * w + x] = s[sy * src.width + x * src.width / w];
            }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(d); t.Apply();
            return t;
        }

        private void Compare()
        {
            string cur = Path.Combine(Application.dataPath, "../production/qa/evidence/final-loop01.png");
            if (!File.Exists(cur)) { Debug.LogWarning("PROTO3D no final-loop01.png"); return; }
            var a = new Texture2D(2, 2); a.LoadImage(File.ReadAllBytes(cur));
            var b = new Texture2D(2, 2); b.LoadImage(File.ReadAllBytes(Path.Combine(Directory, "proto3d-loop01.png")));
            int h = 720, w = 1280;
            var a2 = Upscale(a, w, h); var b2 = Upscale(b, w, h);
            var o = new Texture2D(w * 2 + 8, h, TextureFormat.RGBA32, false);
            var px = new Color32[o.width * h];
            var pa = a2.GetPixels32(); var pb = b2.GetPixels32();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) { px[y * o.width + x] = pa[y * w + x]; px[y * o.width + w + 8 + x] = pb[y * w + x]; }
                for (int x = w; x < w + 8; x++) px[y * o.width + x] = new Color32(255, 255, 255, 255);
            }
            o.SetPixels32(px); o.Apply();
            File.WriteAllBytes(Path.Combine(Directory, "proto3d-vs-current.png"), o.EncodeToPNG());
        }

        private void Finish(int code)
        {
            if (_prevPipeline != null || QualitySettings.renderPipeline != null) QualitySettings.renderPipeline = _prevPipeline;
            Debug.Log("PROTO3D_DONE " + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit();
#endif
        }
    }
}
