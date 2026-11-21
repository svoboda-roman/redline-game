using Redline.Core;

namespace Redline.Game.Audio
{
    /// <summary>How one effect plays (sfx-spec.md columns Var, Vol, Pitch, Voices, Prio).</summary>
    public sealed class SfxDef
    {
        /// <summary>Variant clips, Resources paths relative to Audio/Sfx/ without extension.</summary>
        public string[] Clips;
        /// <summary>Gain range in dB; a random value inside it is used per play.</summary>
        public float MinDb, MaxDb;
        /// <summary>Pitch jitter in semitones (+/-).</summary>
        public float JitterSt;
        /// <summary>Base pitch multiplier (e.g. 0.7 to darken a clip).</summary>
        public float Pitch = 1f;
        /// <summary>Most simultaneous voices of this effect.</summary>
        public int Voices = 2;
        /// <summary>0 (highest) to 5; a lower number may steal a higher one's voice.</summary>
        public int Priority = 3;
        /// <summary>Bus for gain staging and the game-over SFX drop.</summary>
        public SfxBus Bus;
        /// <summary>Play only the first N seconds (with a fade); 0 = whole clip.</summary>
        public float Trim;
        /// <summary>Optional clips layered under the main one for weight.</summary>
        public string[] Layer;
        /// <summary>Layer gain in dB.</summary>
        public float LayerDb;
        /// <summary>Layer pitch multiplier.</summary>
        public float LayerPitch = 1f;
        /// <summary>Layer trim in seconds (0 = whole clip).</summary>
        public float LayerTrim;
        /// <summary>Seconds that must pass between two starts (retrigger guard).</summary>
        public float MinInterval = 0.03f;
        /// <summary>World sound: panned by its event position relative to the player.</summary>
        public bool World;
    }

    /// <summary>One music track: Resources path and the part of it that plays.</summary>
    public sealed class MusicTrackDef
    {
        /// <summary>Resources path relative to Audio/ without extension.</summary>
        public string Clip;
        /// <summary>Seconds of leading silence to skip.</summary>
        public float Head;
        /// <summary>Seconds where the music ends (before trailing silence); the loop restarts before it.</summary>
        public float End;

        public MusicTrackDef(string clip, float head, float end)
        {
            Clip = clip;
            Head = head;
            End = end;
        }
    }

    /// <summary>
    /// The data table mapping sfx-spec.md events to the recorded clips in Assets/Resources/Audio
    /// (tools/audio-sources/manifest.json) and levels to music (direction.md 2.1). Head and End were
    /// measured with ffmpeg silencedetect at -45 dB.
    /// </summary>
    public static class AudioCatalog
    {
        private static readonly string[] SwingLight = { "Katana/katana_swing_light_01", "Katana/katana_swing_light_02", "Katana/katana_swing_light_03", "Katana/katana_swing_light_04" };
        private static readonly string[] SwingHeavy = { "Katana/katana_swing_heavy_01", "Katana/katana_swing_heavy_02", "Katana/katana_swing_heavy_03", "Katana/katana_swing_heavy_04" };
        private static readonly string[] Whoosh = { "Katana/blade_whoosh_01", "Katana/blade_whoosh_02", "Katana/blade_whoosh_03" };
        private static readonly string[] SlashAll = { "Slash/slash_hit_01", "Slash/slash_hit_02", "Slash/slash_hit_03", "Slash/slash_hit_04", "Slash/slash_hit_05", "Slash/slash_hit_06" };
        private static readonly string[] SlashShort = { "Slash/slash_hit_03", "Slash/slash_hit_04", "Slash/slash_hit_06", "Slash/slash_flesh_01", "Slash/slash_flesh_02" };
        private static readonly string[] Flesh = { "Slash/slash_flesh_01", "Slash/slash_flesh_02" };
        private static readonly string[] Heavy = { "Impact/impact_heavy_01", "Impact/impact_heavy_02", "Impact/impact_heavy_03", "Impact/impact_heavy_04", "Impact/impact_heavy_05", "Impact/impact_heavy_06" };
        private static readonly string[] ClangSword = { "Clang/clang_sword_01", "Clang/clang_sword_02", "Clang/clang_sword_03", "Clang/clang_sword_04", "Clang/clang_sword_05", "Clang/clang_sword_06" };
        private static readonly string[] Steps = { "Footstep/footstep_wet_01", "Footstep/footstep_wet_02", "Footstep/footstep_wet_03", "Footstep/footstep_wet_04", "Footstep/footstep_wet_05" };
        private static readonly string[] Slides = { "Slide/slide_scrape_01", "Slide/slide_scrape_02", "Slide/slide_scrape_03" };
        private static readonly string[] Drums = { "Ambience/war_drums_loop" };
        private static readonly string[] Magic = { "Magic/magic_cast_01", "Magic/magic_cast_02", "Magic/magic_cast_03", "Magic/magic_cast_04", "Magic/magic_cast_05", "Magic/magic_cast_06", "Magic/magic_cast_07" };

