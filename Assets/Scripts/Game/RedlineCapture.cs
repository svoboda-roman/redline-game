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
    /// screen, then quits. Never runs in normal play.
    /// </summary>
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

        /// <summary>Starts the tour for a game.</summary>
        public void Init(RedlineGame game)
        {
            _game = game;
            System.IO.Directory.CreateDirectory(Directory);
            StartCoroutine(Tour());
        }

        private IEnumerator Tour()
        {
            var sim = _game.Sim;

            // 1. Wave 1 in progress: grunts closing in.
            yield return WaitForFight(sim, 1.2f);
            yield return Shot("hud-wave1.png");

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

            // 4. Game over.
            sim.Player.Hp = 0;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("gameover.png");

            // 5. Victory: restart and clear every wave.
            sim.Restart();
            float guard = 120f;
            while (sim.State != SimState.Victory && guard > 0f)
            {
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
