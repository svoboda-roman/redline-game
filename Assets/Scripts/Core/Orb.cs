namespace Redline.Core
{
    /// <summary>What an orb gives the player when picked up.</summary>
    public enum OrbKind
    {
        /// <summary>+orbHealAmount HP, capped at playerMaxHp.</summary>
        Health,
        /// <summary>Timed buff: runSpeed, maxSpeed and airMaxSpeed times orbSpeedMultiplier.</summary>
        Speed,
        /// <summary>Timed buff: no hook cooldown and orbHookRangeBonus extra range.</summary>
        Hook
    }

    /// <summary>
    /// A pickup dropped by a killed enemy. It pops up, falls under gravity, rests on the ground and
    /// vanishes after orbLifetime. The sim owns a fixed pool of these and reuses them, so do not
    /// keep a reference after the orb leaves <see cref="RedlineSim.Orbs"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// foreach (var orb in sim.Orbs)
    ///     Draw(orb.Kind, orb.Center, blink: orb.TimeLeft &lt; 2f);
    /// </code>
    /// </example>
    public sealed class Orb : Body
    {
        /// <summary>What this orb gives.</summary>
        public OrbKind Kind;
        /// <summary>Seconds before the orb vanishes (renderers blink it near 0).</summary>
        public float TimeLeft;
        /// <summary>Seconds since the orb spawned (it can be picked up after orbPickupDelay).</summary>
        public float Age;
    }
}
