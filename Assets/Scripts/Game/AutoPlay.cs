using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Redline.Core;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Redline.Game
{
    /// <summary>
    /// Dev-only autoplay for performance measurement in a player build. Inactive unless the environment variable
    /// <c>REDLINE_AUTOPLAY</c> names a level (<c>Loop01</c>, <c>Loop02</c>, <c>Loop03</c>). When set, it starts a run
    /// on that level and plays it in real time for <c>REDLINE_AUTOPLAY_SECONDS</c> (default 60) seconds, then quits.
    /// </summary>
    /// <remarks>
    /// Input goes through the game's own input layer: a virtual keyboard added to the Input System receives key
    /// states for the bound keys (move, jump, attack), so <c>RedlineInput</c> and the sim see real presses. If the
    /// window has no focus (the Input System then disables devices) and the player does not respond within 2 s, it
    /// falls back to driving the sim body directly, as <c>Game3DCapture</c> does; the mode is logged. The player is kept
    /// invulnerable, and a cleared level is reloaded so the whole window is combat. No capture, no ReadPixels.
    /// Logs <c>[AutoPlay] t=.. level=.. state=.. alive=.. onScreen=.. mode=..</c> every 5 s next to PerfProbe's lines.
    /// A comma list (<c>Loop01,Loop02,Loop03</c>) plays each level for the given seconds in turn. With <c>REDLINE_CAMTRACE</c>
    /// (or <c>REDLINE_AUTOPLAY_BOT</c>) it plays through <see cref="RedlineInput.DevFilter"/> instead (works unfocused and
    /// in a batch-mode editor) at a fixed 60 fps, adding jump-and-hook swings every few seconds, so the camera trace sees
    /// runs, jumps and swings; in a batch-mode editor it exits the editor at the end.
    /// </remarks>
    /// <example>REDLINE_PERF=1 REDLINE_AUTOPLAY=Loop02 ./Redline -screen-width 1920 -screen-height 1080 -logFile perf.log</example>
    public sealed class AutoPlay : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_AUTOPLAY";
        private static readonly string[] LevelNames = { "Loop01", "Loop02", "Loop03" };

        /// <summary>The level the autoplay is driving, or null (read by <see cref="CameraTrace"/>).</summary>
        public static string ActiveLevel { get; private set; }

        private int[] _queue;
        private int _queuePos;
        private float _levelT;
        private bool _bot;
        private InputFrame _botFrame;
        private float _hookT = -1f, _swingTimer = 2f;
        private System.Func<InputFrame, InputFrame> _filter;

        private RedlineGame _game;
        private int _levelIndex;
        private float _seconds = 60f;
        private float _t, _logTimer, _attackTimer, _jumpTimer, _stuck, _lastX, _noResponse;
        private int _dir = 1;
        private bool _direct;
        private bool _running;
        private int _maxOnScreen;
#if ENABLE_INPUT_SYSTEM
        private Keyboard _kb;
        private Key _left = Key.A, _right = Key.D, _jump = Key.Space, _attack = Key.J;
        private bool _attackDown, _jumpDown;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string level = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrEmpty(level)) return;
            var go = new GameObject("AutoPlay");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoPlay>();
        }

        private IEnumerator Start()
        {
            string[] levels = Environment.GetEnvironmentVariable(EnvVar).Split(',');
            _queue = new int[levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                _queue[i] = Array.IndexOf(LevelNames, levels[i].Trim());
                if (_queue[i] < 0) { Debug.LogError("[AutoPlay] unknown level " + levels[i]); Quit(); yield break; }
            }
            _levelIndex = _queue[0];
            _bot = Environment.GetEnvironmentVariable("REDLINE_CAMTRACE") != null || Environment.GetEnvironmentVariable("REDLINE_AUTOPLAY_BOT") != null;
            if (_bot)
            {
                Time.captureFramerate = 60;
                _filter = f => { var b = _botFrame; _botFrame.JumpPressed = _botFrame.AttackPressed = _botFrame.HookPressed = false; return b; };
                RedlineInput.DevFilter = _filter;
            }
            if (float.TryParse(Environment.GetEnvironmentVariable("REDLINE_AUTOPLAY_SECONDS"), out float s) && s > 0f) _seconds = s;
            for (int i = 0; i < 3; i++) yield return null;
            _game = FindAnyObjectByType<RedlineGame>();
            if (_game == null) { Debug.LogError("[AutoPlay] no RedlineGame"); Quit(); yield break; }
            Application.runInBackground = true;
#if ENABLE_INPUT_SYSTEM
            _kb = InputSystem.AddDevice<Keyboard>("AutoPlayKeyboard");
            _left = BoundKey(GameAction.MoveLeft, _left);
            _right = BoundKey(GameAction.MoveRight, _right);
            _jump = BoundKey(GameAction.Jump, _jump);
            _attack = BoundKey(GameAction.Attack, _attack);
#else
            _direct = true;
#endif
            Call("ShowMainMenu", new object[] { null });
            yield return null;
            Call("StartRun");
            LoadLevel();
            Debug.Log($"[AutoPlay] start level={LevelNames[_levelIndex]} seconds={_seconds} 3D={_game.Render3DActive} screen={Screen.width}x{Screen.height} bot={_bot}");
            ActiveLevel = LevelNames[_levelIndex];
            _running = true;
        }

        private void LoadLevel()
        {
            Call("TryLoadLevel", new object[] { _levelIndex });
            Call("OnLevelChanged");
        }

        private void Update()
        {
            if (!_running || _game == null || _game.Sim == null) return;
            var sim = _game.Sim;
            var p = sim.Player;
            _t += Time.unscaledDeltaTime;
            _levelT += Time.unscaledDeltaTime;
            p.InvulnTimer = 1f;
            if (_levelT >= _seconds)
            {
                _levelT = 0f;
                if (++_queuePos >= _queue.Length) { Finish(); return; }
                _levelIndex = _queue[_queuePos];
                LoadLevel();
                ActiveLevel = LevelNames[_levelIndex];
                Debug.Log("[AutoPlay] next level " + ActiveLevel);
            }
            if (sim.State == SimState.Victory) LoadLevel(); // keep fighting on the same level
            else if (sim.State == SimState.GameOver) { Call("StartRun"); LoadLevel(); }

            // Target: nearest live enemy (ring-aware); none: run back and forth.
            Enemy target = null; float best = float.MaxValue;
            int onScreen = 0;
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive) continue;
                float d = Mathf.Abs(sim.Map.Dx(p.X, e.X));
                if (d < best) { best = d; target = e; }
                if (d < 12f) onScreen++;   // about half the 3D view width at the play plane
            }
            _maxOnScreen = Mathf.Max(_maxOnScreen, onScreen);
            bool attack = false, jump = false;
            int move = _dir;
            if (target != null)
            {
                float dx = sim.Map.Dx(p.X, target.X);
                move = dx > 0f ? 1 : -1;
                if (Mathf.Abs(dx) < 1.6f) { move = 0; p.Facing = dx > 0f ? 1 : -1; }
                if (Mathf.Abs(dx) < 2.4f && _attackTimer <= 0f) { attack = true; _attackTimer = 0.28f; }
                if (target.Y > p.Y + 1.5f && _jumpTimer <= 0f && p.Grounded) { jump = true; _jumpTimer = 0.9f; }
            }
            _attackTimer -= Time.unscaledDeltaTime;
            _jumpTimer -= Time.unscaledDeltaTime;
            _stuck = move != 0 && Mathf.Abs(p.X - _lastX) < 0.01f ? _stuck + Time.unscaledDeltaTime : 0f;
            _lastX = p.X;
            if (_stuck > 0.4f && p.Grounded && _jumpTimer <= 0f) { jump = true; _jumpTimer = 0.9f; }
            if (_stuck > 1.2f) { _dir = -_dir; _stuck = 0f; }
            if (target == null && _stuck > 0.3f) _dir = -_dir;

            // Input layer not reaching the sim (unfocused window): fall back to the capture-style direct drive.
            if (!_direct)
            {
                _noResponse = !_bot && move != 0 && Mathf.Abs(p.Vx) < 0.01f ? _noResponse + Time.unscaledDeltaTime : 0f;
                if (_noResponse > 2f) { _direct = true; Debug.Log("[AutoPlay] input layer not responding (focus?); driving the sim directly"); }
            }
            if (_bot) Bot(p, move, jump, attack);
            else if (_direct) DriveDirect(sim, p, move, jump, attack);
