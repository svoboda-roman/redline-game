using System;
using System.Collections.Generic;
using Redline.Core;
using Redline.Core.Audio;
using UnityEngine;

namespace Redline.Game.Audio
{
    /// <summary>
    /// Redline's sound (production/audio/music-sfx-overhaul/direction.md and sfx-spec.md): recorded
    /// cinematic SFX for <see cref="SimEvent"/>s and for states the sim has no event for (footsteps,
    /// slide loop, drone dive, kamikaze fuse, tier change, wave clear, low HP), the adaptive orchestral
    /// score (<see cref="MusicDirector"/>), the rain/city ambience and the ducks.
    /// </summary>
    /// <remarks>
    /// Main-thread only. Call <see cref="Process"/> after every <see cref="RedlineSim.Step"/> and
    /// <see cref="Tick"/> once per rendered frame. Clips load once in <see cref="Init"/> from
    /// Resources/Audio (<see cref="AudioCatalog"/>); a missing clip logs once and plays nothing.
    /// The music state (menu, upgrades, training, level) is read from the <see cref="RedlineGame"/>
    /// on the same GameObject and its screens, so RedlineGame's calls are unchanged.
    /// Set the environment variable REDLINE_AUDIO_LOG=1 (or pass -redlineAudioLog) to log every
    /// effect and music change.
    /// </remarks>
    public sealed class RedlineAudio : MonoBehaviour
    {
        private struct Voice
        {
            public SfxId Id;
            public int Priority;
            public float Start;
            public float End;
            public bool Trimmed;
            public bool Stinger;
            /// <summary>Level before the player's volumes, so a volume change can rescale a playing voice.</summary>
            public float BaseVolume;
        }

        private sealed class LoopVoice
        {
            public AudioSource Source;
            public object Key;
            public float BaseVolume;
            public float Started;
            public bool Stopping;
            public bool Seen;
        }

        private static readonly object SlideKey = new object();

        private SfxDef[] _defs;
        private AudioClip[][] _clips;
        private AudioClip[][] _layers;
        private int[] _last;
        private int[] _beforeLast;
        private float[] _lastStart;
        private AudioSource[] _sources;
        private Voice[] _voices;
        private LoopVoice[] _loops;
        private MusicDirector _music;
        private LoopTrack _rain;
        private LoopTrack _city;
        private AudioSource _thunder;
        private readonly DuckResolver _ducks = new DuckResolver(16, AudioMix.MusicDuckFloorDb, AudioMix.AmbienceDuckFloorDb);
        private readonly MusicTierTracker _tier = new MusicTierTracker(AudioMix.LayerHoldSeconds, AudioMix.TierStingInterval);
        private readonly Dictionary<Enemy, DroneMode> _droneModes = new Dictionary<Enemy, DroneMode>(64);
        private readonly System.Random _rng = new System.Random(777);
        private float _now;
        private float _lpUntil = -1f;
        private float _lpCutoff = AudioMix.OpenCutoff;
        private float _stepTimer;
        private float _heartTimer;
        private float _thunderTimer = 30f;
        private bool _ready;
        private bool _paused;
        private bool _gameOverMix;
        private float _playerX;

        private RedlineGame _game;
        private UpgradeScreen _upgrades;
        private SettingsScreen _settings;
        private MenuScreen _mainMenu;
        private float _menuSearch;
        private RedlineSim _lastSim;
        private int _lastRunId = -1;
        private SimState _lastSimState;
        private int _lastMusicTier = 1;
        private MusicState _loggedState = MusicState.Silent;
        private string _levelKey = "";

        /// <summary>True when every effect and music change is written to the log (REDLINE_AUDIO_LOG / -redlineAudioLog).</summary>
        public static bool LogEnabled { get; } =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REDLINE_AUDIO_LOG")) ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "-redlineAudioLog") >= 0;

        /// <summary>True while <see cref="SetPaused"/> has frozen the effects and filtered the music.</summary>
        public bool Paused => _paused;

        /// <summary>Kept for compatibility: the orb events are mapped explicitly now.</summary>
        public bool OrbEventsMapped => true;

        /// <summary>The music state the director is in.</summary>
        public MusicState MusicState => _music != null ? _music.State : MusicState.Silent;

