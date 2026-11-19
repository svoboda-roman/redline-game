namespace Redline.Game.Audio
{
    /// <summary>
    /// Every sound effect (production/audio/music-sfx-overhaul/sfx-spec.md). <see cref="Count"/> is a
    /// sentinel, <see cref="None"/> means "no sound". The clips of each id are in <see cref="AudioCatalog"/>.
    /// </summary>
    public enum SfxId
    {
        None = -1,
        Swing1, Swing2, Swing3,
        Hit1, Hit2, Hit3,
        Kill1, Kill2, Kill3,
        Clang, ShieldBlock, Hurt, DeathGrunt,
        HookFire, HookLatch, HookRelease,
        Jump, WallJump, SlideStart, SlideLoop,
        LandSoft, LandMed, LandHard, Footstep,
        TierUp, TierUpTaiko, TierDown, Heartbeat,
        BruteWindup, BruteCrash, DroneWindup, DroneDive,
        ShooterWindup, Shoot, Reflect, ProjectileHit,
        FuseLoop, Explode, SummonCast, Summon,
        WaveStart, WaveClear, VictorySting, GameOverSting,
        OrbSpawnHealth, OrbSpawnSpeed, OrbSpawnHook,
        OrbPickHealth, OrbPickSpeed, OrbPickHook, OrbExpire,
        UiMove, UiConfirm, UiBack,
        Count
    }

    /// <summary>The bus an effect plays on (direction.md 3: a code-driven stand-in for the mixer groups).</summary>
    public enum SfxBus
    {
        Player,
        Impact,
        Enemy,
        Pickup,
        Stinger,
        Ui
    }

    /// <summary>
    /// The one place for Redline's audio levels and audio tuning (direction.md sections 2-3). Tables of
    /// clips are in <see cref="AudioCatalog"/>; the pure rules are in Redline.Core.Audio.AudioRules.
    /// </summary>
    /// <remarks>
    /// Mixer choice: code-driven (per-bus AudioSources, an AudioLowPassFilter on each music voice, ducks
    /// computed in code) because an AudioMixer asset cannot be authored and verified reliably from a
    /// script in batch mode, and the code path is unit-testable.
    /// </remarks>
    public static class AudioMix
    {
        /// <summary>Master level for everything Redline plays.</summary>
        public const float Master = 0.9f;
        /// <summary>Music bus level at tier 1 (direction.md 3.1: music sits under SFX).</summary>
        public const float MusicBus = 0.42f;
        /// <summary>Sound-effect bus level.</summary>
        public const float SfxBusLevel = 0.9f;
        /// <summary>Ambience bus level (rain sits well under the music).</summary>
        public const float AmbienceBus = 0.16f;

        // Player volume settings (0-1). Main thread only.
        private static float _userMaster = 1f;
        private static float _userMusic = 1f;
        private static float _userSfx = 1f;

        /// <summary>Player master volume (0-1).</summary>
        public static float UserMaster => _userMaster;
        /// <summary>Player music volume (0-1).</summary>
        public static float UserMusic => _userMusic;
        /// <summary>Player effects volume (0-1); also drives ambience and UI.</summary>
        public static float UserSfx => _userSfx;

        /// <summary>
        /// Main thread: sets the player's volumes (each clamped to 0-1). Use
        /// <see cref="RedlineAudio.SetVolumes"/> to also rescale sounds already playing.
        /// </summary>
        /// <example><code>AudioMix.SetVolumes(1f, 0.6f, 0.8f);</code></example>
        public static void SetVolumes(float master, float music, float sfx)
        {
            _userMaster = Clamp01(master);
            _userMusic = Clamp01(music);
            _userSfx = Clamp01(sfx);
        }

        private static float Clamp01(float v) => v > 1f ? 1f : v > 0f ? v : 0f; // NaN -> 0

        // ------------------------------------------------------------ music (direction.md 2)
        /// <summary>Seconds a tier-up crossfade takes.</summary>
        public const float TierUpSeconds = 1.5f;
        /// <summary>Seconds a tier-down crossfade takes.</summary>
        public const float TierDownSeconds = 3.0f;
        /// <summary>Seconds the player must stay below a tier before the music leaves it.</summary>
        public const float LayerHoldSeconds = 2.5f;
        /// <summary>Least seconds between two tier-up stings.</summary>
        public const float TierStingInterval = 4f;
        /// <summary>Overlap of a track's end and its restart (the tracks are not seamless loops).</summary>
        public const float LoopOverlapSeconds = 2f;
        /// <summary>Seconds a level music change crossfades.</summary>
        public const float LevelChangeSeconds = 2f;
        /// <summary>Seconds menu music fades out when a run starts.</summary>
        public const float MenuOutSeconds = 1f;
        /// <summary>Seconds level music fades in.</summary>
        public const float LevelInSeconds = 1.5f;
        /// <summary>Seconds level music fades out on game over.</summary>
        public const float GameOverOutSeconds = 0.4f;
        /// <summary>Seconds level music fades out on victory.</summary>
        public const float VictoryOutSeconds = 0.3f;
        /// <summary>Wave break: INTENSE muted, CALM at this linear level.</summary>
        public const float WaveBreakCalm = 0.6f;
        /// <summary>Wave break low-pass on the level music in Hz.</summary>
        public const float WaveBreakCutoff = 4000f;
        /// <summary>Upgrade screen: music cutoff in Hz.</summary>
        public const float UpgradeCutoff = 2500f;
        /// <summary>Pause: music drop in dB.</summary>
        public const float PauseDb = -6f;
        /// <summary>Pause: music cutoff in Hz.</summary>
        public const float PauseCutoff = 800f;
        /// <summary>Training: lift in dB at tier 3 (otherwise flat).</summary>
        public const float TrainingTier3LiftDb = 1.5f;
        /// <summary>Seconds a music state change (menu, upgrades, one-shots) fades.</summary>
        public const float StateFadeSeconds = 1.2f;
        /// <summary>Cutoff in Hz that counts as "filter open".</summary>
        public const float OpenCutoff = 22000f;

        // ------------------------------------------------------------ ducks (direction.md 3.2)
        /// <summary>Deepest total music duck in dB.</summary>
        public const float MusicDuckFloorDb = -9f;
        /// <summary>Deepest total rain duck in dB (outside game over).</summary>
        public const float AmbienceDuckFloorDb = -8f;
        /// <summary>Low-health heartbeat plays at or below this HP.</summary>
        public const int HeartbeatHp = 2;
        /// <summary>Seconds between heartbeats.</summary>
        public const float HeartbeatInterval = 1.05f;

        // ------------------------------------------------------------ ambience
        /// <summary>Rain level in menus relative to gameplay.</summary>
        public const float MenuRain = 0.7f;
        /// <summary>Rain level on the upgrade screen relative to gameplay.</summary>
        public const float UpgradeRain = 0.5f;
        /// <summary>Game over: rain cutoff in Hz.</summary>
        public const float GameOverRainCutoff = 600f;
        /// <summary>Game over: rain drop in dB; also applied to the gameplay SFX.</summary>
        public const float GameOverRainDb = -6f;
        /// <summary>City hum level relative to the rain.</summary>
        public const float CityHum = 0.45f;
        /// <summary>Seconds between far thunder one-shots (min, max).</summary>
        public const float ThunderMin = 25f, ThunderMax = 60f;

        // ------------------------------------------------------------ effects
        /// <summary>Pooled AudioSources for one-shot effects (direction.md 5: 32-voice budget).</summary>
        public const int SfxVoices = 28;
        /// <summary>Looping effect sources (slide, fuses).</summary>
        public const int LoopVoices = 5;
        /// <summary>Fall speed (tiles/s) at or below which a landing is soft.</summary>
        public const float LandSoftMax = 10f;
        /// <summary>Fall speed (tiles/s) at or above which a landing is hard.</summary>
        public const float LandHardMin = 18f;
        /// <summary>Horizontal speed (tiles/s) above which the grounded player takes steps.</summary>
        public const float StepMinSpeed = 0.5f;
        /// <summary>Footstep interval = clamp(StepBase - StepPerSpeed * speed, StepMin, StepBase).</summary>
        public const float StepBase = 0.42f, StepPerSpeed = 0.025f, StepMinInterval = 0.17f;
        /// <summary>Fuse loop pitch at the start and at the end of a fuse.</summary>
        public const float FusePitchStart = 0.9f, FusePitchEnd = 1.4f;
        /// <summary>Expected fuse length used for the pitch ramp (seconds).</summary>
        public const float FuseRampSeconds = 1.0f;
        /// <summary>Seconds a looping effect fades out when it stops.</summary>
        public const float LoopFadeSeconds = 0.12f;
        /// <summary>Seconds a trimmed one-shot (e.g. the death grunt) fades at its cut.</summary>
        public const float TrimFadeSeconds = 0.25f;
        /// <summary>World sound pan width in tiles from the camera (2D pan; direction.md 4).</summary>
        public const float PanHalfWidth = 12f;
        /// <summary>Largest pan magnitude.</summary>
        public const float PanMax = 0.8f;

        /// <summary>Output peak ceiling of the master limiter (about -1 dBFS).</summary>
        public const float LimiterThreshold = 0.89f;
        /// <summary>Limiter release time in seconds.</summary>
        public const float LimiterReleaseSeconds = 0.15f;
    }
}
