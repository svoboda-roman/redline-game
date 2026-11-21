using System.Text;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>
    /// A falling strike reaches below the feet (strikeDownReach), so a tier-3 player diving onto an
    /// enemy hits it before touching it. Regression: Loop01's wave-2 brute ends trapped in a 2-tile
    /// pit (x 47-49) and a hook fling into the pit took contact damage before the strike could land.
    /// </summary>
    public class DownStrikeTests
    {
        private const int PitLeft = 11;
        private const int PitWidth = 2;

        /// <summary>24×12 room, floor top at y=3 except a 2-wide pit (floor top y=1) at x 11-12.</summary>
        private static TileMap PitRoom()
        {
            const int width = 24, height = 12;
            var sb = new StringBuilder();
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    bool ground = y <= 2 && (x < PitLeft || x >= PitLeft + PitWidth);
                    char c = border || ground ? '#' : '.';
                    if (y == 3 && x == 3) c = 'P';
                    if (y == 3 && x == width - 3) c = 'S';
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            return TileMap.Parse(sb.ToString());
        }

        [Test]
        public void Tier3FallingStrike_OntoBruteInAPit_KillsItWithoutContactDamage()
        {
            // Arrange: the traced Loop01 fling, mirrored into a small room. Speed -17.6, falling at
            // max fall speed, from above and to the right of a brute trapped in the pit.
            var cfg = NoWaves();
            var sim = new RedlineSim(cfg, PitRoom());
            var brute = sim.SpawnEnemy(EnemyKind.Brute, new Vec2(PitLeft + PitWidth * 0.5f, 1f));
            var p = sim.Player;
            p.X = brute.X + 2.8f;
            p.Y = 5.8f;
            p.Vx = -17.6f;
            p.Vy = -cfg.maxFallSpeed;
            p.Facing = -1;
            int startHp = p.Hp;
            bool hurt = false;

            // Act: keep swinging while falling into the pit.
            for (int i = 0; i < 30 && brute.Alive; i++)
            {
                sim.Step(new InputFrame { MoveX = -1f, AttackPressed = true }, Dt);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.PlayerHurt) hurt = true;
            }

            // Assert
            Assert.IsFalse(hurt, "the strike lands before the player's body touches the brute");
            Assert.IsFalse(brute.Alive, "the falling tier-3 strike kills the brute");
            Assert.AreEqual(startHp, p.Hp);
        }

        [Test]
        public void StrikeBox_ReachesBelowTheFeet_OnlyWhileFalling()
        {
            // Arrange
            var cfg = NoWaves();
            var sim = new RedlineSim(cfg, Box(30, 12));
            var p = sim.Player;

            // Act + Assert: standing, the box starts just above the feet.
            sim.GetStrikeBox(out _, out float standingMinY, out _, out _);
            Assert.AreEqual(p.Y + 0.1f, standingMinY, 1e-4f);

            // Falling in the air, it reaches strikeDownReach below them.
            p.Y = 6f;
            p.Vy = -5f;
            p.RefreshContacts(sim.Map);
            sim.GetStrikeBox(out _, out float fallingMinY, out _, out _);
            Assert.AreEqual(p.Y - cfg.strikeDownReach, fallingMinY, 1e-4f);

            // Rising, it does not.
            p.Vy = 5f;
            sim.GetStrikeBox(out _, out float risingMinY, out _, out _);
            Assert.AreEqual(p.Y + 0.1f, risingMinY, 1e-4f);
        }
    }
}
