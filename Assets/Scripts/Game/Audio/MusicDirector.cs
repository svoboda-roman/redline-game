using Redline.Core.Audio;
using UnityEngine;

namespace Redline.Game.Audio
{
    /// <summary>What the music is doing (direction.md 2.1).</summary>
    public enum MusicState
    {
        Silent,
        Menu,
        Upgrades,
        Training,
        Level,
        WaveBreak,
        Victory,
        GameOver
    }

    /// <summary>
    /// One streamed track that loops by overlapping its end with a fresh start (the recorded tracks
    /// are not seamless): two voices, each with its own AudioLowPassFilter, crossfade over
    /// <see cref="AudioMix.LoopOverlapSeconds"/>. A one-shot track plays once and stops at its end.
    /// </summary>
    /// <remarks>Main-thread only. Gain and cutoff are set by the owner every frame.</remarks>
    public sealed class LoopTrack
    {
        private readonly AudioSource[] _voices = new AudioSource[2];
        private readonly AudioLowPassFilter[] _filters = new AudioLowPassFilter[2];
        private readonly float[] _startedAt = new float[2];
        private readonly bool[] _fadeIn = new bool[2];
        private readonly GameObject _root;
        private int _active;
        private bool _restarted;

        /// <summary>What plays.</summary>
        public MusicTrackDef Def { get; }
        /// <summary>True when the clip failed to load (the track stays silent).</summary>
        public bool Missing { get; }
        /// <summary>True for a track that loops.</summary>
        public bool Looping { get; }
        /// <summary>Current linear gain the owner fades (before user volumes).</summary>
        public float Gain;
        /// <summary>Gain <see cref="Gain"/> moves to.</summary>
        public float TargetGain;
        /// <summary>Seconds a full 0-1 gain move takes.</summary>
        public float FadeSeconds = 1f;
        /// <summary>Low-pass cutoff in Hz.</summary>
        public float Cutoff = AudioMix.OpenCutoff;
        /// <summary>The clip name, for logs.</summary>
        public string ClipName => Def.Clip;

        /// <summary>Loads the clip (Resources/Audio/&lt;path&gt;) and builds the voices under <paramref name="parent"/>.</summary>
        /// <example><code>var t = new LoopTrack(transform, AudioCatalog.MainTheme, true, 128);</code></example>
        public LoopTrack(Transform parent, MusicTrackDef def, bool looping, int priority)
        {
            Def = def;
            Looping = looping;
            var clip = Resources.Load<AudioClip>("Audio/" + def.Clip);
            Missing = clip == null;
            if (Missing) Debug.LogWarning("Redline audio: missing clip Audio/" + def.Clip + " (plays nothing).");
            _root = new GameObject("Track " + def.Clip);
            _root.transform.SetParent(parent, false);
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(_root.transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.clip = clip;
                s.spatialBlend = 0f;
                s.priority = priority;
                s.volume = 0f;
                _voices[i] = s;
                _filters[i] = go.AddComponent<AudioLowPassFilter>();
                _filters[i].cutoffFrequency = AudioMix.OpenCutoff;
            }
        }

        /// <summary>True while a voice is playing or scheduled.</summary>
        public bool Playing => _voices[0].isPlaying || _voices[1].isPlaying;

        /// <summary>Seconds into the clip of the active voice.</summary>
        public float Time => _voices[_active].time;

        /// <summary>Starts from the head at DSP time <paramref name="dspTime"/> (0 = now).</summary>
        public void Start(double dspTime)
        {
            if (Missing) return;
            Stop();
            _active = 0;
            _restarted = false;
            var v = _voices[0];
            v.time = Mathf.Min(Def.Head, Mathf.Max(0f, v.clip.length - 0.1f));
            if (dspTime > 0) v.PlayScheduled(dspTime);
            else v.Play();
            _startedAt[0] = UnityEngine.Time.unscaledTime;
            _fadeIn[0] = false;
        }

        /// <summary>Stops both voices.</summary>
        public void Stop()
        {
            _voices[0].Stop();
            _voices[1].Stop();
        }

