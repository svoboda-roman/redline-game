using System;
using System.Collections.Generic;
using Redline.Core;
using UnityEngine;

namespace Redline.Game.Audio
{
    /// <summary>
    /// Redline's sound: plays synthesised effects for <see cref="SimEvent"/>s and drives the
    /// tier-layered synthwave track (design/game-brief.md). All clips are generated once in
    /// <see cref="Init"/> by <see cref="SynthClips"/>; nothing is loaded or downloaded.
    /// </summary>
    /// <remarks>
    /// Main-thread only. Call <see cref="Process"/> after every <see cref="RedlineSim.Step"/> (the
    /// sim clears its events each step) and <see cref="Tick"/> once per rendered frame. No
    /// allocation after <see cref="Init"/>. Effects use a fixed pool of AudioSources with a
    /// per-sound voice limit and retrigger guard (<see cref="AudioMix.Spec"/>), so a kill chain
    /// cannot stack a pile of identical sounds.
    /// </remarks>
    public sealed class RedlineAudio : MonoBehaviour
    {
        // Orb events (OrbSpawn, OrbPickup, OrbExpire) were being added to SimEventType by a parallel
        // change while this was written, so they are resolved by name at Init: this file compiles
        // with or without them, and an absent name simply plays nothing.
        // TODO(orbs): once the orb events are committed, replace this name lookup with explicit cases
        // in MapEvent (SimEventType.OrbSpawn -> SfxId.OrbSpawn, OrbPickup -> OrbPickup, OrbExpire ->
        // OrbExpire) and consider pitching OrbPickup by SimEvent.Detail (OrbKind).
        private static readonly string[] OrbSpawnNames = { "OrbSpawn", "OrbSpawned" };
        private static readonly string[] OrbPickupNames = { "OrbPickup", "OrbPickedUp", "OrbCollected" };
        private static readonly string[] OrbExpireNames = { "OrbExpire", "OrbExpired" };

        private struct Voice
        {
            public SfxId Group;
            public float Start;
            public float End;
        }

        private AudioClip[] _clips;
        private SfxSpec[] _specs;
        private AudioSource[] _sources;
        private Voice[] _voices;
        private float[] _lastStart;
        private int[] _eventToSfx;
        private MusicPlayer _music;
        private Rng _rng = new Rng(777);
        private float _now;
        private int _musicTier = 1;
        private float _tierHold;
        private bool _ready;

        /// <summary>True when at least one orb event name was found in <see cref="SimEventType"/>.</summary>
        public bool OrbEventsMapped { get; private set; }

        /// <summary>
        /// Generates the clips, builds the effect pool and starts the music. Makes sure an
        /// AudioListener exists (on <paramref name="listenerHost"/> if there is none) and puts the
        /// master limiter on the listener's GameObject.
        /// </summary>
        /// <param name="listenerHost">Camera that should carry the listener if the scene has none; may be null.</param>
        public void Init(Camera listenerHost)
        {
            if (_ready) return;
            EnsureListener(listenerHost);
            BuildClips();
            BuildPool();
            BuildEventMap();

            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(transform, false);
            _music = musicGo.AddComponent<MusicPlayer>();
            _music.Init();
            _ready = true;
        }

        /// <summary>Plays the effects for one simulation step's events. Call right after each Step.</summary>
        /// <param name="sim">The simulation (unused today; kept so effects can read state later).</param>
        /// <param name="events">The step's events, usually <see cref="RedlineSim.Events"/>.</param>
        public void Process(RedlineSim sim, IReadOnlyList<SimEvent> events)
        {
            if (!_ready || events == null) return;
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                int type = (int)ev.Type;
                if (type < 0 || type >= _eventToSfx.Length) continue;
                var id = (SfxId)_eventToSfx[type];
                if (id == SfxId.None) continue;
                int tier = ev.Tier < 1 ? 1 : ev.Tier > 3 ? 3 : ev.Tier;
                float pitch = 1f;
                if (id == SfxId.Hit1 || id == SfxId.Kill1) id += tier - 1;
                else if (id == SfxId.Swing) pitch = 1f + AudioMix.SwingPitchPerTier * (tier - 1);
                Play(id, 1f, pitch);
            }
        }

