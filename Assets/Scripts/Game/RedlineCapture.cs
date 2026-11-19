using System;
using System.Collections;
using System.IO;
using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Dev-only screenshot tour for QA evidence. Active only when the player is started with
    /// <c>-redlineCapture &lt;directory&gt;</c>; it captures wave 1 in progress, an armored brute in
    /// wave 2, the same fight at tier 3 (brute breakable), the GAME OVER screen and the VICTORY
    /// screen, then quits. With <c>-redlineCaptureUi</c> as well it runs the UI tour instead
    /// (<c>ui-main-menu.png</c>, <c>ui-hud-play.png</c>, <c>ui-orbs-effects.png</c>, <c>ui-pause.png</c>,
    /// <c>ui-upgrades.png</c> at 1280x720: the game camera and every UI panel are rendered into
    /// textures and composited, so it also works in a batchmode editor). Never runs in normal play.
    /// </summary>
    /// <example>
    /// <code>
    /// Unity -batchmode -projectPath . -executeMethod Redline.Game.RedlineCapture.RunFromCommandLine \
    ///   -redlineCapture /abs/dir -redlineCaptureUi
    /// </code>
    /// </example>
    public sealed class RedlineCapture : MonoBehaviour
    {
        private static bool _parsed;
        private static string _directory;
        private RedlineGame _game;

        /// <summary>Output directory from the command line, or null when capture was not requested.</summary>
        public static string Directory
        {
            get
            {
                if (_parsed) return _directory;
                _parsed = true;
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                    if (args[i] == "-redlineCapture") _directory = args[i + 1];
                return _directory;
            }
        }

        /// <summary>True when the command line asked for a capture tour.</summary>
        public static bool Requested => !string.IsNullOrEmpty(Directory);

        /// <summary>True when the command line asked for the UI tour.</summary>
        public static bool UiRequested => Array.IndexOf(Environment.GetCommandLineArgs(), "-redlineCaptureUi") >= 0;

#if UNITY_EDITOR
        /// <summary>Entry for <c>-executeMethod</c>: enters Play Mode; the game starts the tour itself.</summary>
        public static void RunFromCommandLine() => UnityEditor.EditorApplication.EnterPlaymode();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _parsed = false;
            _directory = null;
        }

        /// <summary>Starts the tour for a game.</summary>
        public void Init(RedlineGame game)
        {
            _game = game;
            System.IO.Directory.CreateDirectory(Directory);
            bool extras = Array.IndexOf(Environment.GetCommandLineArgs(), "-redlineCaptureExtras") >= 0;
            StartCoroutine(extras ? ExtrasTour() : UiRequested ? UiTour() : Tour());
        }

        /// <summary>-redlineCaptureExtras: level 2's shock floor, a wave boss next to the player, the perk pick.</summary>
        private IEnumerator ExtrasTour()
        {
            Call("StartRun");
            yield return new WaitForSecondsRealtime(0.3f);
            Call("TryLoadLevel", new object[] { 1 });
            Call("OnLevelChanged");
            var sim = _game.Sim;
            yield return WaitForFight(sim, 0.5f);
            sim = _game.Sim;
            var p = sim.Player;
            p.InvulnTimer = 30f;
            p.X = 27f; p.Y = 1f; p.Vx = 0f; p.Vy = 0f;
            foreach (var e in sim.Enemies) e.Hp = 0f;
            var boss = sim.SpawnEnemy(EnemyKind.Brute, new Vec2(36f, 1f));
            boss.MakeBoss(sim.Config.bossHpMultiplier);
            boss.StunTimer = 3f;
            boss.Hp = boss.MaxHp * 0.7f;
            float t = 0f;
            while (!sim.ShockLive && t < 4f) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(0.15f);
            yield return Shot("extras-shock-boss.png");
            Call("OfferPerks");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("extras-perks.png");
            Debug.Log("REDLINE_CAPTURE_DONE " + Directory);
            Application.Quit();
        }

        private IEnumerator Tour()
        {
            var sim = _game.Sim;

            // 1. Wave 1 in progress: grunts closing in.
            yield return WaitForFight(sim, 1.2f);
            yield return Shot("hud-wave1.png");

            // 1b. Health bars: the wave lined up beside the player, hurt to different amounts.
            {
                sim = _game.Sim;   // the run may have replaced the sim since the tour started
                var pl = sim.Player;
                pl.InvulnTimer = 5f;
                int n = 0;
                foreach (var e in sim.Enemies)
                {
                    if (!e.Alive) continue;
                    e.X = pl.X + 2.2f + 1.6f * n;
                    e.Y = pl.Y;
                    e.Vx = e.Vy = 0f;
                    e.StunTimer = 2f;
                    e.Hp = e.MaxHp * (n % 2 == 0 ? 0.67f : 0.34f);
                    e.SinceHit = 0.5f;
                    n++;
                }
                yield return new WaitForSecondsRealtime(0.15f);
                yield return Shot("hud-enemy-health.png");
            }


            // 2. Wave 2: a brute appears, armored while the player is slow (steel colour).
            foreach (var e in sim.Enemies) e.Hp = 0f;
            yield return null;
            yield return WaitForFight(sim, 1.5f);
            yield return Shot("wave2-brute-armored.png");

            // 3. Tier 3: launch the player through the air at top-tier speed (brute turns red).
            var p = sim.Player;
            p.X = sim.Map.Width * 0.3f;
            p.Y = sim.Map.Height * 0.55f;
            p.Vy = 4f;
            p.Vx = sim.Config.tier3Speed + 2f;
            p.Chain = 4;                       // show the chain badge
            p.ChainTimer = sim.Config.chainWindow;
            p.RefreshContacts(sim.Map);
            yield return new WaitForSecondsRealtime(0.12f);
            yield return Shot("hud-tier3.png");

            // 3b. Kill feedback: sparks, debris and flash where the brute stands.
            foreach (var e in sim.Enemies)
                if (e.Alive && e.Kind == EnemyKind.Brute)
                {
                    _game.PreviewKillEffects(new Vector2(e.Center.X, e.Center.Y), true);
                    break;
                }
            yield return new WaitForSecondsRealtime(0.06f);
            yield return Shot("fx-kill.png");

            // 3c. Wave 3: drones hover out of reach. 3d. Wave 4: a brute telegraphs a charge.
            yield return ClearUntilWave(sim, 3);
            yield return WaitForFight(sim, 2.5f);
            yield return Shot("wave3-drones.png");
            yield return ClearUntilWave(sim, 4);
            yield return WaitForFight(sim, 0.5f);
            float wait = 8f;
            while (wait > 0f && !AnyBruteIn(sim, BruteMode.Windup))
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            yield return Shot("wave4-charge-telegraph.png");

            // 4. Game over.
            sim.Player.Hp = 0;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("gameover.png");

            // 5. Victory: restart and clear every wave.
            sim.Restart();
            float guard = 120f;
            while (sim.State != SimState.Victory && guard > 0f)
            {
                // Several fixed steps can run between frames; keep the idle player alive meanwhile.
                if (sim.State == SimState.GameOver) sim.Restart();
                sim.Player.InvulnTimer = 1f;
                if (sim.State == SimState.Fighting)
                    foreach (var e in sim.Enemies) e.Hp = 0f;
                guard -= Time.unscaledDeltaTime;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("victory.png");

            Debug.Log("REDLINE_CAPTURE_DONE " + Directory);
            Application.Quit();
        }

        // ------------------------------------------------------------------ UI tour

        private const int ShotWidth = 1280, ShotHeight = 720;
        private RenderTexture _world;

        private IEnumerator UiTour()
        {
            var sim = _game.Sim;
            _world = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            _game.Camera.targetTexture = _world;
            yield return new WaitForSecondsRealtime(0.6f);

            // 1. In play: tier 2 run, a chain, one HP lost (a run replaces the sim, so re-read it).
            Call("StartRun");
            yield return null;
            sim = _game.Sim;
            yield return WaitForFight(sim, 1.0f);
            var p = sim.Player;
            p.InvulnTimer = 30f;
            p.Hp = Mathf.Max(1, p.Hp - 1);
            p.Vx = sim.Config.tier2Speed + 0.6f;
            p.Chain = 4;
            p.ChainTimer = sim.Config.chainWindow;
            yield return new WaitForSecondsRealtime(0.1f);
            yield return UiShot("ui-hud-play.png");

            // 3. Orbs of every kind beside the player, and a kill burst mid-frame.
            p.InvulnTimer = 30f;
            var at = p.Center;
            sim.SpawnOrb(OrbKind.Health, new Vec2(at.X + 2.5f, at.Y));
            sim.SpawnOrb(OrbKind.Speed, new Vec2(at.X + 4f, at.Y));
            sim.SpawnOrb(OrbKind.Hook, new Vec2(at.X + 5.5f, at.Y));
            yield return new WaitForSecondsRealtime(0.25f);
            _game.PreviewKillEffects(_game.ToDraw(new Vector2(at.X - 2.5f, at.Y + 0.5f)), false);
            yield return new WaitForSecondsRealtime(0.05f);
            yield return UiShot("ui-orbs-effects.png");

            // 4. Pause menu over the frozen game.
            Call("Pause");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return UiShot("ui-pause.png");
            Call("Resume");

            // 5. Upgrades.
            Call("OpenUpgradesFromMenu");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return UiShot("ui-upgrades.png");

            // 6. Title menu (the training may have hidden it at launch, so open it explicitly).
            var shop = GetComponent<UpgradeScreen>();
            if (shop != null) shop.Hide();
            Call("ShowMainMenu", new object[] { null });
            yield return new WaitForSecondsRealtime(0.3f);
            yield return UiShot("ui-main-menu.png");

            Debug.Log("REDLINE_CAPTURE_DONE " + Directory);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        /// <summary>Calls a private RedlineGame flow method (dev tool only).</summary>
        private void Call(string method, object[] args = null)
        {
            var m = typeof(RedlineGame).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (m == null) Debug.LogWarning("REDLINE_CAPTURE: no method " + method);
            else m.Invoke(_game, args);
        }

        /// <summary>
        /// Renders every visible UI panel into its own transparent texture and composites them, in
        /// sorting order, over the game camera's texture (UI Toolkit output is premultiplied).
        /// </summary>
        private IEnumerator UiShot(string fileName)
        {
            var docs = FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsSortMode.None);
            var targets = new System.Collections.Generic.List<(float order, RenderTexture rt, UnityEngine.UIElements.PanelSettings ps)>();
            var seen = new System.Collections.Generic.HashSet<UnityEngine.UIElements.PanelSettings>();
            foreach (var d in docs)
            {
                var ps = d.panelSettings;
                if (ps == null || !seen.Add(ps)) continue;
                var rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
                ps.targetTexture = rt;
                ps.clearColor = true;
                ps.colorClearValue = new Color(0f, 0f, 0f, 0f);
                targets.Add((ps.sortingOrder, rt, ps));
            }
            targets.Sort((a, b) => a.order.CompareTo(b.order));
            // No WaitForEndOfFrame: it never fires in a batchmode editor. Panels render during the
            // frame, so a few frames later every target texture holds the current UI.
            for (int i = 0; i < 4; i++) yield return null;

            var px = Read(_world);
            foreach (var t in targets)
            {
                var ui = Read(t.rt);
                for (int i = 0; i < px.Length; i++)
                {
                    float a = ui[i].a;
                    if (a <= 0f) continue;
                    px[i] = new Color(ui[i].r + px[i].r * (1f - a), ui[i].g + px[i].g * (1f - a), ui[i].b + px[i].b * (1f - a), 1f);
                }
                t.ps.targetTexture = null;
                t.ps.clearColor = false;
                t.rt.Release();
                Destroy(t.rt);
            }
            var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            string path = Path.Combine(Directory, fileName);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("REDLINE_CAPTURE " + path);
        }

        private static Color[] Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            Destroy(tex);
            return px;
        }

        private static IEnumerator ClearUntilWave(RedlineSim sim, int wave)
        {
            float guard = 60f;
            while (sim.CurrentWave < wave && guard > 0f)
            {
                sim.Player.InvulnTimer = 1f;
                if (sim.State == SimState.Fighting)
                    foreach (var e in sim.Enemies) e.Hp = 0f;
                guard -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static bool AnyBruteIn(RedlineSim sim, BruteMode mode)
        {
            foreach (var e in sim.Enemies)
                if (e.Alive && e.Kind == EnemyKind.Brute && e.Mode == mode) return true;
            return false;
        }

        private static IEnumerator WaitForFight(RedlineSim sim, float extraSeconds)
        {
            float guard = 30f;
            while (sim.State != SimState.Fighting && guard > 0f)
            {
                guard -= Time.unscaledDeltaTime;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(extraSeconds);
        }

        private IEnumerator Shot(string fileName)
        {
            string path = Path.Combine(Directory, fileName);
            ScreenCapture.CaptureScreenshot(path);
            // The capture is written at the end of the frame; give it a few frames.
            for (int i = 0; i < 5; i++) yield return null;
            Debug.Log("REDLINE_CAPTURE " + path);
        }
    }
}
