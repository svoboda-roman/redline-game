using System.Collections;
using System.Collections.Generic;
using System.IO;
using Redline.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Redline.Game.Render3D.Actors3D
{
    /// <summary>
    /// Dev-only evidence capture of the 3D actor renderers. Active only when the environment variable
    /// <c>REDLINE_ACTORS3D_CAPTURE</c> names an output directory: replaces the running game with the prototype
    /// Loop01 scene (<see cref="Proto3DScene"/>, used read-only), switches to the 3D pipeline via <see cref="Render3DMode"/> for
    /// the session, stages enemies, effects and orbs without a sim, renders 640×360 into a point-filtered RT,
    /// upscales to 1920×1080 and writes actors3d-*.png, restores the pipeline and exits.
    /// </summary>
    /// <remarks>
    /// Never runs in normal play. Entry (editor):
    /// <c>REDLINE_ACTORS3D_CAPTURE=/abs/dir Unity -batchmode -projectPath . -executeMethod Redline.Game.Render3D.Actors3D.Actors3DCapture.RunFromCommandLine</c>
    /// </remarks>
    public sealed class Actors3DCapture : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_ACTORS3D_CAPTURE";
        private const int OutW = 1920, OutH = 1080, LowW = 640, LowH = 360;
        private static string OutDir => System.Environment.GetEnvironmentVariable(EnvVar);

        private bool _prevAsync = true;
        private readonly List<Enemy> _enemies = new List<Enemy>();
        private readonly List<Orb> _orbs = new List<Orb>();
        private readonly List<Vector2> _feet = new List<Vector2>();
        private readonly List<SimEvent> _noEvents = new List<SimEvent>();
        private Enemy3DRenderer _enemyR;
        private Effects3DRenderer _fx;
        private Orb3DRenderer _orbR;
        private RedlineConfig _cfg;
        private TileMap _map;
        private Camera _cam;
        private object _key = new object();

#if UNITY_EDITOR
        /// <summary>Command-line entry: enters Play Mode; the capture boots itself from the environment variable.</summary>
        public static void RunFromCommandLine()
        {
            if (string.IsNullOrEmpty(OutDir)) { Debug.LogError("ACTORS3D: set " + EnvVar); UnityEditor.EditorApplication.Exit(2); return; }
            UnityEditor.AssetDatabase.Refresh();
            UnityEditor.EditorApplication.EnterPlaymode();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (string.IsNullOrEmpty(OutDir)) return;
            var go = new GameObject("Actors3DCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<Actors3DCapture>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(OutDir);
            yield return null;
            yield return null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Destroy(root);
            yield return null;
#if UNITY_EDITOR
            Render3DMode.Enable();
            _prevAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            Debug.Log("ACTORS3D pipeline " + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "null"));
#endif
            yield return null;
            Transform world;
            try
            {
                Random.InitState(7);
                _cfg = new RedlineConfig();
                _map = TileMap.Parse(Resources.Load<TextAsset>("Loop01").text);
                var scene = new Proto3DScene(_map, 0f, 96f, new Proto3DScene.Look());
                scene.ReflectionProbe(new Vector3(36f, 4f, 0f));
                world = scene.Root;
                BuildVolume();
                _cam = new GameObject("Actors3DCamera").AddComponent<Camera>();
                _fx = new Effects3DRenderer(world, _cam);
                _enemyR = new Enemy3DRenderer(world, _fx);
                _orbR = new Orb3DRenderer(world, 8);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("ACTORS3D_FAIL " + ex);
                Finish(1);
                yield break;
            }
            yield return Shots();
        }

        private Enemy Add(EnemyKind kind, float x, float y, int facing)
        {
            var e = new Enemy(kind, new Vec2(x, y), _cfg) { Facing = facing, ShieldDir = facing };
            e.X = x; e.Y = y; e.Vx = 0f;
            _enemies.Add(e);
            _feet.Add(new Vector2(x, y));
            return e;
        }

        private void Tick(float dt)
        {
            _enemyR.SyncRaw(_key, _enemies, _noEvents, _cfg, _map, 30f, new Vector2(30f, 1f), 1, _feet, dt);
            _orbR.SyncRaw(_orbs, null, Time.time);
            _fx.Update(dt);
        }

        private void ClearStage()
        {
            _enemies.Clear(); _feet.Clear(); _orbs.Clear();
            _key = new object();
        }

        private IEnumerator Shots()
        {
            const float floor = 1f;
            // 1) Lineup: all seven kinds under the lamps.
            ClearStage();
            float x = 27.5f;
            foreach (var k in new[] { EnemyKind.Grunt, EnemyKind.Brute, EnemyKind.Shooter, EnemyKind.Shield, EnemyKind.Kamikaze, EnemyKind.Summoner, EnemyKind.Drone })
            {
                var e = Add(k, x, k == EnemyKind.Drone ? floor + 1.6f : floor, -1);
                if (k == EnemyKind.Drone) e.DroneState = DroneMode.Hover;
                x += k == EnemyKind.Brute ? 3.4f : 2.7f;
            }
            for (int i = 0; i < 20; i++) { Tick(1f / 60f); yield return null; }
            yield return Shot("actors3d-lineup.png", new Vector3(36f, 3.2f, 0f), 10.8f);

            // 2) Telegraphs: brute windup, kamikaze fuse, shooter aim.
            ClearStage();
            var brute = Add(EnemyKind.Brute, 31f, floor, 1);
            brute.Mode = BruteMode.Windup; brute.ModeTimer = _cfg.bruteWindupTime;
            var kami = Add(EnemyKind.Kamikaze, 36f, floor, -1);
            kami.Act = EnemyAct.Fuse; kami.ModeTimer = _cfg.kamikazeFuseTime * 0.35f;
            var shooter = Add(EnemyKind.Shooter, 40.5f, floor, -1);
            shooter.Act = EnemyAct.Windup; shooter.ModeTimer = _cfg.shooterWindupTime;
            for (int i = 0; i < 18; i++) { Tick(1f / 60f); yield return null; }
            yield return Shot("actors3d-telegraphs.png", new Vector3(35.8f, 2.6f, 0f), 6.8f);

            // 3) Effects: a hit spark + flash on a grunt, a kill cut-flash, the kamikaze blast, orbs.
            ClearStage();
            var hit = Add(EnemyKind.Grunt, 29.5f, floor, -1);
            hit.SinceHit = 0.02f;
            var tierGold = new Color(1f, 0.84f, 0.34f);
            _fx.Sparks(new Vector2(29.5f, floor + 1f), tierGold, 18, 9f);
            _fx.Debris(new Vector2(33.5f, floor + 0.9f), new Color(1f, 0.55f, 0.15f), 9, 9f);
            _fx.Sparks(new Vector2(33.5f, floor + 0.9f), tierGold, 22, 12f);
            _fx.Pop(new Vector2(39f, floor + 0.8f), new Color(1f, 0.45f, 0.2f), _cfg.kamikazeBlastRadius * 2f, 0.35f);
            _fx.Debris(new Vector2(39f, floor + 0.8f), new Color(1f, 0.3f, 0.2f), 14, 9f);
            _fx.Dust(new Vector2(31.5f, floor), 8, 0f);
            float ox = 42.5f;
            foreach (var k in new[] { OrbKind.Health, OrbKind.Speed, OrbKind.Hook })
            {
                _orbs.Add(new Orb { Kind = k, X = ox, Y = floor + 0.2f, Width = 0.6f, Height = 0.6f, TimeLeft = 10f, Age = ox });
                ox += 1.3f;
            }
            for (int i = 0; i < 9; i++) { Tick(1f / 60f); yield return null; }
            yield return Shot("actors3d-effects.png", new Vector3(36.5f, 2.6f, 0f), 7.6f);
            Finish(0);
        }

        private void BuildVolume()
        {
            var v = new GameObject("Actors3DVolume").AddComponent<Volume>();
            v.isGlobal = true;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(0.85f); bloom.intensity.Override(1.4f); bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(1f, 0.85f, 0.75f));
            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.6f); ca.contrast.Override(18f); ca.saturation.Override(-5f);
            var vg = p.Add<Vignette>(true); vg.intensity.Override(0.3f); vg.smoothness.Override(0.45f);
            v.sharedProfile = p;
        }

        private IEnumerator Shot(string file, Vector3 target, float frameHeight)
        {
            const float fov = 30f, pitch = 6f;
            var cam = _cam;
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
            var rt = new RenderTexture(LowW, LowH, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            cam.targetTexture = rt;
            cam.aspect = (float)LowW / LowH;
            for (int i = 0; i < 8; i++) yield return null;
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var low = new Texture2D(LowW, LowH, TextureFormat.RGB24, false);
            low.ReadPixels(new Rect(0, 0, LowW, LowH), 0, 0);
            low.Apply();
            RenderTexture.active = null;
            var s = low.GetPixels32();
            var d = new Color32[OutW * OutH];
            for (int y = 0; y < OutH; y++)
            {
                int sy = y * LowH / OutH;
                for (int x = 0; x < OutW; x++) d[y * OutW + x] = s[sy * LowW + x * LowW / OutW];
            }
            var big = new Texture2D(OutW, OutH, TextureFormat.RGBA32, false);
            big.SetPixels32(d); big.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, file), big.EncodeToPNG());
            Debug.Log("ACTORS3D_SHOT " + file);
            cam.targetTexture = null;
            Destroy(low); Destroy(big);
            rt.Release(); Destroy(rt);
        }

        private void Finish(int code)
        {
            Render3DMode.Disable();
#if UNITY_EDITOR
            UnityEditor.ShaderUtil.allowAsyncCompilation = _prevAsync;
#endif
            Debug.Log("ACTORS3D_DONE " + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit();
#endif
        }
    }
}
