using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Redline.Core;
using UnityEngine;

namespace Redline.Game.Render3D.Actors3D
{
    /// <summary>
    /// Dev-only evidence capture of the enemy sprite sheets in the REAL game scene: all seven enemy kinds stand in a
    /// row beside the baked ronin under the level's own lamps (lineup.png, the 480x240 world target as rendered, plus
    /// a 4x point upscale), then one grunt walks toward the ronin for 16 consecutive frames at a fixed 24 fps
    /// (walk_NN.png, full world target) so single-texel sparkle can be diffed offline. Active only when the
    /// environment variable <c>REDLINE_ENEMY_LINEUP</c> names an output directory; never runs in normal play.
    /// </summary>
    /// <remarks>
    /// Entry (editor, 3D pipeline):
    /// <c>REDLINE_ENEMY_LINEUP=/abs/dir Unity -batchmode -projectPath . -executeMethod Redline.Game.RedlineCapture.RunFromCommandLine -render3d</c>
    /// (play mode: do not pass -quit). Reads <see cref="Pixel3DCamera.Target"/> only; it does not change the camera.
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    public sealed class EnemyLineupCapture : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_ENEMY_LINEUP";
        private static string Dir => System.Environment.GetEnvironmentVariable(EnvVar);

        private static readonly EnemyKind[] Kinds =
            { EnemyKind.Grunt, EnemyKind.Brute, EnemyKind.Shield, EnemyKind.Shooter, EnemyKind.Summoner, EnemyKind.Kamikaze, EnemyKind.Drone };
        // World x offsets from the ronin (two left, the rest right, the drone hovering above the right end).
        private static readonly float[] Offsets = { -3.0f, -1.6f, 1.5f, 2.6f, 3.6f, 4.5f, 3.9f };

        private RedlineGame _game;
        private readonly List<Enemy> _held = new List<Enemy>();
        private readonly List<Vector2> _spots = new List<Vector2>();
        private bool _hold;
        private Vector2 _player;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (string.IsNullOrEmpty(Dir)) return;
            var go = new GameObject("EnemyLineupCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<EnemyLineupCapture>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            for (int i = 0; i < 3; i++) yield return null;
            _game = FindAnyObjectByType<RedlineGame>();
            if (_game == null || !_game.Render3DActive) { Debug.LogError("LINEUP_FAIL no 3D RedlineGame"); Finish(1); yield break; }
            Call("ShowMainMenu", new object[] { null });
            yield return Wait(1.0f);
            Call("StartRun");
            var sim = _game.Sim;
            float guard = 20f;
            while (sim.State != SimState.Fighting && guard > 0f) { guard -= Time.unscaledDeltaTime; yield return null; }
            guard = 5f;
            while (!sim.Player.Grounded && guard > 0f) { guard -= Time.unscaledDeltaTime; Freeze(); yield return null; }
            _player = new Vector2(sim.Player.X, sim.Player.Y);
            Debug.Log("LINEUP_START state=" + sim.State + " player=" + _player);
            _hold = true;
            Respawn();
            yield return Wait(1.5f);   // camera settles, idles play
            yield return Grab("lineup.png", true);

            // Walking grunt toward the ronin: 16 consecutive frames at a fixed step.
            _hold = false;
            sim.ClearActors();
            var g = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(_player.x - 4.2f, _player.y));
            Time.captureFramerate = 24;
            for (int i = 0; i < 8; i++) { Freeze(); yield return null; }   // into the walk cycle
            for (int i = 0; i < 16; i++)
            {
                Freeze();
                yield return Grab("walk_" + i.ToString("00") + ".png", false);
                Debug.Log("LINEUP_WALK " + i + " x=" + g.X.ToString("F3"));
            }
            Time.captureFramerate = 0;
            Finish(0);
        }

        private void Respawn()
        {
            var sim = _game.Sim;
            sim.ClearActors();
            _held.Clear();
            _spots.Clear();
            for (int k = 0; k < Kinds.Length; k++)
            {
                var pos = new Vector2(_player.x + Offsets[k], _player.y + (Kinds[k] == EnemyKind.Drone ? 1.7f : 0f));
                _held.Add(sim.SpawnEnemy(Kinds[k], new Vec2(pos.x, pos.y)));
                _spots.Add(pos);
            }
        }

        /// <summary>Keeps the ronin still and invulnerable (and, while holding, the lineup in place) before the sim steps.</summary>
        private void Update()
        {
            if (_game == null || _game.Sim == null) return;
            Freeze();
            if (!_hold) return;
            var sim = _game.Sim;
            if (sim.Enemies.Count != _held.Count) Respawn();   // a wave spawned extra enemies
            for (int k = 0; k < _held.Count; k++)
            {
                var e = _held[k];
                e.X = _spots[k].x; e.Y = _spots[k].y; e.Vx = 0f; e.Vy = 0f;
                e.Hp = e.MaxHp;
            }
        }

        private void Freeze()
        {
            var p = _game.Sim.Player;
            p.InvulnTimer = 1f;
            p.Vx = 0f;
            p.Facing = 1;
            if (_hold) { p.X = _player.x; p.Y = _player.y; }
        }

        private IEnumerator Grab(string file, bool upscale)
        {
            yield return null;   // WaitForEndOfFrame never resumes in -batchmode: render the world camera explicitly
            var rig = Pixel3DCamera.Latest != null ? Pixel3DCamera.Latest : FindAnyObjectByType<Pixel3DCamera>();
            var rt = rig != null ? rig.Target : null;
            if (rt == null) { Debug.LogError("LINEUP_FAIL no world target"); yield break; }
            rig.WorldCamera.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            if (upscale)
            {
                var src = tex.GetPixels32();
                int w = rt.width, h = rt.height, s = 4;
                var big = new Texture2D(w * s, h * s, TextureFormat.RGBA32, false);
                var dst = new Color32[w * s * h * s];
                for (int y = 0; y < h * s; y++)
                for (int x = 0; x < w * s; x++)
                    dst[y * w * s + x] = src[(y / s) * w + x / s];
                big.SetPixels32(dst);
                big.Apply();
                File.WriteAllBytes(Path.Combine(Dir, Path.GetFileNameWithoutExtension(file) + "_x4.png"), big.EncodeToPNG());
                Destroy(big);
            }
            Destroy(tex);
            Debug.Log("LINEUP_SHOT " + file + " " + rt.width + "x" + rt.height);
        }

        private void Call(string method, object[] args = null)
        {
            var m = typeof(RedlineGame).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) { Debug.LogError("LINEUP_FAIL no RedlineGame." + method); return; }
            m.Invoke(_game, args ?? new object[0]);
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        private static void Finish(int code)
        {
            Debug.Log("LINEUP_DONE " + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit();
#endif
        }
    }
}
