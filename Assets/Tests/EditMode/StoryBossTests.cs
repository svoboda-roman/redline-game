using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Big Tentacle Bill: wakes when the player enters, a tentacle falls off after three hits, the body shrugs
    /// strikes off until all seven are gone, then takes damage and dies; dying resets the fight.</summary>
    public class StoryBossTests
    {
        private static RedlineSim Arena(RedlineConfig cfg = null)
        {
            var rows = new System.Collections.Generic.List<string>();
            rows.Add(new string('#', 44));
            for (int i = 0; i < 16; i++)
            {
                var r = "#" + new string('.', 42) + "#";
                if (i == 6) r = r.Substring(0, 30) + "B" + r.Substring(31);
                if (i == 15) r = "#P" + r.Substring(2);
                rows.Add(r);
            }
            rows.Add(new string('#', 44));
            var map = TileMap.Parse(";story\n" + string.Join("\n", rows));
            return new RedlineSim(cfg ?? new RedlineConfig { storyBossCooldown = 1e6f }, map);
        }

        /// <summary>Puts the player just left of <paramref name="target"/>, facing it, and swings once.</summary>
        private static void SwingAt(RedlineSim sim, Vec2 target)
        {
            var p = sim.Player;
            p.InvulnTimer = 99f;
            p.Facing = 1;
            p.X = target.X - 0.9f;
            p.Y = target.Y - 0.7f;
            p.Vx = 0f; p.Vy = 0f;
            sim.Step(new InputFrame { AttackPressed = true }, Dt);
            for (int i = 0; i < 40; i++)
            {
                p.InvulnTimer = 99f;
                sim.Step(new InputFrame(), Dt);
            }
        }

        [Test]
        public void Boss_WakesWhenThePlayerEntersItsRoom()
        {
            var sim = Arena();
            Assert.IsNotNull(sim.Boss);
            sim.Step(new InputFrame(), Dt);
            Assert.AreNotEqual(BossMove.Dormant, sim.Boss.Move);
        }

        [Test]
        public void Tentacle_FallsOffAfterThreeHits()
        {
            var sim = Arena();
            sim.Step(new InputFrame(), Dt);
            var t = sim.Boss.Tentacles[3];
            for (int i = 0; i < 8 && !t.Severed; i++) SwingAt(sim, t.Points[9]);
            Assert.IsTrue(t.Severed, "cut off");
            Assert.AreEqual(sim.Config.storyBossTentacleHits, t.Hits);
            Assert.AreEqual(StoryBoss.TentacleCount - 1, sim.Boss.TentaclesLeft);
        }

        [Test]
        public void Body_ShrugsStrikesOff_UntilEveryTentacleIsGone_ThenDies()
        {
            var sim = Arena(new RedlineConfig { storyBossCooldown = 1e6f, storyBossHp = 2f });
            sim.Step(new InputFrame(), Dt);
            var b = sim.Boss;
            SwingAt(sim, new Vec2(b.Body.X - b.Radius + 0.3f, b.Body.Y));
            Assert.AreEqual(b.MaxHp, b.Hp, "tentacles still on: no damage");
            foreach (var t in b.Tentacles)
                for (int i = 0; i < 10 && !t.Severed; i++) SwingAt(sim, t.Points[9]);
            Assert.IsTrue(b.Vulnerable, "all seven off");
            for (int i = 0; i < 6 && b.Move != BossMove.Dying; i++) SwingAt(sim, new Vec2(b.Body.X - b.Radius + 0.3f, b.Body.Y));
            Assert.AreEqual(BossMove.Dying, b.Move, "beaten");
            for (int i = 0; i < 200 && sim.State != SimState.Victory; i++) sim.Step(new InputFrame(), Dt);
            Assert.AreEqual(SimState.Victory, sim.State, "the story so far is complete");
        }

        [Test]
        public void Waking_PutsThePlayerInTheRoom_AndClosesTheFloorBehind()
        {
            var rows = new System.Collections.Generic.List<string>();
            rows.Add(new string('#', 44));
            for (int i = 0; i < 16; i++)
            {
                var r = "#" + new string('.', 42) + "#";
                if (i == 6) r = r.Substring(0, 30) + "B" + r.Substring(31);
                rows.Add(r);
            }
            rows.Add("#####.##########################################".Substring(0, 44));   // a hole in the floor (the shaft)
            rows.Add("#####P######################################".Substring(0, 44));
            var map = TileMap.Parse(";story\n" + string.Join("\n", rows));
            var sim = new RedlineSim(new RedlineConfig { storyBossCooldown = 1e6f }, map);
            Assert.IsFalse(map.IsSolid(5, 1), "the shaft is open");
            sim.Player.Y = 2f;   // climbed up into the room
            sim.Step(new InputFrame(), Dt);
            Assert.AreNotEqual(BossMove.Dormant, sim.Boss.Move);
            Assert.IsTrue(map.IsSolid(5, 1), "the floor closed behind the player");
            Assert.Greater(sim.Player.X, sim.Boss.RoomMinX + 5f, "put in the middle of the room");
            Assert.Less(sim.Player.X, sim.Boss.Body.X - sim.Boss.Radius, "left of the boss");
            sim.Player.InvulnTimer = 0f;
            sim.Player.Hp = 0;
            sim.Step(new InputFrame(), Dt);
            Assert.IsFalse(map.IsSolid(5, 1), "dying opens it again");
        }

        [Test]
        public void Boss_ChasesThePlayer_AndTurnsToFaceThem()
        {
            var sim = Arena(new RedlineConfig { storyBossCooldown = 0.01f });
            sim.Step(new InputFrame(), Dt);
            var b = sim.Boss;
            b.StandLeft = 0;   // its next move is a chase
            b.Cooldown = 0f;
            float startX = b.Body.X;
            for (int i = 0; i < 60; i++)
            {
                sim.Player.InvulnTimer = 99f;
                sim.Player.X = 3f;
                sim.Step(new InputFrame(), Dt);
            }
            Assert.Less(b.Body.X, startX - 1.5f, "it crawled toward the player");
            Assert.AreEqual(-1, b.Facing, "facing the player on its left");
        }

        [Test]
        public void PlayerDeath_ResetsTheBoss()
        {
            var sim = Arena();
            sim.Step(new InputFrame(), Dt);
            var t = sim.Boss.Tentacles[2];
            for (int i = 0; i < 8 && !t.Severed; i++) SwingAt(sim, t.Points[9]);
            Assert.IsTrue(t.Severed);
            sim.Player.InvulnTimer = 0f;
            sim.Player.Hp = 0;
            sim.Step(new InputFrame(), Dt);
            Assert.AreEqual(StoryBoss.TentacleCount, sim.Boss.TentaclesLeft, "tentacles grow back");
        }
    }
}
