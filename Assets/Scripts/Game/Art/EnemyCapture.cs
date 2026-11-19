using System.Collections;
using System.Collections.Generic;
using System.IO;
using Redline.Core;
using Redline.Game.Atmosphere;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Redline.Game.Art
{
    /// <summary>
    /// Dev-only evidence capture for the enemy sheets (same approach as AtmosphereCapture). Active only
    /// when the environment variable <c>REDLINE_ENEMY_CAPTURE</c> names an output directory. It replaces
    /// the running game with a bare view (arena, actors, effects, post) of Loop01 at a lamp, poses enemies
    /// in the sim, renders 1280×720 and writes <c>enemies-ingame-lineup.png</c>, <c>-brute-windup</c>,
    /// <c>-brute-charge</c>, <c>-kamikaze-fuse</c>, <c>-shield-facing</c> and <c>-death</c>, then exits.
    /// Never runs in normal play.
    /// </summary>
    /// <example>
    /// <code>
    /// REDLINE_ENEMY_CAPTURE=/abs/dir Unity -batchmode -projectPath . \
    ///   -executeMethod Redline.Game.Art.EnemyCapture.RunFromCommandLine
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Allocates freely (dev tool, not per-frame game code).</remarks>
    public sealed class EnemyCapture : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_ENEMY_CAPTURE";
        private const int Width = 1280, Height = 720;

        private static string Directory => System.Environment.GetEnvironmentVariable(EnvVar);

#if UNITY_EDITOR
        /// <summary>Entry for <c>-executeMethod</c>: enters Play Mode; the capture boots itself.</summary>
        public static void RunFromCommandLine()
        {
            if (string.IsNullOrEmpty(Directory))
            {
                Debug.LogError("ENEMY_CAPTURE: set " + EnvVar);
                UnityEditor.EditorApplication.Exit(2);
                return;
            }
            UnityEditor.EditorApplication.EnterPlaymode();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (string.IsNullOrEmpty(Directory)) return;
            var go = new GameObject("EnemyCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<EnemyCapture>();
        }

        private IEnumerator Start()
        {
            System.IO.Directory.CreateDirectory(Directory);
            yield return null;
            yield return null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Destroy(root);
            yield return null;

            yield return Shot("enemies-ingame-lineup.png", 4.6f, 6.2f, (sim, p, c) =>
            {
                float dx = 2.6f;
                foreach (var k in new[] { EnemyKind.Grunt, EnemyKind.Brute, EnemyKind.Shooter, EnemyKind.Shield,
                             EnemyKind.Kamikaze, EnemyKind.Summoner, EnemyKind.Drone })
                {
                    var e = Spawn(sim, p, k, dx, k == EnemyKind.Drone ? 2f : 0.3f);
                    e.Facing = -1;
                    e.ShieldDir = -1;
                    dx += k == EnemyKind.Brute ? 2.2f : 1.7f;
                }
            }, 30);
            yield return Shot("enemies-ingame-brute-windup.png", 3.4f, 1.8f, (sim, p, c) =>
            {
                var e = Spawn(sim, p, EnemyKind.Brute, 3.2f, 0.3f);
                e.Facing = -1;
                e.Mode = BruteMode.Windup;
                e.ModeTimer = 10f;
            }, 14);
            yield return Shot("enemies-ingame-brute-charge.png", 3.4f, 1.8f, (sim, p, c) =>
            {
                var e = Spawn(sim, p, EnemyKind.Brute, 3.4f, 0.3f);
                e.Facing = -1;
                e.ChargeDir = -1;
                e.Mode = BruteMode.Charge;
                e.ModeTimer = 10f;
            }, 14);
            yield return Shot("enemies-ingame-kamikaze-fuse.png", 3.4f, 1.6f, (sim, p, c) =>
            {
                var e = Spawn(sim, p, EnemyKind.Kamikaze, 2.4f, 0.3f);
                e.Facing = -1;
                e.Act = EnemyAct.Fuse;
                e.ModeTimer = c.kamikazeFuseTime * 0.35f;
            }, 20);
            yield return Shot("enemies-ingame-shield-facing.png", 3.4f, 1.6f, (sim, p, c) =>
            {
                var e = Spawn(sim, p, EnemyKind.Shield, 2.8f, 0.3f);
                e.Facing = -1;
                e.ShieldDir = -1;
            }, 20);
            // Deaths mid-animation: alive for 10 frames, then killed; captured 0.25 s into the death.
            var dying = new List<Enemy>();
            yield return Shot("enemies-ingame-death.png", 3.8f, 3.2f, (sim, p, c) =>
            {
                dying.Clear();
                dying.Add(Spawn(sim, p, EnemyKind.Grunt, 2.2f, 0.3f));
                dying.Add(Spawn(sim, p, EnemyKind.Brute, 4.4f, 0.3f));
                dying.Add(Spawn(sim, p, EnemyKind.Shooter, 6.6f, 0.3f));
                foreach (var e in dying) e.Facing = -1;
            }, 25, (sim, frame) =>
            {
                if (frame != 10) return;
                foreach (var e in dying) e.Hp = 0f;
            });

            Debug.Log("ENEMY_CAPTURE_DONE " + Directory);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        /// <summary>Spawns <paramref name="kind"/> <paramref name="dx"/> right of the player, <paramref name="up"/> above its feet.</summary>
        private static Enemy Spawn(RedlineSim sim, Player p, EnemyKind kind, float dx, float up) =>
            sim.SpawnEnemy(kind, new Vec2(p.X + dx, p.Y + up));

        private IEnumerator Shot(string file, float size, float lookAhead,
            System.Action<RedlineSim, Player, RedlineConfig> setup, int frames,
            System.Action<RedlineSim, int> onFrame = null)
        {
            var host = new GameObject("CaptureHost");
            var camGo = new GameObject("CaptureCamera");
            camGo.transform.SetParent(host.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = size;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.aspect = (float)Width / Height;

            var config = new RedlineConfig();
            var cfgAsset = Resources.Load<TextAsset>("RedlineConfig");
            if (cfgAsset != null) JsonUtility.FromJsonOverwrite(cfgAsset.text, config);
            var map = TileMap.Parse(Resources.Load<TextAsset>("Loop01").text);
            var sim = new RedlineSim(config, map);
            var world = new GameObject("World").transform;
            world.SetParent(host.transform, false);
            var arena = new ArenaRenderer(world, map);
            var effects = new EffectsRenderer(world, cam);
            var actors = new ActorRenderer(world, effects);
            var post = host.AddComponent<PostFx>();
            post.Init(cam);

            var input = new InputFrame();
            for (int i = 0; i < 150; i++)
            {
                sim.Player.InvulnTimer = 1f;
                sim.Step(input, RedlineSim.FixedDt);
            }
            var p = sim.Player;
            // Stand at the first lamp post (as the samurai shots), facing the enemies on the right.
            for (int i = 0; i < arena.LampSpots.Count; i++)
            {
                var spot = arena.LampSpots[i];
                if (spot.Kind != LampField.Kind.Post) continue;
                p.X = spot.X - 1.6f;
                p.Y = spot.Y + 1f;
                break;
            }
            p.Vx = p.Vy = 0f;
            p.Facing = 1;
            // Settle the player onto the floor, then pose the enemies and let them land (poses re-applied).
            sim.ClearActors();
            for (int i = 0; i < 30; i++)
            {
                p.InvulnTimer = 1f;
                sim.Step(input, RedlineSim.FixedDt);
            }
            setup(sim, p, config);
            var posed = new List<(Enemy e, BruteMode m, EnemyAct a, float t, int f, int s)>();
            foreach (var e in sim.Enemies) posed.Add((e, e.Mode, e.Act, e.ModeTimer, e.Facing, e.ShieldDir));
            for (int i = 0; i < 40; i++)
            {
                p.InvulnTimer = 1f;
                sim.Step(input, RedlineSim.FixedDt);
                foreach (var q in posed)
                {
                    q.e.Vx = 0f;
                    if (q.e.Kind != EnemyKind.Drone) q.e.X = q.e.Post.X;
                    q.e.Mode = q.m;
                    q.e.Act = q.a;
                    q.e.ModeTimer = q.t;
                    q.e.Facing = q.f;
                    q.e.ShieldDir = q.s;
                    q.e.TurnTimer = 0f;
                    q.e.StunTimer = 0f;
                    q.e.SinceHit = 99f;
                }
            }
            p.Vx = 0f;
            p.Facing = 1;
            p.InvulnTimer = 0f;

            var feet = new Vector2(p.X, p.Y);
            var enemyFeet = new List<Vector2>();
            var camPos = new Vector3(feet.x + lookAhead, feet.y + size * 0.45f, -10f);
            cam.transform.position = camPos;
            for (int f = 0; f < frames; f++)
            {
                onFrame?.Invoke(sim, f);
                enemyFeet.Clear();
                foreach (var e in sim.Enemies) enemyFeet.Add(new Vector2(feet.x + map.Dx(p.X, e.X), e.Y));
                actors.Sync(sim, feet, enemyFeet, 1f / 60f);
                arena.Update(1f / 60f, camPos);
                effects.Update(1f / 60f);
                yield return null;
            }

            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(Directory, file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            var kinds = new System.Text.StringBuilder();
            foreach (var e in sim.Enemies) kinds.Append(e.Kind).Append(e.Alive ? "" : "(dead)").Append(e.Grounded ? "/g " : "/air ");
            Debug.Log("ENEMY_CAPTURE " + path + " enemies=" + kinds);
            cam.targetTexture = null;
            Destroy(tex);
            Destroy(host);
            rt.Release();
            Destroy(rt);
            yield return null;
            yield return null;
        }
    }
}
