using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 004 — grunt and armored brute.</summary>
    public class EnemyTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _sim = new RedlineSim(_cfg, Box(40, 14));
        }

        [Test]
        public void Grunt_ChasesThePlayer()
        {
            var grunt = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(9f, 1f)); // inside aggro range
            Run(_sim, Hold(0f), 1f);

            Assert.That(grunt.X, Is.EqualTo(9f - _cfg.gruntSpeed).Within(0.1f));
        }

        [Test]
        public void Grunt_JumpsOverAOneTileStep()
        {
            var map = TileMap.Parse(
                "##########\n" +
                "#........#\n" +
                "#........#\n" +
                "#........#\n" +
                "#P...#..S#\n" +
                "##########\n");
            var sim = new RedlineSim(NoWaves(), map);
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(8.5f, 1f));
            Run(sim, Hold(0f), 2f);

            Assert.Less(grunt.X, 5f, "climbed over the step at x=5");
        }

        [Test]
        public void Grunt_OnLedgeAbovePlayer_DropsDownInsteadOfWaiting()
        {
            // Regression: a grunt on a platform edge straight above the player used to stand still forever.
            var map = TileMap.Parse(
                "############\n" +
                "#..........#\n" +
                "#..........#\n" +
                "#..........#\n" +
                "#.####.....#\n" +
                "#..........#\n" +
                "#..........#\n" +
                "#..........#\n" +
                "#....P....S#\n" +
                "############\n");
            var sim = new RedlineSim(NoWaves(), map);
            // Platform covers x 2..5 at y=5; put the grunt on its right edge, the player just below.
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(5.6f, 6f));
            Teleport(sim, 5.6f, 1f);

            Run(sim, Hold(0f), 2f);

            Assert.Less(grunt.Y, 2f, "grunt reached the floor");
        }

        [Test]
        public void Grunt_LeashedAbovePlayer_TakesTheOtherLedgeDown()
        {
            // Regression (Loop01 high lane): the near ledge was beyond the leash, so the grunt turned
            // for the other ledge, left the "above the player" band after a step and turned back,
            // pacing forever. Platform x 4..24 at y=5; post 10.5: left edge in leash, right edge not.
            var map = TileMap.Parse(
                "########################################\n" +
                "#......................................#\n" +
                "#......................................#\n" +
                "#......................................#\n" +
                "#...#####################..............#\n" +
                "#......................................#\n" +
                "#......................................#\n" +
                "#......................................#\n" +
                "#....P................................S#\n" +
                "########################################\n");
            var sim = new RedlineSim(NoWaves(), map);
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(10.5f, 6f));
            Teleport(sim, 18f, 1f);
            sim.Player.InvulnTimer = 99f;

            Run(sim, Hold(0f), 12f); // ~3 s right to the leash, ~5.5 s back left to the far edge

            Assert.Less(grunt.Y, 2f, "grunt reached the floor");
        }

        [Test]
        public void Contact_DamagesPlayer_GrantsInvulnerability_AndKnocksBack()
        {
            var p = _sim.Player;
            p.X = 10f;
            p.Vx = 15f; // flying into it: all of that momentum is lost
            _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 0.5f, p.Y));
            _sim.Step(Hold(0f), Dt);

            Assert.AreEqual(_cfg.playerMaxHp - _cfg.gruntContactDamage, p.Hp);
            Assert.Greater(p.InvulnTimer, 0f);
            Assert.AreEqual(-_cfg.hurtKnockbackX, p.Vx, "momentum replaced by knockback away from the grunt");

            Run(_sim, Hold(0f), 0.5f);
            Assert.AreEqual(_cfg.playerMaxHp - _cfg.gruntContactDamage, p.Hp, "no damage while invulnerable");
        }

        [Test]
        public void Brute_ContactDealsMoreDamage()
        {
            var p = _sim.Player;
            _sim.SpawnEnemy(EnemyKind.Brute, new Vec2(p.X + 0.5f, p.Y));
            _sim.Step(Hold(0f), Dt);

            Assert.AreEqual(_cfg.playerMaxHp - _cfg.bruteContactDamage, p.Hp);
        }

        [Test]
        public void StunnedEnemy_DealsNoContactDamage()
        {
            var p = _sim.Player;
            var grunt = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 0.5f, p.Y));
            grunt.StunTimer = 1f;
            _sim.Step(Hold(0f), Dt);

            Assert.AreEqual(_cfg.playerMaxHp, p.Hp);
        }

        [Test]
        public void Brute_TakesReducedKnockback()
        {
            _cfg.bruteHp = 100f;
            var p = _sim.Player;
            p.X = 10f;
            p.Vx = 13f;
            var brute = _sim.SpawnEnemy(EnemyKind.Brute, new Vec2(p.X + 1.8f, p.Y));
            _sim.Step(new InputFrame { MoveX = 1f, AttackPressed = true }, Dt);

            Assert.That(brute.Vx, Is.EqualTo(_cfg.tierKnockback[2] * _cfg.bruteKnockbackScale).Within(0.01f));
        }

        [Test]
        public void Enemies_NeverPassThroughWalls()
        {
            _cfg.gruntHp = 100f;
            var p = _sim.Player;
            p.X = 35.5f;
            p.Vx = 13f;
            var grunt = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(37.3f, 1f));
            _sim.Step(new InputFrame { MoveX = 1f, AttackPressed = true }, Dt);
            float furthest = grunt.MaxX;
            for (int i = 0; i < 60; i++)
            {
                _sim.Step(Hold(0f), Dt);
                furthest = System.Math.Max(furthest, grunt.MaxX);
            }

            Assert.LessOrEqual(furthest, 39f, "right wall cell starts at x=39");
            Assert.Greater(furthest, 38.9f, "the knockback actually drove it into the wall");
        }
    }
}
