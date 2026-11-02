using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 003 — speed-scaled combat.</summary>
    public class CombatTests
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

        // Boundary values: the thresholds themselves are the point of this test.
        [TestCase(0f, 1)]
        [TestCase(7.99f, 1)]
        [TestCase(8f, 2)]
        [TestCase(11.99f, 2)]
        [TestCase(12f, 3)]
        [TestCase(-12f, 3)]
        [TestCase(22f, 3)]
        public void TierFor_UsesConfiguredThresholds(float speed, int tier)
        {
            Assert.AreEqual(tier, CombatRules.TierFor(speed, _cfg));
        }

        [TestCase(1, 1f, 5f)]
        [TestCase(2, 2f, 9f)]
        [TestCase(3, 4f, 14f)]
        public void DamageAndKnockback_ScaleWithTier(int tier, float damage, float knockback)
        {
            Assert.AreEqual(damage, CombatRules.Damage(tier, _cfg));
            Assert.AreEqual(knockback, CombatRules.Knockback(tier, _cfg));
        }

        [Test]
        public void CanDamage_RequiresTierAtLeastArmor()
        {
            Assert.IsFalse(CombatRules.CanDamage(2, 3));
            Assert.IsTrue(CombatRules.CanDamage(3, 3));
        }

        private InputFrame Strike(float moveX)
        {
            return new InputFrame { MoveX = moveX, AttackPressed = true };
        }

        [Test]
        public void Strike_StandingStill_KillsGruntAndRefundsMomentum()
        {
            var p = _sim.Player;
            var grunt = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));
            _sim.Step(Strike(0f), Dt);

            Assert.IsFalse(grunt.Alive);
            Assert.AreEqual(1, _sim.Kills);
            Assert.That(p.Vx, Is.EqualTo(_cfg.killMomentumRefund).Within(0.001f));
            Assert.AreEqual(_cfg.playerMaxHp, p.Hp, "a killed enemy deals no contact damage");
        }

        [Test]
        public void Strike_Tier2_ClangsOffBrute()
        {
            var p = _sim.Player;
            p.Vx = _cfg.runSpeed; // tier 2
            var brute = _sim.SpawnEnemy(EnemyKind.Brute, new Vec2(p.X + 1.8f, p.Y));
            _sim.Step(Strike(1f), Dt);

            Assert.AreEqual(_cfg.bruteHp, brute.Hp);
            Assert.That(p.Vx, Is.EqualTo(-_cfg.clangBounceSpeed).Within(0.001f));
            Assert.IsTrue(HasEvent(SimEventType.Clang));
        }

        [Test]
        public void Strike_Tier3_BreaksBruteInOneHit()
        {
            var p = _sim.Player;
            p.Vx = 13f; // tier 3
            var brute = _sim.SpawnEnemy(EnemyKind.Brute, new Vec2(p.X + 1.8f, p.Y));
            _sim.Step(Strike(1f), Dt);

            Assert.IsFalse(brute.Alive);
            Assert.IsTrue(HasEvent(SimEventType.Kill));
            Assert.Greater(p.Vx, 13f, "kill refund keeps the player flying");
        }

        [Test]
        public void Strike_HitsEachEnemyOncePerStrike()
        {
            _cfg.gruntHp = 10f;
            var p = _sim.Player;
            var grunt = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));
            _sim.Step(Strike(0f), Dt);
            for (int i = 0; i < 8; i++) _sim.Step(Hold(0f), Dt); // strike stays active ~9 steps

            Assert.AreEqual(9f, grunt.Hp);
        }

        [Test]
        public void Strike_KnockbackScalesWithTier()
        {
            _cfg.gruntHp = 100f;
            var p = _sim.Player;
            var slowTarget = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));
            _sim.Step(Strike(0f), Dt);
            float slowKnock = slowTarget.Vx;

            SetUp();
            _cfg.gruntHp = 100f;
            p = _sim.Player;
            p.Vx = 13f;
            var fastTarget = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.8f, p.Y));
            _sim.Step(Strike(1f), Dt);

            Assert.Greater(fastTarget.Vx, slowKnock);
            Assert.Greater(_cfg.gruntHp - fastTarget.Hp, 1f, "tier 3 deals more than tier 1 damage");
        }

        private bool HasEvent(SimEventType type) => TestSupport.HasEvent(_sim, type);

        [Test]
        public void Strike_FacingLeft_HitsTheEnemyOnTheLeft()
        {
            var p = _sim.Player;
            p.Facing = -1;
            var left = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X - 1.2f, p.Y));
            var right = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));
            _sim.Step(new InputFrame { MoveX = -1f, AttackPressed = true }, Dt);

            Assert.IsFalse(left.Alive);
            Assert.IsTrue(right.Alive);
        }

        [Test]
        public void Strike_PressedNearTheEndOfCooldown_IsBuffered()
        {
            _sim.Step(Strike(0f), Dt);
            Run(_sim, Hold(0f), _cfg.attackCooldown - _cfg.inputBufferTime * 0.5f);
            int before = _sim.Player.StrikeId;

            _sim.Step(Strike(0f), Dt);
            Run(_sim, Hold(0f), _cfg.inputBufferTime);

            Assert.AreEqual(before + 1, _sim.Player.StrikeId);
        }

        [Test]
        public void Strike_PressedEarlyInCooldown_IsDropped()
        {
            _sim.Step(Strike(0f), Dt);
            _sim.Step(Strike(0f), Dt);
            Run(_sim, Hold(0f), _cfg.attackCooldown + 0.1f);

            Assert.AreEqual(1, _sim.Player.StrikeId);
        }

        [Test]
        public void KillRefund_IsCappedAtMaxSpeed()
        {
            var p = _sim.Player;
            p.Vx = _cfg.maxSpeed - 0.5f;
            _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.8f, p.Y));
            _sim.Step(Strike(1f), Dt);

            Assert.AreEqual(_cfg.maxSpeed, p.Vx);
        }

        [Test]
        public void KillRefund_WhileMovingAgainstFacing_BrakesInsteadOfFlipping()
        {
            // Regression: striking backwards mid-air used to fling the player the other way at full speed.
            Teleport(_sim, 30f, 6f);
            var p = _sim.Player;
            p.Vx = -10f;
            _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));
            _sim.Step(Strike(1f), Dt);

            float braked = -10f + _cfg.airAccel * Dt;
            Assert.AreEqual(1, _sim.Kills);
            Assert.That(p.Vx, Is.EqualTo(braked + _cfg.killMomentumRefund).Within(0.01f));
        }

        [Test]
        public void Clang_DoesNotStopTheStrikeHittingOtherEnemies()
        {
            // Regression: a clang on a brute used to end the strike before a grunt in the same box was hit.
            var p = _sim.Player;
            p.Vx = _cfg.runSpeed; // tier 2
            var brute = _sim.SpawnEnemy(EnemyKind.Brute, new Vec2(p.X + 1.8f, p.Y));
            var grunt = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));
            _sim.Step(Strike(1f), Dt);

            Assert.IsFalse(grunt.Alive);
            Assert.AreEqual(_cfg.bruteHp, brute.Hp);
            Assert.IsTrue(HasEvent(SimEventType.Clang));
            Assert.That(p.Vx, Is.EqualTo(-_cfg.clangBounceSpeed).Within(0.001f));
        }

        [Test]
        public void Clang_DoesNotTurnThePlayerAround()
        {
            // Regression: the clang bounce used to flip facing, so the next swing went the wrong way.
            var p = _sim.Player;
            p.Vx = _cfg.runSpeed;
            _sim.SpawnEnemy(EnemyKind.Brute, new Vec2(p.X + 1.8f, p.Y));
            _sim.Step(Strike(1f), Dt);
            Run(_sim, Hold(0f), 0.1f);

            Assert.AreEqual(1, p.Facing);
        }
    }
}
