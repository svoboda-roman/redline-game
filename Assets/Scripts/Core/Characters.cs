using System;

namespace Redline.Core
{
    /// <summary>The playable characters (chosen in the main menu, saved in the settings file).</summary>
    public enum PlayerCharacter
    {
        /// <summary>The original ronin with the katana: the base tuning, unchanged.</summary>
        Ronin = 0,
        /// <summary>The reaper girl with the scythe: longer reach, slower swings, a bit more damage, heals per hit.</summary>
        Reaper = 1,
    }

    /// <summary>
    /// Per-character run tuning. The Ronin keeps the config as it is; the Reaper swaps in the reaper* values of
    /// <see cref="RedlineConfig"/> (all data-driven in RedlineConfig.json).
    /// </summary>
    /// <example>
    /// <code>
    /// var run = Upgrades.Apply(baseCfg, upgradeCfg, state);   // a fresh copy
    /// Characters.Apply(run, PlayerCharacter.Reaper);           // tunes that copy in place
    /// </code>
    /// </example>
    public static class Characters
    {
        /// <summary>Number of playable characters.</summary>
        public const int Count = 2;

        /// <summary>Clamps a saved index to a valid character (unknown values fall back to the Ronin).</summary>
        public static PlayerCharacter FromIndex(int index) =>
            index == (int)PlayerCharacter.Reaper ? PlayerCharacter.Reaper : PlayerCharacter.Ronin;

        /// <summary>Applies <paramref name="character"/>'s weapon tuning to <paramref name="cfg"/> in place (call on a run copy).</summary>
        public static void Apply(RedlineConfig cfg, PlayerCharacter character)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (character != PlayerCharacter.Reaper) return;
            cfg.attackRange = cfg.reaperAttackRange;
            cfg.attackReachUp = cfg.reaperAttackReachUp;
            cfg.attackCooldown = cfg.reaperAttackCooldown;
            cfg.attackActiveTime = cfg.reaperAttackActiveTime;
            cfg.baseDamage *= cfg.reaperDamageMultiplier;
            cfg.healPerHit = cfg.reaperHealPerHit;
            cfg.attackOnHook = cfg.reaperAttackOnHook;
            cfg.playerMaxHp = Math.Max(1, cfg.playerMaxHp + cfg.reaperMaxHpDelta);
        }
    }
}
