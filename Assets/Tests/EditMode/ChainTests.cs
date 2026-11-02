using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 010 — kill chain: quick kills build a chain that refunds more speed.</summary>
    public class ChainTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _sim = new RedlineSim(_cfg, Box(80, 14));
            _sim.Player.X = 10f;
        }

        /// <summary>Stops the player, kills a grunt standing in front, and returns the speed refunded.</summary>
        private float KillInFront()
        {
            var p = _sim.Player;
            p.Vx = 0f;
            p.Facing = 1;
            _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));
            _sim.Step(new InputFrame { AttackPressed = true }, Dt);
            return p.Vx;
        }

        [Test]
        public void FirstKill_StartsAChainOfOne()
        {
            float refund = KillInFront();

            Assert.AreEqual(1, _sim.Player.Chain);
            Assert.That(refund, Is.EqualTo(_cfg.killMomentumRefund).Within(0.001f));
        }

        [Test]
        public void KillInsideTheWindow_RaisesTheChainAndRefundsMore()
        {
            KillInFront();
            Run(_sim, Hold(0f), _cfg.attackCooldown);

            float refund = KillInFront();

            Assert.AreEqual(2, _sim.Player.Chain);
            Assert.That(refund, Is.EqualTo(_cfg.killMomentumRefund + _cfg.chainBonusPerKill).Within(0.001f));
        }

        [Test]
        public void KillAfterTheWindow_StartsAFreshChain()
        {
            KillInFront();
            Run(_sim, Hold(0f), _cfg.chainWindow + 0.1f);
            Assert.AreEqual(0, _sim.Player.Chain, "chain expired");

            float refund = KillInFront();

            Assert.AreEqual(1, _sim.Player.Chain);
            Assert.That(refund, Is.EqualTo(_cfg.killMomentumRefund).Within(0.001f));
        }

        [Test]
        public void ChainBonus_IsCapped()
        {
            float refund = 0f;
            for (int i = 0; i < 12; i++)
            {
                refund = KillInFront();
                Run(_sim, Hold(0f), _cfg.attackCooldown);
            }

            Assert.AreEqual(12, _sim.Player.Chain);
            Assert.That(refund, Is.EqualTo(_cfg.killMomentumRefund + _cfg.chainMaxBonus).Within(0.001f));
        }

        [Test]
        public void TakingDamage_ResetsTheChain()
        {
            KillInFront();
            Run(_sim, Hold(0f), _cfg.attackCooldown);
            KillInFront();
            Assert.AreEqual(2, _sim.Player.Chain);
            Run(_sim, Hold(0f), _cfg.attackActiveTime + Dt); // strike over, chain still running
            Assert.AreEqual(2, _sim.Player.Chain);

            var p = _sim.Player;
            _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 0.3f, p.Y));
            _sim.Step(Hold(0f), Dt);

            Assert.Less(p.Hp, _cfg.playerMaxHp);
            Assert.AreEqual(0, p.Chain);
        }
    }
}
