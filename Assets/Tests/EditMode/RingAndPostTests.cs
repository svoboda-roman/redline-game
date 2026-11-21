using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Ring levels (leave one side, enter the other) and enemies guarding posts.</summary>
    public class RingAndPostTests
    {
        private RedlineConfig _cfg;
        private TileMap _loop;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _loop = TileMap.Parse(ReadResource("Loop01.txt"));
        }

        [Test]
        public void Loop01_IsARingWithAStartAndPosts()
        {
            Assert.IsTrue(_loop.WrapsX);
            Assert.GreaterOrEqual(_loop.SpawnPoints.Count, 6);
            Assert.IsFalse(_loop.IsSolid(0, 1), "no side wall at the seam");
            Assert.AreEqual(Tile.Solid, _loop.Get(-1, 0), "the floor continues past the seam");
        }

        [Test]
        public void Running_OffTheRightEdge_ComesBackOnTheLeft()
        {
            var sim = new RedlineSim(_cfg, _loop);
            Teleport(sim, _loop.Width - 1.5f, 1f);
            sim.Player.Vx = _cfg.runSpeed;

            Run(sim, Hold(1f), 0.5f);

            Assert.Less(sim.Player.X, 5f, "came round to the left side");
            Assert.That(sim.Player.Vx, Is.EqualTo(_cfg.runSpeed).Within(0.01f), "no wall at the seam");
        }

        [Test]
        public void Strike_AcrossTheSeam_HitsAnEnemyOnTheOtherSide()
        {
            var sim = new RedlineSim(_cfg, _loop);
            Teleport(sim, _loop.Width - 0.6f, 1f);
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(0.7f, 1f));

            sim.Step(new InputFrame { MoveX = 1f, AttackPressed = true }, Dt);

            Assert.IsFalse(grunt.Alive);
        }

        [Test]
        public void Grunt_ChasesAcrossTheSeam()
        {
            var sim = new RedlineSim(_cfg, _loop);
            Teleport(sim, _loop.Width - 2f, 1f);
            sim.Player.InvulnTimer = 99f;
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(1.5f, 1f));

            Run(sim, Hold(0f), 1f); // 3.5 tiles: across the seam at x=0

            Assert.Greater(grunt.X, _loop.Width - 4f, "walked left across the seam toward the player");
        }

        [Test]
        public void Grunt_PlayerFarAway_StaysAtItsPost()
        {
            var sim = new RedlineSim(_cfg, Box(80, 14));
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(60f, 1f));

            Run(sim, Hold(0f), 2f);

            Assert.That(grunt.X, Is.EqualTo(60f).Within(0.6f));
        }

        [Test]
        public void Grunt_PlayerLeaves_WalksBackToItsPost()
        {
            var sim = new RedlineSim(_cfg, Box(80, 14));
            Teleport(sim, 52f, 1f);
            sim.Player.InvulnTimer = 99f;
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(58f, 1f));
            Run(sim, Hold(0f), 1f);
            Assert.Less(grunt.X, 56f, "chased the player");

            Teleport(sim, 10f, 1f);
            Run(sim, Hold(0f), 4f);

            Assert.That(grunt.X, Is.EqualTo(58f).Within(0.6f));
        }

        [Test]
        public void Grunt_NeverChasesBeyondItsLeash()
        {
            var sim = new RedlineSim(_cfg, Box(80, 14));
            Teleport(sim, 40f, 1f);
            sim.Player.InvulnTimer = 99f;
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(47f, 1f));

            // Lead it away slowly, staying just inside aggro range.
            for (int i = 0; i < 600; i++)
            {
                if (sim.Player.X - grunt.X > -4f) sim.Player.X -= 0.05f;
                sim.Player.RefreshContacts(sim.Map);
                sim.Step(Hold(0f), Dt);
            }

            Assert.GreaterOrEqual(grunt.X, 47f - _cfg.enemyLeash - 0.3f);
        }
    }
}
