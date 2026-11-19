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
        /// <summary>
        /// Horizontal speed cap in the air. Above maxSpeed (a hook fling) speed decays toward maxSpeed
        /// at airOverspeedDecay; on the ground overspeedDecay and friction bring it down.
        /// </summary>
        public float airMaxSpeed = 30f;
        /// <summary>Speed lost per second above maxSpeed while airborne.</summary>
        public float airOverspeedDecay = 8f;
        public float jumpSpeed = 15.5f;
        public float jumpCutMultiplier = 0.5f;
        public float coyoteTime = 0.08f;
        public float jumpBufferTime = 0.1f;
        /// <summary>How long an attack, slide or hook press waits for its cooldown/ground before it is dropped.</summary>
        public float inputBufferTime = 0.12f;

        // --- Wall jump ---
        /// <summary>Minimum horizontal launch speed of a wall jump (the wall-impact speed is used when higher).</summary>
        public float wallJumpSpeedX = 8f;
        public float wallJumpSpeedY = 15f;
        public float wallJumpKick = 2f;
        public float wallJumpLockTime = 0.1f;
        public float wallSlideMaxFall = 6f;
        /// <summary>How long the speed you hit a wall with is remembered for the wall jump.</summary>
        public float wallImpactMemory = 0.25f;

        /// <summary>Seconds after leaving a wall in the air during which a jump still wall-jumps (forgiving timing).</summary>
        public float wallCoyoteTime = 0.12f;

        // --- Slide ---
        /// <summary>Speed added by a slide. Run + slide must stay below tier3Speed: tier 3 is earned.</summary>
        public float slideBoost = 1.8f;
        /// <summary>Longest a ground slide lasts. It also ends early once friction has slowed it to slideStopSpeed.</summary>
        public float slideDuration = 0.85f;
        // A ground slide is shaped by the speed tier it starts at: slideHeight/slideBoost/slideDuration
        // above are tier 2 (a normal run). Tier 1 is a half crouch, tier 3 a full slide low to the ground.
        /// <summary>Tier 1 slide (started below tier2Speed): half crouch height.</summary>
        public float slideTier1Height = 1.15f;
        public float slideTier1Boost = 0.8f;
        public float slideTier1Duration = 0.55f;
        /// <summary>Tier 3 slide (started at tier3Speed or faster): body near the ground.</summary>
        public float slideTier3Height = 0.55f;
        public float slideTier3Boost = 2.5f;
        public float slideTier3Duration = 1.0f;
        /// <summary>Length of a slide started in the air (a short dash).</summary>
        public float airSlideDuration = 0.45f;
        /// <summary>Deceleration while sliding on the ground (units/s^2): the slide bleeds speed instead of stopping dead.</summary>
        public float slideFriction = 4f;
        /// <summary>
        /// How long a ground slide holds its full speed before slideFriction starts. Keeps the
        /// slide-into-wall wall jump able to reach tier 3.
        /// </summary>
        public float slideFrictionDelay = 0.4f;
        /// <summary>A ground slide slower than this stands up (e.g. after hitting a wall).</summary>
        public float slideStopSpeed = 2.5f;
        public float slideMinSpeed = 5f;
        public float slideCooldown = 0.4f;
        /// <summary>A sliding player hits enemies in its body plus this much reach in front.</summary>
        public float slideHitReach = 0.4f;
        /// <summary>Slide hit damage as a multiple of the sword's damage at the current tier.</summary>
        public float slideDamageMultiplier = 1.0f;

        // --- Grappling hook ---
        public float hookRange = 11f;
        /// <summary>Angle above horizontal, in degrees, toward the facing direction.</summary>
        public float hookAngleDeg = 60f;
        public float hookMinLength = 1.5f;
        public float hookPumpAccel = 10f;
        public float hookCooldown = 0.2f;
        /// <summary>Rope reel speed while hanging (units/s): up reels in (pulls toward the anchor), down lets out.</summary>
        public float hookReelSpeed = 4f;
        /// <summary>How fast the reel speeds up / slows down (units/s^2): a smooth start and stop instead of a yank.</summary>
        public float hookReelAccel = 16f;
        /// <summary>Longest the rope can be let out to (units).</summary>
        public float hookMaxLength = 12f;
        /// <summary>Speed added toward the anchor when the hook attaches from the ground (an instant lift, no dead time).</summary>
        public float hookAttachPull = 7f;
        /// <summary>Upward speed given when jumping off the rope (only if slower than this).</summary>
        public float hookJumpSpeed = 9f;
        /// <summary>
        /// Speed added along the velocity on a voluntary release in the air (the fling), at full
        /// strength from maxSpeed down, ramping to 0 at hookReleaseBoostMinSpeed.
        /// </summary>
        public float hookReleaseBoost = 8f;
        /// <summary>Speed at or below which a release gives no fling (so a hook tap is not a dash).</summary>
        public float hookReleaseBoostMinSpeed = 9f;
        /// <summary>Seconds on the rope before a release can fling.</summary>
        public float hookReleaseMinTime = 0.25f;

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
        /// <summary>How far above the player's head the sword arc reaches (tiles); the strike box starts at the feet (below them while falling: strikeDownReach).</summary>
        public float attackReachUp = 0.6f;
        /// <summary>
        /// How far below the feet the strike box reaches while the player is airborne and falling
        /// (tiles), so a dive hits an enemy before the body touches it. 0 = never below the feet.
        /// </summary>
        public float strikeDownReach = 0.6f;
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
        public float gruntWidth = 1.0f;
        public float gruntHeight = 1.3f;
        public float gruntHp = 3f;
        public float gruntSpeed = 3.5f;
        /// <summary>High enough (~2.3 tiles) to hop the 2-tile bumps levels use.</summary>
        public float gruntJumpSpeed = 14.5f;
        public int gruntContactDamage = 1;
        public float bruteWidth = 1.5f;
        /// <summary>Hitbox height; kept low enough that a full jump clears a charge (the sprite is drawn taller).</summary>
        public float bruteHeight = 2.1f;
        public float bruteHp = 8f;
        public float bruteSpeed = 1.6f;
        /// <summary>0: brutes are too heavy to jump; a bump stops them (and a charge crashes into it).</summary>
        public float bruteJumpSpeed = 0f;
        public int bruteContactDamage = 2;
        /// <summary>Lowest player tier that can damage a brute.</summary>
        public int bruteMinTier = 3;
        /// <summary>Brutes take this fraction of knockback (they are heavy).</summary>
        public float bruteKnockbackScale = 0.35f;
        public float enemyStunTime = 0.35f;
        /// <summary>Walkers leave their post for a player this close horizontally (tiles).</summary>
        public float enemyAggroRange = 8f;
        /// <summary>...and at most this far above or below (tiles).</summary>
        public float enemyAggroHeight = 16f;
        /// <summary>Walkers never chase further than this from their post (tiles).</summary>
        public float enemyLeash = 10f;
        /// <summary>Drones leave their post for a player this close horizontally (tiles).</summary>
        public float droneAggroRange = 10f;
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
        public float droneSize = 1.0f;
        public float droneHp = 2f;
        public int droneContactDamage = 1;
        public float droneSpeed = 3f;
        /// <summary>Hover height of the drone's underside above the player's feet (out of jump-strike reach).</summary>
        public float droneHoverHeight = 5.5f;
        public float droneWindupTime = 0.45f;
        public float droneDiveSpeed = 12f;
        public float droneDiveTime = 0.8f;
        /// <summary>Hover time before a drone may dive (also after each dive).</summary>
        public float droneDiveCooldown = 1.5f;
        /// <summary>A drone starts its dive when this close (tiles, horizontally) to its hover point, with line of sight.</summary>
        public float droneDiveTriggerX = 1.2f;
        // --- Shooter ---
        public float shooterWidth = 0.9f;
        public float shooterHeight = 1.5f;
        public float shooterHp = 2.0f;
        public int shooterContactDamage = 1;
        /// <summary>Walk speed back to its post (it never chases).</summary>
        public float shooterSpeed = 2.0f;
        public float shooterJumpSpeed = 0.0f;
        /// <summary>Fires when the player is within this many tiles horizontally (and enemyAggroHeight vertically), with line of sight.</summary>
        public float shooterRange = 12.0f;
        /// <summary>Telegraph before each shot: stands still, then fires at where the player is.</summary>
        public float shooterWindupTime = 0.6f;
        /// <summary>Seconds between the end of one shot and the next telegraph.</summary>
        public float shooterCooldown = 1.8f;
        /// <summary>Projectile hitbox (square, tiles).</summary>
        public float projectileSize = 0.4f;
        public float projectileSpeed = 9.0f;
        /// <summary>Damage an unreflected projectile deals to the player.</summary>
        public int projectileDamage = 1;
        /// <summary>Seconds before a projectile fizzles (reset on reflect).</summary>
        public float projectileLifetime = 3.0f;
        /// <summary>Projectile pool size; shots beyond it are skipped.</summary>
        public int projectileMaxCount = 24;
        /// <summary>A reflected projectile flies back this many times faster.</summary>
        public float reflectSpeedMultiplier = 1.5f;
        /// <summary>Damage a reflected projectile deals to the enemy it hits (ignores armor; a shield facing it blocks it).</summary>
        public float reflectedProjectileDamage = 3.0f;
        // --- Shield bearer ---
        public float shieldWidth = 1.1f;
        public float shieldHeight = 1.6f;
        public float shieldHp = 4.0f;
        public int shieldContactDamage = 1;
        public float shieldSpeed = 2.6f;
        public float shieldJumpSpeed = 12.0f;
        /// <summary>Seconds the bearer must face a new way before its shield follows (the window to strike its back).</summary>
        public float shieldTurnTime = 0.6f;
        /// <summary>A falling player whose feet are above this fraction of the bearer's height strikes over the shield.</summary>
        public float shieldAboveFraction = 0.5f;
        // --- Kamikaze ---
        public float kamikazeWidth = 0.9f;
        public float kamikazeHeight = 1.0f;
        public float kamikazeHp = 3.0f;
        /// <summary>Touch damage (0: only the blast hurts, so touching it never grants invulnerability through the blast).</summary>
        public int kamikazeContactDamage = 0;
        /// <summary>Walk speed while guarding its post.</summary>
        public float kamikazeSpeed = 3.0f;
        public float kamikazeJumpSpeed = 13.0f;
        /// <summary>Starts its rush when the player is this close horizontally, with line of sight.</summary>
        public float kamikazeAggroRange = 14.0f;
        /// <summary>Rush speed (above runSpeed: a plain run no longer outpaces it, a slide or a hit does).</summary>
        public float kamikazeRushSpeed = 12.0f;
        /// <summary>Stops and lights its fuse this close to the player (centre to centre).</summary>
        public float kamikazeFuseRange = 2.2f;
        /// <summary>Telegraph: seconds from lighting the fuse to the blast. A kill before the blast defuses it.</summary>
        public float kamikazeFuseTime = 0.5f;
        /// <summary>Fuse length when a non-lethal hit lights it (shorter than <see cref="kamikazeFuseTime"/>).</summary>
        public float kamikazeHitFuseTime = 0.35f;
        public float kamikazeBlastRadius = 3.2f;
        /// <summary>While rushing and grounded within this horizontal distance, it leaps at the player once.</summary>
        public float kamikazeLungeRange = 4.5f;
        /// <summary>Horizontal speed of the lunge (towards the player).</summary>
        public float kamikazeLungeSpeedX = 16.0f;
        /// <summary>Vertical speed of the lunge.</summary>
        public float kamikazeLungeSpeedY = 9.0f;
        /// <summary>Blast damage to the player.</summary>
        public int kamikazeBlastDamage = 2;
        /// <summary>Blast damage to each other enemy in the radius (ignores armor and shields).</summary>
        public float kamikazeBlastEnemyDamage = 3.0f;
        // --- Summoner ---
        public float summonerWidth = 0.9f;
        public float summonerHeight = 1.7f;
        public float summonerHp = 4.0f;
        public int summonerContactDamage = 1;
        /// <summary>Flee speed; must stay below runSpeed so the player can catch it on a ring.</summary>
        public float summonerSpeed = 6.0f;
        public float summonerJumpSpeed = 12.0f;
        /// <summary>Flees while the player is this close horizontally.</summary>
        public float summonerFleeRange = 9.0f;
        /// <summary>Casts only while the player is this close horizontally.</summary>
        public float summonerCastRange = 14.0f;
        /// <summary>Seconds between casts (also before the first).</summary>
        public float summonerCastInterval = 4.0f;
        /// <summary>Telegraph: stands still this long, then the drone appears. A hit cancels the cast.</summary>
        public float summonerCastTime = 0.8f;
        /// <summary>Most live drones one summoner keeps summoned; it waits while at the cap.</summary>
        public int summonerMaxDrones = 2;
        /// <summary>The drone appears this far above the summoner's head.</summary>
        public float summonerSummonHeight = 2.5f;
        public float enemyFriction = 20f;
        /// <summary>Horizontal gap between enemies that share a spawn point (tiles).</summary>
        public float spawnSpacing = 0.8f;

        // --- Orbs (dropped by enemies) ---
        /// <summary>Seed of the sim's own RNG (orb drops). Same seed and inputs give the same run.</summary>
        public int rngSeed = 1;
        /// <summary>Chance (0-1) that a killed grunt drops an orb.</summary>
        public float gruntOrbChance = 0.12f;
        /// <summary>Chance (0-1) that a killed brute drops an orb.</summary>
        public float bruteOrbChance = 0.5f;
        /// <summary>Chance (0-1) that a killed drone drops an orb.</summary>
        public float droneOrbChance = 0.25f;
        /// <summary>Chance (0-1) that a killed shooter drops an orb.</summary>
        public float shooterOrbChance = 0.3f;
        /// <summary>Chance (0-1) that a killed shield drops an orb.</summary>
        public float shieldOrbChance = 0.35f;
        /// <summary>Chance (0-1) that a killed kamikaze drops an orb.</summary>
        public float kamikazeOrbChance = 0.15f;
        /// <summary>Chance (0-1) that a killed summoner drops an orb.</summary>
        public float summonerOrbChance = 0.6f;
        /// <summary>Relative weight of a dropped orb being Health (weights are normalised).</summary>
        public float orbWeightHealth = 1f;
        /// <summary>Relative weight of a dropped orb being Speed.</summary>
        public float orbWeightSpeed = 1f;
        /// <summary>Relative weight of a dropped orb being Hook.</summary>
        public float orbWeightHook = 1f;
        /// <summary>Orb hitbox size (square, tiles).</summary>
        public float orbSize = 0.6f;
        /// <summary>Seconds an orb lies around before it vanishes.</summary>
        public float orbLifetime = 8f;
        /// <summary>Upward speed an orb pops out of the enemy with, before it falls.</summary>
        public float orbPopSpeed = 6f;
        /// <summary>Seconds after spawning before an orb can be picked up (so the pop is seen).</summary>
        public float orbPickupDelay = 0.25f;
        /// <summary>Most orbs alive at once (the pool size); drops beyond it are skipped.</summary>
        public int orbMaxCount = 16;
        /// <summary>HP restored by a Health orb (capped at playerMaxHp).</summary>
        public int orbHealAmount = 1;
        /// <summary>Seconds a Speed orb lasts.</summary>
        public float orbSpeedDuration = 6f;
        /// <summary>Speed orb: multiplier on runSpeed, maxSpeed and airMaxSpeed.</summary>
        public float orbSpeedMultiplier = 1.25f;
        /// <summary>Seconds a Hook orb lasts (no hook cooldown).</summary>
        public float orbHookDuration = 8f;
        /// <summary>Hook orb: extra hook range (tiles).</summary>
        public float orbHookRangeBonus = 3f;

        // --- Waves ---
        public float firstWaveDelay = 1.5f;
        public float waveBreakTime = 2.5f;
        /// <summary>Seconds the victory moment is shown before the game loads the next level.</summary>
        public float levelAdvanceDelay = 3f;
        /// <summary>HP restored when a wave is cleared (capped at playerMaxHp).</summary>
        public int waveClearHeal = 2;
        /// <summary>Grunts in each wave. Length = number of waves.</summary>
        public int[] waveGrunts = { 3, 5, 6, 6, 8 };
        /// <summary>Brutes in each wave. Same length as waveGrunts.</summary>
        public int[] waveBrutes = { 0, 1, 1, 1, 2 };
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
            if (hookReelSpeed < 0f) errors.Add("hookReelSpeed must be >= 0");
            if (wallCoyoteTime < 0f) errors.Add("wallCoyoteTime must be >= 0");
            Positive(nameof(attackRange), attackRange);
            if (strikeDownReach < 0f) errors.Add("strikeDownReach must be >= 0");
            Positive(nameof(attackCooldown), attackCooldown);
            Positive(nameof(inputBufferTime), inputBufferTime);
            Positive(nameof(spawnSpacing), spawnSpacing);
            Positive(nameof(chainWindow), chainWindow);
            if (spikeSpeedKeep < 0f || spikeSpeedKeep > 1f) errors.Add("spikeSpeedKeep must be 0-1");
            Positive(nameof(gruntHp), gruntHp);
            Positive(nameof(bruteHp), bruteHp);
            if (slideHeight > playerHeight) errors.Add("slideHeight must not exceed playerHeight");
            if (maxSpeed < runSpeed) errors.Add("maxSpeed must be >= runSpeed");
            if (airMaxSpeed < maxSpeed) errors.Add("airMaxSpeed must be >= maxSpeed");
            if (airOverspeedDecay < 0f) errors.Add("airOverspeedDecay must be >= 0");
            if (hookReleaseBoost < 0f) errors.Add("hookReleaseBoost must be >= 0");
            if (hookReleaseBoostMinSpeed >= maxSpeed) errors.Add("hookReleaseBoostMinSpeed must be < maxSpeed");
            if (hookReleaseMinTime < 0f) errors.Add("hookReleaseMinTime must be >= 0");
            if (!(tier3Speed > tier2Speed)) errors.Add("tier3Speed must be > tier2Speed");
            if (tierDamageMultipliers == null || tierDamageMultipliers.Length != 3)
                errors.Add("tierDamageMultipliers needs exactly 3 values");
            if (tierKnockback == null || tierKnockback.Length != 3)
                errors.Add("tierKnockback needs exactly 3 values");
            if (bruteMinTier < 1 || bruteMinTier > 3) errors.Add("bruteMinTier must be 1-3");
            if (playerMaxHp < 1) errors.Add("playerMaxHp must be >= 1");
            if (waveClearHeal < 0) errors.Add("waveClearHeal must be >= 0");
            if (levelAdvanceDelay < 0f) errors.Add("levelAdvanceDelay must be >= 0");
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
            void Chance(string name, float v)
            {
                if (!(v >= 0f && v <= 1f)) errors.Add($"{name} must be 0-1 (was {v})");
            }
            Chance(nameof(gruntOrbChance), gruntOrbChance);
            Chance(nameof(bruteOrbChance), bruteOrbChance);
            Chance(nameof(droneOrbChance), droneOrbChance);
            if (orbWeightHealth < 0f || orbWeightSpeed < 0f || orbWeightHook < 0f || !(orbWeightHealth + orbWeightSpeed + orbWeightHook > 0f))
                errors.Add("orb weights must be >= 0 with a positive sum");
            Positive(nameof(orbSize), orbSize);
            Positive(nameof(orbLifetime), orbLifetime);
            Positive(nameof(orbSpeedDuration), orbSpeedDuration);
            Positive(nameof(orbHookDuration), orbHookDuration);
            if (orbPickupDelay < 0f) errors.Add("orbPickupDelay must be >= 0");
            if (orbMaxCount < 1) errors.Add("orbMaxCount must be >= 1");
            if (orbHealAmount < 0) errors.Add("orbHealAmount must be >= 0");
            if (orbSpeedMultiplier < 1f) errors.Add("orbSpeedMultiplier must be >= 1");
            if (orbHookRangeBonus < 0f) errors.Add("orbHookRangeBonus must be >= 0");
            Positive(nameof(droneSize), droneSize);
            Positive(nameof(droneHp), droneHp);
            Positive(nameof(bruteChargeTime), bruteChargeTime);
            Positive(nameof(shooterWidth), shooterWidth);
            Positive(nameof(shooterHeight), shooterHeight);
            Positive(nameof(shooterHp), shooterHp);
            Positive(nameof(shooterWindupTime), shooterWindupTime);
            Positive(nameof(shooterCooldown), shooterCooldown);
            Positive(nameof(projectileSize), projectileSize);
            Positive(nameof(projectileSpeed), projectileSpeed);
            Positive(nameof(projectileLifetime), projectileLifetime);
            Positive(nameof(reflectSpeedMultiplier), reflectSpeedMultiplier);
            Positive(nameof(shieldWidth), shieldWidth);
            Positive(nameof(shieldHeight), shieldHeight);
            Positive(nameof(shieldHp), shieldHp);
            Positive(nameof(shieldTurnTime), shieldTurnTime);
            Positive(nameof(kamikazeWidth), kamikazeWidth);
            Positive(nameof(kamikazeHeight), kamikazeHeight);
            Positive(nameof(kamikazeHp), kamikazeHp);
            Positive(nameof(kamikazeRushSpeed), kamikazeRushSpeed);
            Positive(nameof(kamikazeFuseRange), kamikazeFuseRange);
            Positive(nameof(kamikazeFuseTime), kamikazeFuseTime);
            Positive(nameof(kamikazeBlastRadius), kamikazeBlastRadius);
            Positive(nameof(kamikazeHitFuseTime), kamikazeHitFuseTime);
            Positive(nameof(kamikazeLungeRange), kamikazeLungeRange);
            Positive(nameof(kamikazeLungeSpeedX), kamikazeLungeSpeedX);
            Positive(nameof(kamikazeLungeSpeedY), kamikazeLungeSpeedY);
            Positive(nameof(slideDamageMultiplier), slideDamageMultiplier);
            Positive(nameof(slideDuration), slideDuration);
            Positive(nameof(airSlideDuration), airSlideDuration);
            Positive(nameof(slideTier1Height), slideTier1Height);
            Positive(nameof(slideTier3Height), slideTier3Height);
            Positive(nameof(slideTier1Duration), slideTier1Duration);
            Positive(nameof(slideTier3Duration), slideTier3Duration);
            if (slideTier1Height > playerHeight) errors.Add("slideTier1Height must not exceed playerHeight");
            if (slideTier3Height > playerHeight) errors.Add("slideTier3Height must not exceed playerHeight");
            if (!(slideTier1Boost >= 0f)) errors.Add($"slideTier1Boost must be >= 0 (was {slideTier1Boost})");
            if (!(slideTier3Boost >= 0f)) errors.Add($"slideTier3Boost must be >= 0 (was {slideTier3Boost})");
            if (!(tier2Speed + slideTier1Boost < tier3Speed)) errors.Add("tier2Speed + slideTier1Boost must stay below tier3Speed (tier 3 is earned)");
            if (!(slideFriction >= 0f)) errors.Add($"slideFriction must be >= 0 (was {slideFriction})");
            if (!(slideFrictionDelay >= 0f)) errors.Add($"slideFrictionDelay must be >= 0 (was {slideFrictionDelay})");
            if (!(slideStopSpeed >= 0f)) errors.Add($"slideStopSpeed must be >= 0 (was {slideStopSpeed})");
            if (!(slideHitReach >= 0f)) errors.Add($"slideHitReach must be >= 0 (was {slideHitReach})");
            Positive(nameof(summonerWidth), summonerWidth);
            Positive(nameof(summonerHeight), summonerHeight);
            Positive(nameof(summonerHp), summonerHp);
            Positive(nameof(summonerSpeed), summonerSpeed);
            Positive(nameof(summonerCastInterval), summonerCastInterval);
            Positive(nameof(summonerCastTime), summonerCastTime);
            Chance(nameof(shooterOrbChance), shooterOrbChance);
            Chance(nameof(shieldOrbChance), shieldOrbChance);
            Chance(nameof(kamikazeOrbChance), kamikazeOrbChance);
            Chance(nameof(summonerOrbChance), summonerOrbChance);
            if (projectileMaxCount < 1) errors.Add("projectileMaxCount must be >= 1");
            if (summonerMaxDrones < 0) errors.Add("summonerMaxDrones must be >= 0");
            if (shieldAboveFraction < 0f || shieldAboveFraction > 1f) errors.Add("shieldAboveFraction must be 0-1");
            if (summonerSpeed >= runSpeed) errors.Add("summonerSpeed must be < runSpeed (the player must be able to catch it)");
            return errors;
        }
    }
}
