using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>
    /// The training level (Resources/Training.txt + TrainingSteps.json) and <see cref="TrainingScript"/>:
    /// no waves, each step's goal met by scripted input, the fallback tip, respawn into the station,
    /// skipping, and the TrainingDone setting.
    /// </summary>
    public class TrainingTests
    {
        private static RedlineConfig Config() => new RedlineConfig();

        private static TrainingScript NewScript()
        {
            var sim = new RedlineSim(Config(), TileMap.Parse(ReadResource("Training.txt")));
            return new TrainingScript(sim, TrainingData.Parse(ReadResource("TrainingSteps.json")));
        }

        /// <summary>Skips steps (through their success flash) until the step with this id is active.</summary>
        private static void GoTo(TrainingScript script, string id)
        {
            for (int guard = 0; script.Current != null && script.Current.Id != id; guard++)
            {
                Assert.Less(guard, 100, "no step " + id);
                script.SkipStep();
                Wait(script, script.Data.Settings.successTime + 0.05f);
            }
            Assert.IsNotNull(script.Current, "no step " + id);
            Assert.AreEqual(TrainingPhase.Active, script.Phase);
        }

        /// <summary>Steps the script with no input for the given seconds.</summary>
        private static void Wait(TrainingScript script, float seconds)
        {
            for (int i = 0, n = (int)Math.Round(seconds / Dt); i < n; i++) script.Step(new InputFrame(), Dt);
        }

        /// <summary>Steps the script with an input (edge flags on the first step only) until the time runs out or the step is done.</summary>
        private static bool Play(TrainingScript script, InputFrame input, float seconds)
        {
            int start = script.StepIndex;
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                script.Step(input, Dt);
                input.JumpPressed = input.AttackPressed = input.SlidePressed = input.HookPressed = input.RestartPressed = false;
                if (script.StepIndex == start && script.Phase == TrainingPhase.Success) return true;
            }
            return script.StepIndex == start && script.Phase == TrainingPhase.Success;
        }

        [Test]
        public void TrainingLevel_Parses_AndItsDataFitsIt()
        {
            var map = TileMap.Parse(ReadResource("Training.txt"));
            Assert.IsTrue(map.Training);
            Assert.IsFalse(map.WrapsX);
            Assert.AreEqual(0, map.Hints.Count);
            var data = TrainingData.Parse(ReadResource("TrainingSteps.json"));
            CollectionAssert.IsEmpty(data.Validate(map));
            Assert.AreEqual(16, data.Steps.Count);
            CollectionAssert.AreEqual(
                new[] { "move", "jump", "slide", "attack", "chain", "walljump", "hook", "fling", "tier3", "brute", "drone", "reflect", "shield", "kamikaze", "summoner", "orbs" },
                StepIds(data));
        }

        private static string[] StepIds(TrainingData data)
        {
            var ids = new string[data.Steps.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = data.Steps[i].Id;
            return ids;
        }

        [Test]
        public void TrainingDirective_RejectsWaves()
        {
            Assert.Throws<FormatException>(() => TileMap.Parse(";training\n;wave grunt=1\n#####\n#P.S#\n#####\n"));
        }

        [Test]
        public void TrainingMode_SpawnsNoWaves_AndNeverEnds()
        {
            var sim = new RedlineSim(Config(), TileMap.Parse(ReadResource("Training.txt")));
            Assert.AreEqual(SimState.Training, sim.State);
            Assert.AreEqual(0, sim.WaveCount);
            for (int i = 0; i < 60 * 30; i++)
            {
                sim.Step(new InputFrame(), Dt);
                Assert.IsFalse(HasEvent(sim, SimEventType.WaveStart));
            }
            Assert.AreEqual(SimState.Training, sim.State);
            Assert.AreEqual(0, sim.Enemies.Count);
        }

        [Test]
        public void TrainingMode_HpThatRunsOut_Refills_InsteadOfGameOver()
        {
            var sim = new RedlineSim(Config(), TileMap.Parse(ReadResource("Training.txt")));
            sim.Player.Hp = 0;
            sim.Step(new InputFrame(), Dt);
            Assert.AreEqual(SimState.Training, sim.State);
            Assert.AreEqual(sim.Config.playerMaxHp, sim.Player.Hp);
        }

        [Test]
        public void Move_IsDone_ByRunning()
        {
            var script = NewScript();
            Assert.AreEqual("move", script.Current.Id);
            Assert.IsFalse(Play(script, new InputFrame(), 1f), "standing still does nothing");
            Assert.IsTrue(Play(script, Hold(1f), 2f));
        }

        [Test]
        public void Jump_NeedsAHeldJump_AndATapCountsAsAnAttempt()
        {
            var script = NewScript();
            GoTo(script, "jump");
            Assert.IsFalse(Play(script, new InputFrame { JumpPressed = true }, 1.2f), "a tapped hop is too low");
            Assert.AreEqual(1, script.Attempts);
            Assert.IsTrue(Play(script, new InputFrame { JumpPressed = true, JumpHeld = true }, 1.5f));
        }

        [Test]
        public void Slide_IsDone_BySlidingWhileRunning()
        {
            var script = NewScript();
            GoTo(script, "slide");
            Play(script, Hold(1f), 0.4f);
            Assert.IsTrue(Play(script, new InputFrame { MoveX = 1f, SlidePressed = true }, 0.3f));
        }

        [Test]
        public void Attack_SpawnsAStunnedGrunt_AndIsDone_ByKillingIt()
        {
            var script = NewScript();
            GoTo(script, "attack");
            Assert.AreEqual(1, script.Sim.Enemies.Count);
            var grunt = script.Sim.Enemies[0];
            Assert.AreEqual(EnemyKind.Grunt, grunt.Kind);
            Assert.Greater(grunt.StunTimer, 0f, "it waits while the card is read");
            Assert.IsTrue(grunt.Dazed);

            bool done = false;
            for (int i = 0; i < 60 * 8 && !done; i++)
                done = Play(script, new InputFrame { MoveX = 1f, AttackPressed = i % 15 == 0 }, Dt);
            Assert.IsTrue(done);
        }

        [Test]
        public void Chain_IsDone_ByKillingThreeGruntsInARow()
        {
            // Arrange
            var script = NewScript();
            GoTo(script, "chain");
            Assert.AreEqual(3, script.Sim.Enemies.Count);

            // Act: run into the group, striking whenever the sword is ready.
            bool done = false;
            for (int i = 0; i < 60 * 20 && !done; i++)
                done = Play(script, new InputFrame { MoveX = 1f, AttackPressed = i % 9 == 0 }, Dt);

            // Assert
            Assert.IsTrue(done, "a chain of 3 within the chain window");
        }

        [Test]
        public void WallJump_IsDone_ByClimbingTheShaft()
        {
            // Arrange
            var script = NewScript();
            GoTo(script, "walljump");
            var p = script.Sim.Player;
            int dir = 1;

            // Act: jump into the wall, then kick off whichever wall is touched, toward the other.
            bool done = false;
            for (int i = 0; i < 60 * 15 && !done; i++)
            {
                var input = new InputFrame { MoveX = dir, JumpHeld = true };
                if (p.Grounded && i % 20 == 0) input.JumpPressed = true;
                else if (!p.Grounded && p.WallDir != 0 && p.Vy < 4f)
                {
                    input.JumpPressed = true;
                    dir = -p.WallDir;
                }
                done = Play(script, input, Dt);
            }

            // Assert
            Assert.IsTrue(done, "reached the top of the shaft with wall jumps");
        }

        [Test]
        public void WallJump_IsNotDone_ByJumpingWithoutTouchingAWall()
        {
            var script = NewScript();
            GoTo(script, "walljump");
            Assert.IsFalse(Play(script, new InputFrame { JumpPressed = true, JumpHeld = true }, 1.5f));
        }

        [Test]
        public void Hook_IsDone_ByHangingOnTheAnchor()
        {
            var script = NewScript();
            GoTo(script, "hook");
            Teleport(script.Sim, 88.5f, 1f);
            Play(script, Hold(1f), 2 * Dt); // face right
            Assert.IsTrue(Play(script, new InputFrame { HookPressed = true, HookHeld = true }, 2f));
        }

        [Test]
        public void Fling_IsDone_ByReleasingASwingInTheAir()
        {
            // Run at the anchor, jump, hook at the apex, swing, let go: some release time must fling
            // past the marker (or over maxSpeed). Fresh run per timing, so the search is deterministic.
            for (int release = 4; release <= 60; release += 2)
            {
                var script = NewScript();
                GoTo(script, "fling");
                var p = script.Sim.Player;
                Teleport(script.Sim, 84.5f, 1f);
                Play(script, Hold(1f), 0.5f);
                var input = new InputFrame { MoveX = 1f, JumpPressed = true, JumpHeld = true };
                for (int guard = 0; (p.Vy > 0f || p.Grounded) && guard < 120; guard++)
                {
                    script.Step(input, Dt);
                    input.JumpPressed = false;
                }
                input.HookPressed = true;
                input.HookHeld = true;
                for (int i = 0; i < release; i++)
                {
                    script.Step(input, Dt);
                    input.HookPressed = false;
                }
                if (!p.Hooked) continue;
                if (Play(script, Hold(1f), 3f)) return;
            }
            Assert.Fail("no release timing flung past the marker");
        }

        [Test]
        public void Tier3_IsDone_AtTier3Speed()
        {
            var script = NewScript();
            GoTo(script, "tier3");
            Assert.IsFalse(Play(script, Hold(1f), 1.5f), "running alone stays below tier 3");
            script.Sim.Player.Vy = 5f;
            script.Sim.Player.Vx = script.Sim.Config.tier3Speed + 2f;
            Assert.IsTrue(Play(script, Hold(1f), Dt));
        }

        [Test]
        public void Reflect_IsDone_ByStrikingAShotBack()
        {
            var script = NewScript();
            GoTo(script, "reflect");
            Play(script, Hold(1f), 2 * Dt);
            var p = script.Sim.Player;
            script.Sim.SpawnProjectile(new Vec2(p.MaxX + 0.8f, p.Center.Y), new Vec2(-script.Sim.Config.projectileSpeed, 0f));
            Assert.IsTrue(Play(script, new InputFrame { AttackPressed = true }, 0.2f));
        }

        [Test]
        public void Orbs_IsDone_ByPickingUpAllThree()
        {
            var script = NewScript();
            GoTo(script, "orbs");
            Assert.AreEqual(3, script.Sim.Orbs.Count);
            Assert.IsTrue(Play(script, Hold(1f), 3f));
        }

        [Test]
        public void Tip_Appears_AfterRepeatedFailures_OrALongTime()
        {
            var script = NewScript();
            GoTo(script, "jump");
            for (int i = 0; i < script.Data.Settings.tipAfterAttempts; i++)
            {
                Assert.IsFalse(script.TipVisible);
                Play(script, new InputFrame { JumpPressed = true }, 1.2f);
            }
            Assert.IsTrue(script.TipVisible);

            var slow = NewScript();
            Play(slow, new InputFrame(), slow.Data.Settings.tipAfterSeconds - 0.5f);
            Assert.IsFalse(slow.TipVisible);
            Play(slow, new InputFrame(), 1f);
            Assert.IsTrue(slow.TipVisible);
        }

        [Test]
        public void LeavingTheStation_RespawnsAtItsCheckpoint_AndCountsAnAttempt()
        {
            var script = NewScript();
            Teleport(script.Sim, 60.5f, 1f); // in the arena, not the runway
            script.Step(new InputFrame(), Dt);
            var cp = script.Current.Station.Checkpoint;
            Assert.AreEqual(1, script.Respawns);
            Assert.AreEqual(1, script.Attempts);
            Assert.AreEqual(cp.X, script.Sim.Player.X, 1e-3f);
            Assert.AreEqual(cp.Y, script.Sim.Player.Y, 1e-3f);
        }

        [Test]
        public void NextStation_StartsAtItsCheckpoint()
        {
            var script = NewScript();
            GoTo(script, "attack");
            Assert.AreEqual(script.Current.Station.CheckpointX + 0.5f, script.Sim.Player.X, 1e-3f);
        }

        [Test]
        public void HoldingRestart_SkipsTheStep_WithoutRestartingTheSim()
        {
            var script = NewScript();
            int run = script.Sim.RunId;
            var hold = new InputFrame { RestartPressed = true, RestartHeld = true };
            Assert.IsFalse(Play(script, hold, script.Data.Settings.skipHoldTime * 0.5f));
            Assert.Greater(script.SkipProgress, 0.3f);
            Assert.IsTrue(Play(script, new InputFrame { RestartHeld = true }, script.Data.Settings.skipHoldTime));
            Assert.IsTrue(script.Skipped);
            Assert.AreEqual(run, script.Sim.RunId);
        }

        [Test]
        public void SkippingEveryStep_CompletesTheTraining()
        {
            var script = NewScript();
            for (int i = 0; i < script.StepCount; i++)
            {
                script.SkipStep();
                Wait(script, script.Data.Settings.successTime + 0.05f);
            }
            Assert.AreEqual(TrainingPhase.Complete, script.Phase);
            Assert.IsNull(script.Current);
            Assert.AreEqual(0, script.Sim.Enemies.Count);
        }

        [Test]
        public void TrainingDone_RoundTrips_AndIsOptionalInOlderFiles()
        {
            var loaded = SettingsSave.Deserialize(SettingsSave.Serialize(new SettingsData { TrainingDone = true }), null, out var status);
            Assert.IsTrue(loaded.TrainingDone);
            Assert.AreEqual(SaveLoadStatus.Ok, status);

            var old = SettingsSave.Deserialize(
                SettingsSave.Serialize(new SettingsData()).Replace(",\"trainingDone\":false", ""), null, out var oldStatus);
            Assert.IsFalse(old.TrainingDone, "an older file has not played the training");
            Assert.AreEqual(SaveLoadStatus.Ok, oldStatus, "a file without the field is not a repair");
            Assert.IsFalse(SettingsSave.Deserialize(null, null, out _).TrainingDone, "first launch: no file");
        }
    }
}