        private static SfxDef[] _defs;

        /// <summary>The definition of every <see cref="SfxId"/> (index = id); never null entries.</summary>
        public static SfxDef[] Defs => _defs ??= Build();

        /// <summary>Definition of one effect.</summary>
        public static SfxDef Get(SfxId id) => Defs[(int)id];

        private static readonly string[] ReaperAttackClips = { "Voice/reaper_attack_01", "Voice/reaper_attack_02", "Voice/reaper_attack_03" };
        private static readonly string[] ReaperHurtClips = { "Voice/reaper_damaged_01", "Voice/reaper_damaged_02", "Voice/reaper_damaged_03" };
        private static readonly string[] ReaperJumpClips = { "Voice/reaper_jump_01", "Voice/reaper_jump_02", "Voice/reaper_jump_03" };

        private static SfxDef[] Build()
        {
            var d = new SfxDef[(int)SfxId.Count];
            // Player (sfx-spec 1)
            d[(int)SfxId.Swing1] = new SfxDef { Clips = SwingLight, MinDb = -9, MaxDb = -6, JitterSt = 0.6f, Voices = 2, Priority = 2, Bus = SfxBus.Player, Trim = 0.45f };
            d[(int)SfxId.Swing2] = new SfxDef { Clips = SwingHeavy, MinDb = -8, MaxDb = -5, JitterSt = 0.5f, Voices = 2, Priority = 2, Bus = SfxBus.Player, Trim = 0.6f, Layer = Whoosh, LayerDb = -10 };
            d[(int)SfxId.Swing3] = new SfxDef { Clips = SwingHeavy, MinDb = -6, MaxDb = -3, JitterSt = 0.4f, Pitch = 0.92f, Voices = 2, Priority = 1, Bus = SfxBus.Player, Trim = 0.8f, Layer = new[] { "Clang/clang_metal_hit_02", "Clang/clang_katana_02" }, LayerDb = -13, LayerPitch = 1.15f, LayerTrim = 0.7f };
            d[(int)SfxId.Hit1] = new SfxDef { Clips = SlashShort, MinDb = -8, MaxDb = -5, JitterSt = 0.6f, Voices = 3, Priority = 2, Bus = SfxBus.Impact, Trim = 0.45f, World = true };
            d[(int)SfxId.Hit2] = new SfxDef { Clips = SlashAll, MinDb = -7, MaxDb = -4, JitterSt = 0.5f, Voices = 3, Priority = 2, Bus = SfxBus.Impact, Trim = 0.55f, Layer = Heavy, LayerDb = -12, LayerTrim = 0.4f, World = true };
            d[(int)SfxId.Hit3] = new SfxDef { Clips = SlashAll, MinDb = -5, MaxDb = -2, JitterSt = 0.4f, Pitch = 0.9f, Voices = 3, Priority = 1, Bus = SfxBus.Impact, Trim = 0.7f, Layer = Heavy, LayerDb = -6, LayerPitch = 0.8f, LayerTrim = 0.6f, World = true };
            d[(int)SfxId.Kill1] = new SfxDef { Clips = SlashAll, MinDb = -7, MaxDb = -4, JitterSt = 0.5f, Voices = 3, Priority = 2, Bus = SfxBus.Impact, Trim = 0.6f, Layer = Heavy, LayerDb = -9, LayerTrim = 0.5f, World = true, MinInterval = 0.04f };
            d[(int)SfxId.Kill2] = new SfxDef { Clips = SlashAll, MinDb = -6, MaxDb = -3, JitterSt = 0.5f, Voices = 3, Priority = 2, Bus = SfxBus.Impact, Trim = 0.8f, Layer = new[] { "Explosion/explosion_scifi_02", "Explosion/explosion_small" }, LayerDb = -9, LayerTrim = 0.7f, World = true, MinInterval = 0.04f };
            d[(int)SfxId.Kill3] = new SfxDef { Clips = Heavy, MinDb = -4, MaxDb = 0, JitterSt = 0.3f, Pitch = 0.8f, Voices = 2, Priority = 1, Bus = SfxBus.Impact, Trim = 1.1f, Layer = new[] { "Explosion/explosion_scifi_01", "Explosion/explosion_synth" }, LayerDb = -6, LayerPitch = 0.85f, LayerTrim = 1.2f, World = true, MinInterval = 0.05f };
            d[(int)SfxId.Clang] = new SfxDef { Clips = ClangSword, MinDb = -8, MaxDb = -4, JitterSt = 1f, Voices = 2, Priority = 2, Bus = SfxBus.Impact, Trim = 0.8f, World = true, MinInterval = 0.06f };
            d[(int)SfxId.ShieldBlock] = new SfxDef { Clips = new[] { "Impact/impact_metal", "Clang/clang_metal_hit_01" }, MinDb = -8, MaxDb = -4, JitterSt = 0.6f, Pitch = 0.85f, Voices = 2, Priority = 2, Bus = SfxBus.Impact, Trim = 0.5f, World = true };
            d[(int)SfxId.Hurt] = new SfxDef { Clips = new[] { "Hurt/player_hurt_01", "Hurt/player_hurt_02", "Hurt/player_hurt_03", "Hurt/player_hurt_04", "Hurt/player_hurt_05" }, MinDb = -5, MaxDb = -2, JitterSt = 0.4f, Voices = 1, Priority = 1, Bus = SfxBus.Impact, Layer = new[] { "Hurt/player_hurt_body_hit" }, LayerDb = -4, MinInterval = 0.08f };
            // Reaper voice (female): kiai on swings and jumps (rate-limited so it never chatters), her own hurt and death.
            d[(int)SfxId.ReaperAttack] = new SfxDef { Clips = ReaperAttackClips, MinDb = -9, MaxDb = -6, JitterSt = 0.3f, Voices = 1, Priority = 2, Bus = SfxBus.Player, MinInterval = 0.9f };
            d[(int)SfxId.ReaperHurt] = new SfxDef { Clips = ReaperHurtClips, MinDb = -5, MaxDb = -2, JitterSt = 0.3f, Voices = 1, Priority = 1, Bus = SfxBus.Impact, Layer = new[] { "Hurt/player_hurt_body_hit" }, LayerDb = -6, MinInterval = 0.08f };
            d[(int)SfxId.ReaperDeath] = new SfxDef { Clips = new[] { "Voice/reaper_damaged_01" }, MinDb = -4, MaxDb = -3, Pitch = 0.82f, Voices = 1, Priority = 0, Bus = SfxBus.Stinger, Trim = 1.4f };
            d[(int)SfxId.ReaperJump] = new SfxDef { Clips = ReaperJumpClips, MinDb = -13, MaxDb = -10, JitterSt = 0.3f, Voices = 1, Priority = 3, Bus = SfxBus.Player, MinInterval = 1.4f };
            d[(int)SfxId.DeathGrunt] = new SfxDef { Clips = new[] { "Hurt/player_death_grunt" }, MinDb = -5, MaxDb = -4, Voices = 1, Priority = 0, Bus = SfxBus.Stinger, Trim = 1.2f };
            d[(int)SfxId.HookFire] = new SfxDef { Clips = new[] { "Grapple/grapple_cable_whip" }, MinDb = -9, MaxDb = -6, JitterSt = 0.6f, Voices = 1, Priority = 3, Bus = SfxBus.Player, Trim = 0.5f };
            d[(int)SfxId.HookLatch] = new SfxDef { Clips = new[] { "Grapple/grapple_latch_metal", "Grapple/grapple_link" }, MinDb = -9, MaxDb = -6, JitterSt = 0.5f, Voices = 1, Priority = 3, Bus = SfxBus.Player, Trim = 0.5f };
            d[(int)SfxId.HookRelease] = new SfxDef { Clips = new[] { "Grapple/grapple_unlink" }, MinDb = -12, MaxDb = -9, JitterSt = 0.6f, Voices = 1, Priority = 3, Bus = SfxBus.Player, Trim = 0.5f };
            d[(int)SfxId.Jump] = new SfxDef { Clips = Steps, MinDb = -12, MaxDb = -9, JitterSt = 0.7f, Pitch = 0.85f, Voices = 2, Priority = 3, Bus = SfxBus.Player, Layer = Whoosh, LayerDb = -16, LayerPitch = 0.7f };
            d[(int)SfxId.WallJump] = new SfxDef { Clips = new[] { "Landing/land_metal_thud_03", "Clang/clang_metal_hit_02" }, MinDb = -11, MaxDb = -8, JitterSt = 0.6f, Voices = 2, Priority = 3, Bus = SfxBus.Player, Trim = 0.4f, Layer = Whoosh, LayerDb = -13 };
            d[(int)SfxId.SlideStart] = new SfxDef { Clips = Slides, MinDb = -9, MaxDb = -6, JitterSt = 0.8f, Voices = 1, Priority = 3, Bus = SfxBus.Player, Trim = 0.45f };
            d[(int)SfxId.SlideLoop] = new SfxDef { Clips = Slides, MinDb = -15, MaxDb = -13, Voices = 1, Priority = 4, Bus = SfxBus.Player };
            d[(int)SfxId.LandSoft] = new SfxDef { Clips = Steps, MinDb = -14, MaxDb = -10, JitterSt = 0.6f, Voices = 2, Priority = 4, Bus = SfxBus.Player, MinInterval = 0.08f };
            d[(int)SfxId.LandMed] = new SfxDef { Clips = new[] { "Landing/land_thud_01", "Landing/land_thud_02", "Landing/land_metal_thud_03" }, MinDb = -10, MaxDb = -7, JitterSt = 0.5f, Voices = 2, Priority = 3, Bus = SfxBus.Player, Trim = 0.5f, MinInterval = 0.08f };
            d[(int)SfxId.LandHard] = new SfxDef { Clips = new[] { "Landing/land_metal_thud_01", "Landing/land_metal_thud_02" }, MinDb = -7, MaxDb = -4, JitterSt = 0.4f, Voices = 1, Priority = 2, Bus = SfxBus.Player, Trim = 0.7f, Layer = Heavy, LayerDb = -9, LayerPitch = 0.75f, LayerTrim = 0.6f, MinInterval = 0.08f };
            d[(int)SfxId.Footstep] = new SfxDef { Clips = Steps, MinDb = -21, MaxDb = -17, JitterSt = 0.8f, Voices = 3, Priority = 4, Bus = SfxBus.Player, MinInterval = 0.1f };
            d[(int)SfxId.TierUp] = new SfxDef { Clips = Heavy, MinDb = -9, MaxDb = -7, Pitch = 0.75f, Voices = 1, Priority = 1, Bus = SfxBus.Stinger, Layer = Drums, LayerDb = -8, LayerTrim = 1.4f };
            d[(int)SfxId.TierUpTaiko] = new SfxDef { Clips = SwingHeavy, MinDb = -13, MaxDb = -11, Pitch = 0.8f, Voices = 1, Priority = 1, Bus = SfxBus.Stinger, Trim = 0.9f };
            d[(int)SfxId.TierDown] = new SfxDef { Clips = SwingHeavy, MinDb = -17, MaxDb = -14, Pitch = 0.6f, Voices = 1, Priority = 4, Bus = SfxBus.Stinger, Trim = 0.9f };
            d[(int)SfxId.Heartbeat] = new SfxDef { Clips = new[] { "Landing/land_thud_01", "Landing/land_thud_02" }, MinDb = -19, MaxDb = -18, Pitch = 0.55f, Voices = 1, Priority = 3, Bus = SfxBus.Stinger, Trim = 0.45f, MinInterval = 0.3f };
            // Enemies (sfx-spec 2)
            d[(int)SfxId.BruteWindup] = new SfxDef { Clips = new[] { "Magic/energy_charge_up" }, MinDb = -10, MaxDb = -7, JitterSt = 0.5f, Pitch = 0.6f, Voices = 2, Priority = 2, Bus = SfxBus.Enemy, Trim = 1.0f, Layer = new[] { "Drone/drone_machine_loop_01", "Drone/drone_machine_loop_02" }, LayerDb = -12, LayerPitch = 0.6f, LayerTrim = 1.0f, World = true, MinInterval = 0.1f };
            d[(int)SfxId.BruteCrash] = new SfxDef { Clips = Heavy, MinDb = -6, MaxDb = -3, JitterSt = 0.4f, Pitch = 0.7f, Voices = 1, Priority = 1, Bus = SfxBus.Impact, Layer = new[] { "Explosion/explosion_far", "Impact/impact_stone" }, LayerDb = -6, LayerTrim = 1.2f, World = true };
            d[(int)SfxId.DroneWindup] = new SfxDef { Clips = new[] { "Katana/blade_whoosh_01", "Katana/blade_whoosh_02", "Katana/blade_whoosh_03" }, MinDb = -11, MaxDb = -8, JitterSt = 0.5f, Pitch = 0.5f, Voices = 3, Priority = 2, Bus = SfxBus.Enemy, Trim = 0.7f, World = true, MinInterval = 0.1f };   // the crow beats its wings
            d[(int)SfxId.DroneDive] = new SfxDef { Clips = SwingHeavy, MinDb = -10, MaxDb = -7, JitterSt = 0.6f, Pitch = 0.65f, Voices = 3, Priority = 3, Bus = SfxBus.Enemy, Trim = 0.7f, World = true };
            d[(int)SfxId.ShooterWindup] = new SfxDef { Clips = new[] { "Magic/energy_charge_up" }, MinDb = -12, MaxDb = -9, JitterSt = 0.4f, Pitch = 1.35f, Voices = 2, Priority = 2, Bus = SfxBus.Enemy, Trim = 0.6f, World = true };
            d[(int)SfxId.Shoot] = new SfxDef { Clips = new[] { "Laser/railgun_shot_01", "Laser/railgun_shot_02", "Laser/laser_shot_01", "Laser/laser_shot_03" }, MinDb = -11, MaxDb = -7, JitterSt = 0.6f, Pitch = 0.8f, Voices = 3, Priority = 3, Bus = SfxBus.Enemy, Trim = 0.45f, World = true };
            d[(int)SfxId.Reflect] = new SfxDef { Clips = new[] { "Clang/clang_metal_hit_02", "Clang/clang_sword_05", "Clang/clang_sword_02" }, MinDb = -8, MaxDb = -4, JitterSt = 1f, Pitch = 1.1f, Voices = 2, Priority = 2, Bus = SfxBus.Impact, Trim = 0.5f, Layer = new[] { "Laser/railgun_shot_01" }, LayerDb = -14, LayerPitch = 1.3f, World = true };
            d[(int)SfxId.ProjectileHit] = new SfxDef { Clips = new[] { "Impact/impact_metal", "Explosion/explosion_small" }, MinDb = -14, MaxDb = -10, JitterSt = 0.8f, Voices = 3, Priority = 4, Bus = SfxBus.Enemy, Trim = 0.35f, World = true };
            d[(int)SfxId.FuseLoop] = new SfxDef { Clips = new[] { "Fuse/fuse_hiss_rocket" }, MinDb = -9, MaxDb = -7, Voices = 3, Priority = 2, Bus = SfxBus.Enemy, World = true };
            d[(int)SfxId.Explode] = new SfxDef { Clips = new[] { "Explosion/explosion_big_cinematic", "Explosion/explosion_scifi_01", "Explosion/explosion_synth" }, MinDb = -4, MaxDb = -1, JitterSt = 0.3f, Voices = 2, Priority = 1, Bus = SfxBus.Impact, Trim = 2.2f, Layer = new[] { "Explosion/explosion_muffled_distant" }, LayerDb = -8, LayerTrim = 2.2f, World = true };
            d[(int)SfxId.SummonCast] = new SfxDef { Clips = Magic, MinDb = -12, MaxDb = -9, JitterSt = 0.4f, Pitch = 0.8f, Voices = 1, Priority = 2, Bus = SfxBus.Enemy, Trim = 1.1f, World = true };
            d[(int)SfxId.Summon] = new SfxDef { Clips = new[] { "Magic/magic_cast_02", "Magic/magic_cast_05", "Magic/magic_cast_06" }, MinDb = -11, MaxDb = -7, JitterSt = 0.5f, Pitch = 1.2f, Voices = 2, Priority = 3, Bus = SfxBus.Enemy, Trim = 0.6f, World = true };
            // Flow (sfx-spec 3)
            d[(int)SfxId.WaveStart] = new SfxDef { Clips = Drums, MinDb = -7, MaxDb = -5, Voices = 1, Priority = 1, Bus = SfxBus.Stinger, Trim = 2.0f, Layer = Heavy, LayerDb = -9, LayerPitch = 0.7f, MinInterval = 0.2f };
            d[(int)SfxId.WaveClear] = new SfxDef { Clips = new[] { "Magic/magic_cast_04" }, MinDb = -11, MaxDb = -9, Pitch = 0.75f, Voices = 1, Priority = 1, Bus = SfxBus.Stinger, Trim = 2.4f, Layer = new[] { "Pickup/pickup_gem_04" }, LayerDb = -10, LayerPitch = 0.5f, MinInterval = 0.5f };
            d[(int)SfxId.VictorySting] = new SfxDef { Clips = new[] { "Impact/impact_heavy_05" }, MinDb = -5, MaxDb = -4, Pitch = 0.7f, Voices = 1, Priority = 0, Bus = SfxBus.Stinger, Layer = Drums, LayerDb = -7, LayerTrim = 3f, MinInterval = 0.5f };
            d[(int)SfxId.GameOverSting] = new SfxDef { Clips = new[] { "Explosion/explosion_muffled_distant" }, MinDb = -5, MaxDb = -4, Pitch = 0.8f, Voices = 1, Priority = 0, Bus = SfxBus.Stinger, Trim = 3.5f, MinInterval = 0.5f };
            // Orbs
            d[(int)SfxId.OrbSpawnHealth] = new SfxDef { Clips = new[] { "Pickup/pickup_gem_01", "Pickup/pickup_gem_02", "Pickup/pickup_gem_03" }, MinDb = -16, MaxDb = -12, JitterSt = 0.5f, Pitch = 0.75f, Voices = 3, Priority = 4, Bus = SfxBus.Pickup, World = true };
            d[(int)SfxId.OrbSpawnSpeed] = new SfxDef { Clips = new[] { "Pickup/pickup_tech_02", "Pickup/pickup_tech_03" }, MinDb = -16, MaxDb = -12, JitterSt = 0.5f, Pitch = 0.8f, Voices = 3, Priority = 4, Bus = SfxBus.Pickup, Trim = 0.5f, World = true };
            d[(int)SfxId.OrbSpawnHook] = new SfxDef { Clips = new[] { "Clang/clang_metal_hit_02" }, MinDb = -17, MaxDb = -13, JitterSt = 0.5f, Pitch = 1.5f, Voices = 3, Priority = 4, Bus = SfxBus.Pickup, World = true };
            d[(int)SfxId.OrbPickHealth] = new SfxDef { Clips = new[] { "Pickup/pickup_gem_04", "Pickup/pickup_gem_03" }, MinDb = -10, MaxDb = -7, JitterSt = 0.3f, Pitch = 0.8f, Voices = 2, Priority = 3, Bus = SfxBus.Pickup };
            d[(int)SfxId.OrbPickSpeed] = new SfxDef { Clips = new[] { "Pickup/pickup_tech_01", "Pickup/pickup_tech_03" }, MinDb = -10, MaxDb = -7, JitterSt = 0.3f, Pitch = 0.85f, Voices = 2, Priority = 3, Bus = SfxBus.Pickup, Trim = 0.8f };
            d[(int)SfxId.OrbPickHook] = new SfxDef { Clips = new[] { "Pickup/pickup_tech_02" }, MinDb = -10, MaxDb = -7, JitterSt = 0.3f, Voices = 2, Priority = 3, Bus = SfxBus.Pickup, Layer = new[] { "Grapple/grapple_latch_metal" }, LayerDb = -10 };
            d[(int)SfxId.OrbExpire] = new SfxDef { Clips = new[] { "Ui/ui_tech_blip_01", "Ui/ui_tech_blip_02", "Ui/ui_tech_blip_03" }, MinDb = -23, MaxDb = -19, JitterSt = 0.5f, Pitch = 0.6f, Voices = 2, Priority = 5, Bus = SfxBus.Pickup, World = true };
            // UI (sfx-spec 4)
            d[(int)SfxId.UiMove] = new SfxDef { Clips = new[] { "Ui/ui_click" }, MinDb = -19, MaxDb = -16, JitterSt = 0.4f, Voices = 2, Priority = 5, Bus = SfxBus.Ui };
            d[(int)SfxId.UiConfirm] = new SfxDef { Clips = new[] { "Ui/ui_positive" }, MinDb = -14, MaxDb = -11, JitterSt = 0.3f, Voices = 1, Priority = 4, Bus = SfxBus.Ui };
            d[(int)SfxId.UiBack] = new SfxDef { Clips = new[] { "Ui/ui_negative" }, MinDb = -16, MaxDb = -13, JitterSt = 0.3f, Voices = 1, Priority = 4, Bus = SfxBus.Ui };
            for (int i = 0; i < d.Length; i++)
                d[i] ??= new SfxDef { Clips = new string[0] };
            return d;
        }

