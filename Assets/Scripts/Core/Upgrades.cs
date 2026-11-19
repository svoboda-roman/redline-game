using System;
using System.Collections.Generic;
using System.Reflection;

namespace Redline.Core
{
    /// <summary>The upgrades the player can buy between runs (meta-progression).</summary>
    /// <remarks>The enum names are written to the save file; renaming one drops its saved level.</remarks>
    public enum UpgradeKind
    {
        /// <summary>+max HP per level.</summary>
        MaxHp = 0,
        /// <summary>runSpeed and maxSpeed multiplier per level.</summary>
        Speed = 1,
        /// <summary>Hook range and release fling boost per level.</summary>
        Hook = 2,
        /// <summary>Sword reach, orb drop chance and orb buff duration per level.</summary>
        SwordAndOrbs = 3
    }

    /// <summary>Outcome of a purchase attempt.</summary>
    public enum BuyResult
    {
        /// <summary>The upgrade was bought (or could be).</summary>
        Ok,
        /// <summary>The upgrade is already at its max level.</summary>
        MaxLevel,
        /// <summary>Not enough points for the next level.</summary>
        NotEnoughPoints
    }

    /// <summary>
    /// What one run achieved, used to award upgrade points (win or lose). Totals cover the whole
    /// run across levels: a new <see cref="RedlineSim"/> per level resets <see cref="RedlineSim.Kills"/>,
    /// so the game layer adds them up.
    /// </summary>
    public struct RunResult
    {
        /// <summary>Enemies killed in the run.</summary>
        public int Kills;
        /// <summary>Waves cleared in the run (across all levels).</summary>
        public int WavesCleared;
        /// <summary>Levels cleared (victory reached) in the run.</summary>
        public int LevelsCleared;
        /// <summary>Highest speed tier (1-3) reached at any moment of the run; 0 counts as none.</summary>
        public int HighestTier;
    }

    /// <summary>
    /// Tuning for meta-progression: point rewards, the cost curve and max level of each upgrade, and
    /// the effect of one upgrade level. Defaults mirror Assets/Resources/UpgradeConfig.json (public
    /// fields for Unity JsonUtility). Cost of buying level n+1 from level n is
    /// <c>round(baseCost * costGrowth^n)</c>; an upgrade with maxLevel 0 is disabled.
    /// </summary>
    [Serializable]
    public sealed class UpgradeConfig
    {
        // --- Point rewards (per run, win or lose) ---
        /// <summary>Points per enemy killed.</summary>
        public int pointsPerKill = 1;
        /// <summary>Points per wave cleared.</summary>
        public int pointsPerWaveCleared = 5;
        /// <summary>Points per level cleared.</summary>
        public int pointsPerLevelCleared = 25;
        /// <summary>Points for the highest speed tier reached: index 0 = tier 1, 1 = tier 2, 2 = tier 3 (not cumulative).</summary>
        public int[] pointsForHighestTier = { 0, 5, 15 };

        // --- MaxHp ---
        public int maxHpMaxLevel = 4;
        public int maxHpBaseCost = 40;
        public float maxHpCostGrowth = 1.6f;
        /// <summary>Max HP added per level.</summary>
        public int maxHpPerLevel = 1;

        // --- Speed ---
        public int speedMaxLevel = 3;
        public int speedBaseCost = 50;
        public float speedCostGrowth = 1.7f;
        /// <summary>
        /// Added to the runSpeed and maxSpeed multiplier per level (0.04 = +4%). Keep
        /// runSpeed * (1 + max bonus) + slideBoost below tier3Speed: tier 3 stays earned
        /// (<see cref="ValidateAgainst"/> checks it).
        /// </summary>
        public float speedMultiplierPerLevel = 0.04f;

        // --- Hook ---
        public int hookMaxLevel = 4;
        public int hookBaseCost = 35;
        public float hookCostGrowth = 1.6f;
        /// <summary>Hook range added per level (tiles).</summary>
        public float hookRangePerLevel = 1f;
        /// <summary>Release fling boost (hookReleaseBoost) added per level.</summary>
        public float hookReleaseBoostPerLevel = 1f;

        // --- SwordAndOrbs ---
        public int swordMaxLevel = 4;
        public int swordBaseCost = 45;
        public float swordCostGrowth = 1.6f;
        /// <summary>Sword reach (attackRange) added per level (tiles).</summary>
        public float attackRangePerLevel = 0.15f;
        /// <summary>Added to every enemy's orb drop chance per level (capped at 1).</summary>
        public float orbChancePerLevel = 0.03f;
        /// <summary>Added to the orb buff duration multiplier per level (0.15 = +15% Speed and Hook orb time).</summary>
        public float orbDurationPerLevel = 0.15f;

