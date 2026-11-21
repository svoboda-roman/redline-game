using System;
using NUnit.Framework;
using Redline.Core;

namespace Redline.Tests
{
    /// <summary>Enemies take several hits at low speed and fewer the faster the player is (speed tier = damage).</summary>
    public class EnemyToughnessTests
    {
        private static int HitsToKill(float hp, int tier, RedlineConfig c) =>
            (int)MathF.Ceiling(hp / CombatRules.Damage(tier, c) - 1e-4f);

        [Test]
        public void Grunt_TakesThreeHitsSlow_TwoAtSpeed_OneAtFullSpeed()
        {
            var c = new RedlineConfig();
            Assert.AreEqual(3, HitsToKill(c.gruntHp, 1, c));
            Assert.AreEqual(2, HitsToKill(c.gruntHp, 2, c));
            Assert.AreEqual(1, HitsToKill(c.gruntHp, 3, c));
        }

        [Test]
        public void EveryKind_NeedsMoreThanOneHitAtTier1()
        {
            var c = new RedlineConfig();
            foreach (var hp in new[] { c.gruntHp, c.droneHp, c.shooterHp, c.shieldHp, c.kamikazeHp, c.summonerHp, c.bruteHp })
                Assert.Greater(HitsToKill(hp, 1, c), 1);
        }
    }
}