        // ------------------------------------------------------------------ music

        /// <summary>Main menu and upgrade screen theme.</summary>
        public static readonly MusicTrackDef MainTheme = new MusicTrackDef("Music/main_theme", 0.66f, 307.9f);
        /// <summary>Training (flat, no tier crossfade).</summary>
        public static readonly MusicTrackDef Training = new MusicTrackDef("Music/training_calm", 2.28f, 281.6f);
        /// <summary>Victory one-shot.</summary>
        public static readonly MusicTrackDef Victory = new MusicTrackDef("Music/victory", 0f, 99.2f);
        /// <summary>Game over one-shot.</summary>
        public static readonly MusicTrackDef GameOver = new MusicTrackDef("Music/game_over", 0f, 143.1f);

        private static readonly MusicTrackDef Loop01Calm = new MusicTrackDef("Music/loop01_calm", 0f, 197.4f);
        private static readonly MusicTrackDef Loop01Intense = new MusicTrackDef("Music/loop01_intense", 0f, 163.9f);
        private static readonly MusicTrackDef Loop02Calm = new MusicTrackDef("Music/loop02_calm", 0f, 207.0f);
        private static readonly MusicTrackDef Loop02Intense = new MusicTrackDef("Music/loop02_intense", 0f, 176.2f);
        private static readonly MusicTrackDef Loop03Calm = new MusicTrackDef("Music/loop03_calm", 0f, 327.9f);
        private static readonly MusicTrackDef Loop03Intense = new MusicTrackDef("Music/loop03_intense", 0f, 555.3f);
        private static readonly MusicTrackDef BonusTaiko = new MusicTrackDef("Music/bonus_taiko", 0f, 197.1f);

