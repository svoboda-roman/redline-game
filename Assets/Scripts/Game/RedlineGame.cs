using System;
using System.Collections.Generic;
using Redline.Core;
using Redline.Game.Audio;
using Redline.Game.Render3D;
using Redline.Game.Render3D.Actors3D;
using Redline.Game.Render3D.Integration;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Owns the run: loads config and the level sequence (<see cref="Levels"/>) from Resources, steps <see cref="RedlineSim"/> at a
    /// fixed 60 Hz, interpolates between steps for drawing, and routes simulation events to the
    /// renderers (<see cref="ArenaRenderer"/>, <see cref="ActorRenderer"/>,
    /// <see cref="EffectsRenderer"/>) and <see cref="PostFx"/>. All rules live in Redline.Core.
    /// </summary>
    /// <remarks>Created by <see cref="RedlineBootstrap"/>. Main-thread only.</remarks>
    public sealed partial class RedlineGame : MonoBehaviour
    {
        /// <summary>
        /// The levels played in order (Resources text files). After a level's victory moment
        /// (levelAdvanceDelay) the next one loads with a fresh, fully healed player; a missing or
        /// invalid level is skipped with a logged warning. After the last one the victory stays.
        /// </summary>
        private static readonly string[] Levels = { "City01B", "City01", "Loop01", "Loop02", "Loop03" };   // City01B (main, remix) / City01 (as drawn) are endless: the run ends only on death
        /// <summary>
        /// Half the view height in tiles: a close camera that follows the player. 6.75 frames the
        /// 40 px-per-tile character art at an exact 2× on a 1080p screen (13.5 tiles × 40 px = 540 px).
        /// </summary>
        private const float CameraSize = 10.1f;   // classic 2D view; zoomed out 50% with the 3D view (Pixel3DCamera.FitScreen)
        /// <summary>Seconds of the player's velocity the camera looks ahead by.</summary>
        private const float Lookahead = 0.35f;
        private static readonly Color Background = new Color(0.01f, 0.01f, 0.04f);
        private static readonly Color GruntDebris = new Color(1f, 0.55f, 0.15f);
        private static readonly Color BruteDebris = new Color(1f, 0.2f, 0.28f);
        private static readonly Color DroneDebris = new Color(0.78f, 0.45f, 1f);
        private static readonly Color WindupColor = new Color(1f, 0.85f, 0.4f);
        private static readonly Color HurtColor = new Color(1f, 0.25f, 0.4f);
        private static readonly Color ClangColor = new Color(0.7f, 0.8f, 1f);

        /// <summary>
        /// Colour of each speed tier (index = tier - 1). Shared with the HUD and renderers. Art bible section 4:
        /// tier 1 cold blue #9CC8FF, tier 2 amber #FFB347, tier 3 white-hot #FFF4E8 (luminance rises with tier).
        /// </summary>
        public static readonly Color[] TierColors =
        {
            new Color(0.612f, 0.784f, 1f),
            new Color(1f, 0.702f, 0.278f),
            new Color(1f, 0.957f, 0.91f)
        };

        /// <summary>Where the game is: the sim steps only while Playing (and no upgrade screen is up).</summary>
        private enum Flow { Menu, Playing, Paused, Upgrades }

        private RedlineInput _input;
        private Flow _flow = Flow.Menu;
        /// <summary>Frame of the last flow change: a pause toggle on that frame is ignored (no open-and-close in one press).</summary>
        private int _flowFrame = -1;
        private MenuScreen _mainMenu;
        private MenuScreen _pauseMenu;
        private SettingsScreen _settingsScreen;
        /// <summary>The upgrade screen was opened from the main menu, so its CONTINUE goes back there.</summary>
        private bool _upgradesReturnToMenu;
        private Action _onSettingsChanged;
        // Positions before the latest fixed step, for drawing between steps (render interpolation).
        private readonly List<Vector2> _prevEnemyFeet = new List<Vector2>(32);
        private readonly List<Vector2> _drawEnemyFeet = new List<Vector2>(32);
        private Vector2 _prevPlayerFeet;
        private int _prevRunId = -1;

        private Camera _camera;
        private Transform _world;
        private GameObject _arenaHost;
        private ArenaRenderer _arena;
        private RedlineConfig _config;
        private int _levelIndex;
        private float _victoryTimer;
        /// <summary>The last level's victory: nothing left to load.</summary>
        private bool _finalVictory;
        private IGameActors _actors;
        private IGameEffects _effects;
        private PostFx _postFx;
        private IGameOrbs _orbs;
        private RedlineHud _hud;
        private RedlineAudio _audio;
        private Func<Vector2, Vector2> _toDraw;

        // Meta-progression: the base config stays untouched; each run plays an upgraded copy.
        private UpgradeConfig _upgradeCfg;
        private UpgradeStore _store;
        private UpgradeScreen _screen;
        private RedlineConfig _runConfig;
        private RunResult _run;
        /// <summary>Points for this run were awarded (the upgrade screen is up or was shown).</summary>
        private bool _runEnded;
        /// <summary>The next WaveStart of this level is its first, so it does not count a cleared wave.</summary>
        private bool _firstWaveOfLevel;

        private float _accumulator;
        private float _hitStop;
        private float _shake;
        private float _camX;
        private float _camY;
        private float _lookahead;
        private float _afterimageTimer;
        private float _speedLineTimer;

        /// <summary>The running simulation (read by the HUD).</summary>
        public RedlineSim Sim { get; private set; }

        // Tutorial hints: level zones and first-sight cards, text tokens resolved to the bound keys.
        private TutorialOverlay _tutorial;
        private Func<string, string> _hintLabels;

        // The training level (Resources/Training.txt + TrainingSteps.json): non-null while it runs.
        private TrainingScript _training;
        private TrainingOverlay _trainingOverlay;
        private MenuScreen _trainingPauseMenu;
        private float _trainingCompleteTimer;

        /// <summary>True while the training level runs (no waves, no points; the HUD hides the wave panel).</summary>
        public bool InTraining => _training != null;

        /// <summary>Resolves a hint token ("HOOK", "JUMP"…) to the key bound to that action, or null.</summary>
        private static string HintLabel(string token) =>
            Enum.TryParse<GameAction>(token, true, out var action) ? ControlLabels.Get(action) : null;

        /// <summary>Writes the hints already shown into the settings file.</summary>
        private void SaveTutorialProgress()
        {
            if (_tutorial == null || _tutorial.Progress == null) return;
            GameSettings.Current.TutorialSeen = _tutorial.Progress.Serialize();
            if (!GameSettings.Save()) Debug.LogWarning("Redline: could not save tutorial progress.");
        }

        private void Awake()
        {
            Application.targetFrameRate = 120;
            GameSettings.Load();
            _input = new RedlineInput(GameSettings.Current.Bindings);
            _config = LoadConfig();
            _upgradeCfg = UpgradeStore.LoadConfig(_config);
            _store = new UpgradeStore(_upgradeCfg);
            BeginRunState();
            if (!TryLoadLevel(0)) throw new InvalidOperationException("Redline: no playable level in Resources (" + string.Join(", ", Levels) + ").");

            _world = new GameObject("World").transform;
            _world.SetParent(transform, false);
            SetupCamera();
            _audio = gameObject.AddComponent<RedlineAudio>();
            _audio.Init(_camera);
            BuildViews(); // the arena, effects, actors and orbs in the chosen render mode (Render3D region)
            _toDraw = ToDraw;
            _postFx = gameObject.AddComponent<PostFx>();
            _postFx.Init(_camera);

            _hud = gameObject.AddComponent<RedlineHud>();
            _hud.Init(this);
            _hintLabels = HintLabel;
            _tutorial = gameObject.AddComponent<TutorialOverlay>();
            _tutorial.Init(TutorialOverlay.LoadCatalog(), TutorialProgress.Deserialize(GameSettings.Current.TutorialSeen), _hintLabels);
            _trainingOverlay = gameObject.AddComponent<TrainingOverlay>();
            _trainingOverlay.Init(this, _hintLabels);
            _screen = gameObject.AddComponent<UpgradeScreen>();
            _screen.Init(_store);
            _screen.Continued += OnUpgradesContinue;
            _screen.MenuRequested += () => ShowMainMenu(null);
            BuildMenus();
            _onSettingsChanged = ApplySettings;
            GameSettings.Changed += _onSettingsChanged;
            ApplySettings();
            ShowMainMenu(null);
            // First launch (or an older settings file): the training runs before anything else.
            if (!GameSettings.Current.TrainingDone) StartTraining();
            if (RedlineCapture.Requested) gameObject.AddComponent<RedlineCapture>().Init(this);
        }

        private void Update()
        {
            _input.Poll();
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            SyncPresentation();

            // Pause toggles (bound pause key or gamepad Start); the menus read their own input.
            bool canToggle = Time.frameCount != _flowFrame && !_settingsScreen.Visible;
            if (canToggle && _input.PausePressed)
            {
                if (_flow == Flow.Playing) Pause();
                else if (_flow == Flow.Paused) Resume();
            }

            // F2 while playing: jump to the next map (wraps), to try the levels without beating an endless one.
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (_flow == Flow.Playing && kb != null && kb.f2Key.wasPressedThisFrame)
            {
                int next = (_levelIndex + 1) % Levels.Length;
                if (TryLoadLevel(next) || TryLoadLevel(0)) OnLevelChanged();
            }
#endif

            // The sim steps only in Playing; in menus the presses are dropped so they cannot reach the run.
            bool stepping = _flow == Flow.Playing && !_screen.Visible;
            if (!stepping)
            {
                _input.ClearLatched();
                _accumulator = 0f;
            }
            else if (_hitStop > 0f) _hitStop -= dt;
            else _accumulator += dt;
            while (_accumulator >= RedlineSim.FixedDt)
            {
                var frame = _input.Consume();
                if (frame.RestartPressed && _training == null)
                {
                    // R / Select ends the run (retry after game over, play again after the last
                    // level, or abandon mid-run): points are awarded and the upgrade screen opens.
                    // The sim never restarts itself, so a run cannot farm a level for points.
                    frame.RestartPressed = false;
                    EndRun();
                    _accumulator = 0f;
                    break;
                }
                CapturePrevious();
                bool wasGrounded = Sim.Player.Grounded;
                float fallSpeed = -Sim.Player.Vy;
                // Training: the script runs the sim (Restart held skips a step instead of ending a run).
                if (_training != null) _training.Step(frame, RedlineSim.FixedDt);
                else Sim.Step(frame, RedlineSim.FixedDt);
                if (!wasGrounded && Sim.Player.Grounded && fallSpeed > 6f) OnLanded(fallSpeed);
                HandleEvents();
                // Per step: orb pickups are one-step events. The training teaches with its own cards.
                if (_training == null) _tutorial.Step(Sim, GameSettings.HintsEnabled);
                _audio.Process(Sim, Sim.Events);
                _run.HighestTier = Math.Max(_run.HighestTier, Sim.PlayerTier);
                _accumulator -= RedlineSim.FixedDt;
            }
            _tutorial.Tick(Time.unscaledDeltaTime, paused: !stepping);
            _trainingOverlay.Tick(Time.unscaledDeltaTime, paused: !stepping);
            if (stepping && _training != null) TickTrainingEnd(dt);
            _audio.Tick(Sim, Time.unscaledDeltaTime);
            if (_flow == Flow.Paused) return; // the whole view is frozen behind the pause menu
            if (stepping) AdvanceLevelAfterVictory(dt);

            if (_render3D && MenuBackdropFlow) _pixelCam.SetTarget(MenuBackdrop.Drift(Time.time, Sim.Map, _camX, _camY)); // live menu backdrop
            else UpdateCamera(dt);
            var feet = InterpolatedFeet();
            _actors.Sync(Sim, feet, _drawEnemyFeet, dt);
            _orbs.Sync(Sim, _toDraw, Time.time);
            if (_render3D) _arena3D?.Update(dt, _pixelCam.CameraPosition);
            else _arena.Update(dt, _camera.transform.position);
            SpawnSpeedTrail(dt);
            _effects.Update(dt);
        }

        // ------------------------------------------------------------------ interpolation

        private void CapturePrevious()
        {
            var p = Sim.Player;
            _prevPlayerFeet = new Vector2(p.X, p.Y);
            _prevRunId = Sim.RunId;
            _prevEnemyFeet.Clear();
            var enemies = Sim.Enemies;
            for (int i = 0; i < enemies.Count; i++) _prevEnemyFeet.Add(new Vector2(enemies[i].X, enemies[i].Y));
        }

        /// <summary>Blends the last two fixed steps; fills the enemy list and returns the player's feet.</summary>
        private Vector2 InterpolatedFeet()
        {
            float alpha = Mathf.Clamp01(_accumulator / RedlineSim.FixedDt);
            bool sameRun = _prevRunId == Sim.RunId;
            var p = Sim.Player;
            var enemies = Sim.Enemies;
            bool enemiesMatch = sameRun && _prevEnemyFeet.Count == enemies.Count;
            _drawEnemyFeet.Clear();
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = new Vector2(enemies[i].X, enemies[i].Y);
                _drawEnemyFeet.Add(ToDraw(enemiesMatch ? Blend(_prevEnemyFeet[i], e, alpha) : e));
            }
            var now = new Vector2(p.X, p.Y);
            return ToDraw(sameRun ? Blend(_prevPlayerFeet, now, alpha) : now);
        }

        /// <summary>Blends two step positions; on a ring it goes the short way across the seam.</summary>
        private Vector2 Blend(Vector2 prev, Vector2 now, float alpha)
        {
            float dx = Sim.Map.Dx(prev.x, now.x);
            return new Vector2(now.x - (1f - alpha) * dx, Mathf.Lerp(prev.y, now.y, alpha));
        }

        /// <summary>
        /// Simulation position -> drawing position. On a ring the copy nearest the camera is drawn,
        /// so the level has no visible seam.
        /// </summary>
        public Vector2 ToDraw(Vector2 world) =>
            Sim.Map.WrapsX ? new Vector2(_camX + Sim.Map.Dx(_camX, world.x), world.y) : world;

        /// <summary>The game camera (for HUD tooling).</summary>
        public Camera Camera => _render3D && _pixelCam != null ? _pixelCam.WorldCamera : _camera;

        /// <summary>Name of the level being played (its Resources file, e.g. "Loop01").</summary>
        public string LevelName => Levels[_levelIndex];

        /// <summary>1-based position of the current level in the sequence.</summary>
        public int LevelNumber => _levelIndex + 1;

        /// <summary>Number of levels in the sequence.</summary>
        public int LevelCount => Levels.Length;

        /// <summary>
        /// The level that follows this one, or null on the last. Names the next file in the list;
        /// if that file turns out to be missing or invalid, a later one is loaded instead.
        /// </summary>
        public string NextLevelName => _levelIndex + 1 < Levels.Length ? Levels[_levelIndex + 1] : null;

        private Vector2 ToDraw(Vec2 world) => ToDraw(new Vector2(world.X, world.Y));

        // ------------------------------------------------------------------ loading

        private static RedlineConfig LoadConfig()
        {
            var config = new RedlineConfig();
            var asset = Resources.Load<TextAsset>("RedlineConfig");
            if (asset == null)
            {
                Debug.LogWarning("Redline: Resources/RedlineConfig.json not found, using built-in defaults.");
                return config;
            }
            try
            {
                JsonUtility.FromJsonOverwrite(asset.text, config);
            }
            catch (ArgumentException ex)
            {
                Debug.LogError("Redline: RedlineConfig.json is not valid JSON, using built-in defaults: " + ex.Message);
                return new RedlineConfig();
            }
            var errors = config.Validate();
            if (errors.Count == 0) return config;
            Debug.LogError("Redline: RedlineConfig.json is invalid, using built-in defaults: " + string.Join("; ", errors));
            return new RedlineConfig();
        }

        /// <summary>
        /// Makes <see cref="Sim"/> the first playable level at or after <paramref name="index"/>.
        /// Missing or invalid levels are skipped with a logged warning. False when none is left.
        /// </summary>
        private bool TryLoadLevel(int index)
        {
            for (int i = index; i < Levels.Length; i++)
            {
                string name = Levels[i];
                var asset = Resources.Load<TextAsset>(name);
                if (asset == null)
                {
                    Debug.LogWarning("Redline: Resources/" + name + ".txt is missing; skipping the level.");
                    continue;
                }
                try
                {
                    Sim = new RedlineSim(_runConfig, TileMap.Parse(asset.text));
                }
                catch (FormatException ex)
                {
                    Debug.LogWarning("Redline: " + name + ".txt is not a valid level; skipping it: " + ex.Message);
                    continue;
                }
                catch (ArgumentException ex)
                {
                    Debug.LogWarning("Redline: " + name + ".txt cannot be played yet; skipping it: " + ex.Message);
                    continue;
                }
                _levelIndex = i;
                _victoryTimer = 0f;
                _finalVictory = false;
                _firstWaveOfLevel = true;
                return true;
            }
            return false;
        }

        /// <summary>Resets the run counters and builds this run's config from the bought upgrades.</summary>
        private void BeginRunState()
        {
            _runConfig = Upgrades.Apply(_config, _upgradeCfg, _store.State);
            _run = default;
            _runEnded = false;
        }

        /// <summary>A fresh, not yet started run from the first level with the current upgrades.</summary>
        private void PrepareRun()
        {
            BeginRunState();
            if (!TryLoadLevel(0)) throw new InvalidOperationException("Redline: no playable level in Resources.");
            OnLevelChanged();
        }

        /// <summary>PLAY, or the upgrade screen's CONTINUE after a run: a fresh run starts stepping.</summary>
        private void StartRun()
        {
            _mainMenu.Hide();
            _screen.Hide();
            _audio.SetPaused(false);
            PrepareRun();
            _tutorial.BeginRun();
            SetFlow(Flow.Playing);
        }

        /// <summary>
        /// Awards this run's points once and saves them. Returns the points (0 if already awarded).
        /// Every way out of a run (R, ABANDON RUN, QUIT TO MENU) goes through here exactly once, and a
        /// new run always restarts at the first level, so leaving can neither lose nor farm points.
        /// </summary>
        private int AwardRun()
        {
            if (_runEnded) return 0;
            _runEnded = true;
            _tutorial.Hide();
            SaveTutorialProgress();
            int earned = _store.State.AwardRun(_run, _upgradeCfg);
            if (!_store.Save()) Debug.LogWarning("Redline: could not save upgrade progress.");
            return earned;
        }

        /// <summary>Awards this run's points once, saves them and opens the upgrade screen (CONTINUE = new run).</summary>
        private void EndRun()
        {
            if (_runEnded) return;
            int earned = AwardRun();
            _upgradesReturnToMenu = false;
            _screen.Show(_run, earned, true);
            SetFlow(Flow.Upgrades);
        }

        // ------------------------------------------------------------------ menus and flow

        private bool _hudPaused;

        /// <summary>
        /// Derives the presentation state from the flow and the sim every frame, so no flow path can forget it
        /// (art bible sections 2 and 7): the 2.4:1 cinematic letterbox in menus, upgrades, between waves and at
        /// the end of a run (2.0:1 in play); the key light by tier; the calm grade between waves; the HUD's
        /// picture rect and its paused dim. Reduced Motion makes the bar change instant.
        /// </summary>
        private void SyncPresentation()
        {
            bool render3D = _render3D && _pixelCam != null;
            LetterboxInfo.BarPixels = render3D ? _pixelCam.LetterboxPixels : -1f;
            if (render3D) LetterboxInfo.SetPicture(_pixelCam.PictureFraction, _pixelCam.ViewportFraction); // integer upscale: picture incl. side bars
            if (_hud != null && _hudPaused != (_flow == Flow.Paused))
            {
                _hudPaused = _flow == Flow.Paused;
                _hud.SetPaused(_hudPaused);
            }
            if (_hud != null) MenuBackdrop.SetHidden(_hud.Root, _flow == Flow.Menu || _flow == Flow.Upgrades); // no HUD over the menu backdrop
            if (!render3D || Sim == null) return;
            var state = Sim.State;
            bool betweenWaves = _training == null && state == SimState.WaveBreak;
            bool runOver = state == SimState.Victory || state == SimState.GameOver;
            _pixelCam.CinematicSeconds = UiTheme.ReducedMotion ? 0f : 0.4f;
            _pixelCam.SetCinematic(_flow == Flow.Menu || _flow == Flow.Upgrades || betweenWaves || runOver);
            _pixelCam.SetCalm(betweenWaves);
            if (MenuBackdropFlow) _pixelCam.SetCalm(false); // menu backdrop: full level grade (the sim idles in WaveBreak behind it)
            _pixelCam.SetTier(Sim.PlayerTier);
        }

        private bool MenuBackdropFlow => _flow == Flow.Menu || (_flow == Flow.Upgrades && _upgradesReturnToMenu); // menu, or a screen opened from it

        private void SetFlow(Flow flow)
        {
            _flow = flow;
            _flowFrame = Time.frameCount;
        }

        private void BuildMenus()
        {
            _settingsScreen = gameObject.AddComponent<SettingsScreen>();
            _settingsScreen.Init(_audio.PlayPreview);

            _mainMenu = gameObject.AddComponent<MenuScreen>();
            _mainMenu.Init("MainMenu", 90f, "REDLINE", true);
            _mainMenu.AddItem("PLAY", StartRun);
            _mainMenu.AddItem("TUTORIAL", StartTraining);
            _mainMenu.AddItem("UPGRADES", OpenUpgradesFromMenu);
            _mainMenu.AddItem("SETTINGS", () => OpenSettings(_mainMenu));
            _mainMenu.AddItem("QUIT", QuitGame);

            _pauseMenu = gameObject.AddComponent<MenuScreen>();
            _pauseMenu.Init("PauseMenu", 110f, "PAUSED", false);
            _pauseMenu.AddItem("RESUME", Resume);
            _pauseMenu.AddItem("SETTINGS", () => OpenSettings(_pauseMenu));
            _pauseMenu.AddItem("ABANDON RUN", AbandonRun);
            _pauseMenu.AddItem("QUIT TO MENU", QuitToMenu);
            _pauseMenu.Cancelled = Resume;

            _trainingPauseMenu = gameObject.AddComponent<MenuScreen>();
            _trainingPauseMenu.Init("TrainingPauseMenu", 110f, "PAUSED", false);
            _trainingPauseMenu.AddItem("RESUME", Resume);
            _trainingPauseMenu.AddItem("SETTINGS", () => OpenSettings(_trainingPauseMenu));
            _trainingPauseMenu.AddItem("SKIP TUTORIAL", () => FinishTraining(false));
            _trainingPauseMenu.Cancelled = Resume;
        }

        /// <summary>The title menu over a fresh, frozen level. <paramref name="notice"/> is an optional line under the title.</summary>
        private void ShowMainMenu(string notice)
        {
            _pauseMenu.Hide();
            _trainingPauseMenu.Hide();
            _settingsScreen.Hide();
            _screen.Hide();
            _audio.SetPaused(false);
            _audio.StopEffects();
            _tutorial.Hide();
            _training = null;
            _trainingOverlay.Bind(null);
            PrepareRun();
            _mainMenu.SetNotice(notice);
            _mainMenu.Show();
            SetFlow(Flow.Menu);
        }

        private void OpenUpgradesFromMenu()
        {
            _mainMenu.Hide();
            _upgradesReturnToMenu = true;
            _screen.Show();
            SetFlow(Flow.Upgrades);
        }

        private void OnUpgradesContinue()
        {
            if (_upgradesReturnToMenu) ShowMainMenu(null);
            else StartRun();
        }

        private void OpenSettings(MenuScreen from)
        {
            from.Hide();
            _settingsScreen.Show(() =>
            {
                from.Show();
                SetFlow(_flow); // the Esc that closed the settings must not also toggle pause
            });
        }

        private void Pause()
        {
            if (_flow != Flow.Playing) return;
            _audio.SetPaused(true);
            (_training != null ? _trainingPauseMenu : _pauseMenu).Show();
            SetFlow(Flow.Paused);
        }

        private void Resume()
        {
            if (_flow != Flow.Paused) return;
            _pauseMenu.Hide();
            _trainingPauseMenu.Hide();
            _audio.SetPaused(false);
            _accumulator = 0f;
            SetFlow(Flow.Playing);
        }

        /// <summary>ABANDON RUN: the same path as R / Select (points awarded, upgrade screen opens).</summary>
        private void AbandonRun()
        {
            _pauseMenu.Hide();
            _audio.SetPaused(false);
            _audio.StopEffects();
            EndRun();
        }

        /// <summary>QUIT TO MENU: awards the run's points (as EndRun does) and goes straight to the title menu.</summary>
        private void QuitToMenu()
        {
            int earned = AwardRun();
            ShowMainMenu(earned > 0 ? "+" + earned + " POINTS BANKED FROM THE LAST RUN" : "THE LAST RUN EARNED NO POINTS");
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Applies the volumes and fullscreen of <see cref="GameSettings.Current"/>.</summary>
        private void ApplySettings()
        {
            var s = GameSettings.Current;
            _audio.SetVolumes(s.MasterVolume / 100f, s.MusicVolume / 100f, s.SfxVolume / 100f);
            if (Screen.fullScreen != s.Fullscreen) Screen.fullScreen = s.Fullscreen;
            if (_tutorial != null) _tutorial.SetLabels(_hintLabels); // keys may have been rebound
            if (_flow == Flow.Menu && _world != null && Want3D() != _render3D) BuildViews(); // render mode: now in the menu, else at the next level load
            if (_trainingOverlay != null) _trainingOverlay.SetLabels(_hintLabels);
        }

        private void OnApplicationQuit() => SaveTutorialProgress();

        private void OnDestroy()
        {
            if (_onSettingsChanged != null) GameSettings.Changed -= _onSettingsChanged;
            Render3DMode.Disable();
        }

        // ------------------------------------------------------------------ training

        /// <summary>
        /// TUTORIAL, or the first launch: loads the training level with the base config (no upgrades)
        /// and runs it through a <see cref="TrainingScript"/>. It earns no points and is not part of
        /// the Loop01-03 sequence. Missing or invalid training data logs an error and stays in the menu.
        /// </summary>
        private void StartTraining()
        {
            var data = TrainingOverlay.LoadData();
            var asset = Resources.Load<TextAsset>("Training");
            if (data == null || asset == null)
            {
                if (asset == null) Debug.LogError("Redline: Resources/Training.txt not found; the training is unavailable.");
                return;
            }
            TrainingScript script;
            try
            {
                var map = TileMap.Parse(asset.text);
                var errors = data.Validate(map);
                if (errors.Count > 0)
                {
                    Debug.LogError("Redline: TrainingSteps.json does not fit Training.txt: " + string.Join("; ", errors));
                    return;
                }
                script = new TrainingScript(new RedlineSim(_config, map), data);
            }
            catch (FormatException ex)
            {
                Debug.LogError("Redline: Training.txt is not a valid level: " + ex.Message);
                return;
            }
            catch (ArgumentException ex)
            {
                Debug.LogError("Redline: the training cannot run: " + ex.Message);
                return;
            }
            _mainMenu.Hide();
            _pauseMenu.Hide();
            _trainingPauseMenu.Hide();
            _screen.Hide();
            _tutorial.Hide();
            _audio.SetPaused(false);
            _audio.StopEffects();
            _training = script;
            Sim = script.Sim;
            _run = default;
            _runEnded = true; // the training awards nothing: AwardRun and EndRun are no-ops
            _victoryTimer = 0f;
            _trainingCompleteTimer = 0f;
            OnLevelChanged();
            _trainingOverlay.Bind(script);
            SetFlow(Flow.Playing);
        }

        /// <summary>After the last step the "TRAINING COMPLETE" card shows for completeTime, then the main menu.</summary>
        private void TickTrainingEnd(float dt)
        {
            if (_training.Phase != TrainingPhase.Complete) return;
            _trainingCompleteTimer += dt;
            if (_trainingCompleteTimer >= _training.Data.Settings.completeTime) FinishTraining(true);
        }

        /// <summary>Marks the training done (finished or SKIP TUTORIAL), saves it and returns to the main menu.</summary>
        private void FinishTraining(bool completed)
        {
            GameSettings.Current.TrainingDone = true;
            if (!GameSettings.Save()) Debug.LogWarning("Redline: could not save the training progress.");
            ShowMainMenu(completed ? "TRAINING COMPLETE  ·  PRESS PLAY TO START A RUN" : null);
        }

        /// <summary>After a level's victory moment, loads the next level; after the last, the victory stays.</summary>
        private void AdvanceLevelAfterVictory(float dt)
        {
            if (Sim.State != SimState.Victory)
            {
                _victoryTimer = 0f;
                return;
            }
            if (_finalVictory) return;
            _victoryTimer += dt;
            if (_victoryTimer < _config.levelAdvanceDelay) return;
            if (TryLoadLevel(_levelIndex + 1)) OnLevelChanged();
            else _finalVictory = true;
        }

        /// <summary>A new Sim replaced the old one: rebuild the level view and snap the camera.</summary>
        private void OnLevelChanged()
        {
            if (Want3D() != _render3D) BuildViews(); // also rebuilds the arena
            else BuildArena();
            _prevRunId = -1;
            _accumulator = 0f;
            _camX = Sim.Player.X;
            _camY = Sim.Player.Y + 2.5f;
            _lookahead = 0f;
        }

        /// <summary>(Re)builds the arena view for the current map under its own host object.</summary>
        private void BuildArena()
        {
            _arena3D?.Dispose();
            _arena3D = null;
            if (_arenaHost != null) Destroy(_arenaHost);
            _arenaHost = new GameObject("Level " + Levels[_levelIndex]);
            _arenaHost.transform.SetParent(_world, false);
            if (_render3D) BuildArena3D();
            else _arena = new ArenaRenderer(_arenaHost.transform, Sim.Map);
        }

        // ------------------------------------------------------------------ camera

        private void SetupCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = go.AddComponent<Camera>();
            }
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Background;
            _camX = Sim.Player.X;
            _camY = Sim.Player.Y + 2.5f;
        }

        /// <summary>
        /// Follows the player closely, looking ahead in the running direction; vertically it keeps
        /// the player in the lower half and stays inside the level. Screen shake rides on top.
        /// </summary>
        private void UpdateCamera(float dt)
        {
            if (_render3D) { UpdateCamera3D(dt); return; } // 3D: CameraFollow (RedlineGame.Camera3D.cs)
            var p = Sim.Player;
            var map = Sim.Map;
            _camera.orthographicSize = CameraSize;
            _lookahead = Mathf.Lerp(_lookahead, Mathf.Clamp(p.Vx * Lookahead, -5f, 5f), 1f - Mathf.Exp(-dt * 3f));
            float targetX = _camX + map.Dx(_camX, p.X) + _lookahead;
            float targetY = p.Y + 2.5f;
            _camX = Mathf.Lerp(_camX, targetX, 1f - Mathf.Exp(-dt * 7f));
            _camY = Mathf.Lerp(_camY, targetY, 1f - Mathf.Exp(-dt * 5f));
            float halfW = CameraSize * Mathf.Max(0.1f, _camera.aspect);
            if (!map.WrapsX) _camX = ClampView(_camX, halfW, map.Width);
            _camY = ClampView(_camY, CameraSize, map.Height);

            _shake = Mathf.Max(0f, _shake - dt * 1.5f);
            var offset = _shake > 0f
                ? new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), 0f) * _shake
                : Vector3.zero;
            _camera.transform.position = new Vector3(_camX, _camY, -10f) + offset;
            if (_render3D) _pixelCam.SetTarget(new Vector2(_camX + offset.x, _camY + offset.y));
        }

        /// <summary>Keeps a view of half-size <paramref name="half"/> inside [-1, size+1], centring when it is larger.</summary>
        private static float ClampView(float centre, float half, float size)
        {
            float min = half - 1f;
            float max = size + 1f - half;
            return min > max ? size * 0.5f : Mathf.Clamp(centre, min, max);
        }

        // ------------------------------------------------------------------ feedback

        private void HandleEvents()
        {
            var events = Sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                var at = ToDraw(ev.Position);
                var color = TierColors[Mathf.Clamp(ev.Tier, 1, 3) - 1];
                switch (ev.Type)
                {
                    case SimEventType.Hit: OnHit(at, color, ev.Tier); break;
                    case SimEventType.Kill:
                        _run.Kills++;
                        OnKill(at, color, ev.Tier, (EnemyKind)ev.Detail, ev.Position);
                        break;
                    case SimEventType.Clang: OnClang(at); break;
                    case SimEventType.PlayerHurt: OnHurt(at); break;
                    case SimEventType.GameOver: OnGameOver(at); break;
                    case SimEventType.Jump: OnJump(); break;
                    case SimEventType.WallJump: OnWallJump(color); break;
                    case SimEventType.Slide: OnSlide(color); break;
                    case SimEventType.HookAttach: _effects.Sparks(at, color, 8, 5f); break;
                    case SimEventType.WaveStart:
                        // A wave starting means the previous one of this level was cleared.
                        if (!_firstWaveOfLevel) _run.WavesCleared++;
                        _firstWaveOfLevel = false;
                        _effects.Flash(0.08f);
                        break;
                    case SimEventType.Victory:
                        _run.WavesCleared++; // the level's last wave
                        _run.LevelsCleared++;
                        _effects.Flash(0.25f);
                        break;
                    case SimEventType.BruteWindup: _effects.Sparks(at, WindupColor, 6, 3f); break;
                    case SimEventType.BruteCrash: OnBruteCrash(at); break;
                    case SimEventType.OrbSpawn: _effects.Sparks(at, OrbRenderer.KindColor((OrbKind)ev.Detail), 5, 3f); break;
                    case SimEventType.OrbPickup: OnOrbPickup(at, (OrbKind)ev.Detail); break;
                    case SimEventType.OrbExpire: _effects.Pop(at, OrbRenderer.KindColor((OrbKind)ev.Detail) * 0.6f, 0.5f, 0.3f); break;
                    case SimEventType.ShooterWindup: _effects.Sparks(at, WindupColor, 4, 2f); break;
                    case SimEventType.Shoot: _effects.Pop(at, new Color(1f, 0.4f, 0.15f), 0.5f, 0.12f); break;
                    case SimEventType.ProjectileReflect:
                        _effects.Sparks(at, color, 10, 7f);
                        _hitStop = Mathf.Max(_hitStop, 0.04f);
                        break;
                    case SimEventType.ProjectileHit: _effects.Pop(at, new Color(1f, 0.5f, 0.2f) * 0.8f, 0.4f, 0.15f); break;
                    case SimEventType.ShieldBlock: _effects.Sparks(at, new Color(0.95f, 0.6f, 0.35f), 8, 5f); break;
                    case SimEventType.KamikazeFuse: _effects.Sparks(at, new Color(1f, 0.22f, 0.2f), 6, 3f); break;
                    case SimEventType.Explode:
                        _effects.Pop(at, new Color(1f, 0.45f, 0.2f), Sim.Config.kamikazeBlastRadius * 2f, 0.35f);
                        _effects.Debris(at, new Color(1f, 0.3f, 0.2f), 14, 9f);
                        _shake = Mathf.Max(_shake, 0.35f);
                        _effects.Flash(0.15f);
                        break;
                    case SimEventType.SummonerCast: _effects.Sparks(at, new Color(0.55f, 0.4f, 1f), 6, 2.5f); break;
                    case SimEventType.Summon: _effects.Pop(at, new Color(0.78f, 0.45f, 1f), 1.2f, 0.3f); break;
                }
            }
        }

        private void OnHit(Vector2 at, Color color, int tier)
        {
            _effects.Sparks(at, color, 6 + 3 * tier, 6f + 3f * tier);
            _shake = Mathf.Max(_shake, 0.05f * tier);
        }

        /// <summary>
        /// A kill: the white cut-flash of the blade (no debris pixels) and blood, sprayed away from the player with
        /// splatters on the floor under the body (drones are machines: sparks and debris as before).
        /// </summary>
        private void OnKill(Vector2 at, Color color, int tier, EnemyKind kind, Vec2 simPos)
        {
            bool brute = kind == EnemyKind.Brute;
            if (kind == EnemyKind.Drone)
            {
                _effects.Sparks(at, color, 10 + 6 * tier, 8f + 4f * tier);
                _effects.Debris(at, DroneDebris, 9, 7f + 2f * tier);
            }
            else
            {
                _effects.Debris(at, color, 0, 0f);   // the cut-flash line only
                var map = Sim.Map;
                int ix = Mathf.FloorToInt(simPos.X);
                float floorY = 1f;
                for (int y = Mathf.FloorToInt(simPos.Y); y >= 0; y--)
                    if (map.IsSolid(((ix % map.Width) + map.Width) % map.Width, y)) { floorY = y + 1f; break; }
                float dirX = map.Dx(Sim.Player.X, simPos.X);
                var floorDraw = ToDraw(new Vec2(simPos.X, floorY));
                _effects.Blood(at, dirX, floorDraw.y, (brute ? 1.6f : 1f) + (tier == 3 ? 0.3f : 0f));
            }
            _shake = Mathf.Max(_shake, 0.08f * tier + (brute ? 0.2f : 0f));
            _hitStop = Mathf.Max(_hitStop, 0.02f * tier + (brute ? 0.05f : 0f));
            if (tier == 3) _effects.Flash(brute ? 0.22f : 0.1f);
        }

        private void OnOrbPickup(Vector2 at, OrbKind kind)
        {
            var c = OrbRenderer.KindColor(kind);
            _effects.Pop(at, c, 0.9f, 0.35f);
            _effects.Sparks(at, c, 12, 7f);
            _hud.ShowPickup(kind);
        }

        private void OnBruteCrash(Vector2 at)
        {
            _effects.Sparks(at, WindupColor, 18, 9f);
            _effects.Dust(new Vector2(at.x, at.y - 1f), 8, 0f);
            _shake = Mathf.Max(_shake, 0.35f);
            _hitStop = Mathf.Max(_hitStop, 0.06f);
        }

        private void OnClang(Vector2 at)
        {
            _effects.Sparks(at, ClangColor, 14, 8f);
            _shake = Mathf.Max(_shake, 0.15f);
            _hitStop = Mathf.Max(_hitStop, 0.04f);
        }

        private void OnHurt(Vector2 at)
        {
            _effects.Sparks(at, HurtColor, 14, 8f);
            _postFx.PulseDamage();
            _shake = Mathf.Max(_shake, 0.3f);
            _hitStop = Mathf.Max(_hitStop, 0.05f);
        }

        private void OnGameOver(Vector2 at)
        {
            _effects.Debris(at, TierColors[0], 24, 10f);
            _effects.Sparks(at, HurtColor, 20, 10f);
            _effects.Flash(0.3f);
            _shake = Mathf.Max(_shake, 0.45f);
        }

        private void OnJump()
        {
            var p = Sim.Player;
            _effects.Dust(ToDraw(new Vector2(p.X, p.Y)), 5, p.Vx);
            _actors.Squash(1.25f);
        }

        private void OnWallJump(Color color)
        {
            var p = Sim.Player;
            _effects.Dust(ToDraw(new Vector2(p.X - Mathf.Sign(p.Vx) * p.Width * 0.5f, p.Y + p.Height * 0.5f)), 6, p.Vx);
            _effects.Sparks(ToDraw(new Vector2(p.X, p.Y + p.Height * 0.5f)), color, 5, 4f);
            _actors.Squash(1.2f);
        }

        /// <summary>Slide start feedback, scaled by the tier the slide started at (tier 1 a puff, tier 3 a burst).</summary>
        private void OnSlide(Color color)
        {
            var p = Sim.Player;
            int tier = Mathf.Clamp(p.SlideTier, 1, 3);
            var tierColor = TierColors[tier - 1];
            var feet = ToDraw(new Vector2(p.X, p.Y));
            _effects.Dust(feet, tier == 1 ? 4 : tier == 2 ? 8 : 16, p.Vx);
            if (tier >= 2) _effects.Sparks(ToDraw(new Vector2(p.X, p.Y + 0.1f)), tierColor, tier == 3 ? 16 : 6, tier == 3 ? 9f : 4f);
            if (tier == 3) _shake = Mathf.Max(_shake, 0.18f);
            _slideTrailTimer = 0f;
        }

        private float _slideTrailTimer;

        /// <summary>While sliding on the ground: a floor trail in the slide's tier colour (none at tier 1, sparse at 2, dense at 3).</summary>
        private void SpawnSlideTrail(float dt)
        {
            var p = Sim.Player;
            if (!p.Sliding || !p.Grounded || p.SlideTier < 2 || Sim.State == SimState.GameOver) return;
            _slideTrailTimer -= dt;
            if (_slideTrailTimer > 0f) return;
            bool t3 = p.SlideTier == 3;
            _slideTrailTimer = t3 ? 0.03f : 0.08f;
            var at = ToDraw(new Vector2(p.X - Mathf.Sign(p.Vx) * p.Width * 0.4f, p.Y + 0.05f));
            _effects.Dust(at, t3 ? 3 : 1, p.Vx * 0.3f);
            if (t3) _effects.Sparks(at, TierColors[2], 2, 3f);
        }

        private void OnLanded(float fallSpeed)
        {
            _audio.PlayLand(fallSpeed);
            var p = Sim.Player;
            _effects.Dust(ToDraw(new Vector2(p.X, p.Y)), fallSpeed > 15f ? 8 : 4, 0f);
            _actors.Squash(Mathf.Lerp(0.85f, 0.65f, Mathf.InverseLerp(6f, 24f, fallSpeed)));
        }

        /// <summary>Dev-only: plays the tier-3 kill feedback at a point (used by the screenshot tour).</summary>
        internal void PreviewKillEffects(Vector2 at, bool brute)
        {
            _effects.Sparks(at, TierColors[2], 28, 20f);
            _effects.Debris(at, brute ? BruteDebris : GruntDebris, brute ? 16 : 9, 13f);
            _effects.Flash(0.12f);
        }

        /// <summary>Afterimages at tier 2+, speed lines at tier 3.</summary>
        private void SpawnSpeedTrail(float dt)
        {
            if (Sim.State == SimState.GameOver) return;
            SpawnSlideTrail(dt);
            if (_render3D) { SpawnSpeedTrail3D(dt); return; }
            var p = Sim.Player;
            int tier = Sim.PlayerTier;
            var view = _actors.PlayerView;
            var color = TierColors[tier - 1];

            _afterimageTimer -= dt;
            if (tier >= 2 && _afterimageTimer <= 0f)
            {
                _afterimageTimer = tier == 3 ? 0.025f : 0.045f;
                var c = color;
                c.a = 0.4f;
                _effects.Afterimage(view.sprite, view.transform.localPosition, view.transform.localScale, view.flipX, c);
            }

            _speedLineTimer -= dt;
            if (tier == 3 && _speedLineTimer <= 0f)
            {
                _speedLineTimer = 0.02f;
                var pos = view.transform.localPosition;
                _effects.SpeedLine(new Vector2(pos.x, pos.y), Mathf.Sign(p.Vx), color);
            }
        }
        #region Render3D
        // Cinematic 3D view (Render3D/*, Ronin/*) or the classic 2D view, chosen by the Graphics setting or
        // -render3d / -render2d (RenderModeSwitch). Both share the call sites through IGameEffects/IGameActors/IGameOrbs.

        private bool _render3D;
        private Transform _viewHost;
        private Pixel3DCamera _pixelCam;
        private Arena3DRenderer _arena3D;
        private Actors3DView _actors3D;

        /// <summary>True while the game draws through the 3D renderer.</summary>
        public bool Render3DActive => _render3D;

        private static bool Want3D() => RenderModeSwitch.Want3D(GameSettings.Cinematic3D);

        /// <summary>
        /// (Re)builds every view in the render mode the settings ask for: tears down the previous mode's arena,
        /// renderers and camera rig, switches the URP pipeline asset, then builds effects, actors, orbs and the arena.
        /// </summary>
        private void BuildViews()
        {
            bool want = Want3D();
            if (want && !Render3DMode.Enable()) want = false; // the 3D pipeline asset is missing: stay 2D
            _arena3D?.Dispose();
            _arena3D = null;
            if (_arenaHost != null) Destroy(_arenaHost);
            _arenaHost = null;
            if (_viewHost != null) Destroy(_viewHost.gameObject);
            if (_pixelCam != null) Destroy(_pixelCam.gameObject);
            _pixelCam = null;
            _actors3D?.Dispose();
            _actors3D = null;
            _arena = null;
            if (!want) Render3DMode.Disable();
            _render3D = want;

            _viewHost = new GameObject(want ? "View3D" : "View2D").transform;
            _viewHost.SetParent(_world, false);
            _camera.enabled = !want;
            if (want)
            {
                var rig = new GameObject("Pixel3DRig");
                rig.transform.SetParent(transform, false);
                _pixelCam = rig.AddComponent<Pixel3DCamera>();
                PlayerBuildFixes.Apply(_pixelCam);
                var fx = new Effects3DRenderer(_viewHost, _pixelCam.WorldCamera);
                _effects = new Effects3D(fx);
                _actors3D = new Actors3DView(_viewHost, fx, _runConfig.playerHeight);
                _actors = _actors3D;
                _orbs = new Orbs3D(new Orb3DRenderer(_viewHost, _config.orbMaxCount));
            }
            else
            {
                var fx = new EffectsRenderer(_viewHost, _camera);
                _effects = new Effects2D(fx);
                _actors = new Actors2D(new ActorRenderer(_viewHost, fx));
                _orbs = new Orbs2D(new OrbRenderer(_viewHost, _config.orbMaxCount));
            }
            BuildArena();
            if (_render3D) _pixelCam.SetTarget(new Vector2(_camX, _camY));
        }

        /// <summary>The 3D arena for the current level, with its look applied to the camera rig.</summary>
        private void BuildArena3D()
        {
            var look = Arena3DLook.For(Levels[_levelIndex]);
            _pixelCam.ApplyLook(look);
            _arena3D = new Arena3DRenderer(_arenaHost.transform, Sim.Map, look);
            _arena = null;
        }

        /// <summary>Tier 3 speed lines around the ronin (the 3D model has no sprite afterimage).</summary>
        private void SpawnSpeedTrail3D(float dt)
        {
            if (_actors3D == null) return;
            var p = Sim.Player;
            int tier = Sim.PlayerTier;
            _speedLineTimer -= dt;
            // Only while actually sprinting (tier 3 alone also holds against a wall or in the air).
            if (tier == 3 && p.Grounded && p.WallDir != (int)Mathf.Sign(p.Vx) && Mathf.Abs(p.Vx) >= EffectsRenderer.SpeedLineMinSpeed
                && _speedLineTimer <= 0f)
            {
                _speedLineTimer = EffectsRenderer.SpeedLineInterval;
                var feet = _actors3D.PlayerFeet;
                _effects.SpeedLine(new Vector2(feet.x, feet.y + p.Height * 0.5f), Mathf.Sign(p.Vx), TierColors[tier - 1]);
            }
        }
        #endregion
    }
}