        /// <summary>One line describing the music (state, tier, clips and volumes); for logs and tests.</summary>
        public string DescribeMusic() => _music != null ? _music.Describe() + " | rain vol=" + (_rain != null ? _rain.Volume.ToString("0.000") : "-") : "not ready";

        /// <summary>
        /// Loads the clips, builds the effect pool, the music and the ambience. Makes sure an
        /// AudioListener exists (on <paramref name="listenerHost"/> if there is none) and puts the
        /// master limiter on the listener's GameObject.
        /// </summary>
        /// <param name="listenerHost">Camera that should carry the listener if the scene has none; may be null.</param>
        /// <example><code>_audio = gameObject.AddComponent&lt;RedlineAudio&gt;(); _audio.Init(camera);</code></example>
        public void Init(Camera listenerHost)
        {
            if (_ready) return;
            EnsureListener(listenerHost);
            LoadClips();
            BuildPool();
            _music = new MusicDirector(transform);
            var amb = new GameObject("Ambience").transform;
            amb.SetParent(transform, false);
            _rain = new LoopTrack(amb, AudioCatalog.Rain, true, 8);
            _city = new LoopTrack(amb, AudioCatalog.CityHum, true, 9);
            _rain.Gain = _rain.TargetGain = 1f;
            _city.Gain = _city.TargetGain = 1f;
            _rain.Start(0);
            _city.Start(0);
            _thunder = amb.gameObject.AddComponent<AudioSource>();
            _thunder.playOnAwake = false;
            _thunder.clip = Resources.Load<AudioClip>("Audio/Sfx/" + AudioCatalog.Thunder);
            _game = GetComponent<RedlineGame>();
            _ready = true;
        }

        /// <summary>Plays the effects for one simulation step's events. Call right after each Step.</summary>
        /// <param name="sim">The simulation (player position for panning).</param>
        /// <param name="events">The step's events, usually <see cref="RedlineSim.Events"/>.</param>
        public void Process(RedlineSim sim, IReadOnlyList<SimEvent> events)
        {
            if (!_ready || events == null) return;
            if (sim != null) _playerX = sim.Player.X;
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                int tier = AudioRules.ClampTier(ev.Tier);
                float x = ev.Position.X;
                switch (ev.Type)
                {
                    case SimEventType.Attack: Play(SfxId.Swing1 + (tier - 1), 1f, 1f, x); break;
                    case SimEventType.Hit: Play(SfxId.Hit1 + (tier - 1), 1f, 1f, x); break;
                    case SimEventType.Kill:
                        Play(SfxId.Kill1 + (tier - 1), 1f, 1f, x);
                        if (tier >= 3)
                        {
                            _ducks.Add(DuckTarget.Music, -4f, 0.01f, 0.15f, 0.6f, _now);
                            _ducks.Add(DuckTarget.Ambience, -3f, 0.01f, 0.15f, 0.6f, _now);
                        }
                        else _ducks.Add(DuckTarget.Music, -2f, 0.02f, 0.08f, 0.4f, _now);
                        break;
                    case SimEventType.Clang: Play(SfxId.Clang, 1f, 1f, x); break;
                    case SimEventType.ShieldBlock: Play(SfxId.ShieldBlock, 1f, 1f, x); break;
                    case SimEventType.HookAttach:
                        // No separate fire event: the fire whip and the latch play together.
                        Play(SfxId.HookFire, 1f, 1f, float.NaN);
                        Play(SfxId.HookLatch, 1f, 1f, float.NaN);
                        break;
                    case SimEventType.HookRelease: Play(SfxId.HookRelease, 1f, 1f, float.NaN); break;
                    case SimEventType.Jump: Play(SfxId.Jump, 1f, 1f, float.NaN); break;
                    case SimEventType.WallJump: Play(SfxId.WallJump, 1f, 1f, float.NaN); break;
                    case SimEventType.Slide: Play(SfxId.SlideStart, 1f, 1f, float.NaN); break;
                    case SimEventType.PlayerHurt:
                        Play(SfxId.Hurt, 1f, 1f, float.NaN);
                        _ducks.Add(DuckTarget.Music, -5f, 0.01f, 0.3f, 0.7f, _now);
                        LowPassDuck(1200f, 0.6f);
                        break;
                    case SimEventType.WaveStart:
                        Play(SfxId.WaveStart, 1f, 1f, float.NaN);
                        _ducks.Add(DuckTarget.Music, -3f, 0.05f, 1.5f, 0.8f, _now);
                        break;
                    case SimEventType.Victory: Play(SfxId.VictorySting, 1f, 1f, float.NaN); break;
                    case SimEventType.GameOver:
                        Play(SfxId.GameOverSting, 1f, 1f, float.NaN);
                        Play(SfxId.DeathGrunt, 1f, 1f, float.NaN);
                        break;
                    case SimEventType.BruteWindup: Play(SfxId.BruteWindup, 1f, 1f, x); break;
                    case SimEventType.BruteCrash:
                        Play(SfxId.BruteCrash, 1f, 1f, x);
                        _ducks.Add(DuckTarget.Music, -3f, 0.015f, 0.1f, 0.5f, _now);
                        break;
                    case SimEventType.DroneWindup: Play(SfxId.DroneWindup, 1f, 1f, x); break;
                    case SimEventType.OrbSpawn: Play(AudioCatalog.OrbSpawn(ev.Detail), 1f, 1f, x); break;
                    case SimEventType.OrbPickup: Play(AudioCatalog.OrbPickup(ev.Detail), 1f, 1f, float.NaN); break;
                    case SimEventType.OrbExpire: Play(SfxId.OrbExpire, 1f, 1f, x); break;
                    case SimEventType.ShooterWindup: Play(SfxId.ShooterWindup, 1f, 1f, x); break;
                    case SimEventType.Shoot: Play(SfxId.Shoot, 1f, 1f, x); break;
                    case SimEventType.ProjectileReflect: Play(SfxId.Reflect, 1f, 1f, x); break;
                    case SimEventType.ProjectileHit: Play(SfxId.ProjectileHit, 1f, 1f, x); break;
                    case SimEventType.KamikazeFuse: break; // the fuse loop follows the kamikaze's state in Tick
                    case SimEventType.Explode:
                        Play(SfxId.Explode, 1f, 1f, x);
                        _ducks.Add(DuckTarget.Music, -6f, 0.01f, 0.25f, 0.9f, _now);
                        _ducks.Add(DuckTarget.Ambience, -4f, 0.01f, 0.25f, 0.9f, _now);
                        LowPassDuck(2000f, 1.0f);
                        break;
                    case SimEventType.SummonerCast: Play(SfxId.SummonCast, 1f, 1f, x); break;
                    case SimEventType.Summon: Play(SfxId.Summon, 1f, 1f, x); break;
                }
            }
        }

