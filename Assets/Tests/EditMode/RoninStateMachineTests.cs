using NUnit.Framework;
using Redline.Core.Presentation;

namespace Redline.Tests
{
    /// <summary>Baked ronin animation selection: priorities, slash alternation and hold, run-attack variant, hurt hold.</summary>
    public class RoninStateMachineTests
    {
        private const float Dt = 1f / 60f;

        private static RoninStateInput Ground(float speed = 0f) => new RoninStateInput { Grounded = true, Speed = speed };

        private static RoninStateInput Press(RoninStateInput s, int strike)
        {
            s.Attacking = true;
            s.StrikeId = strike;
            return s;
        }

        [Test]
        public void Standing_IsIdle_Moving_IsRun()
        {
            var m = new RoninStateMachine();
            Assert.AreEqual(BakedAnim.Idle, m.Step(Ground(), Dt));
            Assert.AreEqual(BakedAnim.Run, m.Step(Ground(6f), Dt));
        }

        [Test]
        public void Airborne_RisingIsJump_FallingIsFall()
        {
            var m = new RoninStateMachine();
            Assert.AreEqual(BakedAnim.Jump, m.Step(new RoninStateInput { VelocityY = 3f }, Dt));
            Assert.AreEqual(BakedAnim.Fall, m.Step(new RoninStateInput { VelocityY = -3f }, Dt));
        }

        [Test]
        public void Presses_AlternateAttack1AndAttack2_AndRestart()
        {
            var m = new RoninStateMachine();
            Assert.AreEqual(BakedAnim.Attack1, m.Step(Press(Ground(), 1), Dt));
            Assert.IsTrue(m.Restart);
            Assert.AreEqual(BakedAnim.Attack1, m.Step(Press(Ground(), 1), Dt));   // held, same strike: no new press
            Assert.IsFalse(m.Restart);
            Assert.AreEqual(BakedAnim.Attack2, m.Step(Press(Ground(), 2), Dt));
            Assert.IsTrue(m.Restart);
        }

        [Test]
        public void Death_ClearsPendingSlashAndHurt_SoRespawnStartsIdle()
        {
            var m = new RoninStateMachine();
            m.Step(Press(Ground(), 1), Dt);
            m.Step(new RoninStateInput { Grounded = true, Hurt = true }, Dt);
            Assert.AreEqual(BakedAnim.Death, m.Step(new RoninStateInput { Grounded = true, Dead = true }, Dt));
            Assert.AreEqual(BakedAnim.Idle, m.Step(Ground(), Dt));
        }

        [Test]
        public void Slash_HoldsForAttackDuration_ThenReturnsToLocomotion()
        {
            var m = new RoninStateMachine { AttackDuration = 0.3f };
            m.Step(Press(Ground(), 1), Dt);
            int frames = 1;
            while (m.Step(Ground(), Dt) == BakedAnim.Attack1) frames++;
            Assert.AreEqual(BakedAnim.Idle, m.Current);
            Assert.That(frames, Is.InRange(17, 19));   // 0.3 s at 60 Hz
        }

        [Test]
        public void Slash_WhileRunningOnGround_IsRunAttack_InAir_IsJumpAttack()
        {
            var m = new RoninStateMachine();
            Assert.AreEqual(BakedAnim.RunAttack1, m.Step(Press(Ground(6f), 1), Dt));
            Assert.AreEqual(BakedAnim.RunAttack2, m.Step(Press(Ground(6f), 2), Dt));
            Assert.AreEqual(BakedAnim.JumpAttack, m.Step(Press(new RoninStateInput { VelocityY = 2f }, 3), Dt));
        }

        [Test]
        public void Hurt_HoldsForHurtDuration_AndBeatsAttack()
        {
            var m = new RoninStateMachine { HurtDuration = 0.2f };
            var hurt = Ground();
            hurt.Hurt = true;
            Assert.AreEqual(BakedAnim.Hurt, m.Step(hurt, Dt));
            Assert.IsTrue(m.Restart);
            Assert.AreEqual(BakedAnim.Hurt, m.Step(Press(Ground(), 1), Dt));
            for (int i = 0; i < 12; i++) m.Step(Ground(), Dt);
            Assert.AreEqual(BakedAnim.Idle, m.Step(Ground(), Dt));
        }

        [Test]
        public void Death_BeatsEverything()
        {
            var m = new RoninStateMachine();
            var s = Press(Ground(6f), 1);
            s.Hurt = true; s.Dead = true; s.Sliding = true;
            Assert.AreEqual(BakedAnim.Death, m.Step(s, Dt));
        }

        [Test]
        public void Slide_And_Hang_Priorities()
        {
            var m = new RoninStateMachine();
            Assert.AreEqual(BakedAnim.Slide, m.Step(new RoninStateInput { Grounded = true, Sliding = true, Speed = 8f }, Dt));
            Assert.AreEqual(BakedAnim.Hang, m.Step(new RoninStateInput { Hooked = true }, Dt));
        }