        /// <summary>
        /// Per-frame update: advances the voice clock and sets the music layers from the player's
        /// tier (a layer fades in at once, and fades out only after the player has stayed below its
        /// tier for <see cref="AudioMix.LayerHoldSeconds"/>). Ducks the music on game over.
        /// </summary>
        /// <param name="sim">The simulation.</param>
        /// <param name="dt">Real (unscaled) frame time in seconds; keep it running during hit-stop.</param>
        public void Tick(RedlineSim sim, float dt)
        {
            if (!_ready || sim == null) return;
            if (dt < 0f) dt = 0f;
            _now += dt;

            bool over = sim.State == SimState.GameOver;
            if (!over)
            {
                int tier = sim.PlayerTier;
                if (tier >= _musicTier)
                {
                    _musicTier = tier;
                    _tierHold = AudioMix.LayerHoldSeconds;
                }
                else
                {
                    _tierHold -= dt;
                    if (_tierHold <= 0f)
                    {
                        _musicTier--;
                        _tierHold = AudioMix.LayerHoldSeconds;
                    }
                }
            }

            var sequencer = _music != null ? _music.Sequencer : null;
            if (sequencer == null) return;
            sequencer.SetLayers(_musicTier >= 2, _musicTier >= 3);
            sequencer.SetDucked(over);
        }

        /// <summary>Landing thud, louder and lower for harder falls. Call where the game detects a landing.</summary>
        /// <param name="fallSpeed">Downward speed at touchdown in tiles/s.</param>
        public void PlayLand(float fallSpeed)
        {
            if (!_ready) return;
            float k = Mathf.InverseLerp(AudioMix.LandSoftSpeed, AudioMix.LandHardSpeed, fallSpeed);
            Play(SfxId.Land, Mathf.Lerp(0.45f, 1f, k), Mathf.Lerp(1.08f, 0.85f, k));
        }

        /// <summary>
        /// Plays one effect through the pool, respecting its voice limit and retrigger guard.
        /// </summary>
        /// <param name="id">The sound.</param>
        /// <param name="volumeScale">Extra level multiplier (0-1).</param>
        /// <param name="pitch">Playback pitch (1 = as generated); a small random jitter is added.</param>
        public void Play(SfxId id, float volumeScale, float pitch)
        {
            if (!_ready || id <= SfxId.None || id >= SfxId.Count) return;
            var clip = _clips[(int)id];
            if (clip == null) return;
            var spec = _specs[(int)id];
            var group = spec.Group == SfxId.None ? id : spec.Group;
            if (_now - _lastStart[(int)group] < spec.MinInterval) return;

            int slot = PickVoice(group, spec.MaxVoices);
            pitch *= 1f + spec.PitchJitter * _rng.Next();
            if (pitch < 0.1f) pitch = 0.1f;

            var source = _sources[slot];
            source.Stop();
            source.clip = clip;
            source.pitch = pitch;
            source.volume = Mathf.Clamp01(spec.Volume * volumeScale * AudioMix.SfxBus * AudioMix.Master);
            source.Play();

            _voices[slot].Group = group;
            _voices[slot].Start = _now;
            _voices[slot].End = _now + clip.length / pitch;
            _lastStart[(int)group] = _now;
        }

        // ------------------------------------------------------------------ voices