        /// <summary>
        /// Per frame: music state and tier, derived effects (footsteps, slide loop, drone dive, fuse
        /// loops, wave clear, tier sting, heartbeat), ducks, ambience and voice fades.
        /// </summary>
        /// <param name="sim">The simulation.</param>
        /// <param name="dt">Real (unscaled) frame time in seconds; keep it running during hit-stop.</param>
        public void Tick(RedlineSim sim, float dt)
        {
            if (!_ready || sim == null) return;
            if (dt < 0f) dt = 0f;
            if (!_paused) _now += dt;
            _playerX = sim.Player.X;

            var state = ResolveState(sim, out bool stepping);
            bool newRun = sim != _lastSim || sim.RunId != _lastRunId;
            bool levelish = state == MusicState.Level || state == MusicState.WaveBreak || state == MusicState.Training;
            if (levelish && newRun)
            {
                _lastSim = sim;
                _lastRunId = sim.RunId;
                _tier.Reset();
                _droneModes.Clear();
                StopLoops();
                _lastSimState = sim.State;
            }
            _music.SetState(state, _levelKey, levelish && newRun);

            if (stepping && !_paused)
            {
                if (sim.State == SimState.Fighting || sim.State == SimState.WaveBreak || sim.State == SimState.Training)
                {
                    int sting = _tier.Update(sim.PlayerTier, dt);
                    if (sting >= 2 && state != MusicState.Training)
                    {
                        Play(SfxId.TierUp, sting >= 3 ? 1f : 0.7f, 1f, float.NaN);
                        Play(SfxId.TierUpTaiko, 1f, 1f, float.NaN);
                    }
                    if (_tier.Tier < _lastMusicTier && state != MusicState.Training) Play(SfxId.TierDown, 1f, 1f, float.NaN);
                }
                if (_lastSimState == SimState.Fighting && sim.State == SimState.WaveBreak)
                {
                    Play(SfxId.WaveClear, 1f, 1f, float.NaN);
                    _ducks.Add(DuckTarget.Music, -3f, 0.05f, 2.0f, 0.8f, _now);
                }
                _lastSimState = sim.State;
                DeriveEffects(sim, dt);
            }
            if (!stepping || sim.State == SimState.GameOver || sim.State == SimState.Victory) StopLoops();
            if (_tier.Tier != _lastMusicTier && LogEnabled) Debug.Log("AUDIO music tier " + _lastMusicTier + "->" + _tier.Tier + " | " + DescribeMusic());
            _lastMusicTier = _tier.Tier;
            _music.Tier = _tier.Tier;

            _gameOverMix = state == MusicState.GameOver;
            _music.Paused = _paused;
            _music.DuckDb = _ducks.Evaluate(DuckTarget.Music, _now);
            _music.DuckCutoff = _now < _lpUntil ? _lpCutoff : AudioMix.OpenCutoff;
            _music.Update(dt);
            UpdateAmbience(state, dt);
            UpdateVoices();
            if (state != _loggedState && LogEnabled) Debug.Log("AUDIO music state " + _loggedState + "->" + state + " level=" + _levelKey + " | " + DescribeMusic());
            _loggedState = state;
        }