        /// <summary>Pauses or resumes both voices.</summary>
        public void SetPaused(bool paused)
        {
            for (int i = 0; i < 2; i++)
            {
                if (paused) _voices[i].Pause();
                else _voices[i].UnPause();
            }
        }

        /// <summary>Per frame: gain fade, loop overlap, volumes and cutoff.</summary>
        /// <param name="dt">Unscaled frame time.</param>
        /// <param name="outScale">Linear factor after <see cref="Gain"/> (buses, user volume, ducks).</param>
        public void Update(float dt, float outScale)
        {
            float step = dt / Mathf.Max(0.01f, FadeSeconds);
            Gain = Mathf.MoveTowards(Gain, TargetGain, step);
            if (Missing) return;
            float overlap = AudioMix.LoopOverlapSeconds;
            float end = Mathf.Min(Def.End, _voices[0].clip.length);
            var a = _voices[_active];
            if (a.isPlaying && !_restarted && a.time >= end - overlap)
            {
                if (Looping)
                {
                    int other = 1 - _active;
                    var b = _voices[other];
                    b.time = Def.Head;
                    b.Play();
                    _startedAt[other] = UnityEngine.Time.unscaledTime;
                    _fadeIn[other] = true;
                    _active = other;
                }
                _restarted = true;
            }
            if (_restarted && !_voices[1 - _active].isPlaying) _restarted = false;
            if (_restarted && !Looping) _restarted = true;
            float now = UnityEngine.Time.unscaledTime;
            for (int i = 0; i < 2; i++)
            {
                var v = _voices[i];
                if (!v.isPlaying) continue;
                float env = 1f;
                if (_fadeIn[i]) env = Mathf.Clamp01((now - _startedAt[i]) / overlap);
                float left = end - v.time;
                if (left < overlap) env = Mathf.Min(env, Mathf.Clamp01(left / overlap));
                if (left <= 0f) { v.Stop(); continue; }
                // Equal-power shape on the overlap.
                env = Mathf.Sin(env * Mathf.PI * 0.5f);
                v.volume = Mathf.Clamp01(Gain * env * outScale);
                _filters[i].cutoffFrequency = Mathf.Clamp(Cutoff, 10f, AudioMix.OpenCutoff);
            }
        }

        /// <summary>Current output volume of the active voice (for logs).</summary>
        public float Volume => _voices[_active].isPlaying ? _voices[_active].volume : 0f;

