using System;

namespace Redline.Core
{
    /// <summary>
    /// An axis-aligned box that moves through a TileMap. X is the horizontal centre,
    /// Y is the bottom edge (feet). Velocity is in tiles per second.
    /// </summary>
    public class Body
    {
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Width;
        public float Height;

        /// <summary>Standing on solid ground after the last move.</summary>
        public bool Grounded { get; private set; }

        /// <summary>-1 when touching a wall on the left, +1 on the right, 0 otherwise.</summary>
        public int WallDir { get; private set; }

        public float MinX => X - Width * 0.5f;
        public float MaxX => X + Width * 0.5f;
        public float MaxY => Y + Height;

        /// <summary>Centre of the box.</summary>
        public Vec2 Center => new Vec2(X, Y + Height * 0.5f);

        /// <summary>True when this box overlaps another box.</summary>
        public bool Overlaps(float minX, float minY, float maxX, float maxY) =>
            MaxX > minX && MinX < maxX && MaxY > minY && Y < maxY;

        /// <summary>True when this box overlaps another body (measured around the ring when the level wraps).</summary>
        public bool Overlaps(Body other, TileMap map)
        {
            float dx = map.Dx(X, other.X);
            return MathF.Abs(dx) < (Width + other.Width) * 0.5f && MaxY > other.Y && Y < other.MaxY;
        }

        /// <summary>
        /// Moves by velocity × dt with axis-separated collision against solid tiles.
        /// Velocity on a blocked axis is set to 0. Returns the horizontal speed lost to a wall
        /// this move (0 when not blocked).
        /// </summary>
        public float Move(TileMap map, float dt)
        {
            float dx = Vx * dt;
            float dy = Vy * dt;
            int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) / 0.4f));
            float sx = dx / steps;
            float sy = dy / steps;
            float wallImpact = 0f;
            for (int i = 0; i < steps; i++)
            {
                if (sx != 0f)
                {
                    X += sx;
                    if (map.OverlapsSolid(MinX, Y, MaxX, MaxY))
                    {
                        if (sx > 0f) X = MathF.Floor(MaxX - 1e-4f) - Width * 0.5f - 1e-3f;
                        else X = MathF.Floor(MinX + 1e-4f) + 1f + Width * 0.5f + 1e-3f;
                        wallImpact = MathF.Max(wallImpact, MathF.Abs(Vx));
                        Vx = 0f;
                        sx = 0f;
                    }
                }
                if (sy != 0f)
                {
                    Y += sy;
                    if (map.OverlapsSolid(MinX, Y, MaxX, MaxY))
                    {
                        if (sy > 0f) Y = MathF.Floor(MaxY - 1e-4f) - Height - 1e-3f;
                        else Y = MathF.Floor(Y + 1e-4f) + 1f;
                        Vy = 0f;
                        sy = 0f;
                    }
                }
            }
            if (map.WrapsX) X = map.WrapX(X);
            RefreshContacts(map);
            return wallImpact;
        }

        /// <summary>Recomputes Grounded and WallDir from the current position.</summary>
        public void RefreshContacts(TileMap map)
        {
            const float probe = 0.02f;
            Grounded = map.OverlapsSolid(MinX, Y - probe, MaxX, Y + 0.01f);
            if (map.OverlapsSolid(MinX - probe, Y + 0.05f, MinX, MaxY - 0.05f)) WallDir = -1;
            else if (map.OverlapsSolid(MaxX, Y + 0.05f, MaxX + probe, MaxY - 0.05f)) WallDir = 1;
            else WallDir = 0;
        }
    }
}