        // --- Random upgrade (spin): pay, a random not-maxed upgrade gains a level; the price grows per spin ---
        public int spinBaseCost = 30;
        public float spinCostGrowth = 1.35f;

        /// <summary>Price of the next random upgrade after <paramref name="spins"/> spins.</summary>
        public int SpinCost(int spins)
        {
            double cost = Math.Round(spinBaseCost * Math.Pow(spinCostGrowth, Math.Max(0, spins)), MidpointRounding.AwayFromZero);
            return cost >= int.MaxValue ? int.MaxValue : (int)cost;
        }

        /// <summary>Highest level the upgrade can reach.</summary>
        public int MaxLevel(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.MaxHp: return maxHpMaxLevel;
                case UpgradeKind.Speed: return speedMaxLevel;
                case UpgradeKind.Hook: return hookMaxLevel;
                case UpgradeKind.SwordAndOrbs: return swordMaxLevel;
                default: return 0;
            }
        }

        /// <summary>Cost of the level after <paramref name="currentLevel"/> (for 0, the first level).</summary>
        public int CostOf(UpgradeKind kind, int currentLevel)
        {
            int baseCost;
            float growth;
            switch (kind)
            {
                case UpgradeKind.MaxHp: baseCost = maxHpBaseCost; growth = maxHpCostGrowth; break;
                case UpgradeKind.Speed: baseCost = speedBaseCost; growth = speedCostGrowth; break;
                case UpgradeKind.Hook: baseCost = hookBaseCost; growth = hookCostGrowth; break;
                case UpgradeKind.SwordAndOrbs: baseCost = swordBaseCost; growth = swordCostGrowth; break;
                default: return int.MaxValue;
            }
            double cost = Math.Round(baseCost * Math.Pow(growth, Math.Max(0, currentLevel)), MidpointRounding.AwayFromZero);
            return cost >= int.MaxValue ? int.MaxValue : (int)cost;
        }

        /// <summary>Points a run earns. Negative counts are treated as 0; the total saturates at int.MaxValue.</summary>
        public int PointsFor(RunResult run)
        {
            long total = (long)Math.Max(0, run.Kills) * pointsPerKill
                         + (long)Math.Max(0, run.WavesCleared) * pointsPerWaveCleared
                         + (long)Math.Max(0, run.LevelsCleared) * pointsPerLevelCleared;
            int tier = Math.Min(run.HighestTier, pointsForHighestTier == null ? 0 : pointsForHighestTier.Length);
            if (tier >= 1) total += pointsForHighestTier[tier - 1];
            return total >= int.MaxValue ? int.MaxValue : (int)Math.Max(0, total);
        }

        /// <summary>Returns a list of human-readable problems; empty when the config is usable.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            void NonNegative(string name, float v)
            {
                if (!(v >= 0f)) errors.Add($"{name} must be >= 0 (was {v})");
            }
            void Curve(string prefix, int maxLevel, int baseCost, float growth)
            {
                if (maxLevel < 0) errors.Add($"{prefix}MaxLevel must be >= 0 (was {maxLevel})");
                if (baseCost < 1) errors.Add($"{prefix}BaseCost must be >= 1 (was {baseCost})");
                if (!(growth >= 1f)) errors.Add($"{prefix}CostGrowth must be >= 1 (was {growth})");
            }

            NonNegative(nameof(pointsPerKill), pointsPerKill);
            NonNegative(nameof(pointsPerWaveCleared), pointsPerWaveCleared);
            NonNegative(nameof(pointsPerLevelCleared), pointsPerLevelCleared);
            if (pointsForHighestTier == null || pointsForHighestTier.Length != 3)
                errors.Add("pointsForHighestTier needs exactly 3 values");
            else
                foreach (int p in pointsForHighestTier)
                    if (p < 0) errors.Add("pointsForHighestTier values must be >= 0");

            Curve("maxHp", maxHpMaxLevel, maxHpBaseCost, maxHpCostGrowth);
            Curve("speed", speedMaxLevel, speedBaseCost, speedCostGrowth);
            Curve("hook", hookMaxLevel, hookBaseCost, hookCostGrowth);
            Curve("sword", swordMaxLevel, swordBaseCost, swordCostGrowth);
            Curve("spin", 1, spinBaseCost, spinCostGrowth);

