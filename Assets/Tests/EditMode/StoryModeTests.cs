using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story mode (';story' levels): no waves or game over, checkpoints, hook unlock, lever and gates,
    /// collapsing bridge, lift, fire traps, placed enemies, the boss room.</summary>
    public class StoryModeTests
    {
        // 30 x 8 room. Row strings are top to bottom.
        private static TileMap Room(params string[] rows) => TileMap.Parse(";story\n" + string.Join("\n", rows));

        private static readonly string[] Basic =
        {
            "##############################",
            "#............................#",
            "#............................#",
            "#............................#",
            "#............................#",
            "#............................#",
            "#P....C.......L..G...........#",
            "##############################",
        };

        private static RedlineSim Sim(TileMap map, RedlineConfig cfg = null) => new RedlineSim(cfg ?? new RedlineConfig(), map);

        private static void Run(RedlineSim sim, float seconds, InputFrame input = default)
        {
            for (float t = 0f; t < seconds; t += Dt) sim.Step(input, Dt);
        }

        private static void Walk(RedlineSim sim, float toX)
        {
            for (int i = 0; i < 600 && sim.Player.X < toX; i++) sim.Step(new InputFrame { MoveX = 1f }, Dt);
        }

        [Test]
        public void StoryLevel_StartsInStoryState_WithoutHook()
        {
            var sim = Sim(Room(Basic));
            Assert.IsTrue(sim.StoryMode);
            Assert.AreEqual(SimState.Story, sim.State);
            Assert.IsFalse(sim.HookUnlocked);
            Run(sim, 3f);
            Assert.AreEqual(0, sim.Enemies.Count, "no waves on a story level");
        }

        [Test]
        public void FirstCheckpoint_UnlocksHook()
        {
            var sim = Sim(Room(Basic));
            Walk(sim, 6.6f);
            Assert.AreEqual(0, sim.StoryCheckpoint);
            Assert.IsTrue(sim.HookUnlocked);
        }

        [Test]
        public void Lever_OpensGate()
        {
            var map = Room(Basic);
            var sim = Sim(map);
            int gx = map.Story.Gates[0][0].x, gy = map.Story.Gates[0][0].y;
            Assert.IsTrue(map.IsSolid(gx, gy), "gate starts closed");
            Walk(sim, 14.6f);
            Assert.IsTrue(sim.LeverPulled);
            Assert.IsTrue(sim.GateOpen(0));
            Assert.IsFalse(map.IsSolid(gx, gy), "gate cell is open");
        }

        [Test]
        public void EachLever_OpensItsNearestGate_Only()
        {
            var map = Room(
                "##############################",
                "#............................#",
                "#............................#",
                "#..........G.................#",
                "#..........G.................#",
                "#..........G.................#",
                "#P..L....................L..G#",
                "##############################");
            var sim = Sim(map);
            int near = map.Story.LeverGate[0], far = map.Story.LeverGate[1];
            Assert.AreNotEqual(near, far);
            Walk(sim, 4.6f);
            Assert.IsTrue(sim.GateOpen(near), "the first lever opens the gate next to it");
            Assert.IsFalse(sim.GateOpen(far), "not the far one");
            Walk(sim, 25.6f);
            Assert.IsTrue(sim.LeverPulledAt(1));
            Assert.IsTrue(sim.GateOpen(far), "the second lever opens its own gate");
        }

        [Test]
        public void Death_RespawnsAtLastCheckpoint_NoGameOver()
        {
            var sim = Sim(Room(Basic));
            Walk(sim, 6.6f);
            Walk(sim, 10f);
            sim.Player.Hp = 1;
            sim.Player.InvulnTimer = 0f;
            sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(sim.Player.X, sim.Player.Y));
            Run(sim, 0.5f);
            Assert.AreNotEqual(SimState.GameOver, sim.State);
            Assert.AreEqual(SimState.Story, sim.State);
            Assert.AreEqual(sim.Map.Story.Checkpoints[0].X, sim.Player.X, 1.5f, "back at the checkpoint");
            Assert.AreEqual(sim.Config.playerMaxHp, sim.Player.Hp);
        }

        [Test]
        public void Death_BringsKilledEnemiesBack()
        {
            var map = Room(
                "##############################",
                "#............................#",
                "#............................#",
                "#............................#",
                "#............................#",
                "#............................#",
                "#P..........g................#",
                "##############################");
            var sim = Sim(map, new RedlineConfig { storyActivateRange = 15f });
            Run(sim, 0.1f);
            Assert.AreEqual(1, sim.Enemies.Count, "woke up");
            sim.Enemies[0].Hp = 0f;   // killed
            Run(sim, 0.1f);
            Assert.AreEqual(0, sim.EnemiesAlive);
            sim.Player.Hp = 0;
            Run(sim, 0.1f);
            Run(sim, 0.1f);
            Assert.AreEqual(1, sim.EnemiesAlive, "the killed enemy is back after the player died");
        }

        [Test]
        public void StoryMode_DoubleJumpAtTierOne()
        {
            var sim = Sim(Room(Basic));
            Run(sim, 0.2f);
            sim.Step(new InputFrame { JumpPressed = true, JumpHeld = true }, Dt);
            for (int i = 0; i < 20; i++) sim.Step(new InputFrame { JumpHeld = true }, Dt);
            float vyBefore = sim.Player.Vy;
            sim.Step(new InputFrame { JumpPressed = true, JumpHeld = true }, Dt);
            Assert.IsTrue(sim.Player.AirJumpUsed, "double jump available without speed tier");
            Assert.Greater(sim.Player.Vy, vyBefore);
        }

        [Test]
        public void Bridge_CrumblesUnderPlayer_AndComesBack()
        {
            var map = Room(
                "##############################",
                "#............................#",
                "#............................#",
                "#P...........................#",
                "#######==========#############",
                "#............................#",
                "#............................#",
                "##############################");
            var cfg = new RedlineConfig { storyBridgeRespawn = 1f };
            var sim = Sim(map, cfg);
            Walk(sim, 9f);
            Run(sim, cfg.storyBridgeDelay + 0.3f);
            int gone = 0;
            for (int i = 0; i < map.Story.BridgeCells.Count; i++) if (sim.BridgeGone(i)) gone++;
            Assert.Greater(gone, 0, "the bridge gave way under the player");
            Run(sim, 3f);
            for (int i = 0; i < map.Story.BridgeCells.Count; i++)
            {
                var b = map.Story.BridgeCells[i];
                if (!sim.Player.Overlaps(b.x, b.y, b.x + 1, b.y + 1)) Assert.IsFalse(sim.BridgeGone(i), $"cell {i} came back");
            }
        }

        [Test]
        public void Lift_CarriesPlayerUp_AndOpensFarGates()
        {
            var map = Room(
                "##############################",
                "#............................#",
                "#............................#",
                "#............................#",
                "#.................G..........#",
                "#....||......................#",
                "#....||......................#",
                "#....||......................#",
                "#P...||......................#",
                "##############################");
            var sim = Sim(map, new RedlineConfig { storyLiftPause = 1.5f });
            Walk(sim, 5.5f);
            float startY = sim.Player.Y, maxY = startY;
            for (float t = 0f; t < 3f; t += Dt)
            {
                sim.Step(new InputFrame(), Dt);
                maxY = System.Math.Max(maxY, sim.Player.Y);
            }
            Assert.Greater(maxY, startY + 2.5f, "the lift carried the player up");
            Assert.IsTrue(sim.GateOpen(0), "no lever here: gate 0 opens at the lift top too");
        }

        [Test]
        public void Lift_CarriesEnemies_AndFastFallsDoNotPassThrough()
        {
            var map = Room(
                "##############################",
                "#............................#",
                "#............................#",
                "#............................#",
                "#............................#",
                "#....||......................#",
                "#....||......................#",
                "#....||......................#",
                "#P...||......................#",
                "##############################");
            var sim = Sim(map, new RedlineConfig { storyLiftPause = 0.3f, storyActivateRange = 0f });
            var e = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(5.9f, 1f));
            e.StunTimer = 99f;   // stands still on the lift
            float startY = e.Y, maxY = startY;
            for (float t = 0f; t < 1.5f; t += Dt) { sim.Step(new InputFrame(), Dt); maxY = System.Math.Max(maxY, e.Y); }
            Assert.Greater(maxY, startY + 2f, "the lift carried the enemy up");

            // The player dropped from high above at full fall speed lands on the lift instead of passing through it.
            float liftTop = sim.LiftTop(0);
            sim.Player.X = 5.9f;
            sim.Player.Y = liftTop + 4f;
            sim.Player.Vy = -sim.Config.maxFallSpeed;
            for (int i = 0; i < 30; i++) sim.Step(new InputFrame(), Dt);
            Assert.GreaterOrEqual(sim.Player.Y, sim.LiftTop(0) - 0.15f, "standing on the lift, not below it");
        }

        [Test]
        public void Fire_HurtsOnlyWhileLive()
        {
            var map = Room(
                "##############################",
                "#............................#",
                "#............................#",
                "#............................#",
                "#............................#",
                "#............................#",
                "#P..F........................#",
                "##############################");
            var cfg = new RedlineConfig { storyFireOn = 1f, storyFireOff = 1f, storyFireRadius = 1.5f };
            var sim = Sim(map, cfg);
            sim.Player.X = 3.4f;
            int hp = sim.Player.Hp;
            sim.Step(new InputFrame(), Dt);
            Assert.IsTrue(sim.FireLive);
            Assert.Less(sim.Player.Hp, hp, "burnt while live");
        }

        [Test]
        public void PlacedEnemies_SpawnWhenNear_Once()
        {
            var map = Room(
                "##############################",
                "#............................#",
                "#............................#",
                "#............................#",
                "#............................#",
                "#............................#",
                "#P..........................g#",
                "##############################");
            var sim = Sim(map, new RedlineConfig { storyActivateRange = 10f });
            Run(sim, 0.2f);
            Assert.AreEqual(0, sim.Enemies.Count, "still far away");
            Walk(sim, 19f);
            Assert.AreEqual(1, sim.Enemies.Count, "woke up");
            Walk(sim, 22f);
            Assert.AreEqual(1, sim.Enemies.Count, "spawns once");
        }
    }
}
