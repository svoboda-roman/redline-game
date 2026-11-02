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
        public bool JumpPressed;
        public bool JumpHeld;
        public bool AttackPressed;
        public bool SlidePressed;
        public bool HookPressed;
        public bool HookHeld;
        public bool RestartPressed;
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
        GameOver
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
        GameOver
    }

    /// <summary>A feedback event emitted during one simulation step.</summary>
    public readonly struct SimEvent
    {
        public readonly SimEventType Type;
        public readonly Vec2 Position;
        /// <summary>Player speed tier when the event happened (1-3).</summary>
        public readonly int Tier;

        public SimEvent(SimEventType type, Vec2 position, int tier)
        {
            Type = type;
            Position = position;
            Tier = tier;
        }
    }
}
