using System.Collections;
using System.Collections.Generic;
using System.IO;
using Redline.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Redline.Game.Atmosphere
{
    /// <summary>
    /// Dev-only evidence capture for the atmosphere pass. Active only when the environment variable
    /// <c>REDLINE_ATMO_CAPTURE</c> names an output directory. It replaces the running game with a
    /// bare view (arena, actors, effects, post) of Loop01 and Loop03, renders the camera into a
    /// 1280×720 texture and writes <c>atmo-loop01.png</c>, <c>atmo-loop03.png</c> and
    /// <c>atmo-closeup-lamp.png</c>, plus the samurai player shots <c>samurai-ingame-*.png</c>
    /// (run at tier 1 and 3, wide run, slide, strike at tier 1 and 3), then exits. With
    /// <c>REDLINE_ATMO_SHOTS=levels</c> it writes only the level-dressing framings <c>level-*.png</c>
    /// (Loop01-03, Arena01, Training). Never runs in normal play.
    /// </summary>
    /// <example>
    /// <code>
    /// REDLINE_ATMO_CAPTURE=/abs/dir Unity -batchmode -projectPath . \
    ///   -executeMethod Redline.Game.Atmosphere.AtmosphereCapture.RunFromCommandLine
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Allocates freely (dev tool, not per-frame game code).</remarks>
    public sealed class AtmosphereCapture : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_ATMO_CAPTURE";
        private const int Width = 1280, Height = 720;
        /// <summary>RedlineGame.CameraSize (orthographic half-height) used by the final shots.</summary>
        private const float GameHalfHeight = 6.75f;

        private static string Directory => System.Environment.GetEnvironmentVariable(EnvVar);

#if UNITY_EDITOR
        /// <summary>Entry for <c>-executeMethod</c>: enters Play Mode; the capture boots itself.</summary>
        public static void RunFromCommandLine()
        {
            if (string.IsNullOrEmpty(Directory))
            {
                Debug.LogError("ATMO_CAPTURE: set " + EnvVar);
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
            var go = new GameObject("AtmosphereCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<AtmosphereCapture>();
        }

        private IEnumerator Start()
        {
            System.IO.Directory.CreateDirectory(Directory);
            yield return null;
            yield return null;
            // Remove the game the bootstrap started (menus, HUD, its camera): a bare view only.
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Destroy(root);
            yield return null;

            if (System.Environment.GetEnvironmentVariable("REDLINE_ATMO_SHOTS") == "final")
            {
                // Final-polish evidence at the game's real camera size, samurai + a few enemies placed.
                yield return Shot("Loop01", "final-loop01.png", GameHalfHeight, false, 0, null, 20, true);
                yield return Shot("Loop02", "final-loop02.png", GameHalfHeight, false, 1, null, 20, true);
                yield return Shot("Loop03", "final-loop03.png", GameHalfHeight, false, 2, null, 20, true);
                yield return Shot("Loop01", "final-closeup.png", 3.4f, true, 0, null, 20, false);
                Debug.Log("ATMO_CAPTURE_DONE " + Directory);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(0);
#else
                Application.Quit();
#endif
                yield break;
            }
            if (System.Environment.GetEnvironmentVariable("REDLINE_ATMO_SHOTS") == "levels")
            {
                // Level-dressing evidence only: gameplay framings of every level.
                yield return Shot("Loop01", "level-loop01.png", 8f, false, -1);
                yield return Shot("Loop02", "level-loop02.png", 8f, false, 1);
                yield return Shot("Loop03", "level-loop03.png", 8f, false, 2);
                yield return Shot("Arena01", "level-arena01.png", 8f, false, -1);
                yield return Shot("Training", "level-training.png", 8f, false, -1);
                Debug.Log("ATMO_CAPTURE_DONE " + Directory);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(0);
#else
                Application.Quit();
#endif
                yield break;
            }
            yield return ConeDirectionTest();
            yield return Shot("Loop01", "atmo-loop01.png", 8f, false, -1);
            yield return Shot("Loop03", "atmo-loop03.png", 8f, false, 2);
            yield return Shot("Loop01", "atmo-closeup-lamp.png", 3.4f, true, 0);
            // Samurai player sheet in the real scene: Loop01 at a lamp.
            yield return Shot("Loop01", "samurai-ingame-run-t1.png", 3.4f, true, 0, (p, c) => Pose(p, c, 6f), 20);
            yield return Shot("Loop01", "samurai-ingame-run-t3.png", 3.4f, true, 0, (p, c) => Pose(p, c, c.tier3Speed + 2f), 20);
            yield return Shot("Loop01", "samurai-ingame-wide-run.png", 8f, false, 0, (p, c) => Pose(p, c, c.tier3Speed + 2f), 20);
            yield return Shot("Loop01", "samurai-ingame-slide.png", 3.4f, true, 0, (p, c) => { Pose(p, c, c.tier2Speed + 1f); p.Sliding = true; }, 20);
            yield return Shot("Loop01", "samurai-ingame-attack-t1.png", 3.4f, true, 0, (p, c) => { Pose(p, c, 0f); Strike(p, c); }, 9);
            yield return Shot("Loop01", "samurai-ingame-attack-t3.png", 3.4f, true, 0, (p, c) => { Pose(p, c, c.tier3Speed + 2f); Strike(p, c); }, 9);
            Debug.Log("ATMO_CAPTURE_DONE " + Directory);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Renders one lamp light exactly as LampField builds it (rotated 180°) onto a lit white wall
        /// and logs the brightness 2.5 units below and above it: the cone must point down.
        /// </summary>
        private IEnumerator ConeDirectionTest()
        {
            var host = new GameObject("ConeTest");
            host.transform.position = new Vector3(1000f, 1000f, 0f);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.transform.SetParent(host.transform, false);
            cam.transform.localPosition = new Vector3(0f, 0f, -10f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var wall = SpriteFactory.CreateLit("Wall", host.transform, SpriteFactory.Square, Color.white, 0);
            wall.transform.localScale = new Vector3(20f, 12f, 1f);
            var light = Lights2D.Point(host.transform, "TestLamp", Vector3.zero, Color.white, 1f, 4f, 0.2f,
                AtmosphereConfig.LampOuterAngle, AtmosphereConfig.LampInnerAngle, 0.5f);
            light.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            for (int i = 0; i < 5; i++) yield return null;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            float pxPerUnit = Height / 10f;
            float below = tex.GetPixel(Width / 2, Mathf.RoundToInt(Height / 2f - 2.5f * pxPerUnit)).grayscale;
            float above = tex.GetPixel(Width / 2, Mathf.RoundToInt(Height / 2f + 2.5f * pxPerUnit)).grayscale;
            Debug.Log("ATMO_CONE_TEST below=" + below + " above=" + above + (below > above ? " POINTS_DOWN" : " NOT_DOWN"));
            cam.targetTexture = null;
            Destroy(tex);
            Destroy(host);
            rt.Release();
            Destroy(rt);
            yield return null;
        }

        /// <summary>Grounded, facing right at <paramref name="vx"/> (the tier follows speed), no blink.</summary>
        private static void Pose(Player p, RedlineConfig c, float vx)
        {
            p.Vx = vx;
            p.Vy = 0f;
            p.Facing = 1;
            p.InvulnTimer = 0f;
        }

        /// <summary>Starts a strike (held: the sim is not stepped while the frames render).</summary>
        private static void Strike(Player p, RedlineConfig c)
        {
            p.AttackTimer = c.attackActiveTime;
            p.StrikeId++;
        }

        private IEnumerator Shot(string level, string file, float size, bool closeUp, int postIndex,
            System.Action<Player, RedlineConfig> pose = null, int frames = 20, bool enemies = false)
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
            var map = TileMap.Parse(Resources.Load<TextAsset>(level).text);
            var sim = new RedlineSim(config, map);
            var world = new GameObject("World").transform;
            world.SetParent(host.transform, false);
            var arena = new ArenaRenderer(world, map);
            var effects = new EffectsRenderer(world, cam);
            var actors = new ActorRenderer(world, effects);
            var post = host.AddComponent<PostFx>();
            post.Init(cam);

            // Let the run start and a wave walk in (player invulnerable, standing).
            var input = new InputFrame();
            for (int i = 0; i < 150; i++)
            {
                sim.Player.InvulnTimer = 1f;
                sim.Step(input, RedlineSim.FixedDt);
            }
            var p = sim.Player;
            if (postIndex >= 0)
            {
                int seen = 0;
                for (int i = 0; i < arena.LampSpots.Count; i++)
                {
                    var spot = arena.LampSpots[i];
                    if (spot.Kind != LampField.Kind.Post || seen++ < postIndex) continue;
                    p.X = spot.X + 1.6f;
                    p.Y = spot.Y + 1f;
                    p.Vx = 0f;
                    p.Vy = 0f;
                    p.Facing = -1;
                    break;
                }
            }

            if (enemies)
            {
                // As EnemyCapture: settle the player, then pose a few enemies to the right and let them land.
                float px = p.X, py = p.Y;
                sim.ClearActors();
                p.Facing = 1;
                var placed = new List<Enemy>
                {
                    sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(px + 4.5f, py + 0.3f)),
                    sim.SpawnEnemy(EnemyKind.Shield, new Vec2(px + 7.5f, py + 0.3f)),
                    sim.SpawnEnemy(EnemyKind.Drone, new Vec2(px + 10f, py + 2.5f))
                };
                for (int i = 0; i < 40; i++)
                {
                    p.InvulnTimer = 1f;
                    sim.Step(input, RedlineSim.FixedDt);
                    p.X = px;
                    p.Vx = 0f;
                    p.Facing = 1;
                    foreach (var e in placed)
                    {
                        e.Vx = 0f;
                        if (e.Kind != EnemyKind.Drone) e.X = e.Post.X;
                        e.Facing = -1;
                        e.ShieldDir = -1;
                    }
                }
            }
            pose?.Invoke(p, config);
            var feet = new Vector2(p.X, p.Y);
            var enemyFeet = new List<Vector2>();
            foreach (var e in sim.Enemies) enemyFeet.Add(new Vector2(feet.x + map.Dx(p.X, e.X), e.Y));
            var camPos = new Vector3(feet.x + (closeUp ? -0.6f : 2f), feet.y + (closeUp ? 1.2f : 2.5f), -10f);
            if (!closeUp) camPos.y = Mathf.Clamp(camPos.y, size - 1f, map.Height + 1f - size);
            cam.transform.position = camPos;
            for (int f = 0; f < frames; f++)
            {
                actors.Sync(sim, feet, enemyFeet, 1f / 60f);
                arena.Update(1f / 60f, camPos);
                effects.Update(1f / 60f);
                yield return null;
            }

            int lightsOnScreen = 0;
            var view = new Rect(camPos.x - size * cam.aspect, camPos.y - size, size * cam.aspect * 2f, size * 2f);
            foreach (var l in FindObjectsByType<Light2D>(FindObjectsSortMode.None))
            {
                if (!l.isActiveAndEnabled) continue;
                if (l.lightType == Light2D.LightType.Global) { lightsOnScreen++; continue; }
                var lp = l.transform.position;
                float r = l.pointLightOuterRadius;
                if (lp.x + r > view.xMin && lp.x - r < view.xMax && lp.y + r > view.yMin && lp.y - r < view.yMax) lightsOnScreen++;
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
            Debug.Log("ATMO_CAPTURE " + path + " lightsTouchingView=" + lightsOnScreen + " arenaLights=" + arena.LightCount
                      + " lampSpots=" + arena.LampSpots.Count + " props=" + arena.PropPlan.Count);
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