        private MusicState ResolveState(RedlineSim sim, out bool stepping)
        {
            FindScreens();
            stepping = false;
            _levelKey = _game != null ? (_game.InTraining ? "Training" : _game.LevelName) : "Loop01";
            if ((_mainMenu != null && _mainMenu.Visible) || CreditsScreen.AnyVisible) return MusicState.Menu;
            if (_upgrades != null && _upgrades.Visible) return MusicState.Upgrades;
            var current = _music.State;
            if (_settings != null && _settings.Visible && current == MusicState.Menu) return MusicState.Menu;
            stepping = true;
            if (_game != null && _game.InTraining) return MusicState.Training;
            switch (sim.State)
            {
                case SimState.GameOver: return MusicState.GameOver;
                case SimState.Victory: return MusicState.Victory;
                case SimState.WaveBreak: return sim.WaveIndex == 0 && sim.Kills == 0 ? MusicState.Level : MusicState.WaveBreak;
                case SimState.Training: return MusicState.Training;
                default: return MusicState.Level;
            }
        }

        private void FindScreens()
        {
            if (_mainMenu != null && _upgrades != null && _settings != null) return;
            _menuSearch -= Time.unscaledDeltaTime;
            if (_menuSearch > 0f) return;
            _menuSearch = 0.5f;
            if (_upgrades == null) _upgrades = GetComponent<UpgradeScreen>();
            if (_settings == null) _settings = GetComponent<SettingsScreen>();
            if (_mainMenu == null)
            {
                var menus = GetComponents<MenuScreen>();
                for (int i = 0; i < menus.Length; i++)
                    if (menus[i].ScreenName == "MainMenu") _mainMenu = menus[i];
            }
        }

