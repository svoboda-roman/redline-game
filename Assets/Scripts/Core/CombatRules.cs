using System;

namespace Redline.Core
{
    /// <summary>Pure rules that turn speed into combat power.</summary>
    public static class CombatRules
    {
        /// <summary>Speed tier (1-3) for an absolute horizontal speed.</summary>
        public static int TierFor(float speed, RedlineConfig cfg)
        {
            float s = MathF.Abs(speed);
            if (s >= cfg.tier3Speed) return 3;
            if (s >= cfg.tier2Speed) return 2;
            return 1;
        }

        /// <summary>Damage dealt by one strike at a tier: baseDamage × tier multiplier.</summary>
        public static float Damage(int tier, RedlineConfig cfg) =>
            cfg.baseDamage * cfg.tierDamageMultipliers[ClampTier(tier) - 1];

        /// <summary>Knockback speed given to a struck enemy at a tier.</summary>
        public static float Knockback(int tier, RedlineConfig cfg) =>
            cfg.tierKnockback[ClampTier(tier) - 1];

        /// <summary>True when a strike at this tier can damage an enemy with the given armor tier.</summary>
        public static bool CanDamage(int tier, int enemyMinTier) => tier >= enemyMinTier;

        /// <summary>Speed as a fraction of maxSpeed, for HUD meters (0-1).</summary>
        public static float SpeedFraction(float speed, RedlineConfig cfg) =>
            MathF.Min(1f, MathF.Abs(speed) / cfg.maxSpeed);

        private static int ClampTier(int tier) => tier < 1 ? 1 : tier > 3 ? 3 : tier;
    }
}