#if ENABLE_INPUT_SYSTEM
            else Inject(move, jump, attack);
#endif

            _logTimer += Time.unscaledDeltaTime;
            if (_logTimer >= 5f)
            {
                _logTimer = 0f;
                Debug.Log($"[AutoPlay] t={_t:F0}s level={LevelNames[_levelIndex]} state={sim.State} alive={sim.EnemiesAlive} onScreen={onScreen} maxOnScreen={_maxOnScreen} mode={(_direct ? "direct" : "input")}");
                _maxOnScreen = 0;
            }
            if (_queue.Length == 1 && _t >= _seconds) Finish();
        }

        private void Finish()
        {
            _running = false;
            Debug.Log("[AutoPlay] done");
            CameraTrace.Instance?.Flush();
            ActiveLevel = null;
            Quit();
        }

        /// <summary>
        /// Bot input for the next fixed steps: the chase decision plus a jump-and-hook swing every ~3.5 s (jump, hook
        /// pressed 0.12 s later, held 0.7 s; the sim attaches only when its 60-degree ray finds a ceiling in range).
        /// </summary>
        private void Bot(Player p, int move, bool jump, bool attack)
        {
            float dt = Time.unscaledDeltaTime;
            _swingTimer -= dt;
            if (_swingTimer <= 0f && p.Grounded && move != 0) { jump = true; _hookT = 0f; _swingTimer = 3.5f; }
            bool hookPress = false, hookHeld = false;
            if (_hookT >= 0f)
            {
                _hookT += dt;
                hookPress = _hookT >= 0.12f && _hookT - dt < 0.12f;
                hookHeld = _hookT >= 0.12f && _hookT < 0.82f;
                if (_hookT >= 0.82f) _hookT = -1f;
            }
            _botFrame.MoveX = move;
            _botFrame.JumpPressed |= jump;
            _botFrame.JumpHeld = jump || (!p.Grounded && p.Vy > 0f);
            _botFrame.AttackPressed |= attack;
            _botFrame.HookPressed |= hookPress;
            _botFrame.HookHeld = hookHeld || hookPress;
            if (move != 0 && !p.Hooked) p.Facing = move;
        }

        private static void DriveDirect(RedlineSim sim, Player p, int move, bool jump, bool attack)
        {
            p.Vx = move * sim.Config.runSpeed;
            if (move != 0) p.Facing = move;
            if (jump && p.Grounded) p.Vy = sim.Config.jumpSpeed;
            if (attack) { p.AttackTimer = 0.15f; p.StrikeId++; }
        }

