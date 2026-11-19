using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>
    /// Combat changes: the air slide (one per airtime, survives landing), the slide as a weapon
    /// (one hit per enemy per slide, clangs like the sword) and the tougher kamikaze (2 hp, a weak
    /// hit lights a short fuse, lunge).
    /// </summary>
    public class SlideCombatTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _sim = new RedlineSim(_cfg, Box(60, 20));
        }

        private static InputFrame SlideRight() => new InputFrame { MoveX = 1f, SlidePressed = true };

        private int Count(SimEventType type, float seconds, InputFrame first)
        {
            int n = 0;
            int steps = (int)System.Math.Round(seconds / Dt);
            var input = first;
            for (int i = 0; i < steps; i++)
            {
                _sim.Step(input, Dt);
                foreach (var e in _sim.Events) if (e.Type == type) n++;
                input = Hold(1f);
            }
            return n;
        }

        // ------------------------------------------------------------------ air slide

        [Test]
        public void AirSlide_StartsInTheAir_AndKeepsRunningWhileAirborne()
        {
            Teleport(_sim, 10f, 12f);
            _sim.Player.Vx = _cfg.runSpeed;

            _sim.Step(SlideRight(), Dt);

            Assert.IsTrue(_sim.Player.Sliding);
            Assert.IsFalse(_sim.Player.Grounded);
            Assert.IsTrue(_sim.Player.AirSlideUsed);
            Assert.That(_sim.Player.Vx, Is.EqualTo(_cfg.runSpeed + _cfg.slideBoost).Within(0.001f));
            Run(_sim, Hold(1f), 0.2f);
            Assert.IsTrue(_sim.Player.Sliding, "being airborne does not end the slide");
        }

        [Test]
        public void AirSlide_BelowMinSpeed_StillDashesAlongFacing()
        {
            Teleport(_sim, 10f, 12f);
            _sim.Player.Vx = 0f;
            _sim.Player.Facing = -1;

            _sim.Step(new InputFrame { MoveX = 0f, SlidePressed = true }, Dt);

            Assert.IsTrue(_sim.Player.Sliding);
            Assert.That(_sim.Player.Vx, Is.EqualTo(-(_cfg.slideMinSpeed + _cfg.slideBoost)).Within(0.001f));
        }

        [Test]
        public void AirSlide_OnlyOncePerAirtime_AndResetsOnLanding()
        {
            _cfg.slideCooldown = 0.05f; // short, so the player is still airborne when the cooldown is over
            _sim = new RedlineSim(_cfg, Box(60, 20));
            Teleport(_sim, 10f, 17f);
            _sim.Player.Vx = _cfg.runSpeed;
            _sim.Step(SlideRight(), Dt);
            Run(_sim, Hold(1f), _cfg.slideDuration + _cfg.slideCooldown + 0.05f);
            Assert.IsFalse(_sim.Player.Sliding);
            Assert.IsFalse(_sim.Player.Grounded, "still falling");

            _sim.Step(SlideRight(), Dt);
            Assert.IsFalse(_sim.Player.Sliding, "no second air slide in the same airtime");

            Run(_sim, Hold(0f), 2f);
            Assert.IsTrue(_sim.Player.Grounded);
            Assert.IsFalse(_sim.Player.AirSlideUsed, "landing refills it");
        }

        [Test]
        public void AirSlide_SurvivesLanding_AsAGroundSlide()
        {
            Teleport(_sim, 10f, 1.6f);
            _sim.Player.Vx = _cfg.runSpeed;
            _sim.Step(SlideRight(), Dt);
            Assert.IsTrue(_sim.Player.Sliding);

            bool landedSliding = false;
            for (int i = 0; i < 12 && !landedSliding; i++)
            {
                _sim.Step(Hold(1f), Dt);
                landedSliding = _sim.Player.Grounded && _sim.Player.Sliding;
            }
            Assert.IsTrue(landedSliding, "landing mid-slide continues the slide");

            Run(_sim, Hold(1f), _cfg.slideDuration + 0.1f);
            Assert.IsFalse(_sim.Player.Sliding);
            Assert.AreEqual(_cfg.playerHeight, _sim.Player.Height);
        }

        [Test]
        public void AirSlide_NotWhileHooked()
        {
            Teleport(_sim, 10f, 8f);
            _sim.Player.Vx = _cfg.runSpeed;
            _sim.Player.Hooked = true;
            _sim.Player.HookAnchor = new Vec2(12f, 18f);
            _sim.Player.HookLength = 10f;

            _sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true, HookHeld = true }, Dt);

            Assert.IsFalse(_sim.Player.Sliding);
        }

        // ------------------------------------------------------------------ slide as a weapon

        [Test]
        public void Slide_KillsAGrunt_WithoutHurtingThePlayer()
        {
            Teleport(_sim, 10f, 1f);
            _sim.Player.Vx = _cfg.runSpeed;
            var grunt = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(14f, 1f));
            int hp = _sim.Player.Hp;

            _sim.Step(SlideRight(), Dt);
            for (int i = 0; i < 40 && grunt.Alive; i++) _sim.Step(Hold(1f), Dt);

            Assert.IsFalse(grunt.Alive);
            Assert.AreEqual(1, _sim.Kills);
            Assert.AreEqual(hp, _sim.Player.Hp, "the stunning hit lands before contact damage");
        }

        [Test]
        public void Slide_HitsEachEnemyOncePerSlide()
        {
            Teleport(_sim, 10f, 1f);
            _sim.Player.Vx = _cfg.runSpeed;
            var grunt = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(13f, 1f));
            grunt.Hp = 100f;

            int hits = Count(SimEventType.Hit, _cfg.slideDuration, SlideRight());

            Assert.AreEqual(1, hits);
            Assert.That(grunt.Hp, Is.EqualTo(100f - CombatRules.Damage(2, _cfg) * _cfg.slideDamageMultiplier).Within(0.001f));
        }

        [Test]
        public void Slide_ClangsOnABruteBelowTier3_AndEndsTheSlide()
        {
            Teleport(_sim, 10f, 1f);
            _sim.Player.Vx = _cfg.runSpeed;
            var brute = _sim.SpawnEnemy(EnemyKind.Brute, new Vec2(13f, 1f));
            float hp = brute.Hp;

            bool clang = false;
            _sim.Step(SlideRight(), Dt);
            for (int i = 0; i < 30 && !clang; i++)
            {
                _sim.Step(Hold(1f), Dt);
                clang = HasEvent(_sim, SimEventType.Clang);
            }

            Assert.IsTrue(clang);
            Assert.AreEqual(hp, brute.Hp);
            Assert.IsFalse(_sim.Player.Sliding, "a clang ends the slide");
            Assert.Less(_sim.Player.Vx, 0f, "and bounces the player back");
        }

        [Test]
        public void Slide_ShieldBlocksFromTheFront_ButNotFromBehind()
        {
            // Front: the shield faces the player.
            Teleport(_sim, 10f, 1f);
            _sim.Player.Vx = _cfg.runSpeed;
            var front = _sim.SpawnEnemy(EnemyKind.Shield, new Vec2(13f, 1f));
            front.ShieldDir = -1;
            front.Facing = -1;
            bool blocked = false;
            _sim.Step(SlideRight(), Dt);
            for (int i = 0; i < 30 && !blocked; i++)
            {
                _sim.Step(Hold(1f), Dt);
                blocked = HasEvent(_sim, SimEventType.ShieldBlock);
            }
            Assert.IsTrue(blocked);
            Assert.AreEqual(_cfg.shieldHp, front.Hp);

            // Behind: the shield faces away.
            var sim = new RedlineSim(_cfg, Box(60, 20));
            Teleport(sim, 10f, 1f);
            sim.Player.Vx = _cfg.runSpeed;
            var back = sim.SpawnEnemy(EnemyKind.Shield, new Vec2(13f, 1f));
            back.ShieldDir = 1;
            back.Facing = 1;
            bool hit = false;
            sim.Step(SlideRight(), Dt);
            for (int i = 0; i < 30 && !hit; i++)
            {
                sim.Step(Hold(1f), Dt);
                hit = back.Hp < _cfg.shieldHp;
            }
            Assert.IsTrue(hit, "a slide through the unshielded back lands");
        }

        [Test]
        public void SwordSwing_DuringASlide_StillHits()
        {
            Teleport(_sim, 10f, 1f);
            _sim.Player.Vx = _cfg.runSpeed;
            var dummy = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(10f + 0.45f + 1.0f + 0.45f, 1f));
            dummy.Hp = 100f;
            dummy.StunTimer = 99f;

            _sim.Step(new InputFrame { MoveX = 1f, SlidePressed = true, AttackPressed = true }, Dt);

            Assert.IsTrue(HasEvent(_sim, SimEventType.Attack));
            Assert.IsTrue(_sim.Player.Sliding);
            Assert.Less(dummy.Hp, 100f, "the swing landed while sliding");
        }

        // ------------------------------------------------------------------ kamikaze

        [Test]
        public void Kamikaze_Tier1Hit_SurvivesAndLightsTheShortFuse()
        {
            Teleport(_sim, 20f, 1f);
            var bomb = _sim.SpawnEnemy(EnemyKind.Kamikaze, new Vec2(21.5f, 1f));
            bool fuse = false;

            _sim.Step(new InputFrame { AttackPressed = true }, Dt);
            fuse |= HasEvent(_sim, SimEventType.KamikazeFuse);

            Assert.AreEqual(1, _sim.PlayerTier);
            Assert.IsTrue(bomb.Alive);
            Assert.That(bomb.Hp, Is.EqualTo(_cfg.kamikazeHp - CombatRules.Damage(1, _cfg)).Within(0.001f));
            Assert.AreEqual(EnemyAct.Fuse, bomb.Act);
            Assert.That(bomb.ModeTimer, Is.LessThanOrEqualTo(_cfg.kamikazeHitFuseTime).And.GreaterThan(_cfg.kamikazeHitFuseTime - Dt * 2f));
            Assert.IsTrue(fuse);
        }

        [Test]
        public void Kamikaze_HitFuse_BlastsAfterTheShortFuse()
        {
            Teleport(_sim, 20f, 1f);
            var bomb = _sim.SpawnEnemy(EnemyKind.Kamikaze, new Vec2(21.5f, 1f));
            _sim.Step(new InputFrame { AttackPressed = true }, Dt);

            float t = 0f;
            bool boom = false;
            for (int i = 0; i < 60 && !boom; i++)
            {
                _sim.Step(Hold(0f), Dt);
                t += Dt;
                boom = HasEvent(_sim, SimEventType.Explode);
            }

            Assert.IsTrue(boom);
            Assert.Less(t, _cfg.kamikazeHitFuseTime + Dt * 3f);
            Assert.Less(t, _cfg.kamikazeFuseTime, "shorter than the normal fuse");
        }

        [Test]
        public void Kamikaze_Tier2Hit_KillsItOutright()
        {
            Teleport(_sim, 20f, 1f);
            _sim.Player.Vx = _cfg.runSpeed;
            var bomb = _sim.SpawnEnemy(EnemyKind.Kamikaze, new Vec2(21.5f, 1f));

            _sim.Step(new InputFrame { MoveX = 1f, AttackPressed = true }, Dt);

            Assert.AreEqual(2, _sim.PlayerTier);
            Assert.IsFalse(bomb.Alive);
            Assert.IsFalse(HasEvent(_sim, SimEventType.KamikazeFuse));
        }

        [Test]
        public void Kamikaze_RushingWithinLungeRange_LeapsAtThePlayerOnce()
        {
            Teleport(_sim, 20f, 1f);
            var bomb = _sim.SpawnEnemy(EnemyKind.Kamikaze, new Vec2(20f + _cfg.kamikazeLungeRange - 0.3f, 1f));

            bool lunged = false;
            int leaps = 0;
            bool wasAirborneFast = false;
            bool fuse = false;
            for (int i = 0; i < 90; i++)
            {
                _sim.Step(Hold(0f), Dt);
                bool fast = bomb.Alive && bomb.Vy > _cfg.kamikazeLungeSpeedY * 0.8f
                    && bomb.Vx < -_cfg.kamikazeLungeSpeedX * 0.8f;
                if (fast && !wasAirborneFast) leaps++;
                wasAirborneFast = fast;
                lunged |= fast;
                fuse |= HasEvent(_sim, SimEventType.KamikazeFuse);
                if (!bomb.Alive) break;
            }

            Assert.IsTrue(lunged, "leaps toward the player");
            Assert.AreEqual(1, leaps);
            Assert.IsTrue(fuse, "lights the fuse after the leap");
        }

        [Test]
        public void Config_NewKnobs_AreValidated()
        {
            var c = new RedlineConfig { kamikazeHitFuseTime = 0f, slideHitReach = -1f, kamikazeLungeSpeedY = 0f };
            var errors = c.Validate();
            Assert.That(errors, Has.Some.Contain("kamikazeHitFuseTime"));
            Assert.That(errors, Has.Some.Contain("slideHitReach"));
            Assert.That(errors, Has.Some.Contain("kamikazeLungeSpeedY"));
        }
    }
}