        [Test]
        public void Names_MatchSheetAnimations()
        {
            Assert.AreEqual("runattack1", RoninStateMachine.Name(BakedAnim.RunAttack1));
            Assert.AreEqual("jumpattack", RoninStateMachine.Name(BakedAnim.JumpAttack));
        }

        [Test]
        public void Speed_AboveSprintThreshold_IsSprint_WithHysteresis()
        {
            var m = new RoninStateMachine { SprintThreshold = 13f, SprintExitThreshold = 12f };
            Assert.AreEqual(BakedAnim.Run, m.Step(Ground(12.5f), Dt));
            Assert.AreEqual(BakedAnim.Sprint, m.Step(Ground(14f), Dt));
            Assert.AreEqual(BakedAnim.Sprint, m.Step(Ground(12.5f), Dt));   // between exit and enter: stays
            Assert.AreEqual(BakedAnim.Run, m.Step(Ground(11.9f), Dt));
            Assert.AreEqual(BakedAnim.Run, m.Step(Ground(12.5f), Dt));
        }

        [Test]
        public void Landing_FromFastFall_PlaysLandOnce_ThenIdle()
        {
            var m = new RoninStateMachine { LandSpeed = 12f, LandDuration = 0.1f };
            m.Step(new RoninStateInput { VelocityY = -15f }, Dt);
            Assert.AreEqual(BakedAnim.Land, m.Step(Ground(), Dt));
            Assert.IsTrue(m.Restart);
            Assert.AreEqual(BakedAnim.Land, m.Step(Ground(), Dt));
            Assert.IsFalse(m.Restart);
            for (int i = 0; i < 6; i++) m.Step(Ground(), Dt);
            Assert.AreEqual(BakedAnim.Idle, m.Step(Ground(), Dt));
        }

        [Test]
        public void Landing_FromSlowFall_SkipsLand()
        {
            var m = new RoninStateMachine { LandSpeed = 12f };
            m.Step(new RoninStateInput { VelocityY = -11f }, Dt);
            Assert.AreEqual(BakedAnim.Idle, m.Step(Ground(), Dt));
        }

        [Test]
        public void Landing_WhileMovingFasterThanLandMaxRunSpeed_KeepsRunning()
        {
            var m = new RoninStateMachine { LandSpeed = 12f, LandMaxRunSpeed = 9f };
            m.Step(new RoninStateInput { VelocityY = -15f, Speed = 15f }, Dt);
            Assert.AreEqual(BakedAnim.Sprint, m.Step(Ground(15f), Dt));
        }

        [Test]
        public void Landing_SlashOrJump_CancelsLand()
        {
            var m = new RoninStateMachine { LandSpeed = 12f, LandDuration = 0.3f };
            m.Step(new RoninStateInput { VelocityY = -15f }, Dt);
            Assert.AreEqual(BakedAnim.Land, m.Step(Ground(), Dt));
            Assert.AreEqual(BakedAnim.Attack1, m.Step(Press(Ground(), 1), Dt));
            var n = new RoninStateMachine { LandSpeed = 12f, LandDuration = 0.3f };
            n.Step(new RoninStateInput { VelocityY = -15f }, Dt);
            n.Step(Ground(), Dt);
            Assert.AreEqual(BakedAnim.Jump, n.Step(new RoninStateInput { VelocityY = 10f }, Dt));
            Assert.AreEqual(BakedAnim.Idle, n.Step(Ground(), Dt));   // a slow re-landing does not resume the old land
        }

        [Test]
        public void LocomotionRate_MatchesStride_WithoutUpperCap()
        {
            // 2 units per 0.5 s cycle at 1x: 22 u/s needs 5.5x, 4 u/s exactly 1x.
            Assert.AreEqual(5.5f, RoninStateMachine.LocomotionRate(22f, 2f, 0.5f, 0.3f), 1e-4f);
            Assert.AreEqual(1f, RoninStateMachine.LocomotionRate(4f, 2f, 0.5f, 0.3f), 1e-4f);
            Assert.AreEqual(0.3f, RoninStateMachine.LocomotionRate(0.1f, 2f, 0.5f, 0.3f), 1e-4f);   // floor
            Assert.AreEqual(1f, RoninStateMachine.LocomotionRate(9f, 0f, 0.5f, 0.3f), 1e-4f);       // no stride data
        }

        [Test]
        public void Names_SprintAndLand_MatchSheetAnimations()
        {
            Assert.AreEqual("sprint", RoninStateMachine.Name(BakedAnim.Sprint));
            Assert.AreEqual("land", RoninStateMachine.Name(BakedAnim.Land));
        }
    }
}