#if ENABLE_INPUT_SYSTEM
        private void Inject(int move, bool jump, bool attack)
        {
            // A press needs a released frame in between (wasPressedThisFrame), so presses alternate with releases.
            bool a = attack && !_attackDown, j = jump && !_jumpDown;
            _attackDown = a; _jumpDown = j;
            var keys = new List<Key>(3);
            if (move < 0) keys.Add(_left);
            if (move > 0) keys.Add(_right);
            if (a) keys.Add(_attack);
            if (j) keys.Add(_jump);
            _kb.MakeCurrent();
            InputSystem.QueueStateEvent(_kb, new KeyboardState(keys.ToArray()));
        }

        private static Key BoundKey(GameAction action, Key fallback)
        {
            var b = GameSettings.Current?.Bindings;
            if (b == null) return fallback;
            for (int s = 0; s < ControlBindings.SlotsPerAction; s++)
            {
                string name = b.Get(action, s);
                if (name != null && Enum.TryParse(name, false, out Key k) && k != Key.None) return k;
            }
            return fallback;
        }
#endif

        private void OnDestroy()
        {
            if (_filter != null && RedlineInput.DevFilter == _filter) RedlineInput.DevFilter = null;
            ActiveLevel = null;
#if ENABLE_INPUT_SYSTEM
            if (_kb != null && _kb.added) InputSystem.RemoveDevice(_kb);
#endif
        }

        private void Call(string method, object[] args = null)
        {
            var m = typeof(RedlineGame).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null) Debug.LogWarning("[AutoPlay] no method " + method);
            else m.Invoke(_game, args);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(0);
            else UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }
    }
}
