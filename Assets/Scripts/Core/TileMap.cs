using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>Contents of one grid cell.</summary>
    public enum Tile : byte
    {
        Empty,
        Solid,
        Spike
    }

    /// <summary>
    /// Arena grid. Cell (x, y) covers [x, x+1) × [y, y+1); y grows upward.
    /// Text format: row 0 is the TOP of the arena. '#' solid, '^' spike, 'P' player start,
    /// 'S' enemy spawn (guard post), anything else empty. Lines starting with ';' are directives:
    /// ';wrap' makes the level a horizontal ring (leaving one side enters the other);
    /// ';wave grunt=3 brute=1 ...' declares the level's next wave (see <see cref="WaveSpec.Parse"/>);
    /// a level without ';wave' lines uses the config's waves. Other ';' lines are comments.
    /// Cells above or below the grid are solid; left/right of it are solid unless the level wraps.
    /// </summary>
    public sealed class TileMap
    {
        private readonly Tile[] _tiles;

        public int Width { get; }
        public int Height { get; }

        /// <summary>True when the level is a horizontal ring.</summary>
        public bool WrapsX { get; }

        /// <summary>Bottom-centre of the player start cell.</summary>
        public Vec2 PlayerStart { get; }

        /// <summary>Bottom-centre of each enemy spawn cell, in text order.</summary>
        public IReadOnlyList<Vec2> SpawnPoints { get; }

        /// <summary>The level's own waves from ';wave' lines, in order; empty when it uses the config's.</summary>
        public IReadOnlyList<WaveSpec> Waves { get; }

        private TileMap(int width, int height, Tile[] tiles, Vec2 start, List<Vec2> spawns, bool wraps, List<WaveSpec> waves)
        {
            Waves = waves;
            WrapsX = wraps;
            Width = width;
            Height = height;
            _tiles = tiles;
            PlayerStart = start;
            SpawnPoints = spawns;
        }

        /// <summary>Parses the text format described on the class. Throws FormatException on invalid maps.</summary>
        public static TileMap Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var rows = new List<string>();
            bool wraps = false;
            var waves = new List<WaveSpec>();
            int lineNumber = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNumber++;
                var line = raw.TrimEnd('\r');
                if (line.StartsWith(";"))
                {
                    var directive = line.Trim();
                    if (directive == ";wrap") wraps = true;
                    else if (directive == ";wave" || directive.StartsWith(";wave ") || directive.StartsWith(";wave\t"))
                    {
                        try
                        {
                            waves.Add(WaveSpec.Parse(directive));
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException($"line {lineNumber}: {ex.Message}", ex);
                        }
                    }
                    continue;
                }
                rows.Add(line);
            }
            while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0) rows.RemoveAt(rows.Count - 1);
            while (rows.Count > 0 && rows[0].Trim().Length == 0) rows.RemoveAt(0);
            if (rows.Count == 0) throw new FormatException("map is empty");

            int height = rows.Count;
            int width = 0;
            foreach (var r in rows) width = Math.Max(width, r.Length);

            var tiles = new Tile[width * height];
            Vec2? start = null;
            var spawns = new List<Vec2>();
            for (int row = 0; row < height; row++)
            {
                int y = height - 1 - row;
                string line = rows[row];
                for (int x = 0; x < width; x++)
                {
                    char c = x < line.Length ? line[x] : '.';
                    Tile t = Tile.Empty;
                    switch (c)
                    {
                        case '#': t = Tile.Solid; break;
                        case '^': t = Tile.Spike; break;
                        case 'P':
                            if (start.HasValue) throw new FormatException("map has more than one 'P'");
                            start = new Vec2(x + 0.5f, y);
                            break;
                        case 'S': spawns.Add(new Vec2(x + 0.5f, y)); break;
                    }
                    tiles[y * width + x] = t;
                }
            }
            if (!start.HasValue) throw new FormatException("map has no player start 'P'");
            if (spawns.Count == 0) throw new FormatException("map has no enemy spawn 'S'");
            return new TileMap(width, height, tiles, start.Value, spawns, wraps, waves);
        }

        /// <summary>Tile at a cell; above/below the grid is Solid, and so are the sides unless the level wraps.</summary>
        public Tile Get(int x, int y)
        {
            if (y < 0 || y >= Height) return Tile.Solid;
            if (WrapsX) x = ((x % Width) + Width) % Width;
            else if (x < 0 || x >= Width) return Tile.Solid;
            return _tiles[y * Width + x];
        }

        /// <summary>A world x folded into [0, Width) on a ring; unchanged otherwise.</summary>
        public float WrapX(float x) => WrapsX ? x - Width * MathF.Floor(x / Width) : x;

        /// <summary>Shortest signed horizontal distance from one x to another (around the ring when it wraps).</summary>
        public float Dx(float from, float to)
        {
            float d = to - from;
            return WrapsX ? d - Width * MathF.Round(d / Width) : d;
        }

        /// <summary>The copy of <paramref name="to"/> nearest to <paramref name="from"/> (same point off a ring).</summary>
        public Vec2 Near(Vec2 from, Vec2 to) => new Vec2(from.X + Dx(from.X, to.X), to.Y);

        /// <summary>True when the cell blocks movement.</summary>
        public bool IsSolid(int x, int y) => Get(x, y) == Tile.Solid;

        /// <summary>True when the box [minX,maxX) × [minY,maxY) overlaps any solid cell.</summary>
        public bool OverlapsSolid(float minX, float minY, float maxX, float maxY)
        {
            const float eps = 1e-4f;
            int x0 = (int)MathF.Floor(minX + eps);
            int x1 = (int)MathF.Floor(maxX - eps);
            int y0 = (int)MathF.Floor(minY + eps);
            int y1 = (int)MathF.Floor(maxY - eps);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (IsSolid(x, y)) return true;
            return false;
        }

        /// <summary>True when the box overlaps the hurt zone (lower half) of any spike cell.</summary>
        public bool OverlapsSpike(float minX, float minY, float maxX, float maxY)
        {
            int x0 = (int)MathF.Floor(minX);
            int x1 = (int)MathF.Floor(maxX);
            int y0 = (int)MathF.Floor(minY);
            int y1 = (int)MathF.Floor(maxY);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (Get(x, y) != Tile.Spike) continue;
                    // Spike hurt zone: [x, x+1) × [y, y+0.5)
                    if (maxX > x && minX < x + 1 && maxY > y && minY < y + 0.5f) return true;
                }
            return false;
        }

        /// <summary>
        /// Marches a ray from origin along a unit direction. Returns true and the hit point
        /// when a solid cell is met within maxDistance.
        /// </summary>
        public bool Raycast(Vec2 origin, Vec2 direction, float maxDistance, out Vec2 hit)
        {
            const float step = 0.05f;
            for (float d = 0f; d <= maxDistance; d += step)
            {
                var p = origin + direction * d;
                if (IsSolid((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y)))
                {
                    hit = p;
                    return true;
                }
            }
            hit = default;
            return false;
        }
    }
}
