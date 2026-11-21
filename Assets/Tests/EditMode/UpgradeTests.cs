using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Meta-progression: run points, costs and max levels, applying upgrades, and the save format.</summary>
    public class UpgradeTests
    {
        private UpgradeConfig _ucfg;
        private RedlineConfig _base;

        [SetUp]
        public void SetUp()
        {
            _ucfg = new UpgradeConfig();
            _base = new RedlineConfig();
        }

        private static UpgradeState StateWith(UpgradeConfig cfg, int points, params (UpgradeKind kind, int level)[] levels)
        {
            var s = new UpgradeState();
            s.SetPoints(points);
            foreach (var (kind, level) in levels) s.SetLevel(kind, level, cfg);
            return s;
        }

        // ------------------------------------------------------------------ config

        [Test]
        public void Defaults_AreValid_AgainstDefaultGameConfig()
        {
            Assert.IsEmpty(_ucfg.ValidateAgainst(_base));
        }

        [Test]
        public void ConfigJson_MatchesConfigFieldsAndDefaults()
        {
            string json = ReadResource("UpgradeConfig.json");
            var values = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*(\\[[^\\]]*\\]|[-0-9.eE]+)"))
                values[m.Groups[1].Value] = m.Groups[2].Value;

            var fields = typeof(UpgradeConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Assert.AreEqual(fields.Length, values.Count, "every upgrade config field appears once in the JSON, and nothing else");
            foreach (var f in fields)
            {
                Assert.IsTrue(values.ContainsKey(f.Name), $"JSON is missing {f.Name}");
                string raw = values[f.Name];
                object def = f.GetValue(_ucfg);
                if (def is float fl)
                    Assert.AreEqual(fl, float.Parse(raw, CultureInfo.InvariantCulture), 1e-5f, f.Name);
                else if (def is int i)
                    Assert.AreEqual(i, int.Parse(raw, CultureInfo.InvariantCulture), f.Name);
                else if (def is int[] ia)
                {
                    var parsed = new List<int>();
                    foreach (var part in raw.Trim('[', ']').Split(','))
                        if (part.Trim().Length > 0) parsed.Add(int.Parse(part.Trim(), CultureInfo.InvariantCulture));
                    CollectionAssert.AreEqual(ia, parsed, f.Name);
                }
                else Assert.Fail($"unhandled upgrade config field type {f.FieldType} for {f.Name}");
            }
        }

        [Test]
        public void Validate_RejectsBadValues()
        {
            _ucfg.speedCostGrowth = 0.5f;
            _ucfg.hookBaseCost = 0;
            _ucfg.pointsForHighestTier = new[] { 1, 2 };
            _ucfg.orbChancePerLevel = -0.1f;
            var errors = _ucfg.Validate();
            Assert.AreEqual(4, errors.Count, string.Join("; ", errors));
        }

        [Test]
        public void ValidateAgainst_RejectsSpeedThatGivesTier3ForFree()
        {
            _ucfg.speedMultiplierPerLevel = 0.5f;
            Assert.IsTrue(_ucfg.ValidateAgainst(_base).Exists(e => e.Contains("tier3Speed")));
        }

        // ------------------------------------------------------------------ points

        [Test]
        public void PointsFor_SumsKillsWavesLevelsAndHighestTier()
        {
            var run = new RunResult { Kills = 7, WavesCleared = 3, LevelsCleared = 1, HighestTier = 3 };
            int expected = 7 * _ucfg.pointsPerKill + 3 * _ucfg.pointsPerWaveCleared
                           + 1 * _ucfg.pointsPerLevelCleared + _ucfg.pointsForHighestTier[2];
            Assert.AreEqual(expected, _ucfg.PointsFor(run));
        }

        [Test]
        public void PointsFor_LosingRunStillEarns_AndBadCountsAreIgnored()
        {
            Assert.AreEqual(2 * _ucfg.pointsPerKill + _ucfg.pointsForHighestTier[1],
                _ucfg.PointsFor(new RunResult { Kills = 2, HighestTier = 2 }));
            Assert.AreEqual(0, _ucfg.PointsFor(new RunResult { Kills = -5, WavesCleared = -1, HighestTier = 0 }));
            Assert.AreEqual(_ucfg.pointsForHighestTier[2], _ucfg.PointsFor(new RunResult { HighestTier = 9 }), "tier clamps to 3");
        }

        [Test]
        public void Award_AccumulatesAndSaturates()
        {
            var s = new UpgradeState();
            int earned = s.AwardRun(new RunResult { Kills = 4 }, _ucfg);
            Assert.AreEqual(earned, s.Points);
            s.Award(-100);
            Assert.AreEqual(earned, s.Points, "negative awards never take points");
            s.Award(int.MaxValue);
            Assert.AreEqual(int.MaxValue, s.Points);
        }

        // ------------------------------------------------------------------ buying

        [Test]
        public void CostOf_FollowsTheCurve()
        {
            Assert.AreEqual(_ucfg.maxHpBaseCost, _ucfg.CostOf(UpgradeKind.MaxHp, 0));
            int expected = (int)System.Math.Round(_ucfg.maxHpBaseCost * System.Math.Pow(_ucfg.maxHpCostGrowth, 2),
                System.MidpointRounding.AwayFromZero);
            Assert.AreEqual(expected, _ucfg.CostOf(UpgradeKind.MaxHp, 2));
            Assert.Greater(_ucfg.CostOf(UpgradeKind.Hook, 1), _ucfg.CostOf(UpgradeKind.Hook, 0));
        }

        [Test]
        public void TryBuy_SpendsCostAndRaisesLevel()
        {
            int cost = _ucfg.CostOf(UpgradeKind.Hook, 0);
            var s = StateWith(_ucfg, cost + 3);
            Assert.AreEqual(BuyResult.Ok, s.TryBuy(UpgradeKind.Hook, _ucfg));
            Assert.AreEqual(1, s.LevelOf(UpgradeKind.Hook));
            Assert.AreEqual(3, s.Points);
        }

        [Test]
        public void TryBuy_InsufficientPoints_ChangesNothing()
        {
            int cost = _ucfg.CostOf(UpgradeKind.Speed, 0);
            var s = StateWith(_ucfg, cost - 1);
            Assert.AreEqual(BuyResult.NotEnoughPoints, s.TryBuy(UpgradeKind.Speed, _ucfg));
            Assert.AreEqual(0, s.LevelOf(UpgradeKind.Speed));
            Assert.AreEqual(cost - 1, s.Points);
        }

        [Test]
        public void TryBuy_AtMaxLevel_ChangesNothing()
        {
            int max = _ucfg.MaxLevel(UpgradeKind.MaxHp);
            var s = StateWith(_ucfg, int.MaxValue, (UpgradeKind.MaxHp, max));
            Assert.AreEqual(BuyResult.MaxLevel, s.TryBuy(UpgradeKind.MaxHp, _ucfg));
            Assert.AreEqual(max, s.LevelOf(UpgradeKind.MaxHp));
            Assert.AreEqual(int.MaxValue, s.Points);
        }

        [Test]
        public void SetLevel_ClampsToConfigMax_AndDisabledUpgradeCannotBeBought()
        {
            var s = StateWith(_ucfg, 1000, (UpgradeKind.Speed, 99));
            Assert.AreEqual(_ucfg.speedMaxLevel, s.LevelOf(UpgradeKind.Speed));
            _ucfg.hookMaxLevel = 0;
            Assert.AreEqual(BuyResult.MaxLevel, s.CanBuy(UpgradeKind.Hook, _ucfg));
        }

        [Test]
        public void Reset_WipesPointsAndLevels()
        {
            var s = StateWith(_ucfg, 50, (UpgradeKind.MaxHp, 2), (UpgradeKind.Hook, 1));
            s.Reset();
            Assert.AreEqual(0, s.Points);
            foreach (var kind in UpgradeState.Kinds) Assert.AreEqual(0, s.LevelOf(kind));
        }

        // ------------------------------------------------------------------ apply

        [Test]
        public void Apply_NoUpgrades_GivesEqualCopy()
        {
            var c = Upgrades.Apply(_base, _ucfg, new UpgradeState());
            Assert.AreNotSame(_base, c);
            Assert.AreNotSame(_base.waveGrunts, c.waveGrunts, "arrays are deep-copied");
            foreach (var f in typeof(RedlineConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object a = f.GetValue(_base), b = f.GetValue(c);
                if (a is System.Array arr) CollectionAssert.AreEqual(arr, (System.Array)b, f.Name);
                else Assert.AreEqual(a, b, f.Name);
            }
        }

        [Test]
        public void Apply_AllUpgrades_ChangesTheRightFields()
        {
            var s = StateWith(_ucfg, 0, (UpgradeKind.MaxHp, 2), (UpgradeKind.Speed, 3),
                (UpgradeKind.Hook, 1), (UpgradeKind.SwordAndOrbs, 4));
            var c = Upgrades.Apply(_base, _ucfg, s);

            Assert.AreEqual(_base.playerMaxHp + 2 / _ucfg.maxHpLevelsPerHp * _ucfg.maxHpPerLevel, c.playerMaxHp);
            float sm = 1f + 3 * _ucfg.speedMultiplierPerLevel;
            Assert.AreEqual(_base.runSpeed * sm, c.runSpeed, 1e-4f);
            Assert.AreEqual(_base.maxSpeed * sm, c.maxSpeed, 1e-4f);
            Assert.AreEqual(_base.hookRange + _ucfg.hookRangePerLevel, c.hookRange, 1e-4f);
            Assert.AreEqual(_base.hookReleaseBoost + _ucfg.hookReleaseBoostPerLevel, c.hookReleaseBoost, 1e-4f);
            Assert.AreEqual(_base.attackRange + 4 * _ucfg.attackRangePerLevel, c.attackRange, 1e-4f);
            Assert.AreEqual(_base.gruntOrbChance + 4 * _ucfg.orbChancePerLevel, c.gruntOrbChance, 1e-4f);
            Assert.AreEqual(_base.bruteOrbChance + 4 * _ucfg.orbChancePerLevel, c.bruteOrbChance, 1e-4f);
            Assert.AreEqual(_base.droneOrbChance + 4 * _ucfg.orbChancePerLevel, c.droneOrbChance, 1e-4f);
            float dm = 1f + 4 * _ucfg.orbDurationPerLevel;
            Assert.AreEqual(_base.orbSpeedDuration * dm, c.orbSpeedDuration, 1e-4f);
            Assert.AreEqual(_base.orbHookDuration * dm, c.orbHookDuration, 1e-4f);
            Assert.IsEmpty(c.Validate());
        }

        [Test]
        public void Apply_DoesNotMutateTheBase()
        {
            var pristine = new RedlineConfig();
            var s = StateWith(_ucfg, 0, (UpgradeKind.MaxHp, 4), (UpgradeKind.Speed, 3),
                (UpgradeKind.Hook, 4), (UpgradeKind.SwordAndOrbs, 4));
            Upgrades.Apply(_base, _ucfg, s);
            foreach (var f in typeof(RedlineConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object a = f.GetValue(pristine), b = f.GetValue(_base);
                if (a is System.Array arr) CollectionAssert.AreEqual(arr, (System.Array)b, f.Name);
                else Assert.AreEqual(a, b, f.Name);
            }
        }

        [Test]
        public void Apply_OrbChanceCapsAtOne_AndAirMaxSpeedKeepsUp()
        {
            _base.bruteOrbChance = 0.99f;
            _base.airMaxSpeed = _base.maxSpeed;
            var s = StateWith(_ucfg, 0, (UpgradeKind.SwordAndOrbs, 4), (UpgradeKind.Speed, 3));
            var c = Upgrades.Apply(_base, _ucfg, s);
            Assert.AreEqual(1f, c.bruteOrbChance);
            Assert.GreaterOrEqual(c.airMaxSpeed, c.maxSpeed);
        }

        [Test]
        public void Apply_UpgradedConfigRunsInTheSim()
        {
            var s = StateWith(_ucfg, 0, (UpgradeKind.MaxHp, 3));
            var sim = new RedlineSim(Upgrades.Apply(NoWaves(), _ucfg, s), Box(30, 10));
            Assert.AreEqual(new RedlineConfig().playerMaxHp + 3 / _ucfg.maxHpLevelsPerHp * _ucfg.maxHpPerLevel, sim.Player.Hp);
        }

        // ------------------------------------------------------------------ save

        [Test]
        public void Save_RoundTrips()
        {
            var s = StateWith(_ucfg, 123, (UpgradeKind.MaxHp, 1), (UpgradeKind.Hook, 3), (UpgradeKind.SwordAndOrbs, 2));
            var loaded = UpgradeSave.Deserialize(UpgradeSave.Serialize(s), _ucfg, out var status);
            Assert.AreEqual(SaveLoadStatus.Ok, status);
            Assert.AreEqual(123, loaded.Points);
            foreach (var kind in UpgradeState.Kinds) Assert.AreEqual(s.LevelOf(kind), loaded.LevelOf(kind), kind.ToString());
        }

        [Test]
        public void Save_ToleratesWhitespaceUnknownKeysAndMissingUpgrades()
        {
            const string json = "{ \"version\" : 1 ,\n \"extra\": [1, {\"a\": null}, true],\n" +
                                " \"points\": 40, \"levels\": { \"Hook\": 2, \"Laser\": 9 } }";
            var loaded = UpgradeSave.Deserialize(json, _ucfg, out var status);
            Assert.AreEqual(SaveLoadStatus.Ok, status);
            Assert.AreEqual(40, loaded.Points);
            Assert.AreEqual(2, loaded.LevelOf(UpgradeKind.Hook));
            Assert.AreEqual(0, loaded.LevelOf(UpgradeKind.MaxHp));
        }

        [TestCase(null, SaveLoadStatus.Missing)]
        [TestCase("   ", SaveLoadStatus.Missing)]
        [TestCase("{\"version\":1,\"points\":", SaveLoadStatus.Corrupt)]
        [TestCase("not json", SaveLoadStatus.Corrupt)]
        [TestCase("[1,2,3]", SaveLoadStatus.Corrupt)]
        [TestCase("{\"points\":50}", SaveLoadStatus.Corrupt)]
        [TestCase("{\"version\":1,\"points\":50} trailing", SaveLoadStatus.Corrupt)]
        [TestCase("{\"version\":0,\"points\":50,\"levels\":{\"Hook\":1}}", SaveLoadStatus.UnsupportedVersion)]
        [TestCase("{\"version\":99,\"points\":50,\"levels\":{\"Hook\":1}}", SaveLoadStatus.UnsupportedVersion)]
        public void Save_CorruptMissingOrOld_GivesDefaults(string json, SaveLoadStatus expected)
        {
            var loaded = UpgradeSave.Deserialize(json, _ucfg, out var status);
            Assert.AreEqual(expected, status);
            Assert.AreEqual(0, loaded.Points);
            foreach (var kind in UpgradeState.Kinds) Assert.AreEqual(0, loaded.LevelOf(kind));
        }

        [Test]
        public void Save_BadFields_AreRepairedIndividually()
        {
            const string json = "{\"version\":1,\"points\":-5,\"levels\":{\"MaxHp\":1.5,\"Speed\":\"2\",\"Hook\":99,\"SwordAndOrbs\":1}}";
            var loaded = UpgradeSave.Deserialize(json, _ucfg, out var status);
            Assert.AreEqual(SaveLoadStatus.Repaired, status);
            Assert.AreEqual(0, loaded.Points);
            Assert.AreEqual(0, loaded.LevelOf(UpgradeKind.MaxHp));
            Assert.AreEqual(0, loaded.LevelOf(UpgradeKind.Speed));
            Assert.AreEqual(_ucfg.hookMaxLevel, loaded.LevelOf(UpgradeKind.Hook), "clamped to max");
            Assert.AreEqual(1, loaded.LevelOf(UpgradeKind.SwordAndOrbs));
        }

        [Test]
        public void Save_DeeplyNestedJson_DoesNotCrash()
        {
            string json = "{\"version\":1,\"x\":" + new string('[', 5000) + new string(']', 5000) + "}";
            UpgradeSave.Deserialize(json, _ucfg, out var status);
            Assert.AreEqual(SaveLoadStatus.Corrupt, status);
        }
    }
}
