using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 012 — drone: hovers out of ground reach, telegraphs, dives, climbs back.</summary>
    public class DroneTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _sim = new RedlineSim(_cfg, Box(60, 20));
            Teleport(_sim, 20f, 1f);
        }

        [Test]
        public void Drone_HoversAboveThePlayer_WithoutGravity()
        {
            var drone = _sim.SpawnEnemy(EnemyKind.Drone, new Vec2(27f, 12f)); // inside aggro range
            drone.ModeTimer = 99f; // hold it in hover for this test

            Run(_sim, Hold(0f), 8f);

            Assert.AreNotEqual(DroneMode.Dive, drone.DroneState);
            Assert.That(drone.Y, Is.EqualTo(_sim.Player.Y + _cfg.droneHoverHeight).Within(0.6f));
            Assert.Less(MathF.Abs(drone.X - _sim.Player.X), 3f, "drifted over the player");
        }

        [Test]
        public void HoveringDrone_IsOutOfReachOfAJumpStrike()
        {
            var drone = _sim.SpawnEnemy(EnemyKind.Drone, new Vec2(20.8f, 1f + _cfg.droneHoverHeight));
            drone.ModeTimer = 99f; // hold it in hover for this test

            // Full jump with the strike timed at the apex.
            var input = new InputFrame { JumpPressed = true, JumpHeld = true };
            for (int i = 0; i < 40; i++)
            {
                input.AttackPressed = _sim.Player.Vy <= 0.5f && _sim.Player.Vy > -0.5f && !_sim.Player.Grounded;
                _sim.Step(input, Dt);
                input.JumpPressed = false;
            }

            Assert.IsTrue(drone.Alive);
        }

        [Test]
        public void DroneAbove_TelegraphsThenDivesAtThePlayer()
        {
            var drone = _sim.SpawnEnemy(EnemyKind.Drone, new Vec2(20.5f, 1f + _cfg.droneHoverHeight));
            float startY = drone.Y;
            int guard = 0;
            while (drone.DroneState != DroneMode.Windup && guard++ < 300) _sim.Step(Hold(0f), Dt);
            Assert.AreEqual(DroneMode.Windup, drone.DroneState);
            Assert.IsTrue(HasEvent(_sim, SimEventType.DroneWindup));

            Run(_sim, Hold(0f), _cfg.droneWindupTime + Dt);

            Assert.AreEqual(DroneMode.Dive, drone.DroneState);
            Assert.Less(drone.Vy, 0f, "diving down");
        }

        [Test]
        public void DivingDrone_CanBeCounterHitFromTheGround()
        {
            var drone = _sim.SpawnEnemy(EnemyKind.Drone, new Vec2(21.2f, 1f + _cfg.droneHoverHeight));
            int guard = 0;
            while (drone.DroneState != DroneMode.Dive && guard++ < 400) _sim.Step(Hold(0f), Dt);
            Assert.AreEqual(DroneMode.Dive, drone.DroneState);

            guard = 0;
            while (drone.Alive && guard++ < 60)
            {
                _sim.Step(new InputFrame { AttackPressed = drone.Y < 3.2f }, Dt);
            }

            Assert.IsFalse(drone.Alive);
        }

        [Test]
        public void Drone_AfterADive_ClimbsBackToHoverHeight()
        {
            var drone = _sim.SpawnEnemy(EnemyKind.Drone, new Vec2(20.5f, 1f + _cfg.droneHoverHeight));
            _sim.Player.InvulnTimer = 99f;
            int guard = 0;
            while (drone.DroneState != DroneMode.Dive && guard++ < 400) _sim.Step(Hold(0f), Dt);
            guard = 0;
            while (drone.DroneState == DroneMode.Dive && guard++ < 200) _sim.Step(Hold(0f), Dt);
            Teleport(_sim, 40f, 1f);

            Run(_sim, Hold(0f), 2f);

            Assert.That(drone.Y, Is.EqualTo(_sim.Player.Y + _cfg.droneHoverHeight).Within(0.8f));
        }

        [Test]
        public void Drone_NeverPassesThroughTerrain()
        {
            var sim = new RedlineSim(_cfg, Box(30, 8)); // ceiling below the hover height
            Teleport(sim, 10f, 1f);
            var drone = sim.SpawnEnemy(EnemyKind.Drone, new Vec2(20f, 4f));

            Run(sim, Hold(0f), 3f);

            Assert.LessOrEqual(drone.MaxY, 7f, "stays under the ceiling at y=7");
            Assert.GreaterOrEqual(drone.Y, 1f, "never sinks into the floor");
        }

        [Test]
        public void Drone_DiesToAnyHit()
        {
            var drone = _sim.SpawnEnemy(EnemyKind.Drone, new Vec2(21.2f, 1.2f));
            _sim.Step(new InputFrame { AttackPressed = true }, Dt);

            Assert.IsFalse(drone.Alive);
        }

        [Test]
        public void Drone_WithAPlatformBetween_StillGetsAtThePlayer()
        {
            // Regression: a drone above a platform the player stood under rested on the platform forever.
            var map = TileMap.Parse(
                "##############################\n" +
                "#............................#\n" +
                "#............................#\n" +
                "#............................#\n" +
                "#............................#\n" +
                "#............................#\n" +
                "#..........S.................#\n" +
                "#........#######.............#\n" +
                "#............................#\n" +
                "#............................#\n" +
                "#............................#\n" +
                "#............................#\n" +
                "#...........P................#\n" +
                "##############################\n");
            var sim = new RedlineSim(_cfg, map);
            sim.Player.InvulnTimer = 99f;
            var drone = sim.SpawnEnemy(EnemyKind.Drone, new Vec2(12.5f, 7f));
            bool dived = false;

            for (int i = 0; i < 600 && !dived; i++)
            {
                sim.Step(Hold(0f), Dt);
                dived = drone.DroneState == DroneMode.Dive;
            }

            Assert.IsTrue(dived, $"drone stuck at ({drone.X:0.0}, {drone.Y:0.0}) in {drone.DroneState}");
        }
    }
}