            NonNegative(nameof(maxHpPerLevel), maxHpPerLevel);
            NonNegative(nameof(speedMultiplierPerLevel), speedMultiplierPerLevel);
            NonNegative(nameof(hookRangePerLevel), hookRangePerLevel);
            NonNegative(nameof(hookReleaseBoostPerLevel), hookReleaseBoostPerLevel);
            NonNegative(nameof(attackRangePerLevel), attackRangePerLevel);
            NonNegative(nameof(orbChancePerLevel), orbChancePerLevel);
            NonNegative(nameof(orbDurationPerLevel), orbDurationPerLevel);
            return errors;
        }

        /// <summary>
        /// <see cref="Validate"/> plus checks against a game config: with every upgrade at max level the
        /// upgraded config must still pass <see cref="RedlineConfig.Validate"/>, and running plus a slide
        /// must stay below tier3Speed (tier 3 is earned, never given by an upgrade).
        /// </summary>
        public List<string> ValidateAgainst(RedlineConfig baseCfg)
        {
            var errors = Validate();
            if (errors.Count > 0 || baseCfg == null) return errors;
            var maxed = new UpgradeState();
            foreach (UpgradeKind kind in UpgradeState.Kinds) maxed.SetLevel(kind, MaxLevel(kind), this);
            var upgraded = Upgrades.Apply(baseCfg, this, maxed);
            foreach (var e in upgraded.Validate()) errors.Add("with all upgrades maxed: " + e);
            if (!(upgraded.runSpeed + upgraded.slideBoost < upgraded.tier3Speed))
                errors.Add($"with Speed maxed, runSpeed + slideBoost ({upgraded.runSpeed + upgraded.slideBoost:0.##}) must stay below tier3Speed ({upgraded.tier3Speed:0.##})");
            return errors;
        }
    }

    /// <summary>
    /// The player's persistent progress: unspent points and the level of each upgrade. Points are
    /// only lost by spending them (or by <see cref="Reset"/>). Levels never exceed the config's max.
    /// </summary>
    public sealed class UpgradeState
    {
        /// <summary>Every upgrade kind, in display order.</summary>
        public static readonly UpgradeKind[] Kinds =
            { UpgradeKind.MaxHp, UpgradeKind.Speed, UpgradeKind.Hook, UpgradeKind.SwordAndOrbs };

        private readonly int[] _levels = new int[Kinds.Length];

        /// <summary>Unspent points (never negative).</summary>
        public int Points { get; private set; }

        /// <summary>Current level of an upgrade (0 = not bought).</summary>
        public int LevelOf(UpgradeKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < _levels.Length ? _levels[i] : 0;
        }

        /// <summary>Adds points (negative amounts are ignored); saturates at int.MaxValue.</summary>
        public void Award(int points)
        {
            if (points <= 0) return;
            long total = (long)Points + points;
            Points = total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <summary>Awards the points a run earned and returns them.</summary>
        public int AwardRun(RunResult run, UpgradeConfig cfg)
        {
            int earned = cfg.PointsFor(run);
            Award(earned);
            return earned;
        }

        /// <summary>Whether the next level of <paramref name="kind"/> can be bought now.</summary>
        public BuyResult CanBuy(UpgradeKind kind, UpgradeConfig cfg)
        {
            int level = LevelOf(kind);
            if (level >= cfg.MaxLevel(kind)) return BuyResult.MaxLevel;
            if (Points < cfg.CostOf(kind, level)) return BuyResult.NotEnoughPoints;
            return BuyResult.Ok;
        }

        /// <summary>Buys the next level of <paramref name="kind"/> if allowed; nothing changes otherwise.</summary>
        public BuyResult TryBuy(UpgradeKind kind, UpgradeConfig cfg)
        {
            var result = CanBuy(kind, cfg);
            if (result != BuyResult.Ok) return result;
            int i = (int)kind;
            Points -= cfg.CostOf(kind, _levels[i]);
            _levels[i]++;
            return BuyResult.Ok;
        }

        /// <summary>Random upgrades bought so far (sets the next spin's price).</summary>
        public int Spins { get; private set; }

        /// <summary>Whether a random upgrade can be bought now (MaxLevel = every upgrade is maxed).</summary>
        public BuyResult CanSpin(UpgradeConfig cfg)
        {
            bool any = false;
            foreach (var kind in Kinds) if (LevelOf(kind) < cfg.MaxLevel(kind)) { any = true; break; }
            if (!any) return BuyResult.MaxLevel;
            if (Points < cfg.SpinCost(Spins)) return BuyResult.NotEnoughPoints;
            return BuyResult.Ok;
        }

        /// <summary>
        /// Pays <see cref="UpgradeConfig.SpinCost"/> and gives a level to a uniformly random upgrade that is not maxed
        /// (<paramref name="roll"/> in [0, 1) picks it). Nothing changes unless the result is Ok.
        /// </summary>
        public BuyResult TrySpin(UpgradeConfig cfg, double roll, out UpgradeKind won)
        {
            won = UpgradeKind.MaxHp;
            var result = CanSpin(cfg);
            if (result != BuyResult.Ok) return result;
            var open = new System.Collections.Generic.List<UpgradeKind>();
            foreach (var kind in Kinds) if (LevelOf(kind) < cfg.MaxLevel(kind)) open.Add(kind);
            won = open[Math.Min(open.Count - 1, (int)(Math.Max(0.0, roll) * open.Count))];
            Points -= cfg.SpinCost(Spins);
            Spins++;
            _levels[(int)won]++;
            return BuyResult.Ok;
        }

        /// <summary>Sets the spin count directly (loading a save); negative becomes 0.</summary>
        public void SetSpins(int spins) => Spins = Math.Max(0, spins);

        /// <summary>Wipes all progress: 0 points, 0 spins and every upgrade back to level 0.</summary>
        public void Reset()
        {
            Points = 0;
            Spins = 0;
            Array.Clear(_levels, 0, _levels.Length);
        }

        /// <summary>Sets a level directly (loading a save), clamped to 0..max level of the config.</summary>
        public void SetLevel(UpgradeKind kind, int level, UpgradeConfig cfg)
        {
            int i = (int)kind;
            if (i < 0 || i >= _levels.Length) return;
            _levels[i] = Math.Max(0, Math.Min(level, cfg.MaxLevel(kind)));
        }

        /// <summary>Sets the unspent points directly (loading a save); negative becomes 0.</summary>
        public void SetPoints(int points) => Points = Math.Max(0, points);

        /// <summary>Clamps every level to the config's max (a config may lower a max after a save).</summary>
        public void ClampTo(UpgradeConfig cfg)
        {
            foreach (var kind in Kinds) SetLevel(kind, LevelOf(kind), cfg);
        }
    }

    /// <summary>Applies bought upgrades to a game config.</summary>
    /// <example>
    /// <code>
    /// var runCfg = Upgrades.Apply(baseCfg, upgradeCfg, state); // baseCfg is untouched
    /// var sim = new RedlineSim(runCfg, map);
    /// </code>
    /// </example>
    public static class Upgrades
    {
        /// <summary>
        /// Returns an upgraded deep copy of <paramref name="baseCfg"/>; the base is never mutated.
        /// MaxHp: playerMaxHp += maxHpPerLevel * L. Speed: runSpeed and maxSpeed *= 1 + speedMultiplierPerLevel * L
        /// (airMaxSpeed is raised to maxSpeed if it would fall below it). Hook: hookRange += hookRangePerLevel * L,
        /// hookReleaseBoost += hookReleaseBoostPerLevel * L. SwordAndOrbs: attackRange += attackRangePerLevel * L,
        /// each enemy orb chance += orbChancePerLevel * L (capped at 1), orbSpeedDuration and orbHookDuration
        /// *= 1 + orbDurationPerLevel * L.
        /// </summary>
        public static RedlineConfig Apply(RedlineConfig baseCfg, UpgradeConfig cfg, UpgradeState state)
        {
            if (baseCfg == null) throw new ArgumentNullException(nameof(baseCfg));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            var c = Copy(baseCfg);
            if (state == null) return c;

            int hp = state.LevelOf(UpgradeKind.MaxHp);
            c.playerMaxHp += cfg.maxHpPerLevel * hp;

            int speed = state.LevelOf(UpgradeKind.Speed);
            float speedMul = 1f + cfg.speedMultiplierPerLevel * speed;
            c.runSpeed *= speedMul;
            c.maxSpeed *= speedMul;
            if (c.airMaxSpeed < c.maxSpeed) c.airMaxSpeed = c.maxSpeed;

            int hook = state.LevelOf(UpgradeKind.Hook);
            c.hookRange += cfg.hookRangePerLevel * hook;
            c.hookReleaseBoost += cfg.hookReleaseBoostPerLevel * hook;

            int sword = state.LevelOf(UpgradeKind.SwordAndOrbs);
            c.attackRange += cfg.attackRangePerLevel * sword;
            float chance = cfg.orbChancePerLevel * sword;
            c.gruntOrbChance = Math.Min(1f, c.gruntOrbChance + chance);
            c.bruteOrbChance = Math.Min(1f, c.bruteOrbChance + chance);
            c.droneOrbChance = Math.Min(1f, c.droneOrbChance + chance);
            float durationMul = 1f + cfg.orbDurationPerLevel * sword;
            c.orbSpeedDuration *= durationMul;
            c.orbHookDuration *= durationMul;
            return c;
        }

        /// <summary>Deep copy of a config: every public instance field, arrays cloned.</summary>
        public static RedlineConfig Copy(RedlineConfig source)
        {
            var copy = new RedlineConfig();
            foreach (var f in typeof(RedlineConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object value = f.GetValue(source);
                if (value is Array array) value = array.Clone();
                f.SetValue(copy, value);
            }
            return copy;
        }
    }
}
