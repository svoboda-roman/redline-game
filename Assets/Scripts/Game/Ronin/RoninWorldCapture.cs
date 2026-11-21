using System.Collections;
using System.IO;
using Redline.Core;
using Redline.Game.Atmosphere;
using Redline.Game.Render3D;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Redline.Game.Ronin
{
    /// <summary>
    /// Dev-only evidence capture of the Tech Ronin inside the real 3D world. Active only when the environment
    /// variable <c>REDLINE_RONIN_WORLD</c> names an output directory. Replaces the running scene with a Loop01
    /// <see cref="Arena3DRenderer"/> + <see cref="Pixel3DCamera"/>, stands ronins on the play plane at a sodium
    /// lamp, and writes ronin-v2-closeup.png, ronin-v2-poses.png (idle, run, slide, slash, jump attack, hang; both rendered at 1920x1080, presented x1) and
    /// ronin-v2-gamescale.png (game framing: 640x360 point-upscaled to 1920x1080), then exits.
    /// </summary>
    /// <remarks>Never runs in normal play. Entry: Redline.RoninEditor.RoninSetup.CaptureWorldFromCommandLine.</remarks>
    public sealed class RoninWorldCapture : MonoBehaviour
    {
        /// <summary>Environment variable holding the output directory.</summary>
        public const string EnvVar = "REDLINE_RONIN_WORLD";
        private const int OutW = 1920, OutH = 1080;
        private const float Dt = 1f / 60f;
        private static string Dir => System.Environment.GetEnvironmentVariable(EnvVar);
        private Pixel3DCamera _rig;
        private Arena3DRenderer _arena;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (string.IsNullOrEmpty(Dir)) return;
            var go = new GameObject("RoninWorldCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<RoninWorldCapture>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            yield return null;
            yield return null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) if (root != gameObject) Destroy(root);
            yield return null;
            var assets = RoninAssets.Load();
            if (assets == null || !Render3DMode.Enable()) { Debug.LogError("RONIN_FAIL assets or pipeline"); Finish(1); yield break; }
            yield return null;
            _rig = new GameObject("Pixel3DRig").AddComponent<Pixel3DCamera>();
            var map = TileMap.Parse(Resources.Load<TextAsset>("Loop01").text);
            var look = Arena3DLook.For("Loop01");
            _rig.ApplyLook(look);
            _arena = new Arena3DRenderer(new GameObject("Host").transform, map, look);

            // A post lamp near the start with open floor to its right.
            var spots = LampField.Plan(map);
            Vector2 stand = new Vector2(map.PlayerStart.X, map.PlayerStart.Y);
            float best = float.MaxValue;
            foreach (var s in spots)
            {
                if (s.Kind != LampField.Kind.Post) continue;
                var p = s.StandPoint;
                bool open = true;
                for (int dx = -6; dx <= 7 && open; dx++)
                    open = FloorY(map, p.x + dx) == p.y && !map.IsSolid(Mathf.FloorToInt(map.WrapX(p.x + dx)), Mathf.FloorToInt(p.y) + 1);
                float d = Mathf.Abs(p.x - map.PlayerStart.X);
                if (open && d < best) { best = d; stand = p; }
            }
            Debug.Log("RONIN world lamp stand " + stand + (best < float.MaxValue ? "" : " (no open post, using start)"));
            float fy = FloorY(map, stand.x);

            // Game framing: six poses spread around the lamp.
            var views = Poses(assets, new Vector2(stand.x - 4.6f, fy), 1.85f);
            _rig.FramingHeight = 11f;
            _rig.SetResolution(OutW / 3, OutH / 3);   // 640x360: an exact x3 into the 1920x1080 output
            yield return Settle(new Vector2(stand.x + 0.3f, fy + 3.6f));
            yield return Save("ronin-v2-gamescale.png");
            ReportHeight(views[0]);

            // Poses, closer: same scene, 6.6 units tall, rendered at the 1920x1080 output size. Pixel3DCamera presents
            // at an integer texel scale, so the low-res size must divide the output (1280x720 would land at x1).
            _rig.FramingHeight = 6.6f;
            _rig.SetResolution(OutW, OutH);
            yield return Settle(new Vector2(stand.x + 0.1f, fy + 2.4f));
            yield return Save("ronin-v2-poses.png");
            foreach (var v in views) Destroy(v.gameObject);
            yield return null;

            // Closeup: one idle ronin beside the lamp.
            var c = Spawn(assets, new Vector3(stand.x + 0.9f, fy, 0f));
            for (int i = 0; i < 80; i++) { c.Input = new RoninInput { Grounded = true, Facing = 1 }; c.Tick(Dt); }
            _rig.FramingHeight = 2.5f;
            _rig.SetResolution(OutW, OutH);
            yield return Settle(new Vector2(stand.x + 0.9f, fy + 1.3f));
            yield return Save("ronin-v2-closeup.png");
            Finish(0);
        }

        private static float FloorY(TileMap map, float x)
        {
            int ix = Mathf.FloorToInt(map.WrapX(x));
            for (int y = map.Height - 1; y >= 0; y--) if (map.IsSolid(ix, y) && !map.IsSolid(ix, Mathf.Min(map.Height - 1, y + 1))) return y + 1f;
            return 1f;
        }

        private static RoninView Spawn(RoninAssets assets, Vector3 pos)
        {
            var v = RoninView.Create(assets, null);
            v.AutoTick = false;
            v.transform.position = pos;
            v.ResetSprings();
            return v;
        }

        /// <summary>idle, run, slide attack, slash, jump attack, hang; each moving at its gameplay speed so the cloth reacts.</summary>
        private static RoninView[] Poses(RoninAssets assets, Vector2 origin, float spacing)
        {
            var poses = new RoninView[6];
            float[] speed = { 0f, 6.5f, 9f, 0f, 4f, 0f };
            float[] dur = { 1.2f, 1.25f, 0.55f, 0.55f, 0.75f, 1.0f };
            for (int i = 0; i < 6; i++)
            {
                var slot = new Vector3(origin.x + i * spacing, origin.y + (i == 4 ? 0.7f : i == 5 ? 0.9f : 0f), 0f);
                var start = slot - new Vector3(speed[i] * dur[i], 0f, 0f);
                poses[i] = Spawn(assets, start);
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
                    if (i == 5) p = slot + new Vector3(Mathf.Sin(t * 2.5f) * 0.08f, 0f, 0f);
                    poses[i].transform.position = p;
                    poses[i].Tick(Dt);
                }
            }
            return poses;
        }

        private void ReportHeight(RoninView v)
        {
            var b = new Bounds(v.transform.position, Vector3.zero);
            foreach (var r in v.GetComponentsInChildren<SkinnedMeshRenderer>()) b.Encapsulate(r.bounds);
            Debug.Log($"RONIN world idle height {b.size.y:F2} u = {b.size.y / _rig.FramingHeight * _rig.Height:F0} px at 640x360");
        }

        private IEnumerator Settle(Vector2 target)
        {
            for (int i = 0; i < 30; i++)
            {
                _rig.SetTarget(target);
                _arena.Update(Time.deltaTime, _rig.CameraPosition);
                yield return null;
            }
        }

        private IEnumerator Save(string file)
        {
            _rig.WorldCamera.Render();
            var big = new RenderTexture(OutW, OutH, 24, RenderTextureFormat.ARGB32);
            _rig.PresentCamera.targetTexture = big;
            _rig.PresentCamera.aspect = (float)OutW / OutH;
            _rig.RefreshLayout();   // presenter scale and letterbox follow the capture aspect
            _rig.PresentCamera.Render();
            _rig.PresentCamera.targetTexture = null;
            _rig.PresentCamera.ResetAspect();
            _rig.RefreshLayout();
            RenderTexture.active = big;
            var t = new Texture2D(OutW, OutH, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, OutW, OutH), 0, 0);
            t.Apply();
            RenderTexture.active = null;
            big.Release(); Destroy(big);
            File.WriteAllBytes(Path.Combine(Dir, file), t.EncodeToPNG());
            Destroy(t);
            Debug.Log("RONIN shot " + file);
            yield return null;
        }

        private void Finish(int code)
        {
            Render3DMode.Disable();
            Debug.Log("RONIN_WORLD_DONE " + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit();
#endif
        }
    }
}
