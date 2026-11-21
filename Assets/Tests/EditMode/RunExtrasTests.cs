using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Combo multiplier, perks, challenges, spin rarity, cosmetics, best waves, bosses and the shock floor.</summary>
    public class RunExtrasTests
    {
        [TestCase(0, 1)] [TestCase(2, 1)] [TestCase(3, 2)] [TestCase(6, 3)] [TestCase(9, 4)] [TestCase(12, 5)] [TestCase(40, 5)]
        public void ComboMultiplier_StepsEveryThreeKills_CappedAtFive(int chain, int mult) =>
            Assert.AreEqual(mult, Combo.Multiplier(chain));

        [Test]
        public void BonusPoints_AreAddedToTheRun()
        {
            var cfg = new UpgradeConfig();
            var a = cfg.PointsFor(new RunResult { Kills = 10 });
            var b = cfg.PointsFor(new RunResult { Kills = 10, BonusPoints = 25 });
            Assert.AreEqual(a + 25, b);
        }

        [Test]
        public void PerkRoll_GivesThreeDifferentPerks_AndApplyChangesTheConfig()
        {
            var picks = Perks.Roll(new Random(3));
            Assert.AreEqual(3, picks.Length);
            Assert.AreNotEqual(picks[0], picks[1]);
            Assert.AreNotEqual(picks[1], picks[2]);
            Assert.AreNotEqual(picks[0], picks[2]);
            var c = new RedlineConfig();
            var p = new Player { Hp = 1 };
            Perks.Apply(PerkKind.Vital, c, p);
            Assert.AreEqual(new RedlineConfig().playerMaxHp + new RedlineConfig().perkMaxHpBonus, c.playerMaxHp);
            Assert.AreEqual(c.playerMaxHp, p.Hp);
            Perks.Apply(PerkKind.AirDancer, c, p);
            Assert.AreEqual(1, c.doubleJumpMinTier);
        }

        [Test]
        public void Challenges_CountProgress_AndReportCompletionOnce()
        {
            var t = new ChallengeTracker();
            t.NewRun(new Random(1));
            Assert.AreEqual(3, t.Active.Count);
            for (int i = 0; i < 60; i++) t.OnKill(3, true, true, EnemyKind.Brute);
            t.OnChain(20);
            for (int i = 0; i < 10; i++) t.OnWaveCleared(false);
            foreach (var c in t.Active) Assert.IsTrue(c.Done, c.Text);
            Assert.AreEqual(3, t.TakeCompleted().Count);
            Assert.AreEqual(0, t.TakeCompleted().Count);
        }

        [Test]
        public void Spin_Legendary_GivesThreeLevels_RareTwo()
        {
            var cfg = new UpgradeConfig();
            var s = new UpgradeState();
            s.Award(100000);
            s.TrySpin(cfg, 0.0, 0.0, out var won, out var rarity);
            Assert.AreEqual(SpinRarity.Legendary, rarity);
            Assert.AreEqual(3, s.LevelOf(won));
            s.TrySpin(cfg, 0.99, cfg.spinLegendaryChance + 0.01, out var won2, out var rarity2);
            Assert.AreEqual(SpinRarity.Rare, rarity2);
            Assert.AreEqual(2, s.LevelOf(won2) - (won2 == won ? 3 : 0));
        }

        [Test]
        public void Cosmetics_AndBestWaves_SurviveSaveAndLoad()
        {
            var cfg = new UpgradeConfig();
            var s = new UpgradeState();
            s.Award(1000);
            Assert.AreEqual(BuyResult.Ok, s.TryEquipOrBuy(CosmeticKind.Cyan, cfg));
            Assert.IsTrue(s.RecordWave("City01B", 12));
            Assert.IsFalse(s.RecordWave("City01B", 5));
            var back = UpgradeSave.Deserialize(UpgradeSave.Serialize(s), cfg, out var status);
            Assert.AreEqual(SaveLoadStatus.Ok, status);
            Assert.IsTrue(back.Owns(CosmeticKind.Cyan));
            Assert.AreEqual(CosmeticKind.Cyan, back.Equipped);
            Assert.AreEqual(12, back.BestWave("City01B"));
            Assert.AreEqual(1000 - cfg.cosmeticCosts[(int)CosmeticKind.Cyan], back.Points);
        }

        [Test]
        public void ShockFloor_HurtsOnlyWhileLive()
        {
            var map = TileMap.Parse(";endless\n##########\n#........#\n#........#\n#P..EE..S#\n##########");
            var cfg = NoWaves();
            var sim = new RedlineSim(cfg, map);
            int hp = sim.Player.Hp;
            Teleport(sim, 4.5f, 1f);
            sim.Step(Hold(0f), Dt);                    // t ~ 0: live
            Assert.IsTrue(sim.ShockLive);
            Assert.Less(sim.Player.Hp, hp);
        }

        [Test]
        public void EveryTenthEndlessWave_BringsABoss()
        {
            var cfg = new RedlineConfig();
            var sim = new RedlineSim(cfg, TileMap.Parse(UnityEngine.Resources.Load<UnityEngine.TextAsset>("City02").text));
            bool boss = false;
            for (int i = 0; i < 20000 && !boss; i++)
            {
                sim.Player.Hp = cfg.playerMaxHp;
                sim.Player.InvulnTimer = 1f;
                if (sim.State == SimState.Fighting && sim.CurrentWave < cfg.bossEveryWaves)
                    foreach (var e in sim.Enemies) e.Hp = 0f;
                sim.Step(Hold(0f), Dt);
                foreach (var e in sim.Enemies) if (e.Alive && e.IsBoss) boss = true;
            }
            Assert.IsTrue(boss);
            Assert.AreEqual(cfg.bossEveryWaves, sim.CurrentWave);
        }
    }
}
