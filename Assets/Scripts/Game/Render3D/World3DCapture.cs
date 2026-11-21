using System.Collections;
using System.IO;
using System.Text;
using Redline.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Dev-only evidence capture for the 3D level renderer. Active only when the environment variable
    /// <c>REDLINE_WORLD3D_CAPTURE</c> names an output directory. Replaces the running game with
    /// <see cref="Arena3DRenderer"/> + <see cref="Pixel3DCamera"/> for Loop01/02/03 and Arena01, puts placeholder
    /// sprite characters on the play plane, renders 640×360 and presents it through the rig's point upscale into
    /// 1920×1080, writes world3d-*.png, a seam check, timings and framer coverage to world3d-report.txt, exits.
    /// </summary>
    /// <remarks>Never runs in normal play. Entry: Redline.Proto3DEditor.Proto3DSetup.RunWorld3DFromCommandLine.</remarks>
    public sealed class World3DCapture : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_WORLD3D_CAPTURE";
        private const int OutW = 1920, OutH = 1080;
        private static string Dir => System.Environment.GetEnvironmentVariable(EnvVar);
        private readonly StringBuilder _report = new StringBuilder();
        private Pixel3DCamera _rig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (string.IsNullOrEmpty(Dir)) return;
            var go = new GameObject("World3DCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<World3DCapture>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            yield return null;
            yield return null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) if (root != gameObject) Destroy(root);
            yield return null;
            if (!Render3DMode.Enable()) { Finish(1); yield break; }
            yield return null;
            var rigGo = new GameObject("Pixel3DRig");
            _rig = rigGo.AddComponent<Pixel3DCamera>();
            string only = System.Environment.GetEnvironmentVariable("REDLINE_WORLD3D_ONLY");
            foreach (var level in new[] { "Loop01", "Loop02", "Loop03", "Arena01" })
            {
                if (!string.IsNullOrEmpty(only) && !only.Contains(level)) continue;
                yield return Level(level);
            }
            if (string.IsNullOrEmpty(only) || only.Contains("seam")) yield return Seam();
            File.WriteAllText(Path.Combine(Dir, "world3d-report.txt"), _report.ToString());
            Debug.Log("WORLD3D_REPORT\n" + _report);
            Finish(0);
        }

        private IEnumerator Level(string level)
        {
            var map = TileMap.Parse(Resources.Load<TextAsset>(level).text);
            var look = Arena3DLook.For(level);
            var host = new GameObject("Host " + level).transform;
            Arena3DRenderer arena = null;
            try
            {
                arena = new Arena3DRenderer(host, map, look);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("WORLD3D_FAIL " + level + " " + ex);
                Finish(1);
                yield break;
            }
            _rig.ApplyLook(look);
            float sx = map.PlayerStart.X;
            Actors(host, map, sx);
            var target = new Vector2(sx + 6.5f, map.PlayerStart.Y + 1.0f);   // picture centre (the rig no longer offsets it)
            yield return Settle(arena, target);
            float ms = Timing();
            float fg = FramerCoverage(arena, target);
            yield return Save("world3d-" + level.ToLowerInvariant() + ".png");
            _report.AppendLine($"{level}: lamps={arena.LampCount} shadowed={arena.ShadowedLampCount} lights={arena.LightCount} msPerWorldFrame={ms:F2} framerCoverage={fg:P1} target={target}");
            arena.Dispose();
            Destroy(host.gameObject);
            yield return null;
        }

        /// <summary>
        /// Seam check on Loop01: frames at camX = W - ε (centre copy under the camera) and W + ε (the copies just
        /// jumped one level width). Rain off so the two frames are deterministic. Reports the mean absolute difference
        /// and saves the seam frame.
        /// </summary>
        private IEnumerator Seam()
        {
            var map = TileMap.Parse(Resources.Load<TextAsset>("Loop01").text);
            var look = Arena3DLook.For("Loop01");
            look.Rain = 0f;
            var host = new GameObject("Host seam").transform;
            var arena = new Arena3DRenderer(host, map, look);
            _rig.ApplyLook(look);
            float w = map.Width;
            // Actors straddling the seam, placed in unwrapped space near x = W.
            Actors(host, map, w - 4f);
            var a = new Vector2(w - 0.001f, 2.0f);
            yield return Settle(arena, a);
            var before = Grab();
            var b = new Vector2(w + 0.001f, 2.0f);
            _rig.SetTarget(b);
            arena.Update(0f, _rig.CameraPosition);
            yield return null;
            var after = Grab();
            var pa = before.GetPixels32(); var pb = after.GetPixels32();
            double sum = 0; int maxd = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                int d = Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
                sum += d; if (d > maxd) maxd = d;
            }
            _report.AppendLine($"Seam (Loop01, W={w}): meanAbsDiff={(sum / pa.Length / 3.0):F3}/255 maxDiff={maxd}/765 across the copy jump (film grain + DOF noise included)");
            Destroy(before); Destroy(after);
            yield return Save("world3d-seam.png");
            arena.Dispose();
            Destroy(host.gameObject);
        }

        private IEnumerator Settle(Arena3DRenderer arena, Vector2 target)
        {
            for (int i = 0; i < 40; i++)
            {
                _rig.SetTarget(target);
                arena.Update(Time.deltaTime, _rig.CameraPosition);
                yield return null;
            }
        }

        private static float FloorY(TileMap map, float x)
        {
            int ix = Mathf.FloorToInt(map.WrapX(x));
            for (int y = map.Height - 1; y >= 0; y--) if (map.IsSolid(ix, y) && !map.IsSolid(ix, Mathf.Min(map.Height - 1, y + 1))) return y + 1f;
            return 1f;
        }

        private static void Actors(Transform host, TileMap map, float sx)
        {
            string art = Path.Combine(Application.dataPath, "Resources/Art/Characters");
            var cyan = new Color(0.4f, 2.5f, 3f);
            var red = new Color(3f, 0.2f, 0.2f);
            (string kind, string frame, float dx, int face)[] cast =
            {
                ("Samurai", "samurai_idle_00", 3.5f, 1), ("Grunt", "grunt_idle_00", 8f, -1),
                ("Shield", "shield_idle_00", 11f, -1), ("Brute", "brute_idle_00", 14.5f, -1),
            };
            foreach (var c in cast)
            {
                float x = sx + c.dx;
                if (map.IsSolid(Mathf.FloorToInt(map.WrapX(x)), 1)) continue;
                var tint = c.kind == "Samurai" ? new Color(3f, 0.3f, 0.35f) : red;
                var tg = c.kind == "Samurai" ? cyan : new Color(3f, 0.3f, 0.2f);
                var actor = new Proto3DSpriteActor(host, art, c.kind, c.frame, tint, tg, tg);
                actor.Place(new Vector2(x, 1f), c.face);
            }
        }

        private float Timing()
        {
            var tex1 = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int N = 20;
            for (int i = 0; i < N; i++) _rig.WorldCamera.Render();
            RenderTexture.active = _rig.Target;
            tex1.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            RenderTexture.active = null;
            sw.Stop();
            Destroy(tex1);
            return (float)(sw.Elapsed.TotalMilliseconds / N);
        }

        /// <summary>Fraction of the visible frame covered by the foreground framers (hard edges, before DOF).</summary>
        private float FramerCoverage(Arena3DRenderer arena, Vector2 target)
        {
            const int L = 29;
            var cam = _rig.WorldCamera;
            var data = cam.GetUniversalAdditionalCameraData();
            int mask = cam.cullingMask; var bg = cam.backgroundColor; bool post = data.renderPostProcessing;
            var layers = new System.Collections.Generic.List<(GameObject go, int layer)>();
            foreach (var f in arena.Framers) foreach (var t in f.GetComponentsInChildren<Transform>()) { layers.Add((t.gameObject, t.gameObject.layer)); t.gameObject.layer = L; }
            cam.cullingMask = 1 << L; cam.backgroundColor = Color.black; data.renderPostProcessing = false;
            bool fog = RenderSettings.fog; RenderSettings.fog = false;
            cam.Render();
            var tex = Read(_rig.Target);
            RenderSettings.fog = fog;
            cam.cullingMask = mask; cam.backgroundColor = bg; data.renderPostProcessing = post;
            foreach (var (go, layer) in layers) go.layer = layer;
            var px = tex.GetPixels32();
            int covered = 0, total = 0;
            for (int y = 1; y < tex.height - 1; y++)
            for (int x = 1; x < tex.width - 1; x++)
            {
                var p = px[y * tex.width + x];
                total++;
                if (p.r + p.g + p.b > 0) covered++;
            }
            Destroy(tex);
            return covered / (float)total;
        }

        private static Texture2D Read(RenderTexture rt)
        {
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = null;
            return t;
        }

        /// <summary>Renders the world into the low-res target and presents it through the rig into a 1920×1080 texture.</summary>
        private Texture2D Grab()
        {
            _rig.WorldCamera.Render();
            var big = new RenderTexture(OutW, OutH, 24, RenderTextureFormat.ARGB32);
            _rig.PresentCamera.targetTexture = big;
            _rig.PresentCamera.aspect = (float)OutW / OutH;
            _rig.RefreshLayout();
            _rig.PresentCamera.Render();
            _rig.PresentCamera.targetTexture = null;
            _rig.PresentCamera.ResetAspect();
            _rig.RefreshLayout();
            var t = Read(big);
            big.Release(); Destroy(big);
            return t;
        }

        private IEnumerator Save(string file)
        {
            var t = Grab();
            File.WriteAllBytes(Path.Combine(Dir, file), t.EncodeToPNG());
            Debug.Log("WORLD3D_SHOT " + file);
            Destroy(t);
            yield return null;
        }

        private void Finish(int code)
        {
            Render3DMode.Disable();
            Debug.Log("WORLD3D_DONE " + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit();
#endif
        }
    }
}