        private void DeriveEffects(RedlineSim sim, float dt)
        {
            var p = sim.Player;
            // Footsteps (sfx-spec 1 notes): grounded, moving, not sliding.
            float speed = Mathf.Abs(p.Vx);
            if (p.Grounded && !p.Sliding && speed > AudioMix.StepMinSpeed && sim.State != SimState.GameOver)
            {
                _stepTimer -= dt;
                if (_stepTimer <= 0f)
                {
                    _stepTimer = Mathf.Clamp(AudioMix.StepBase - AudioMix.StepPerSpeed * speed, AudioMix.StepMinInterval, AudioMix.StepBase);
                    int tier = sim.PlayerTier;
                    Play(SfxId.Footstep, tier >= 2 ? 1.4f : speed < 4f ? 0.7f : 1f, 1f, float.NaN);
                }
            }
            else _stepTimer = 0.05f;

            // Low-HP heartbeat.
            if (p.Hp > 0 && p.Hp <= AudioMix.HeartbeatHp && sim.State != SimState.Training)
            {
                _heartTimer -= dt;
                if (_heartTimer <= 0f)
                {
                    _heartTimer = AudioMix.HeartbeatInterval;
                    Play(SfxId.Heartbeat, 1f, 1f, float.NaN);
                }
            }
            else _heartTimer = 0f;

            for (int i = 0; i < _loops.Length; i++) _loops[i].Seen = false;
            if (p.Sliding) KeepLoop(SlideKey, SfxId.SlideLoop, float.NaN, Mathf.Lerp(0.9f, 1.15f, speed / 20f), Mathf.Lerp(0.7f, 1.2f, speed / 20f));

            var enemies = sim.Enemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.Alive) continue;
                if (e.Kind == EnemyKind.Drone)
                {
                    _droneModes.TryGetValue(e, out var before);
                    if (before == DroneMode.Windup && e.DroneState == DroneMode.Dive) Play(SfxId.DroneDive, 1f, 1f, e.X);
                    if (before != e.DroneState) _droneModes[e] = e.DroneState;
                }
                else if (e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse)
                {
                    var loop = KeepLoop(e, SfxId.FuseLoop, e.X, 1f, 1f);
                    if (loop != null)
                    {
                        float t = Mathf.Clamp01((_now - loop.Started) / AudioMix.FuseRampSeconds);
                        loop.Source.pitch = Mathf.Lerp(AudioMix.FusePitchStart, AudioMix.FusePitchEnd, t);
                    }
                }
            }
            // Loops whose owner is gone (slide ended, kamikaze exploded or died) fade out.
            for (int i = 0; i < _loops.Length; i++)
                if (_loops[i].Key != null && !_loops[i].Seen) _loops[i].Stopping = true;
        }

        private LoopVoice KeepLoop(object key, SfxId id, float worldX, float pitch, float volumeScale)
        {
            LoopVoice free = null;
            for (int i = 0; i < _loops.Length; i++)
            {
                var l = _loops[i];
                if (l.Key == key)
                {
                    l.Seen = true;
                    l.Stopping = false;
                    if (key == SlideKey) l.Source.pitch = pitch;
                    if (!float.IsNaN(worldX)) l.Source.panStereo = Pan(worldX);
                    return l;
                }
                if (l.Key == null && free == null) free = l;
            }
            var clips = _clips[(int)id];
            if (free == null || clips.Length == 0) return null;
            var def = _defs[(int)id];
            int v = AudioRules.PickVariant(clips.Length, _last[(int)id], _beforeLast[(int)id], (float)_rng.NextDouble());
            _beforeLast[(int)id] = _last[(int)id];
            _last[(int)id] = v;
            free.Key = key;
            free.Seen = true;
            free.Stopping = false;
            free.Started = _now;
            free.BaseVolume = AudioRules.DbToLinear(Mathf.Lerp(def.MinDb, def.MaxDb, (float)_rng.NextDouble())) * volumeScale * AudioMix.SfxBusLevel * AudioMix.Master;
            free.Source.clip = clips[v];
            free.Source.pitch = pitch * def.Pitch;
            free.Source.panStereo = float.IsNaN(worldX) ? 0f : Pan(worldX);
            free.Source.time = 0f;
            free.Source.volume = Mathf.Clamp01(free.BaseVolume * UserSfx);
            free.Source.Play();
            if (LogEnabled) Debug.Log("AUDIO loop start " + id + " clip=" + clips[v].name + " vol=" + free.Source.volume.ToString("0.000"));
            return free;
        }

        private void StopLoops()
        {
            for (int i = 0; i < _loops.Length; i++)
                if (_loops[i].Key != null) _loops[i].Stopping = true;
        }

        private void UpdateAmbience(MusicState state, float dt)
        {
            float level = AudioMix.AmbienceBus * AudioMix.Master * AudioMix.UserSfx * AudioMix.UserMaster;
            float rainDb = AudioCatalog.RainDb(_levelKey) + _ducks.Evaluate(DuckTarget.Ambience, _now);
            float cut = AudioMix.OpenCutoff;
            float scale = 1f;
            if (state == MusicState.Menu) scale = AudioMix.MenuRain;
            else if (state == MusicState.Upgrades) scale = AudioMix.UpgradeRain;
            if (state == MusicState.GameOver)
            {
                rainDb = AudioMix.GameOverRainDb;
                cut = AudioMix.GameOverRainCutoff;
            }
            if (_paused) cut = Mathf.Min(cut, AudioMix.PauseCutoff);
            _rain.Cutoff = cut;
            _city.Cutoff = cut;
            _rain.Update(dt, level * scale * AudioRules.DbToLinear(rainDb));
            _city.Update(dt, level * scale * AudioMix.CityHum * AudioRules.DbToLinear(rainDb));

            if (_thunder.clip != null && !_paused && AudioCatalog.ThunderIn(_levelKey) && state != MusicState.GameOver)
            {
                _thunderTimer -= dt;
                if (_thunderTimer <= 0f)
                {
                    _thunderTimer = Mathf.Lerp(AudioMix.ThunderMin, AudioMix.ThunderMax, (float)_rng.NextDouble());
                    _thunder.panStereo = Mathf.Lerp(-0.6f, 0.6f, (float)_rng.NextDouble());
                    _thunder.volume = Mathf.Clamp01(level * 2.2f);
                    _thunder.Play();
                }
            }
        }

        private void UpdateVoices()
        {
            float user = UserSfx;
            for (int i = 0; i < _sources.Length; i++)
            {
                var v = _voices[i];
                var s = _sources[i];
                if (!s.isPlaying || _paused) continue;
                float drop = _gameOverMix && !v.Stinger ? AudioRules.DbToLinear(AudioMix.GameOverRainDb) : 1f;
                float vol = v.BaseVolume * user * drop;
                if (v.Trimmed)
                {
                    float left = v.End - _now;
                    if (left <= 0f) { s.Stop(); continue; }
                    if (left < AudioMix.TrimFadeSeconds) vol *= left / AudioMix.TrimFadeSeconds;
                }
                s.volume = Mathf.Clamp01(vol);
            }
            float fadeStep = Time.unscaledDeltaTime / AudioMix.LoopFadeSeconds;
            for (int i = 0; i < _loops.Length; i++)
            {
                var l = _loops[i];
                if (l.Key == null) continue;
                if (l.Stopping)
                {
                    float target = l.Source.volume - fadeStep * l.BaseVolume * user;
                    if (target <= 0f || _paused)
                    {
                        l.Source.Stop();
                        l.Key = null;
                        l.Stopping = false;
                        continue;
                    }
                    l.Source.volume = target;
                }
                else l.Source.volume = Mathf.Clamp01(l.BaseVolume * user);
            }
        }

        /// <summary>Landing: soft/medium/hard by fall speed (sfx-spec 1). Call where the game detects a landing.</summary>
        /// <param name="fallSpeed">Downward speed at touchdown in tiles/s.</param>
        public void PlayLand(float fallSpeed)
        {
            if (!_ready) return;
            var id = fallSpeed >= AudioMix.LandHardMin ? SfxId.LandHard : fallSpeed <= AudioMix.LandSoftMax ? SfxId.LandSoft : SfxId.LandMed;
            float k = Mathf.Clamp01(fallSpeed / AudioMix.LandHardMin);
            Play(id, AudioRules.DbToLinear(Mathf.Lerp(-3f, 3f, k)), 1f, float.NaN);
        }

        /// <summary>Plays one effect (all variants of the id, layered clips included); the voice limit and priority decide if it sounds.</summary>
        /// <param name="id">The effect.</param>
        /// <param name="volumeScale">Extra level multiplier.</param>
        /// <param name="pitch">Extra pitch multiplier (1 = as defined); jitter is added.</param>
        public void Play(SfxId id, float volumeScale, float pitch) => Play(id, volumeScale, pitch, float.NaN);

        private void Play(SfxId id, float volumeScale, float pitch, float worldX)
        {
            if (!_ready || id <= SfxId.None || id >= SfxId.Count) return;
            int n = (int)id;
            var clips = _clips[n];
            if (clips.Length == 0) return;
            var def = _defs[n];
            if (_now - _lastStart[n] < def.MinInterval) return;
            int variant = AudioRules.PickVariant(clips.Length, _last[n], _beforeLast[n], (float)_rng.NextDouble());
            float jitter = Mathf.Pow(2f, def.JitterSt * ((float)_rng.NextDouble() * 2f - 1f) / 12f);
            float p = Mathf.Max(0.1f, def.Pitch * pitch * jitter);
            float db = Mathf.Lerp(def.MinDb, def.MaxDb, (float)_rng.NextDouble());
            float pan = float.IsNaN(worldX) || !def.World ? 0f : Pan(worldX);
            if (!StartVoice(id, def, clips[variant], AudioRules.DbToLinear(db) * volumeScale, p, def.Trim, pan)) return;
            _beforeLast[n] = _last[n];
            _last[n] = variant;
            _lastStart[n] = _now;
            var layers = _layers[n];
            if (layers.Length > 0)
            {
                var layer = layers[_rng.Next(layers.Length)];
                StartVoice(id, def, layer, AudioRules.DbToLinear(def.LayerDb) * volumeScale, def.LayerPitch * pitch * jitter, def.LayerTrim, pan);
            }
        }

        private bool StartVoice(SfxId id, SfxDef def, AudioClip clip, float gain, float pitch, float trim, float pan)
        {
            int slot = PickVoice(id, def.Voices, def.Priority);
            if (slot < 0) return false;
            var source = _sources[slot];
            source.Stop();
            source.clip = clip;
            source.pitch = pitch;
            source.panStereo = pan;
            float baseVolume = gain * AudioMix.SfxBusLevel * AudioMix.Master;
            bool stinger = def.Bus == SfxBus.Stinger || def.Bus == SfxBus.Ui;
            float drop = _gameOverMix && !stinger ? AudioRules.DbToLinear(AudioMix.GameOverRainDb) : 1f;
            source.volume = Mathf.Clamp01(baseVolume * UserSfx * drop);
            source.Play();
            float length = clip.length / pitch;
            bool trimmed = trim > 0f && trim < clip.length;
            if (trimmed) length = trim / pitch;
            _voices[slot] = new Voice
            {
                Id = id,
                Priority = def.Priority,
                Start = _now,
                End = _now + length,
                Trimmed = trimmed,
                Stinger = stinger,
                BaseVolume = baseVolume
            };
            if (LogEnabled) Debug.Log("AUDIO sfx " + id + " clip=" + clip.name + " vol=" + source.volume.ToString("0.000") + " pitch=" + pitch.ToString("0.00") + " pan=" + pan.ToString("0.00"));
            return true;
        }

        private float Pan(float worldX) => Mathf.Clamp((worldX - _playerX) / AudioMix.PanHalfWidth, -AudioMix.PanMax, AudioMix.PanMax);

        private static float UserSfx => AudioMix.UserSfx * AudioMix.UserMaster;

        /// <summary>
        /// Main thread: applies the player's volumes (0-1 each) to the music and to effects, including
        /// effects already playing.
        /// </summary>
        /// <example><code>audio.SetVolumes(settings.MasterVolume / 100f, settings.MusicVolume / 100f, settings.SfxVolume / 100f);</code></example>
        public void SetVolumes(float master, float music, float sfx)
        {
            AudioMix.SetVolumes(master, music, sfx);
            if (!_ready) return;
            float user = UserSfx;
            for (int i = 0; i < _sources.Length; i++)
                _sources[i].volume = Mathf.Clamp01(_voices[i].BaseVolume * user);
        }

        /// <summary>
        /// Freezes (true) or resumes (false) the audio with the game: playing effects pause in place and
        /// resume where they were; the music drops 6 dB behind an 800 Hz low-pass (direction.md 2.2).
        /// </summary>
        public void SetPaused(bool paused)
        {
            if (!_ready || paused == _paused) return;
            _paused = paused;
            for (int i = 0; i < _sources.Length; i++)
            {
                if (paused) _sources[i].Pause();
                else _sources[i].UnPause();
            }
            for (int i = 0; i < _loops.Length; i++)
            {
                if (paused) _loops[i].Source.Pause();
                else _loops[i].Source.UnPause();
            }
            _music.Paused = paused;
        }

        /// <summary>Stops every effect at once (a run is abandoned or left); the music and rain keep playing.</summary>
        public void StopEffects()
        {
            if (!_ready) return;
            for (int i = 0; i < _sources.Length; i++)
            {
                _sources[i].Stop();
                _voices[i].End = 0f;
            }
            for (int i = 0; i < _loops.Length; i++)
            {
                _loops[i].Source.Stop();
                _loops[i].Key = null;
            }
            _ducks.Clear();
        }

        /// <summary>Plays a short effect at the player's current effects volume, as a preview in the settings.</summary>
        public void PlayPreview() => Play(SfxId.OrbPickHealth, 1f, 1f, float.NaN);

        // ------------------------------------------------------------------ voices

        /// <summary>
        /// Oldest voice of the id if it is at its limit; else a free voice; else the least important
        /// (highest priority number), oldest voice if it is not more important than the new sound; else -1.
        /// </summary>
        private int PickVoice(SfxId id, int maxVoices, int priority)
        {
            int inGroup = 0, oldestInGroup = -1, free = -1, victim = -1;
            for (int i = 0; i < _voices.Length; i++)
            {
                var v = _voices[i];
                bool busy = v.End > _now && _sources[i].isPlaying;
                if (!busy)
                {
                    if (free < 0) free = i;
                    continue;
                }
                if (victim < 0 || v.Priority > _voices[victim].Priority ||
                    (v.Priority == _voices[victim].Priority && v.Start < _voices[victim].Start)) victim = i;
                if (v.Id != id) continue;
                inGroup++;
                if (oldestInGroup < 0 || v.Start < _voices[oldestInGroup].Start) oldestInGroup = i;
            }
            // Layers count against the id's limit too, so allow two sources per voice.
            if (inGroup >= Math.Max(1, maxVoices) * 2 && oldestInGroup >= 0) return oldestInGroup;
            if (free >= 0) return free;
            if (victim >= 0 && _voices[victim].Priority >= priority) return victim;
            return -1;
        }

        private void LowPassDuck(float cutoff, float seconds)
        {
            if (_now < _lpUntil && _lpCutoff < cutoff) { _lpUntil = Mathf.Max(_lpUntil, _now + seconds); return; }
            _lpCutoff = cutoff;
            _lpUntil = _now + seconds;
        }

        // ------------------------------------------------------------------ setup

        private void EnsureListener(Camera host)
        {
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener == null)
            {
                var go = host != null ? host.gameObject : gameObject;
                listener = go.AddComponent<AudioListener>();
            }
            if (listener.GetComponent<MasterLimiter>() == null) listener.gameObject.AddComponent<MasterLimiter>();
        }

        private void LoadClips()
        {
            int count = (int)SfxId.Count;
            _defs = AudioCatalog.Defs;
            _clips = new AudioClip[count][];
            _layers = new AudioClip[count][];
            _last = new int[count];
            _beforeLast = new int[count];
            _lastStart = new float[count];
            for (int i = 0; i < count; i++)
            {
                _clips[i] = Load(_defs[i].Clips);
                _layers[i] = Load(_defs[i].Layer);
                _last[i] = -1;
                _beforeLast[i] = -1;
                _lastStart[i] = float.NegativeInfinity;
            }
        }

        private static AudioClip[] Load(string[] paths)
        {
            if (paths == null || paths.Length == 0) return Array.Empty<AudioClip>();
            var list = new List<AudioClip>(paths.Length);
            for (int i = 0; i < paths.Length; i++)
            {
                var clip = Resources.Load<AudioClip>("Audio/Sfx/" + paths[i]);
                if (clip != null) list.Add(clip);
                else Debug.LogWarning("Redline audio: missing clip Audio/Sfx/" + paths[i] + " (plays nothing).");
            }
            return list.ToArray();
        }

        private void BuildPool()
        {
            var poolGo = new GameObject("Sfx");
            poolGo.transform.SetParent(transform, false);
            _sources = new AudioSource[AudioMix.SfxVoices];
            _voices = new Voice[AudioMix.SfxVoices];
            for (int i = 0; i < _sources.Length; i++)
            {
                _sources[i] = NewSource(poolGo, false);
                _voices[i].Id = SfxId.None;
                _voices[i].Start = float.NegativeInfinity;
                _voices[i].End = float.NegativeInfinity;
            }
            _loops = new LoopVoice[AudioMix.LoopVoices];
            for (int i = 0; i < _loops.Length; i++)
                _loops[i] = new LoopVoice { Source = NewSource(poolGo, true) };
        }

        private static AudioSource NewSource(GameObject host, bool loop)
        {
            var s = host.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.dopplerLevel = 0f;
            s.priority = 64;
            return s;
        }

        private void OnDestroy()
        {
            _music?.Destroy();
        }
    }
}
