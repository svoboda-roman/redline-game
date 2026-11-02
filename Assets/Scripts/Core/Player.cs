namespace Redline.Core
{
    /// <summary>The player's body and per-ability timers. Rules live in RedlineSim.</summary>
    public sealed class Player : Body
    {
        public int Hp;
        /// <summary>+1 facing right, -1 facing left.</summary>
        public int Facing = 1;

        public float InvulnTimer;
        public float CoyoteTimer;
        public float JumpBufferTimer;
        /// <summary>Time left on a buffered attack press.</summary>
        public float AttackBufferTimer;
        /// <summary>Time left on a buffered slide press.</summary>
        public float SlideBufferTimer;
        /// <summary>Time left on a buffered hook press (fires only while hook is still held).</summary>
        public float HookBufferTimer;
        public bool JumpCutAvailable;
        public float WallJumpLockTimer;
        public float WallImpactSpeed;
        public float WallImpactTimer;

        public bool Sliding;
        public float SlideTimer;
        public float SlideCooldownTimer;

        public bool Hooked;
        public Vec2 HookAnchor;
        public float HookLength;
        public float HookCooldownTimer;

        /// <summary>Remaining active time of the current strike (0 when not striking).</summary>
        public float AttackTimer;
        public float AttackCooldownTimer;
        /// <summary>Increments for every strike so each enemy is hit at most once per strike.</summary>
        public int StrikeId;

        /// <summary>Kills in the current chain (0 when no chain is running).</summary>
        public int Chain;
        /// <summary>Time left for the next kill to extend the chain.</summary>
        public float ChainTimer;

        public bool Attacking => AttackTimer > 0f;
    }
}
