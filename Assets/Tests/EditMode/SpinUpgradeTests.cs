using NUnit.Framework;
using Redline.Core;

namespace Redline.Tests
{
    /// <summary>RANDOM UPGRADE: pay a rising price, a random upgrade that is not maxed gains a level.</summary>
    public class SpinUpgradeTests
    {
        [Test]
        public void Spin_PaysTheCost_RaisesOneUpgrade_AndThePriceRises()
        {
            var cfg = new UpgradeConfig();
            var s = new UpgradeState();
            s.Award(1000);
            int cost0 = cfg.SpinCost(0);
            Assert.AreEqual(BuyResult.Ok, s.TrySpin(cfg, 0.5, out var won));
            Assert.AreEqual(1000 - cost0, s.Points);
            Assert.AreEqual(1, s.LevelOf(won));
            Assert.AreEqual(1, s.Spins);
            Assert.Greater(cfg.SpinCost(1), cost0);
        }

        [Test]
        public void Spin_NotEnoughPoints_ChangesNothing()
        {
            var cfg = new UpgradeConfig();
            var s = new UpgradeState();
            s.Award(cfg.SpinCost(0) - 1);
            Assert.AreEqual(BuyResult.NotEnoughPoints, s.TrySpin(cfg, 0.1, out _));
            Assert.AreEqual(0, s.Spins);
            foreach (var k in UpgradeState.Kinds) Assert.AreEqual(0, s.LevelOf(k));
        }

        [Test]
        public void Spin_NeverPicksAMaxedUpgrade_AndStopsWhenAllAreMaxed()
        {
            var cfg = new UpgradeConfig();
            var s = new UpgradeState();
            s.Award(int.MaxValue);
            foreach (var k in UpgradeState.Kinds)
                if (k != UpgradeKind.Hook) s.SetLevel(k, cfg.MaxLevel(k), cfg);
            for (int i = 0; i < cfg.hookMaxLevel; i++)
            {
                Assert.AreEqual(BuyResult.Ok, s.TrySpin(cfg, 0.99, out var won));
                Assert.AreEqual(UpgradeKind.Hook, won);
            }
            Assert.AreEqual(BuyResult.MaxLevel, s.TrySpin(cfg, 0.3, out _));
        }

        [Test]
        public void Spins_SurviveSaveAndLoad()
        {
            var cfg = new UpgradeConfig();
            var s = new UpgradeState();
            s.Award(500);
            s.TrySpin(cfg, 0.2, out _);
            s.TrySpin(cfg, 0.7, out _);
            var back = UpgradeSave.Deserialize(UpgradeSave.Serialize(s), cfg, out var status);
            Assert.AreEqual(SaveLoadStatus.Ok, status);
            Assert.AreEqual(2, back.Spins);
            Assert.AreEqual(s.Points, back.Points);
        }
    }
}