        /// <summary>CALM and INTENSE tracks of a level (unknown levels use Loop01's pair).</summary>
        public static void LevelPair(string level, out MusicTrackDef calm, out MusicTrackDef intense)
        {
            switch (level)
            {
                case "Loop02": calm = Loop02Calm; intense = Loop02Intense; return;
                case "Loop03": calm = Loop03Calm; intense = Loop03Intense; return;
                case "Arena01": calm = Loop03Calm; intense = BonusTaiko; return;
                default: calm = Loop01Calm; intense = Loop01Intense; return;
            }
        }

        // ------------------------------------------------------------------ ambience

        /// <summary>Rain bed (always on).</summary>
        public static readonly MusicTrackDef Rain = new MusicTrackDef("Sfx/Ambience/rain_loop_03", 0f, 45f);
        /// <summary>Distant city hum (always on, under the rain).</summary>
        public static readonly MusicTrackDef CityHum = new MusicTrackDef("Sfx/Ambience/city_hum_loop", 0f, 29.3f);
        /// <summary>Far thunder one-shot (Resources path under Audio/Sfx/).</summary>
        public const string Thunder = "Ambience/thunder_01";

        /// <summary>Rain gain offset in dB per level (sfx-spec 5: Loop01 heavier, Loop03 lighter).</summary>
        public static float RainDb(string level) => level == "Loop01" ? 2f : level == "Loop03" ? -3f : 0f;

        /// <summary>Whether far thunder plays in a level.</summary>
        public static bool ThunderIn(string level) => level != "Loop03" && level != "Arena01";

        /// <summary>The orb spawn effect of an orb kind (SimEvent.Detail).</summary>
        public static SfxId OrbSpawn(int kind) => kind == (int)OrbKind.Speed ? SfxId.OrbSpawnSpeed : kind == (int)OrbKind.Hook ? SfxId.OrbSpawnHook : SfxId.OrbSpawnHealth;

        /// <summary>The orb pickup effect of an orb kind (SimEvent.Detail).</summary>
        public static SfxId OrbPickup(int kind) => kind == (int)OrbKind.Speed ? SfxId.OrbPickSpeed : kind == (int)OrbKind.Hook ? SfxId.OrbPickHook : SfxId.OrbPickHealth;
    }
}
