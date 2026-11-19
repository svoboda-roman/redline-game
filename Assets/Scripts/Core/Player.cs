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

        /// <summary>Wall-jump grace after leaving a wall in the air, and the side that wall was on.</summary>
        public float WallCoyoteTimer;
        public int LastWallDir;

        public bool Sliding;
        public float SlideTimer;
        /// <summary>Full length of the current slide (SlideTimer's start value).</summary>
        public float SlideLength;
        /// <summary>Speed tier (1-3) the current slide started at: sets its height, boost and length. 0 when not sliding.</summary>
        public int SlideTier;
        public float SlideCooldownTimer;
        /// <summary>True once this airtime's air slide is spent; cleared on landing, wall jump and hook attach.</summary>
        public bool AirSlideUsed;
        /// <summary>True once this airtime's double jump is spent; cleared on landing, wall jump and hook attach.</summary>
        public bool AirJumpUsed;

        /// <summary>Capacity of <see cref="SlideHits"/> (more enemies than that cannot be tracked per slide).</summary>
        public const int MaxSlideHits = 32;
        /// <summary>Enemies the current slide has already hit (first <see cref="SlideHitCount"/> entries): one hit per enemy per slide.</summary>
        public readonly Enemy[] SlideHits = new Enemy[MaxSlideHits];
        /// <summary>How many entries of <see cref="SlideHits"/> are valid.</summary>
        public int SlideHitCount;

        public bool Hooked;
        public Vec2 HookAnchor;
        public float HookLength;
        public float HookCooldownTimer;
        /// <summary>Seconds on the current rope (a release only flings after a real swing).</summary>
        public float HookTime;
        /// <summary>Current rope reel speed (units/s; + reeling in, - letting out). Eases toward the input.</summary>
        public float HookReelSpeed;

        /// <summary>Time left on the speed orb buff (0 when inactive). Read by the HUD.</summary>
        public float SpeedBuffTimer;
        /// <summary>Time left on the hook orb buff: no hook cooldown, extra range (0 when inactive). Read by the HUD.</summary>
        public float HookBuffTimer;

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