        /// <summary>Oldest voice of the group if the group is full, else a free voice, else the oldest voice.</summary>
        private int PickVoice(SfxId group, int maxVoices)
        {
            int inGroup = 0, oldestInGroup = -1, free = -1, oldest = -1;
            for (int i = 0; i < _voices.Length; i++)
            {
                var v = _voices[i];
                bool busy = v.End > _now;
                if (!busy)
                {
                    if (free < 0) free = i;
                    continue;
                }
                if (oldest < 0 || v.Start < _voices[oldest].Start) oldest = i;
                if (v.Group != group) continue;
                inGroup++;
                if (oldestInGroup < 0 || v.Start < _voices[oldestInGroup].Start) oldestInGroup = i;
            }
            if (inGroup >= Math.Max(1, maxVoices) && oldestInGroup >= 0) return oldestInGroup;
            return free >= 0 ? free : oldest >= 0 ? oldest : 0;
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

        private void BuildClips()
        {
            int count = (int)SfxId.Count;
            _clips = new AudioClip[count];
            _specs = new SfxSpec[count];
            _lastStart = new float[count];
            int rate = AudioMix.ClipSampleRate;
            for (int i = 0; i < count; i++)
            {
                var id = (SfxId)i;
                _specs[i] = AudioMix.Spec(id);
                _lastStart[i] = float.NegativeInfinity;
                float[] pcm = SynthClips.Render(id, rate);
                if (pcm.Length == 0) continue;
                var clip = AudioClip.Create("redline_" + id, pcm.Length, 1, rate, false);
                clip.SetData(pcm, 0);
                _clips[i] = clip;
            }
        }

        private void BuildPool()
        {
            var poolGo = new GameObject("Sfx");
            poolGo.transform.SetParent(transform, false);
            _sources = new AudioSource[AudioMix.SfxVoices];
            _voices = new Voice[AudioMix.SfxVoices];
            for (int i = 0; i < _sources.Length; i++)
            {
                var s = poolGo.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;
                s.dopplerLevel = 0f;
                s.priority = 64;
                _sources[i] = s;
                _voices[i].Group = SfxId.None;
                _voices[i].Start = float.NegativeInfinity;
                _voices[i].End = float.NegativeInfinity;
            }
        }

        private void BuildEventMap()
        {
            var values = (SimEventType[])Enum.GetValues(typeof(SimEventType));
            int max = 0;
            for (int i = 0; i < values.Length; i++) max = Math.Max(max, (int)values[i]);
            _eventToSfx = new int[max + 1];
            for (int i = 0; i < _eventToSfx.Length; i++) _eventToSfx[i] = (int)SfxId.None;
            for (int i = 0; i < values.Length; i++)
            {
                if ((int)values[i] < 0) continue;
                _eventToSfx[(int)values[i]] = (int)MapEvent(values[i]);
            }
        }

        /// <summary>Which sound an event plays (Hit and Kill map to tier 1; tier is added when played).</summary>
        private SfxId MapEvent(SimEventType type)
        {
            switch (type)
            {
                case SimEventType.Attack: return SfxId.Swing;
                case SimEventType.Hit: return SfxId.Hit1;
                case SimEventType.Kill: return SfxId.Kill1;
                case SimEventType.Clang: return SfxId.Clang;
                case SimEventType.HookAttach: return SfxId.HookAttach;
                case SimEventType.HookRelease: return SfxId.HookRelease;
                case SimEventType.Jump: return SfxId.Jump;
                case SimEventType.WallJump: return SfxId.WallJump;
                case SimEventType.Slide: return SfxId.Slide;
                case SimEventType.PlayerHurt: return SfxId.Hurt;
                case SimEventType.WaveStart: return SfxId.WaveStart;
                case SimEventType.Victory: return SfxId.Victory;
                case SimEventType.GameOver: return SfxId.GameOver;
                case SimEventType.BruteWindup: return SfxId.BruteWindup;
                case SimEventType.BruteCrash: return SfxId.BruteCrash;
                case SimEventType.DroneWindup: return SfxId.DroneWindup;
            }
            string name = type.ToString();
            if (Array.IndexOf(OrbSpawnNames, name) >= 0)
            {
                OrbEventsMapped = true;
                return SfxId.OrbSpawn;
            }
            if (Array.IndexOf(OrbPickupNames, name) >= 0)
            {
                OrbEventsMapped = true;
                return SfxId.OrbPickup;
            }
            if (Array.IndexOf(OrbExpireNames, name) >= 0)
            {
                OrbEventsMapped = true;
                return SfxId.OrbExpire;
            }
            return SfxId.None;
        }

        private void OnDestroy()
        {
            if (_clips == null) return;
            for (int i = 0; i < _clips.Length; i++)
                if (_clips[i] != null) Destroy(_clips[i]);
        }
    }
}
