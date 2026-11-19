using System;

namespace Redline.Core
{
    /// <summary>2D vector used by the engine-free simulation.</summary>
    public readonly struct Vec2
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Euclidean length.</summary>
        public float Length => MathF.Sqrt(X * X + Y * Y);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public override string ToString() => $"({X:0.00}, {Y:0.00})";
    }

    /// <summary>One fixed step of player input. "Pressed" flags are edges, "Held" flags are levels.</summary>
    public struct InputFrame
    {
        /// <summary>Horizontal input in [-1, 1].</summary>
        public float MoveX;
        /// <summary>Vertical input in [-1, 1] (up positive): reels the hook rope in (up) or out (down) while hanging.</summary>
        public float MoveY;
        /// <summary>
        /// The jump press came only from an "up" key (W / Up arrow), not Space or the pad's jump button. While
        /// hanging, that press reels the rope in instead of letting go.
        /// </summary>
        public bool JumpPressedByUp;
        public bool JumpPressed;
        public bool JumpHeld;
        public bool AttackPressed;
        public bool SlidePressed;
        public bool HookPressed;
        public bool HookHeld;
        public bool RestartPressed;
        /// <summary>Restart is held (a level). The sim ignores it; the training script uses it to skip a step.</summary>
        public bool RestartHeld;
    }

    /// <summary>High-level state of a run.</summary>
    public enum SimState
    {
        /// <summary>Short pause before the next wave spawns. The player can move.</summary>
        WaveBreak,
        /// <summary>A wave is alive.</summary>
        Fighting,
        /// <summary>All waves cleared.</summary>
        Victory,
        /// <summary>Player HP reached 0.</summary>
        GameOver,
        /// <summary>
        /// The training level (<see cref="TileMap.Training"/>): no waves, no victory and no game over
        /// (HP that reaches 0 refills). It lasts until the game leaves the level.
        /// </summary>
        Training
    }

    /// <summary>Kinds of feedback events the simulation reports to the presentation layer.</summary>
    public enum SimEventType
    {
        Jump,
        WallJump,
        Slide,
        HookAttach,
        HookRelease,
        Attack,
        Hit,
        Kill,
        Clang,
        PlayerHurt,
        WaveStart,
        Victory,
        GameOver,
        /// <summary>A brute starts telegraphing a charge.</summary>
        BruteWindup,
        /// <summary>A charging brute hit a wall and is dazed.</summary>
        BruteCrash,
        /// <summary>A drone starts telegraphing a dive.</summary>
        DroneWindup,
        /// <summary>An orb dropped (Detail = (int)OrbKind; Position = its centre).</summary>
        OrbSpawn,
        /// <summary>The player picked up an orb (Detail = (int)OrbKind).</summary>
        OrbPickup,
        /// <summary>An orb ran out of time and vanished (Detail = (int)OrbKind).</summary>
        OrbExpire,
        /// <summary>A shooter starts telegraphing a shot (Position = the shooter's centre).</summary>
        ShooterWindup,
        /// <summary>A shooter fired (Position = where the projectile starts).</summary>
        Shoot,
        /// <summary>A sword strike reflected a projectile (Position = the projectile).</summary>
        ProjectileReflect,
        /// <summary>A projectile ended: hit a wall, a target or ran out of time (Position = where).</summary>
        ProjectileHit,
        /// <summary>A shield stopped a strike or a reflected projectile (sent with Clang for strikes).</summary>
        ShieldBlock,
        /// <summary>A kamikaze stopped and lit its fuse (the telegraph before it explodes).</summary>
        KamikazeFuse,
        /// <summary>A kamikaze exploded (Position = blast centre; radius kamikazeBlastRadius).</summary>
        Explode,
        /// <summary>A summoner starts its cast (the telegraph before a drone appears).</summary>
        SummonerCast,
        /// <summary>A summoner summoned a drone (Position = the new drone's centre).</summary>
        Summon
    }

    /// <summary>A feedback event emitted during one simulation step.</summary>
    public readonly struct SimEvent
    {
        public readonly SimEventType Type;
        public readonly Vec2 Position;
        /// <summary>Player speed tier when the event happened (1-3).</summary>
        public readonly int Tier;
        /// <summary>
        /// Event-specific payload: (int)<see cref="OrbKind"/> for orb events, (int)<see cref="EnemyKind"/>
        /// of the dead enemy for Kill, 0 otherwise.
        /// </summary>
        public readonly int Detail;

        public SimEvent(SimEventType type, Vec2 position, int tier) : this(type, position, tier, 0)
        {
        }

        public SimEvent(SimEventType type, Vec2 position, int tier, int detail)
        {
            Type = type;
            Position = position;
            Tier = tier;
            Detail = detail;
        }
    }
}
