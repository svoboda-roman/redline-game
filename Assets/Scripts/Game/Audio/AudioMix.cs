namespace Redline.Game.Audio
{
    /// <summary>Every synthesised sound effect. <see cref="Count"/> is a sentinel, <see cref="None"/> means "no sound".</summary>
    public enum SfxId
    {
        None = -1,
        Swing,
        Hit1,
        Hit2,
        Hit3,
        Kill1,
        Kill2,
        Kill3,
        Clang,
        HookAttach,
        HookRelease,
        Jump,
        WallJump,
        Land,
        Slide,
        Hurt,
        WaveStart,
        Victory,
        GameOver,
        BruteWindup,
        BruteCrash,
        DroneWindup,
        OrbSpawn,
        OrbPickup,
        OrbExpire,
        Count
    }

    /// <summary>Per-sound mix settings: level, polyphony limit and retrigger guard.</summary>
    public readonly struct SfxSpec
    {
        /// <summary>Linear level before the SFX bus and master (0-1).</summary>
        public readonly float Volume;
        /// <summary>Most voices of this sound's <see cref="Group"/> that may sound at once; the oldest is stolen beyond it.</summary>
        public readonly int MaxVoices;
        /// <summary>Seconds that must pass between two starts of the same group; starts inside it are dropped.</summary>
        public readonly float MinInterval;
        /// <summary>Random pitch spread (+/- fraction) so repeats do not sound identical.</summary>
        public readonly float PitchJitter;
        /// <summary>Sounds sharing a group share one voice limit (e.g. the three kill tiers).</summary>
        public readonly SfxId Group;

        public SfxSpec(float volume, int maxVoices, float minInterval, float pitchJitter, SfxId group)
        {
            Volume = volume;
            MaxVoices = maxVoices;
            MinInterval = minInterval;
            PitchJitter = pitchJitter;
            Group = group;
        }
    }

    /// <summary>
    /// The one place for Redline's audio levels and audio tuning (design/game-brief.md: driving
    /// synthwave). Pure C#: shared by the main thread, the audio thread and the offline renderer.
    /// </summary>
    /// <remarks>
    /// Gain staging: every SFX clip is normalised to <see cref="ClipPeak"/>, then scaled by its
    /// <see cref="SfxSpec.Volume"/>, <see cref="SfxBus"/> and <see cref="Master"/>. Music is scaled by
    /// <see cref="MusicBus"/> and <see cref="Master"/>, so it sits under the effects. A peak limiter on
    /// the AudioListener (<see cref="LimiterThreshold"/>) catches any pile-up so the output never clips.
    /// </remarks>
    public static class AudioMix
    {
        /// <summary>Master level for everything Redline plays.</summary>
        public const float Master = 0.9f;
        /// <summary>Level of the sound-effect bus.</summary>
        public const float SfxBus = 0.85f;
        /// <summary>Level of the music bus (kept well under the SFX bus).</summary>
        public const float MusicBus = 0.34f;

        /// <summary>Music level multiplier while the run is over.</summary>
        public const float GameOverDuck = 0.55f;
        /// <summary>Seconds for the music to duck or recover.</summary>
        public const float DuckSeconds = 0.8f;

        /// <summary>Music tempo in beats per minute.</summary>
        public const float MusicBpm = 112f;
        /// <summary>Seconds for a music layer to fade in when its tier is reached.</summary>
        public const float LayerFadeInSeconds = 0.7f;
        /// <summary>Seconds for a music layer to fade out.</summary>
        public const float LayerFadeOutSeconds = 2.0f;
        /// <summary>Seconds the player must stay below a tier before its music layer starts to fade out.</summary>
        public const float LayerHoldSeconds = 2.5f;
        /// <summary>Seconds the base layer takes to fade in when the game starts.</summary>
        public const float MusicStartFadeSeconds = 1.0f;

        /// <summary>Number of pooled AudioSources for effects (Unity's default real-voice budget is 32).</summary>
        public const int SfxVoices = 14;
        /// <summary>Sample rate the effect clips are generated at (Unity resamples to the output rate).</summary>
        public const int ClipSampleRate = 44100;
        /// <summary>Peak every effect clip is normalised to before its volume is applied.</summary>
        public const float ClipPeak = 0.9f;

        /// <summary>Output peak ceiling of the master limiter (about -1 dBFS).</summary>
        public const float LimiterThreshold = 0.89f;
        /// <summary>Limiter release time in seconds.</summary>
        public const float LimiterReleaseSeconds = 0.15f;

        /// <summary>Fall speed (tiles/s) at or below which a landing is quietest.</summary>
        public const float LandSoftSpeed = 6f;
        /// <summary>Fall speed (tiles/s) at which a landing is loudest.</summary>
        public const float LandHardSpeed = 24f;
        /// <summary>Swing pitch rise per speed tier above 1.</summary>
        public const float SwingPitchPerTier = 0.07f;

        /// <summary>Mix settings of one sound.</summary>
        public static SfxSpec Spec(SfxId id)
        {
            switch (id)
            {
                case SfxId.Swing: return new SfxSpec(0.56f, 2, 0.04f, 0.04f, SfxId.Swing);
                case SfxId.Hit1: return new SfxSpec(0.50f, 3, 0.03f, 0.05f, SfxId.Hit1);
                case SfxId.Hit2: return new SfxSpec(0.58f, 3, 0.03f, 0.05f, SfxId.Hit1);
                case SfxId.Hit3: return new SfxSpec(0.66f, 3, 0.03f, 0.05f, SfxId.Hit1);
                case SfxId.Kill1: return new SfxSpec(0.60f, 2, 0.05f, 0.04f, SfxId.Kill1);
                case SfxId.Kill2: return new SfxSpec(0.70f, 2, 0.05f, 0.04f, SfxId.Kill1);
                case SfxId.Kill3: return new SfxSpec(0.80f, 2, 0.05f, 0.04f, SfxId.Kill1);
                case SfxId.Clang: return new SfxSpec(0.55f, 2, 0.06f, 0.03f, SfxId.Clang);
                case SfxId.HookAttach: return new SfxSpec(0.52f, 1, 0.03f, 0.03f, SfxId.HookAttach);
                case SfxId.HookRelease: return new SfxSpec(0.46f, 1, 0.03f, 0.04f, SfxId.HookRelease);
                case SfxId.Jump: return new SfxSpec(0.38f, 2, 0.03f, 0.03f, SfxId.Jump);
                case SfxId.WallJump: return new SfxSpec(0.42f, 1, 0.03f, 0.03f, SfxId.WallJump);
                case SfxId.Land: return new SfxSpec(0.38f, 1, 0.08f, 0.05f, SfxId.Land);
                case SfxId.Slide: return new SfxSpec(0.44f, 1, 0.05f, 0.03f, SfxId.Slide);
                case SfxId.Hurt: return new SfxSpec(0.72f, 1, 0.08f, 0f, SfxId.Hurt);
                case SfxId.WaveStart: return new SfxSpec(0.48f, 1, 0.2f, 0f, SfxId.WaveStart);
                case SfxId.Victory: return new SfxSpec(0.62f, 1, 0.5f, 0f, SfxId.Victory);
                case SfxId.GameOver: return new SfxSpec(0.66f, 1, 0.5f, 0f, SfxId.GameOver);
                case SfxId.BruteWindup: return new SfxSpec(0.38f, 2, 0.1f, 0.04f, SfxId.BruteWindup);
                case SfxId.BruteCrash: return new SfxSpec(0.62f, 2, 0.08f, 0.04f, SfxId.BruteCrash);
                case SfxId.DroneWindup: return new SfxSpec(0.3f, 2, 0.1f, 0.05f, SfxId.DroneWindup);
                case SfxId.OrbSpawn: return new SfxSpec(0.4f, 2, 0.06f, 0.05f, SfxId.OrbSpawn);
                case SfxId.OrbPickup: return new SfxSpec(0.48f, 3, 0.03f, 0.03f, SfxId.OrbPickup);
                case SfxId.OrbExpire: return new SfxSpec(0.22f, 2, 0.06f, 0.05f, SfxId.OrbExpire);
                default: return new SfxSpec(0f, 1, 1f, 0f, SfxId.None);
            }
        }
    }
}
