using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>Run-only perks offered (pick 1 of 3) after every few waves; they last until the run ends.</summary>
    public enum PerkKind
    {
        Vital,       // +2 max HP, full heal
        Swift,       // +8% run and top speed
        LongHook,    // +2.5 hook range, no hook cooldown
        WideBlade,   // +0.35 sword reach
        Sharp,       // +50% base damage
        AirDancer,   // double jump at any speed
        Leech,       // +2 HP healed per cleared wave
        OrbMagnet,   // +15% orb drop chance
        ChainMaster, // +1 s chain window
    }

    /// <summary>Perk names, descriptions, rolls and effects (applied to the run's config in place).</summary>
    public static class Perks
    {
        public static readonly PerkKind[] All = (PerkKind[])Enum.GetValues(typeof(PerkKind));

        public static string Title(PerkKind k)
        {
            switch (k)
            {
                case PerkKind.Vital: return "VITAL";
                case PerkKind.Swift: return "SWIFT";
                case PerkKind.LongHook: return "LONG HOOK";
                case PerkKind.WideBlade: return "WIDE BLADE";
                case PerkKind.Sharp: return "SHARP EDGE";
                case PerkKind.AirDancer: return "AIR DANCER";
                case PerkKind.Leech: return "LEECH";
                case PerkKind.OrbMagnet: return "ORB MAGNET";
                case PerkKind.ChainMaster: return "CHAIN MASTER";
                default: return k.ToString().ToUpperInvariant();
            }
        }

        public static string Description(PerkKind k)
        {
            switch (k)
            {
                case PerkKind.Vital: return "+2 MAX HP AND A FULL HEAL";
                case PerkKind.Swift: return "+8% RUN AND TOP SPEED";
                case PerkKind.LongHook: return "+2.5 HOOK RANGE, NO COOLDOWN";
                case PerkKind.WideBlade: return "+0.35 SWORD REACH";
                case PerkKind.Sharp: return "+50% DAMAGE";
                case PerkKind.AirDancer: return "DOUBLE JUMP AT ANY SPEED";
                case PerkKind.Leech: return "+2 HP FOR EVERY CLEARED WAVE";
                case PerkKind.OrbMagnet: return "+15% ORB DROP CHANCE";
                case PerkKind.ChainMaster: return "+1 S TO KEEP A CHAIN ALIVE";
                default: return "";
            }
        }

        /// <summary>Three different perks, picked with <paramref name="rng"/>.</summary>
        public static PerkKind[] Roll(Random rng, int count = 3)
        {
            var pool = new List<PerkKind>(All);
            var picks = new PerkKind[Math.Min(count, pool.Count)];
            for (int i = 0; i < picks.Length; i++)
            {
                int j = rng.Next(pool.Count);
                picks[i] = pool[j];
                pool.RemoveAt(j);
            }
            return picks;
        }

        /// <summary>Applies a perk to the running config (the sim reads it live) and to the player.</summary>
        public static void Apply(PerkKind k, RedlineConfig c, Player p)
        {
            switch (k)
            {
                case PerkKind.Vital:
                    c.playerMaxHp += 2;
                    if (p != null) p.Hp = c.playerMaxHp;
                    break;
                case PerkKind.Swift:
                    c.runSpeed *= 1.08f;
                    c.maxSpeed *= 1.08f;
                    if (c.airMaxSpeed < c.maxSpeed) c.airMaxSpeed = c.maxSpeed;
                    break;
                case PerkKind.LongHook:
                    c.hookRange += 2.5f;
                    c.hookCooldown = 0f;
                    break;
                case PerkKind.WideBlade: c.attackRange += 0.35f; break;
                case PerkKind.Sharp: c.baseDamage *= 1.5f; break;
                case PerkKind.AirDancer: c.doubleJumpMinTier = 1; break;
                case PerkKind.Leech: c.waveClearHeal += 2; break;
                case PerkKind.OrbMagnet:
                    c.gruntOrbChance = Math.Min(1f, c.gruntOrbChance + 0.15f);
                    c.bruteOrbChance = Math.Min(1f, c.bruteOrbChance + 0.15f);
                    c.droneOrbChance = Math.Min(1f, c.droneOrbChance + 0.15f);
                    break;
                case PerkKind.ChainMaster: c.chainWindow += 1f; break;
            }
        }
    }
}