        /// <summary>Destroys the voices.</summary>
        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
        }
    }

    /// <summary>
    /// The adaptive score (direction.md 2): a CALM/INTENSE pair per level started in sync with
    /// PlayScheduled and crossfaded by the music tier (AudioRules tier table, up 1.5 s, down 3 s, low
    /// pass on INTENSE), plus the menu/upgrade theme, the training track and the victory and game-over
    /// one-shots. Ducks and the pause filter are applied on top.
    /// </summary>
    /// <remarks>Main-thread only. Owned by <see cref="RedlineAudio"/>.</remarks>
    public sealed class MusicDirector
    {
        private readonly Transform _host;
        private LoopTrack _menu;
        private LoopTrack _calm;
        private LoopTrack _intense;
        private LoopTrack _oneShot;
        private LoopTrack _retireA;
        private LoopTrack _retireB;
        private string _levelKey;
        private float _position = 1f;

        /// <summary>Current state.</summary>
        public MusicState State { get; private set; } = MusicState.Silent;
        /// <summary>Music tier the crossfade heads to (1-3).</summary>
        public int Tier { get; set; } = 1;
        /// <summary>Continuous crossfade position (1-3).</summary>
        public float Position => _position;
        /// <summary>Paused: -6 dB and 800 Hz on all music.</summary>
        public bool Paused { get; set; }
        /// <summary>Music duck in dB (0 or negative) from the duck resolver.</summary>
        public float DuckDb { get; set; }
        /// <summary>Extra low-pass from ducks (Hz; open = none).</summary>
        public float DuckCutoff { get; set; } = AudioMix.OpenCutoff;

        public MusicDirector(Transform host)
        {
            _host = host;
        }

        /// <summary>
        /// Sets the state. <paramref name="levelKey"/> names the level ("Loop01", "Training"…); a new key
        /// or <paramref name="restart"/> starts the level pair from the beginning (old pair fades 2 s).
        /// </summary>
        public void SetState(MusicState state, string levelKey, bool restart)
        {
            bool levelish = state == MusicState.Level || state == MusicState.WaveBreak || state == MusicState.Training;
            if (levelish && (restart || levelKey != _levelKey || _calm == null)) StartLevel(levelKey, state == MusicState.Training);
            if (state == State) return;
            var previous = State;
            State = state;
            switch (state)
            {
                case MusicState.Menu:
                case MusicState.Upgrades:
                    if (_menu == null) _menu = new LoopTrack(_host, AudioCatalog.MainTheme, true, 0);
                    if (!_menu.Playing) { _menu.Gain = 0f; _menu.Start(0); }
                    break;
                case MusicState.Victory:
                    StartOneShot(AudioCatalog.Victory);
                    break;
                case MusicState.GameOver:
                    StartOneShot(AudioCatalog.GameOver);
                    break;
            }
            if (previous == MusicState.Victory || previous == MusicState.GameOver)
            {
                if (_oneShot != null) { _oneShot.TargetGain = 0f; _oneShot.FadeSeconds = AudioMix.StateFadeSeconds; }
            }
        }

        private void StartOneShot(MusicTrackDef def)
        {
            Retire(_oneShot, null);
            _oneShot = new LoopTrack(_host, def, false, 0);
            _oneShot.Gain = 1f;
            _oneShot.TargetGain = 1f;
            _oneShot.Start(0);
        }

        private void StartLevel(string key, bool training)
        {
            Retire(_calm, _intense);
            _levelKey = key;
            _position = 1f;
            Tier = 1;
            MusicTrackDef calm, intense;
            if (training)
            {
                calm = AudioCatalog.Training;
                intense = null;
            }
            else AudioCatalog.LevelPair(key, out calm, out intense);
            _calm = new LoopTrack(_host, calm, true, 0);
            _intense = intense != null ? new LoopTrack(_host, intense, true, 1) : null;
            // Both tracks start on the same DSP time so they stay in step (direction.md 2.2).
            double at = AudioSettings.dspTime + 0.1;
            _calm.Start(at);
            _intense?.Start(at);
        }

        private void Retire(LoopTrack a, LoopTrack b)
        {
            _retireA?.Destroy();
            _retireB?.Destroy();
            _retireA = a;
            _retireB = b;
            if (a != null) { a.TargetGain = 0f; a.FadeSeconds = AudioMix.LevelChangeSeconds; }
            if (b != null) { b.TargetGain = 0f; b.FadeSeconds = AudioMix.LevelChangeSeconds; }
        }

        /// <summary>Pauses (freezes) or resumes every track; used while the app/game is suspended.</summary>
        public void SetPaused(bool paused)
        {
            Paused = paused;
        }

        /// <summary>Per frame: targets, tier crossfade, fades and output volumes.</summary>
        /// <param name="dt">Unscaled frame time.</param>
        public void Update(float dt)
        {
            if (dt < 0f) dt = 0f;
            bool inLevel = State == MusicState.Level;
            if (!Paused) _position = AudioRules.StepPosition(_position, inLevel ? Tier : 1, dt, AudioMix.TierUpSeconds, AudioMix.TierDownSeconds);
            var mix = AudioRules.At(_position);

            float menuTarget = 0f, calmTarget = 0f, intenseTarget = 0f;
            float calmCut = AudioMix.OpenCutoff, intenseCut = mix.IntenseCutoffHz;
            float levelFade = AudioMix.LevelInSeconds, menuFade = AudioMix.MenuOutSeconds;
            float lift = AudioRules.DbToLinear(mix.LiftDb);
            switch (State)
            {
                case MusicState.Menu:
                    menuTarget = 1f;
                    menuFade = AudioMix.StateFadeSeconds;
                    levelFade = AudioMix.MenuOutSeconds;
                    break;
                case MusicState.Upgrades:
                    menuTarget = 1f;
                    menuFade = AudioMix.StateFadeSeconds;
                    levelFade = AudioMix.MenuOutSeconds;
                    break;
                case MusicState.Training:
                    calmTarget = Tier >= 3 ? AudioRules.DbToLinear(AudioMix.TrainingTier3LiftDb) : 1f;
                    break;
                case MusicState.Level:
                    calmTarget = AudioRules.DbToLinear(mix.CalmDb) * lift;
                    intenseTarget = AudioRules.DbToLinear(mix.IntenseDb) * lift;
                    levelFade = 0.25f; // the tier crossfade itself is shaped by the position
                    break;
                case MusicState.WaveBreak:
                    calmTarget = AudioMix.WaveBreakCalm;
                    calmCut = intenseCut = AudioMix.WaveBreakCutoff;
                    break;
                case MusicState.Victory:
                    levelFade = AudioMix.VictoryOutSeconds;
                    break;
                case MusicState.GameOver:
                    levelFade = AudioMix.GameOverOutSeconds;
                    break;
            }

            float duck = AudioRules.DbToLinear(DuckDb);
            float pause = Paused ? AudioRules.DbToLinear(AudioMix.PauseDb) : 1f;
            float pauseCut = Paused ? AudioMix.PauseCutoff : AudioMix.OpenCutoff;
            float user = AudioMix.MusicBus * AudioMix.Master * AudioMix.UserMusic * AudioMix.UserMaster;
            float levelOut = user * duck * pause;
            float duckCut = Mathf.Min(DuckCutoff, pauseCut);

            if (_menu != null)
            {
                _menu.TargetGain = menuTarget;
                _menu.FadeSeconds = menuFade;
                _menu.Cutoff = pauseCut;
                _menu.Update(dt, user * pause);
                if (menuTarget <= 0f && _menu.Gain <= 0f && _menu.Playing) _menu.Stop();
            }
            if (_calm != null)
            {
                // A fade in from the level start; a level change moves at its own rate.
                _calm.TargetGain = calmTarget;
                _calm.FadeSeconds = State == MusicState.Level && _calm.Gain < 0.05f ? AudioMix.LevelInSeconds : levelFade;
                _calm.Cutoff = Mathf.Min(calmCut, duckCut);
                _calm.Update(dt, levelOut);
            }
            if (_intense != null)
            {
                _intense.TargetGain = intenseTarget;
                _intense.FadeSeconds = levelFade;
                _intense.Cutoff = Mathf.Min(intenseCut, duckCut);
                _intense.Update(dt, levelOut);
            }
            if (_oneShot != null)
            {
                _oneShot.Cutoff = pauseCut;
                _oneShot.Update(dt, user * pause);
            }
            _retireA?.Update(dt, levelOut);
            _retireB?.Update(dt, levelOut);
        }

        /// <summary>One log line: state, tier, and every audible track's clip and volume.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder(160);
            sb.Append("state=").Append(State).Append(" tier=").Append(Tier).Append(" pos=").Append(_position.ToString("0.00"));
            Append(sb, "menu", _menu);
            Append(sb, "calm", _calm);
            Append(sb, "intense", _intense);
            Append(sb, "oneshot", _oneShot);
            return sb.ToString();
        }

        private static void Append(System.Text.StringBuilder sb, string label, LoopTrack t)
        {
            if (t == null || (!t.Playing && t.Gain <= 0f)) return;
            sb.Append(" | ").Append(label).Append('=').Append(t.ClipName)
              .Append(t.Playing ? "" : " (not playing)")
              .Append(" gain=").Append(t.Gain.ToString("0.000"))
              .Append(" vol=").Append(t.Volume.ToString("0.000"))
              .Append(" (").Append(AudioRules.LinearToDb(t.Volume).ToString("0.0")).Append(" dB)")
              .Append(" lp=").Append(t.Cutoff.ToString("0")).Append(" t=").Append(t.Time.ToString("0.0"));
        }

        /// <summary>Destroys every track.</summary>
        public void Destroy()
        {
            _menu?.Destroy();
            _calm?.Destroy();
            _intense?.Destroy();
            _oneShot?.Destroy();
            _retireA?.Destroy();
            _retireB?.Destroy();
        }
    }
}
