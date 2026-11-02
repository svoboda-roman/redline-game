using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>
    /// All tuning values. Defaults here mirror Assets/Resources/RedlineConfig.json;
    /// the game layer overwrites them from that file (Unity JsonUtility needs public fields).
    /// Units: tiles and seconds (1 tile = 1 world unit).
    /// </summary>
    [Serializable]
    public sealed class RedlineConfig
    {
        // --- Player body ---
        public float playerWidth = 0.8f;
        public float playerHeight = 1.6f;
        public float slideHeight = 0.8f;

        // --- Movement ---
        public float gravity = 45f;
        public float maxFallSpeed = 24f;
        public float runSpeed = 9f;
        public float maxSpeed = 22f;
        public float groundAccel = 60f;
        public float groundFriction = 50f;
        public float airAccel = 30f;
        /// <summary>Speed lost per second above runSpeed while running on the ground.</summary>
        public float overspeedDecay = 2.5f;
        public float jumpSpeed = 15.5f;
        public float jumpCutMultiplier = 0.5f;
        public float coyoteTime = 0.08f;
        public float jumpBufferTime = 0.1f;
        /// <summary>How long an attack, slide or hook press waits for its cooldown/ground before it is dropped.</summary>
        public float inputBufferTime = 0.12f;

        // --- Wall jump ---
        /// <summary>Minimum horizontal launch speed of a wall jump (the wall-impact speed is used when higher).</summary>
        public float wallJumpSpeedX = 8f;
        public float wallJumpSpeedY = 14f;
        public float wallJumpKick = 2f;
        public float wallJumpLockTime = 0.15f;
        public float wallSlideMaxFall = 6f;
        /// <summary>How long the speed you hit a wall with is remembered for the wall jump.</summary>
        public float wallImpactMemory = 0.25f;

        // --- Slide ---
        /// <summary>Speed added by a slide. Run + slide must stay below tier3Speed: tier 3 is earned.</summary>
        public float slideBoost = 1.5f;
        public float slideDuration = 0.45f;
        public float slideMinSpeed = 5f;
        public float slideCooldown = 0.4f;

        // --- Grappling hook ---
        public float hookRange = 11f;
        /// <summary>Angle above horizontal, in degrees, toward the facing direction.</summary>
        public float hookAngleDeg = 60f;
        public float hookMinLength = 1.5f;
        public float hookPumpAccel = 10f;
        public float hookCooldown = 0.2f;
        /// <summary>Upward speed given when jumping off the rope (only if slower than this).</summary>
        public float hookJumpSpeed = 9f;

        // --- Speed tiers and combat ---
        /// <summary>Horizontal speed at which tier 2 starts.</summary>
        public float tier2Speed = 8f;
        /// <summary>Horizontal speed at which tier 3 starts.</summary>
        public float tier3Speed = 12f;
        public float baseDamage = 1f;
        /// <summary>Damage multiplier for tiers 1, 2, 3.</summary>
        public float[] tierDamageMultipliers = { 1f, 2f, 4f };
        /// <summary>Knockback speed applied to enemies for tiers 1, 2, 3.</summary>
        public float[] tierKnockback = { 5f, 9f, 14f };
        public float attackRange = 1.5f;
        public float attackHeight = 1.6f;
        public float attackActiveTime = 0.15f;
        public float attackCooldown = 0.25f;
        public float killMomentumRefund = 2.5f;
        /// <summary>Seconds after a kill in which the next kill extends the chain.</summary>
        public float chainWindow = 1.5f;
        /// <summary>Extra refund per chain step beyond the first kill.</summary>
        public float chainBonusPerKill = 0.75f;
        /// <summary>Cap on the extra chain refund.</summary>
        public float chainMaxBonus = 3f;
        public float clangBounceSpeed = 7f;
        /// <summary>How far the strike box reaches behind the player's centre (tiles).</summary>
        public float strikeBackReach = 0.2f;
        /// <summary>Upward knockback on a struck enemy, as a fraction of its horizontal knockback.</summary>
        public float knockbackLift = 0.4f;

        // --- Player health ---
        public int playerMaxHp = 6;
        public float invulnTime = 1f;
        public float hurtKnockbackX = 7f;
        public float hurtKnockbackY = 9f;
        public int spikeDamage = 1;
        public float spikeBounceSpeed = 15f;
        /// <summary>Fraction of horizontal speed kept when bouncing off spikes.</summary>
        public float spikeSpeedKeep = 0.3f;

        // --- Enemies ---
        public float gruntWidth = 0.9f;
        public float gruntHeight = 1.1f;
        public float gruntHp = 1f;
        public float gruntSpeed = 3.5f;
        public float gruntJumpSpeed = 13f;
        public int gruntContactDamage = 1;
        public float bruteWidth = 1.3f;
        public float bruteHeight = 2f;
        public float bruteHp = 4f;
        public float bruteSpeed = 1.6f;
        public float bruteJumpSpeed = 11f;
        public int bruteContactDamage = 2;
        /// <summary>Lowest player tier that can damage a brute.</summary>
        public int bruteMinTier = 3;
        /// <summary>Brutes take this fraction of knockback (they are heavy).</summary>
        public float bruteKnockbackScale = 0.35f;
        public float enemyStunTime = 0.35f;
        /// <summary>Charging brutes: how far away (tiles, same level) the player triggers a charge.</summary>
        public float bruteChargeRange = 9f;
        public float bruteWindupTime = 0.5f;
        public float bruteChargeSpeed = 11f;
        public float bruteChargeTime = 1f;
        public float bruteRecoverTime = 0.6f;
        /// <summary>How long a brute stays dazed (armor down) after charging into a wall.</summary>
        public float bruteCrashTime = 1.5f;
        public float bruteChargeCooldown = 2f;
        /// <summary>First wave (1-based) whose brutes charge.</summary>
        public int bruteChargeFromWave = 4;
        // --- Drone ---
        public float droneSize = 0.8f;
        public float droneHp = 1f;
        public int droneContactDamage = 1;
        public float droneSpeed = 3f;
        /// <summary>Hover height of the drone's underside above the player's feet (out of jump-strike reach).</summary>
        public float droneHoverHeight = 5f;
        public float droneWindupTime = 0.45f;
        public float droneDiveSpeed = 12f;
        public float droneDiveTime = 0.8f;
        /// <summary>Hover time before a drone may dive (also after each dive).</summary>
        public float droneDiveCooldown = 1.5f;
        /// <summary>A drone starts its dive when this close (tiles, horizontally) to its hover point, with line of sight.</summary>
        public float droneDiveTriggerX = 1.2f;
        public float enemyFriction = 20f;
        /// <summary>Horizontal gap between enemies that share a spawn point (tiles).</summary>
        public float spawnSpacing = 0.8f;

        // --- Waves ---
        public float firstWaveDelay = 1.5f;
        public float waveBreakTime = 2.5f;
        /// <summary>HP restored when a wave is cleared (capped at playerMaxHp).</summary>
        public int waveClearHeal = 2;
        /// <summary>Grunts in each wave. Length = number of waves.</summary>
        public int[] waveGrunts = { 3, 5, 6, 6, 8 };
        /// <summary>Brutes in each wave. Same length as waveGrunts.</summary>
        public int[] waveBrutes = { 0, 1, 1, 2, 3 };
        /// <summary>Drones in each wave. Same length as waveGrunts.</summary>
        public int[] waveDrones = { 0, 0, 2, 2, 3 };

        /// <summary>Number of waves in a run.</summary>
        public int WaveCount => waveGrunts == null ? 0 : waveGrunts.Length;

        /// <summary>Returns a list of human-readable problems; empty when the config is usable.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            void Positive(string name, float v)
            {
                if (!(v > 0f)) errors.Add($"{name} must be > 0 (was {v})");
            }

            Positive(nameof(playerWidth), playerWidth);
            Positive(nameof(playerHeight), playerHeight);
            Positive(nameof(slideHeight), slideHeight);
            Positive(nameof(gravity), gravity);
            Positive(nameof(runSpeed), runSpeed);
            Positive(nameof(jumpSpeed), jumpSpeed);
            Positive(nameof(hookRange), hookRange);
            Positive(nameof(attackRange), attackRange);
            Positive(nameof(attackCooldown), attackCooldown);
            Positive(nameof(inputBufferTime), inputBufferTime);
            Positive(nameof(spawnSpacing), spawnSpacing);
            Positive(nameof(chainWindow), chainWindow);
            if (spikeSpeedKeep < 0f || spikeSpeedKeep > 1f) errors.Add("spikeSpeedKeep must be 0-1");
            Positive(nameof(gruntHp), gruntHp);
            Positive(nameof(bruteHp), bruteHp);
            if (slideHeight > playerHeight) errors.Add("slideHeight must not exceed playerHeight");
            if (maxSpeed < runSpeed) errors.Add("maxSpeed must be >= runSpeed");
            if (!(tier3Speed > tier2Speed)) errors.Add("tier3Speed must be > tier2Speed");
            if (tierDamageMultipliers == null || tierDamageMultipliers.Length != 3)
                errors.Add("tierDamageMultipliers needs exactly 3 values");
            if (tierKnockback == null || tierKnockback.Length != 3)
                errors.Add("tierKnockback needs exactly 3 values");
            if (bruteMinTier < 1 || bruteMinTier > 3) errors.Add("bruteMinTier must be 1-3");
            if (playerMaxHp < 1) errors.Add("playerMaxHp must be >= 1");
            if (waveClearHeal < 0) errors.Add("waveClearHeal must be >= 0");
            if (waveGrunts == null || waveGrunts.Length == 0) errors.Add("waveGrunts needs at least one wave");
            if (waveBrutes == null || waveGrunts == null || waveBrutes.Length != waveGrunts.Length)
                errors.Add("waveBrutes must have the same length as waveGrunts");
            else if (waveDrones == null || waveDrones.Length != waveGrunts.Length)
                errors.Add("waveDrones must have the same length as waveGrunts");
            else
                for (int i = 0; i < waveGrunts.Length; i++)
                    if (waveGrunts[i] < 0 || waveBrutes[i] < 0 || waveDrones[i] < 0
                        || waveGrunts[i] + waveBrutes[i] + waveDrones[i] == 0)
                        errors.Add($"wave {i + 1} must have a non-negative, non-zero enemy count");
            Positive(nameof(droneSize), droneSize);
            Positive(nameof(droneHp), droneHp);
            Positive(nameof(bruteChargeTime), bruteChargeTime);
            return errors;
        }
    }
}
