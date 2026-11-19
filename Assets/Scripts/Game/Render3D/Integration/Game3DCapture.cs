using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Redline.Core;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Redline.Game.Render3D.Integration
{
    /// <summary>
    /// Dev-only evidence capture of the REAL game loop in the render mode chosen by the command line
    /// (<c>-render3d</c> / <c>-render2d</c>). Active only when the environment variable <c>REDLINE_GAME3D_CAPTURE</c>
    /// names an output directory. Drives <see cref="RedlineGame"/> through its own flow methods (main menu, run, level
    /// loads), keeps the player invulnerable and running toward the nearest enemy, and writes 1920×1080 shots with every
    /// UI Toolkit panel composited at native resolution over the (pixel-upscaled) game view, then exits.
    /// 3D: game3d-menu, game3d-loop01..03, game3d-hud. 2D: game3d-2d-fallback. Switches: REDLINE_GAME3D_BURST (flicker
/// measurement), REDLINE_GAME3D_NORAIN (burst with the rain stopped), REDLINE_GAME3D_FX (fx.png effects/orbs shot).
    /// </summary>
    /// <example>
    /// REDLINE_GAME3D_CAPTURE=/abs/dir Unity -batchmode -projectPath . -executeMethod Redline.Game.RedlineCapture.RunFromCommandLine -render3d
    /// </example>
    /// <remarks>Never runs in normal play.</remarks>
    public sealed class Game3DCapture : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_GAME3D_CAPTURE";
        private const int W = 1920, H = 1080;
        private static string Dir => Environment.GetEnvironmentVariable(EnvVar);
        // Optional file-name prefix for the loop shots (REDLINE_GAME3D_PREFIX, default "game3d-").
        private static string Prefix => Environment.GetEnvironmentVariable("REDLINE_GAME3D_PREFIX") ?? "game3d-";
        private RedlineGame _game;
        private bool _drive;
        private float _dirTimer;
        private int _dir = 1;
        private float _lastX, _stuck;
        private Vector2? _closeTarget;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (string.IsNullOrEmpty(Dir)) return;
            var go = new GameObject("Game3DCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<Game3DCapture>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            for (int i = 0; i < 3; i++) yield return null;
            _game = FindAnyObjectByType<RedlineGame>();
            if (_game == null) { Debug.LogError("GAME3D_FAIL no RedlineGame"); Finish(1); yield break; }
            Debug.Log("GAME3D mode 3D=" + _game.Render3DActive);
            Call("ShowMainMenu", new object[] { null });
            yield return Wait(1.0f);
            if (!_game.Render3DActive)
            {
                Call("StartRun");
                yield return Fight(2.5f);
                yield return Shot("game3d-2d-fallback.png");
                Finish(0);
                yield break;
            }
            yield return Shot("game3d-menu.png");
            ReportClipImpacts();
            Call("StartRun");
            yield return Fight(3.0f);
            if (Environment.GetEnvironmentVariable("REDLINE_GAME3D_FX") != null) yield return FxShot();
            // REDLINE_GAME3D_ENEMIES: enemy-animation evidence only (walk cycles closing in, then a grunt thrust).
            if (Environment.GetEnvironmentVariable("REDLINE_GAME3D_ENEMIES") != null) { yield return EnemyShots(); Finish(0); yield break; }
            if (Environment.GetEnvironmentVariable("REDLINE_GAME3D_BURST") != null)
            {
                yield return Burst();
                Actors3DView.UseBakedRonin = true;
                yield return AttackSequence();   // in-game crop strip of the baked ronin (stand + run slashes)
                yield return AnimSequence();     // in-game run / jump / attack rows of the baked ronin
                // REDLINE_GAME3D_BURST_CONTINUE: go on to the loop shots in the same session (one editor run for both).
                if (Environment.GetEnvironmentVariable("REDLINE_GAME3D_BURST_CONTINUE") == null) { Finish(0); yield break; }
                if (rigForBurst() is Pixel3DCamera r && r.Profile != null && r.Profile.TryGet<FilmGrain>(out var g)) g.active = true;
                Time.captureFramerate = 0;
                _drive = true;
                yield return Fight(2.0f);
            }
            yield return Shot(Prefix + "loop01.png");
            yield return AttackSequence();
            yield return RoninCloseUp();
            yield return JumpShot();
            yield return Wait(1.5f);
            yield return Shot("game3d-hud.png");
            for (int level = 1; level <= 2; level++)
            {
                Call("TryLoadLevel", new object[] { level });
                Call("OnLevelChanged");
                yield return Fight(3.0f);
                if (Environment.GetEnvironmentVariable("REDLINE_GAME3D_PROBE") != null) Probe("loop0" + (level + 1));
                yield return Shot(Prefix + "loop0" + (level + 1) + ".png");
            }
            Finish(0);
        }

        private void Update()
        {
            if (!_drive || _game == null || _game.Sim == null) return;
            var sim = _game.Sim;
            var p = sim.Player;
            p.InvulnTimer = 1f;
            // Run back and forth; turn around when blocked or after a while.
            _stuck = Mathf.Abs(p.X - _lastX) < 0.01f ? _stuck + Time.deltaTime : 0f;
            _lastX = p.X;
            _dirTimer -= Time.deltaTime;
            if (_stuck > 0.15f || _dirTimer <= 0f) { _dir = -_dir; _dirTimer = 2.5f; _stuck = 0f; }
            p.Vx = _dir * sim.Config.runSpeed;
            p.Facing = _dir;
        }

        /// <summary>The player stands still (invulnerable) so grunts walk in, then shoots their spear thrust.</summary>
        private IEnumerator EnemyShots()
        {
            var sim = _game.Sim;
            _drive = false;
            sim.Player.Vx = 0f;
            float t = 0f;
            bool walked = false;
            while (t < 8f)
            {
                sim.Player.InvulnTimer = 1f;
                sim.Player.Vx = 0f;
                bool inRange = false, closing = false;
                foreach (var e in sim.Enemies)
                {
                    if (!e.Alive || e.Kind != EnemyKind.Grunt || !e.Grounded) continue;
                    float dx = Mathf.Abs(sim.Map.Dx(e.X, sim.Player.X));
                    if (dx <= Redline.Game.Art.EnemyLook.GruntStrikeRange && Mathf.Abs(sim.Player.Y - e.Y) <= Redline.Game.Art.EnemyLook.GruntStrikeFeetBand && sim.Player.Grounded) inRange = true;
                    else if (dx < 7f && Mathf.Abs(e.Vx) > 0.3f) closing = true;
                }
                if (!walked && closing) { yield return Shot(Prefix + "enemies-walk.png"); walked = true; }
                if (inRange) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            for (float w = 0f; w < 0.2f; w += Time.unscaledDeltaTime) { sim.Player.InvulnTimer = 1f; yield return null; } // thrust frames 3-4
            yield return Shot(Prefix + "grunt-thrust.png");
            for (float w = 0f; w < 1.2f; w += Time.unscaledDeltaTime) { sim.Player.InvulnTimer = 1f; yield return null; }
            yield return Shot(Prefix + "grunt-thrust2.png");
        }

        private IEnumerator Fight(float seconds)
        {
            _drive = true;
            float guard = 20f;
            while (_game.Sim.State != SimState.Fighting && guard > 0f) { guard -= Time.unscaledDeltaTime; yield return null; }
            yield return Wait(seconds);
            // Put the player a few steps from a grounded enemy so the shot shows the ronin among them.
            var sim = _game.Sim;
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive || !e.Grounded) continue;
                sim.Player.X = e.X - 2.5f * _dir;
                sim.Player.Y = e.Y;
                sim.Player.Vy = 0f;
                break;
            }
            yield return Wait(0.6f);
        }

        /// <summary>Close framing of the standing ronin (eyes, coat, brim read check).</summary>
        private IEnumerator RoninCloseUp()
        {
            var rig = FindAnyObjectByType<Pixel3DCamera>();
            var actors = FindAnyObjectByType<Redline.Game.Ronin.RoninView>();
            if (rig == null || actors == null) yield break;
            // Stay on the deck where Fight left the player (next to a grounded enemy); stand still facing right.
            _drive = false;
            var p = _game.Sim.Player;
            p.Vx = 0f; p.Facing = 1;
            float guard = 3f;
            while (!p.Grounded && guard > 0f) { guard -= Time.unscaledDeltaTime; p.Vx = 0f; yield return null; }
            yield return Wait(1.2f);
            float framing = rig.FramingHeight;
            rig.FramingHeight = 4.5f;
            var feet = actors.transform.position;
            _closeTarget = new Vector2(feet.x, feet.y + 1.0f);   // picture centre 1 unit above the feet
            yield return Shot(Prefix + "ronin.png");
            _closeTarget = null;
            rig.FramingHeight = framing;
            _drive = true;
        }

        /// <summary>Mid-jump framing check: a full jump from flat ground, shot near the apex.</summary>
        private IEnumerator JumpShot()
        {
            var p = _game.Sim.Player;
            _drive = false;
            float guard = 3f;
            while (!p.Grounded && guard > 0f) { guard -= Time.unscaledDeltaTime; yield return null; }
            yield return Wait(0.5f);
            p.Vy = _game.Sim.Config.jumpSpeed;
            float t = 0f;
            while (t < 0.6f && p.Vy > 0.5f) { t += Time.unscaledDeltaTime; p.InvulnTimer = 1f; yield return null; }
            yield return Shot(Prefix + "jump.png");
            _drive = true;
        }

        /// <summary>
        /// Samples the right hand's speed (relative to the hips) through Attack1, Attack2 and JumpAttack and logs the
        /// peak-speed time (the impact) and clip length: GAME3D_CLIP lines.
        /// </summary>
        private static void ReportClipImpacts()
        {
            var assets = Redline.Game.Ronin.RoninAssets.Load();
            if (assets == null) return;
            var go = Instantiate(assets.Model);
            var anim = go.GetComponent<Animator>();
            if (anim == null || !anim.isHuman) { Destroy(go); return; }
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.applyRootMotion = false;
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            foreach (var slot in new[] { Redline.Game.Ronin.RoninClip.Attack1, Redline.Game.Ronin.RoninClip.Attack2, Redline.Game.Ronin.RoninClip.JumpAttack, Redline.Game.Ronin.RoninClip.SlideAttack })
            {
                var clip = assets.Clips[(int)slot];
                if (clip == null) continue;
                var graph = UnityEngine.Playables.PlayableGraph.Create("probe");
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var cp = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
                cp.SetApplyFootIK(false);
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "probe", anim);
                output.SetSourcePlayable(cp);
                const float step = 1f / 60f;
                Vector3 prev = Vector3.zero;
                float best = 0f, bestT = 0f;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i * step <= clip.length + 0.0001f; i++)
                {
                    cp.SetTime(i * step);
                    graph.Evaluate(0f);
                    Vector3 rel = hips.InverseTransformPoint(hand.position);
                    if (i > 0)
                    {
                        float v = (rel - prev).magnitude / step;
                        if (v > best) { best = v; bestT = i * step; }
                        if (i % 3 == 0) sb.Append($"{i * step:F2}:{v:F1} ");
                    }
                    prev = rel;
                }
                Debug.Log($"GAME3D_CLIP {slot} len={clip.length:F3} peak={bestT:F3} speed={best:F2} | {sb}");
                graph.Destroy();
            }
            Destroy(go);
        }

        /// <summary>
        /// Two rows of six crops of the low-res game view centred on the ronin, ~0.05 s apart from one attack press:
        /// top standing (full-body slash), bottom running (upper-body slash). The player is first moved to a spot of
        /// the deck with no enemy within 7 units. 3x nearest upscale; the press time of each frame is logged.
        /// </summary>
        private IEnumerator AttackSequence()
        {
            var rig = FindAnyObjectByType<Pixel3DCamera>();
            var actors = Actors3DView.Instance;
            var ronin = actors != null ? actors.PlayerTransform : null;
            if (rig == null || ronin == null) yield break;
            const int N = 6, C = 128, S = 3;
            var strip = new Texture2D(N * C * S, 2 * C * S, TextureFormat.RGBA32, false);
            var rt = rig.Target;
            var tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var sim = _game.Sim;
            var p = sim.Player;
            _drive = false;
            // Clear spot: step along the deck until no live enemy is near.
            float baseX = p.X;
            for (int k = 0; k < 40; k++)
            {
                float x = baseX + (k % 2 == 0 ? 1 : -1) * (k / 2) * 1.5f;
                bool clear = true;
                foreach (var e in sim.Enemies) if (e.Alive && Mathf.Abs(sim.Map.Dx(x, e.X)) < 7f) { clear = false; break; }
                if (clear) { p.X = x; break; }
            }
            for (int row = 0; row < 2; row++)
            {
                bool running = row == 1;
                _drive = running;
                float guard = 3f;
                while (guard > 0f && (!p.Grounded || guard > 2.2f))
                {
                    guard -= Time.unscaledDeltaTime;
                    p.Vx = running ? sim.Config.runSpeed * 0.6f * _dir : 0f; p.Facing = running ? _dir : 1; p.InvulnTimer = 1f;
                    yield return null;
                }
                p.AttackTimer = 0.15f;
                p.StrikeId++;
                float pressed = Time.time;
                for (int f = 0; f < N; f++)
                {
                    yield return null;
                    if (running) p.Vx = sim.Config.runSpeed * 0.6f * _dir; else p.Vx = 0f;
                    rig.WorldCamera.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tmp.Apply();
                    RenderTexture.active = prev;
                    Vector3 sp = rig.WorldCamera.WorldToScreenPoint(ronin.position + Vector3.up * 0.8f);
                    int cx = Mathf.RoundToInt(sp.x) - C / 2, cy = Mathf.RoundToInt(sp.y) - C / 2;
                    int oy = (1 - row) * C * S;
                    for (int y = 0; y < C; y++)
                    for (int x = 0; x < C; x++)
                    {
                        var c = tmp.GetPixel(Mathf.Clamp(cx + x, 0, rt.width - 1), Mathf.Clamp(cy + y, 0, rt.height - 1));
                        if (x == 0 || y == 0) c = Color.white;
                        for (int sy = 0; sy < S; sy++)
                        for (int sx = 0; sx < S; sx++) strip.SetPixel(f * C * S + x * S + sx, oy + y * S + sy, c);
                    }
                    Debug.Log($"GAME3D_ATTACK row={row} frame={f} t={Time.time - pressed:F3} clip={(actors.Baked != null && Actors3DView.UseBakedRonin ? actors.Baked.Current.ToString() : actors.Ronin != null ? actors.Ronin.Current.ToString() : "?")}");
                    float t = 0f;
                    while (t < 0.05f) { t += Time.unscaledDeltaTime; yield return null; }
                }
                yield return Wait(0.4f);
            }
            strip.Apply();
            File.WriteAllBytes(Path.Combine(Dir, Prefix + "attack-sequence.png"), strip.EncodeToPNG());
            Destroy(strip);
            Destroy(tmp);
            _drive = true;
            Debug.Log("GAME3D_SHOT " + Prefix + "attack-sequence.png");
        }


        /// <summary>
        /// In-game animation read of the baked ronin: five rows of eight 3x crops of the low-res game view: run (sim run
        /// speed, 1/16 s apart), sprint (18 u/s, 1/24 s), jump (0.07 s), standing slash (1/30 s) and the landing squash
        /// after a fast fall (1/30 s from touchdown). Writes <c>&lt;prefix&gt;anim-sequence.png</c>; logs GAME3D_ANIM lines with the shown animation.
        /// </summary>
        private IEnumerator AnimSequence()
        {
            var rig = FindAnyObjectByType<Pixel3DCamera>();
            var actors = Actors3DView.Instance;
            var ronin = actors != null ? actors.PlayerTransform : null;
            if (rig == null || ronin == null) yield break;
            const int N = 8, C = 112, S = 3, Rows = 5;
            var strip = new Texture2D(N * C * S, Rows * C * S, TextureFormat.RGBA32, false);
            var rt = rig.Target;
            var tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var sim = _game.Sim;
            var p = sim.Player;
            _drive = false;
            // Rows: run (sim run speed), sprint (18 u/s), jump, standing slash, landing after a fast fall.
            float[] gaps = { 1f / 16f, 1f / 24f, 0.07f, 1f / 30f, 1f / 30f };
            float RowVx(int r) => r == 0 ? sim.Config.runSpeed * _dir : r == 1 ? 18f * _dir : 0f;
            for (int row = 0; row < Rows; row++)
            {
                // Clear, grounded start: step along the deck until no live enemy is near. The run and sprint rows also
                // need open floor ahead for the whole warm-up and capture (no wall or crate, no drop), else the ronin
                // ends up standing against an obstacle while the row is shot.
                float baseX = p.X;
                float need = row <= 1 ? Mathf.Abs(RowVx(row)) * (0.6f + N * (gaps[row] + 1f / 30f)) + 2f : 0f;
                bool placed = false;
                for (int pass = 0; pass < 2 && !placed; pass++)
                for (int k = 0; k < 120 && !placed; k++)
                {
                    float x = baseX + (k % 2 == 0 ? 1 : -1) * (k / 2) * 1.0f;
                    bool clear = true;
                    foreach (var e in sim.Enemies) if (e.Alive && Mathf.Abs(sim.Map.Dx(x, e.X)) < 8f) { clear = false; break; }
                    if (!clear) continue;
                    if (row <= 1 && pass == 0)
                    {
                        int dir = 0;
                        if (Runway(sim, p, x, _dir, need)) dir = _dir;
                        else if (Runway(sim, p, x, -_dir, need)) dir = -_dir;
                        if (dir == 0) continue;
                        _dir = dir;
                    }
                    p.X = x;
                    placed = true;
                }
                float guard = 3f;
                while (guard > 0f && (!p.Grounded || guard > 2.4f))
                {
                    guard -= Time.unscaledDeltaTime;
                    p.Vx = RowVx(row); p.Facing = row <= 1 ? _dir : 1; p.InvulnTimer = 1f;
                    yield return null;
                }
                if (row == 2) p.Vy = sim.Config.jumpSpeed;
                if (row == 3) { p.AttackTimer = 0.15f; p.StrikeId++; }
                if (row == 4)
                {
                    // Drop from 3.5 units at -14 u/s (past the 12 u/s land threshold); shoot from the touchdown on.
                    p.Y += 3.5f; p.Vy = -14f;
                    float g2 = 1.5f;
                    yield return null;
                    while (!p.Grounded && g2 > 0f) { g2 -= Time.unscaledDeltaTime; p.Vx = 0f; p.InvulnTimer = 1f; yield return null; }
                }
                for (int f = 0; f < N; f++)
                {
                    yield return null;
                    p.InvulnTimer = 1f;
                    p.Vx = RowVx(row);
                    rig.WorldCamera.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tmp.Apply();
                    RenderTexture.active = prev;
                    Vector3 sp = rig.WorldCamera.WorldToScreenPoint(ronin.position + Vector3.up * 0.8f);
                    int cx = Mathf.RoundToInt(sp.x) - C / 2, cy = Mathf.RoundToInt(sp.y) - C / 2;
                    int oy = (Rows - 1 - row) * C * S;
                    for (int y = 0; y < C; y++)
                    for (int x = 0; x < C; x++)
                    {
                        var c = tmp.GetPixel(Mathf.Clamp(cx + x, 0, rt.width - 1), Mathf.Clamp(cy + y, 0, rt.height - 1));
                        if (x == 0 || y == 0) c = Color.white;
                        for (int sy = 0; sy < S; sy++)
                        for (int sx = 0; sx < S; sx++) strip.SetPixel(f * C * S + x * S + sx, oy + y * S + sy, c);
                    }
                    Debug.Log($"GAME3D_ANIM row={row} frame={f} anim={(actors.Baked != null ? actors.Baked.Current + "#" + actors.Baked.Frame : "?")} vy={p.Vy:F2} grounded={p.Grounded}");
                    float t = 0f;
                    while (t < gaps[row]) { t += Time.unscaledDeltaTime; p.InvulnTimer = 1f; if (row <= 1) p.Vx = RowVx(row); yield return null; }
                }
                p.Vx = 0f;
                yield return Wait(0.5f);
            }
            strip.Apply();
            File.WriteAllBytes(Path.Combine(Dir, Prefix + "anim-sequence.png"), strip.EncodeToPNG());
            Destroy(strip);
            Destroy(tmp);
            _drive = true;
            Debug.Log("GAME3D_SHOT " + Prefix + "anim-sequence.png");
        }

        /// <summary>True when the floor under the player's feet row is solid and the body box is free of walls for
        /// <paramref name="length"/> units from <paramref name="x"/> in <paramref name="dir"/>.</summary>
        private static bool Runway(RedlineSim sim, Player p, float x, int dir, float length)
        {
            var map = sim.Map;
            float y = Mathf.Round(p.Y);
            for (float d = -p.Width; d <= length; d += 0.5f)
            {
                float cx = x + dir * d;
                if (map.OverlapsSolid(cx - p.Width * 0.5f, y + 0.05f, cx + p.Width * 0.5f, y + p.Height)) return false;
                if (!map.OverlapsSolid(cx - 0.05f, y - 0.5f, cx + 0.05f, y - 0.05f)) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- flicker burst (REDLINE_GAME3D_BURST)

        private sealed class BurstResult { public string Strip; public Color32[] Pixels; public int W, H; }

        /// <summary>
        /// Flicker measurement: for the live 3D ronin and the baked sprite, in two situations (standing, running at constant
        /// speed), captures N consecutive low-res frames around the ronin (film grain off so it cannot mask the
        /// difference), with a second render with the ronin hidden to get his pixel mask. Scores: mean = mean per-pixel absolute
        /// luminance change between consecutive frames over the ronin's pixels (union of both frames' masks); bbox = the same
        /// over the whole ronin bounding box; sparkle = pixels per frame that jump across the threshold and back within 2 frames.
        /// Logs GAME3D_FLICKER lines and writes flicker-&lt;variant&gt;.png (3x strips, standing on top, running below).
        /// </summary>
        private IEnumerator Burst()
        {
            var rig = FindAnyObjectByType<Pixel3DCamera>();
            var view = Actors3DView.Instance;
            if (rig == null || view == null) { Debug.LogError("GAME3D_FAIL burst: no rig or actors"); yield break; }
            _drive = false;
            Time.captureFramerate = 60;
            if (rig.Profile != null && rig.Profile.TryGet<FilmGrain>(out var grain)) grain.active = false;
            int rainOff = NoRain ? StopRain() : 0;
            Debug.Log($"GAME3D_FLICKER_SETUP rain={(NoRain ? "off" : "on")} rainSystemsStopped={rainOff}");
            string only = Environment.GetEnvironmentVariable("REDLINE_GAME3D_BURST");
            var variants = new List<(string name, bool baked)>();
            if (only == "1" || only == "live3d") variants.Add(("live3d", false));
            if (only == "1" || only == "baked") variants.Add(("baked", true));
            // The baked sprite without its real key-light shadow: separates sprite sparkle from shadow-map edge sparkle.
            if (only == "1" || only == "baked") variants.Add(("baked-noshadow", true));
            // The baked sprite with the camera's texel snap not locked to it (the pre-fix behaviour), for comparison.
            if (only == "1" || only == "baked") variants.Add(("baked-unlocked", true));
            // Situation-major order so both variants of a situation are measured back to back at the same place.
            var rowsByVariant = new List<BurstResult>[variants.Count];
            for (int i = 0; i < variants.Count; i++) rowsByVariant[i] = new List<BurstResult>();
            foreach (var sit in new[] { "idle", "run" })
                for (int i = 0; i < variants.Count; i++)
                {
                    Actors3DView.UseBakedRonin = variants[i].baked;
                    if (view.Baked != null) view.Baked.CastShadows = variants[i].name != "baked-noshadow";
                    Actors3DView.LockCameraToRonin = variants[i].name != "baked-unlocked";
                    _burst = null;
                    yield return BurstRun(rig, view, variants[i].name, sit);
                    if (_burst != null) rowsByVariant[i].Add(_burst);
                }
            for (int vi = 0; vi < variants.Count; vi++)
            {
                var v = variants[vi];
                var rows = rowsByVariant[vi];
                if (rows.Count == 0) continue;
                int w = rows[0].W, h = 0;
                foreach (var r in rows) h += r.H;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                int oy = h;
                foreach (var r in rows) { oy -= r.H; tex.SetPixels32(0, oy, w, r.H, r.Pixels); }
                tex.Apply();
                File.WriteAllBytes(Path.Combine(Dir, "flicker-" + v.name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
            }
            Actors3DView.UseBakedRonin = true;
            Actors3DView.LockCameraToRonin = true;
            if (view.Baked != null) view.Baked.CastShadows = true;
            Time.captureFramerate = 0;   // back to real time for whatever runs after the burst
        }

        /// <summary>REDLINE_GAME3D_NORAIN set: the flicker burst runs with the rain particle systems stopped and cleared,
        /// so rain streaks crossing the ronin cannot count as sparkle.</summary>
        private static bool NoRain => Environment.GetEnvironmentVariable("REDLINE_GAME3D_NORAIN") != null;

        /// <summary>Stops and clears the arena's rain, far/near streaks and deck splashes (by the names Arena3DRenderer
        /// gives them). Returns how many systems were stopped.</summary>
        private static int StopRain()
        {
            int n = 0;
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                string name = ps.gameObject.name;
                if (name != "rainFar" && name != "rainNear" && name != "splashes") continue;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null) r.enabled = false;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Effects evidence (REDLINE_GAME3D_FX): one of each orb kind beside the ronin plus sparks, a kill cut with
        /// debris, a landing splash and a blast, shot two frames after spawning: GAME3D_SHOT fx.png.
        /// </summary>
        private IEnumerator FxShot()
        {
            var view = Actors3DView.Instance;
            if (view == null || view.Effects == null) yield break;
            _drive = false;
            var sim = _game.Sim;
            var p = sim.Player;
            float guard = 3f;
            while (!p.Grounded && guard > 0f) { guard -= Time.unscaledDeltaTime; p.Vx = 0f; yield return null; }
            p.Vx = 0f; p.Facing = 1;
            yield return Wait(0.3f);
            for (int k = 0; k < 3; k++) sim.SpawnOrb((OrbKind)k, new Vec2(p.X + 1.8f + 0.9f * k, p.Y + 0.6f));
            yield return null;
            var feet = view.PlayerFeet;
            var fx = view.Effects;
            fx.Sparks(feet + new Vector2(-1.4f, 1.0f), new Color(1f, 0.35f, 0.3f), 12, 9f);
            fx.Debris(feet + new Vector2(-2.8f, 0.8f), new Color(0.4f, 0.9f, 1f), 10, 6f);
            fx.Dust(feet + new Vector2(-0.6f, 0f), 6, 0f);
            fx.Pop(feet + new Vector2(4.6f, 1.0f), new Color(1f, 0.6f, 0.3f), 2.6f, 0.5f);
            yield return null;
            yield return null;
            yield return Shot("fx.png");
            _drive = true;
        }

        private BurstResult _burst;
        private static Pixel3DCamera rigForBurst() => FindAnyObjectByType<Pixel3DCamera>();

        private IEnumerator BurstRun(Pixel3DCamera rig, Actors3DView view, string variant, string situation)
        {
            const int N = 48, CW = 112, CH = 112, S = 3, Shown = 12;   // 48 texels/unit: the 72-texel ronin plus blade fits
            const float Thr = 0.12f;
            var sim = _game.Sim;
            var p = sim.Player;
            bool running = situation == "run";
            // Start every run from the level's player start (always open ground; Fight's enemy-side placement can land the
            // player inside a block), turning around when blocked. The hidden-player mask keeps enemies out of the score.
            p.X = sim.Map.PlayerStart.X; p.Y = sim.Map.PlayerStart.Y; p.Vx = 0f; p.Vy = 0f;
            float speed = sim.Config.runSpeed * 0.6f;
            int dir = 1;
            // Warm up; when running, turn around if blocked so the run row really runs (constant speed, no wall).
            float guard = 20f;
            while (sim.State != SimState.Fighting && guard > 0f) { guard -= Time.unscaledDeltaTime; p.InvulnTimer = 1f; yield return null; }
            float lastX = p.X; int blocked = 0, flips = 0;
            for (int f = 0; f < 150; f++)
            {
                p.InvulnTimer = 1f; p.Facing = dir; p.Vx = running ? speed * dir : 0f;
                yield return null;
                if (running && flips < 4)
                {
                    blocked = Mathf.Abs(p.X - lastX) < 0.005f ? blocked + 1 : 0; lastX = p.X;
                    if (blocked > 3) { dir = -dir; blocked = 0; f = 0; flips++; }
                }
            }
            float startX = p.X;
            var lum = new float[N][];
            var mask = new bool[N][];
            var rgb = new Color32[N][];
            var px = new int[N]; var py = new int[N];
            var spx = new int[N]; var spy = new int[N];   // pivot on the final output: RT texel x scale minus the slide
            var frames = new System.Text.StringBuilder();
            int cx = 0, cy = 0;
            var rt = rig.Target;
            var tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            for (int f = 0; f < N; f++)
            {
                p.InvulnTimer = 1f; p.Facing = dir; p.Vx = running ? speed * dir : 0f;
                yield return null; // WaitForEndOfFrame never resumes in batch-mode play mode; ReadRt renders itself
                var on = ReadRt(rig, tmp);
                view.SetPlayerVisible(false);
                var off = ReadRt(rig, tmp);
                view.SetPlayerVisible(true);
                Vector3 piv = rig.WorldCamera.WorldToScreenPoint(view.PlayerTransform.position);
                px[f] = Mathf.RoundToInt(piv.x); py[f] = Mathf.RoundToInt(piv.y);
                // At 1920x1080 (x3 for 640x320) whatever the batch-mode output is: PixelUpscale rounds the slide to whole pixels.
                int Ppt = Redline.Core.Presentation.PixelUpscale.IntegerScale(1920, 1080, rig.Width, rig.Height, rig.MaxOverscan);
                var rem = rig.Remainder;
                var slide = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(rem.x * Ppt + 0.5f), 0, Ppt), Mathf.Clamp(Mathf.FloorToInt(rem.y * Ppt + 0.5f), 0, Ppt));
                spx[f] = px[f] * Ppt - slide.x; spy[f] = py[f] * Ppt - slide.y;
                if (running)
                    frames.Append(FormattableString.Invariant(
                        $"\n  f={f} feetX={view.PlayerFeet.x:F5} spriteX={view.PlayerTransform.position.x:F5} camX={rig.CameraPosition.x:F5} remX={rem.x:F3} slideX={slide.x} rtX={piv.x:F2} screenX={spx[f]}"));
                // Crop follows the ronin's whole-texel pivot, so the camera's texel snap (undone on screen by the presenter's
                // sub-texel slide) does not count as motion; only what moves on the ronin itself does.
                cx = Mathf.Clamp(px[f] - CW / 2, 0, rt.width - CW);
                cy = Mathf.Clamp(py[f] - CH / 2 + 34, 0, rt.height - CH);
                lum[f] = new float[CW * CH]; mask[f] = new bool[CW * CH]; rgb[f] = new Color32[CW * CH];
                for (int y = 0; y < CH; y++)
                for (int x = 0; x < CW; x++)
                {
                    int src = (cy + y) * rt.width + cx + x, k = y * CW + x;
                    var c = on[src]; var o = off[src];
                    rgb[f][k] = c;
                    lum[f][k] = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                    mask[f][k] = Mathf.Abs(c.r - o.r) > 2 || Mathf.Abs(c.g - o.g) > 2 || Mathf.Abs(c.b - o.b) > 2;
                }
            }
            Destroy(tmp);
            double meanSum = 0, bboxSum = 0, area = 0, sparkle = 0;
            int pairs = 0;
            int minX = CW, maxX = -1, minY = CH, maxY = -1;
            for (int f = 0; f < N; f++)
                for (int k = 0; k < CW * CH; k++)
                    if (mask[f][k]) { int x = k % CW, y = k / CW; minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            int bboxArea = Mathf.Max(1, (maxX - minX + 1) * (maxY - minY + 1));
            for (int f = 0; f + 1 < N; f++)
            {
                double sum = 0; int cnt = 0;
                for (int k = 0; k < CW * CH; k++)
                    if (mask[f][k] || mask[f + 1][k]) { sum += Mathf.Abs(lum[f + 1][k] - lum[f][k]); cnt++; }
                if (cnt > 0) { meanSum += sum / cnt; pairs++; }
                double bs = 0;
                for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++) { int k = y * CW + x; bs += Mathf.Abs(lum[f + 1][k] - lum[f][k]); }
                bboxSum += bs / bboxArea;
                int a = 0; for (int k = 0; k < CW * CH; k++) if (mask[f][k]) a++;
                area += a;
            }
            int triples = 0;
            for (int f = 1; f + 1 < N; f++)
            {
                for (int k = 0; k < CW * CH; k++)
                {
                    if (!(mask[f - 1][k] || mask[f][k] || mask[f + 1][k])) continue;
                    float d1 = lum[f][k] - lum[f - 1][k], d2 = lum[f + 1][k] - lum[f][k];
                    if (Mathf.Abs(d1) > Thr && Mathf.Abs(d2) > Thr && d1 * d2 < 0f) sparkle++;
                }
                triples++;
            }
            // Screen-position jitter of the feet pivot: whole-texel steps that reverse direction (back-and-forth shimmer).
            int jitter = 0, lastDx = 0, lastDy = 0;
            var path = new System.Text.StringBuilder();
            for (int f = 1; f < N; f++)
            {
                int dx = px[f] - px[f - 1], dy = py[f] - py[f - 1];
                if (dx != 0) { if (lastDx != 0 && Math.Sign(dx) != Math.Sign(lastDx)) jitter++; lastDx = dx; }
                if (dy != 0) { if (lastDy != 0 && Math.Sign(dy) != Math.Sign(lastDy)) jitter++; lastDy = dy; }
                path.Append(dx).Append(',').Append(dy).Append(' ');
            }
            int screenJitter = 0, lastS = 0;
            var spath = new System.Text.StringBuilder();
            for (int f = 1; f < N; f++)
            {
                int dx = spx[f] - spx[f - 1];
                if (dx != 0) { if (lastS != 0 && Math.Sign(dx) != Math.Sign(lastS)) screenJitter++; lastS = dx; }
                spath.Append(dx).Append(' ');
            }
            Debug.Log($"GAME3D_FLICKER_PATH variant={variant} situation={situation} steps={path}");
            Debug.Log($"GAME3D_FLICKER_SCREEN variant={variant} situation={situation} screenJitter={screenJitter} dxOutputPx@1080p={spath}");
            if (running) Debug.Log($"GAME3D_FLICKER_FRAMES variant={variant}{frames}");
            Debug.Log($"GAME3D_FLICKER variant={variant} situation={situation} state={sim.State} dir={dir} moved={(running ? Mathf.Abs(p.X - startX) : 0f):F2} jitter={jitter} mean={meanSum / Mathf.Max(1, pairs):F5} bbox={bboxSum / (N - 1):F5} sparkle={sparkle / Mathf.Max(1, triples):F2} area={area / (N - 1):F0}");

            var strip = new Color32[Shown * CW * S * CH * S];
            int sw = Shown * CW * S;
            for (int f = 0; f < Shown; f++)
                for (int y = 0; y < CH * S; y++)
                    for (int x = 0; x < CW * S; x++)
                    {
                        var c = rgb[f][(y / S) * CW + x / S];
                        if (x % (CW * S) == 0) c = new Color32(255, 255, 255, 255);
                        strip[y * sw + f * CW * S + x] = c;
                    }
            _burst = new BurstResult { Pixels = strip, W = sw, H = CH * S };
        }

        private static Color32[] ReadRt(Pixel3DCamera rig, Texture2D tmp)
        {
            rig.WorldCamera.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rig.Target;
            tmp.ReadPixels(new Rect(0, 0, rig.Target.width, rig.Target.height), 0, 0);
            RenderTexture.active = prev;
            return tmp.GetPixels32();
        }

        /// <summary>
        /// Diagnostic (REDLINE_GAME3D_PROBE): casts camera rays through a few points of the 1920x1080 shot (letterbox
        /// 60 px) against every readable mesh and logs the nearest hit: GAME3D_PROBE lines with renderer path, sub-mesh,
        /// material, layer, rendering-layer mask, world normal and distance; then the scene's lights.
        /// </summary>
        private static void Probe(string tag)
        {
            var rig = FindAnyObjectByType<Pixel3DCamera>();
            if (rig == null) return;
            var cam = rig.WorldCamera;
            (int x, int y)[] pts = { (1800, 450), (1760, 600), (1880, 660), (1705, 420), (300, 600), (380, 540), (960, 700) };
            var filters = FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            foreach (var pt in pts)
            {
                var ray = cam.ViewportPointToRay(new Vector3(pt.x / 1920f, (1020f - pt.y) / 960f, 0f));
                float best = float.MaxValue; string info = "none";
                foreach (var mf in filters)
                {
                    var r = mf.GetComponent<MeshRenderer>();
                    var mesh = mf.sharedMesh;
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || mesh == null || !mesh.isReadable) continue;
                    if (!r.bounds.IntersectRay(ray, out float bd) || bd > best) continue;
                    var m = mf.transform.localToWorldMatrix;
                    var verts = mesh.vertices;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        var tris = mesh.GetTriangles(sub);
                        for (int i = 0; i < tris.Length; i += 3)
                        {
                            Vector3 a = m.MultiplyPoint3x4(verts[tris[i]]), b = m.MultiplyPoint3x4(verts[tris[i + 1]]), c = m.MultiplyPoint3x4(verts[tris[i + 2]]);
                            if (!RayTri(ray, a, b, c, out float t) || t >= best) continue;
                            best = t;
                            var n = Vector3.Cross(b - a, c - a).normalized;
                            var mats = r.sharedMaterials;
                            var mat = sub < mats.Length ? mats[sub] : null;
                            string path = mf.name; for (var p = mf.transform.parent; p != null && p.parent != null; p = p.parent) path = p.name + "/" + path;
                            info = $"{path} sub={sub} mat={(mat != null ? mat.name + "(" + mat.shader.name + ") base=" + (mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString("F3") : "-") + " kw=" + string.Join(",", mat.shaderKeywords) : "null")} layer={r.gameObject.layer} rlm={r.renderingLayerMask} shadows={r.shadowCastingMode}/{r.receiveShadows} n={n:F2} hit={ray.GetPoint(t):F2} dist={t:F2}";
                        }
                    }
                }
                Debug.Log($"GAME3D_PROBE {tag} px=({pt.x},{pt.y}) {info}");
            }
            var all = FindObjectsByType<Light>(FindObjectsSortMode.None);
            Debug.Log($"GAME3D_PROBE_ENV {tag} device={SystemInfo.graphicsDeviceType} shaderLevel={SystemInfo.graphicsShaderLevel} maxVisibleAdditionalLights={UniversalRenderPipeline.maxVisibleAdditionalLights} lights={all.Length}");
            foreach (var l in all)
                if (l.type == LightType.Directional || l.name.Contains("Fill") || l.name.Contains("fill") || l.name.Contains("rim"))
                    Debug.Log($"GAME3D_PROBE_LIGHT {tag} {l.name} {l.type} on={l.isActiveAndEnabled} I={l.intensity:F2} col={l.color} fwd={l.transform.forward:F2} rl={(uint)l.GetUniversalAdditionalLightData().renderingLayers} cull={l.cullingMask} shadows={l.shadows}");
        }

        private static bool RayTri(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-8f) return false;
            float inv = 1f / det;
            Vector3 tv = ray.origin - a;
            float u = Vector3.Dot(tv, pv) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(ray.direction, qv) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, qv) * inv;
            return t > 0f;
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        private IEnumerator Shot(string file)
        {
            var docs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            var targets = new List<(float order, RenderTexture rt, PanelSettings ps)>();
            var seen = new HashSet<PanelSettings>();
            foreach (var d in docs)
            {
                var ps = d.panelSettings;
                if (ps == null || !d.isActiveAndEnabled || !seen.Add(ps)) continue;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                ps.targetTexture = rt;
                ps.clearColor = true;
                ps.colorClearValue = new Color(0f, 0f, 0f, 0f);
                targets.Add((ps.sortingOrder, rt, ps));
            }
            targets.Sort((a, b) => a.order.CompareTo(b.order));
            for (int i = 0; i < 4; i++) yield return null;

            var big = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var rig = FindAnyObjectByType<Pixel3DCamera>();
            if (_game.Render3DActive && rig != null)
            {
                if (_closeTarget.HasValue) rig.SetTarget(_closeTarget.Value);
                rig.WorldCamera.Render();
                rig.PresentCamera.targetTexture = big;
                rig.PresentCamera.aspect = (float)W / H;
                rig.RefreshLayout();   // letterbox and presenter quad follow the 16:9 capture aspect
                rig.PresentCamera.Render();
                rig.PresentCamera.targetTexture = null;
                rig.PresentCamera.ResetAspect();
                rig.RefreshLayout();
            }
            else
            {
                var cam = _game.Camera;
                cam.targetTexture = big;
                cam.Render();
                cam.targetTexture = null;
            }
            var px = Read(big);
            big.Release();
            Destroy(big);
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
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log("GAME3D_SHOT " + file);
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

        private void Call(string method, object[] args = null)
        {
            var m = typeof(RedlineGame).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) Debug.LogWarning("GAME3D: no method " + method);
            else m.Invoke(_game, args);
        }

        private static void Finish(int code)
        {
            Debug.Log("GAME3D_DONE " + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit();
#endif
        }
    }
}
