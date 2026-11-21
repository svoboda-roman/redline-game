using UnityEngine;

namespace Redline.Game.Art
{
    /// <summary>
    /// Tuning for how the samurai player sheet is drawn by <see cref="ActorRenderer"/>: sheet path,
    /// animation names, emissive colours and brightness, run-cycle rate scaling, lean.
    /// The speed-tier colours themselves come from <see cref="RedlineGame.TierColors"/>.
    /// </summary>
    public static class SamuraiLook
    {
        /// <summary>Resources path of the sheet, without suffix.</summary>
        public const string SheetPath = "Art/Characters/Samurai/samurai";

        /// <summary>Animation names in the sheet JSON.</summary>
        public const string Idle = "idle", Run = "run", Slide = "slide", Jump = "jump", Fall = "fall",
            WallSlide = "wall_slide", HookSwing = "hook_swing", Attack = "attack", AirAttack = "air_attack",
            Hurt = "hurt", Death = "death";

        /// <summary>
        /// Multiplier on the lit body sprite. Above 1 lifts the dark palette off the murky background
        /// (vertex colour is clamped to 1 per channel, so values are kept at 1; brightness comes from the rim light).
        /// </summary>
        public static readonly Color BodyTint = Color.white;

        /// <summary>
        /// Rim light intensity multiplier over AtmosphereConfig.PlayerLightIntensity: the samurai's dark
        /// palette blended into the night background at 1.0 (see samurai-ingame-* evidence).
        /// </summary>
        public const float RimLightBoost = 1.6f;

        /// <summary>Blade colour = tier colour mixed this far toward white (hot core reads brightest).</summary>
        public const float BladeWhiteMix = 0.35f;
        /// <summary>Visor slit (fixed red).</summary>
        public static readonly Color Visor = new Color(1f, 0.2f, 0.27f);
        /// <summary>Cyber-arm light strips (fixed cyan).</summary>
        public static readonly Color Arm = new Color(0.42f, 0.95f, 1f);

        /// <summary>Soft glow sprite around the blade: base alpha, per-tier alpha, base size, per-tier size.</summary>
        public const float BladeGlowAlpha = 0.3f, BladeGlowAlphaPerTier = 0.06f, BladeGlowSize = 1.3f, BladeGlowSizePerTier = 0.25f;
        /// <summary>Alpha of the tier-coloured blade ghosts left while striking, and their life in seconds.</summary>
        public const float SlashGhostAlpha = 0.5f, SlashGhostLife = 0.12f;

        /// <summary>
        /// Strike playback rates, overriding the sheet's 18 fps so the swing matches the 0.15 s
        /// strike hitbox (attackActiveTime): attack (8 frames, smear on 2-3) runs 0.27 s with the
        /// smear at 0.07-0.13 s; air_attack (5 frames, smear on 1-2) runs 0.25 s with it at 0.05-0.15 s.
        /// </summary>
        public const float AttackFps = 30f, AirAttackFps = 20f;

        /// <summary>Run cycle: fps scale = |Vx| / runSpeed, clamped to this range.</summary>
        public const float RunRateMin = 0.6f, RunRateMax = 2.2f;
        /// <summary>|Vx| below this is standing (idle).</summary>
        public const float RunMinSpeed = 0.6f;
        /// <summary>Lean into the run: degrees per unit of speed, and the cap (the drawn run already leans).</summary>
        public const float LeanPerSpeed = 0.35f, MaxLean = 6f;

        /// <summary>Invulnerability blink: toggles per second and the alpha of the faded half.</summary>
        public const float BlinkRate = 16f, BlinkAlpha = 0.3f;

        /// <summary>Sorting orders: body, then blade, visor, arm drawn just above it.</summary>
        public const int BodyOrder = 10, BladeOrder = 12, VisorOrder = 11, ArmOrder = 11;
    }
}
