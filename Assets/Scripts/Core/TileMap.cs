using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>Contents of one grid cell.</summary>
    public enum Tile : byte
    {
        Empty,
        Solid,
        Spike,
        /// <summary>Shock floor ('E'): passable, hurts like a spike only while the current is live (RedlineSim.ShockLive).</summary>
        Shock
    }

    /// <summary>
    /// Arena grid. Cell (x, y) covers [x, x+1) × [y, y+1); y grows upward.
    /// Text format: row 0 is the TOP of the arena. '#' solid, '^' spike, 'P' player start,
    /// 'S' enemy spawn (guard post), anything else empty. Lines starting with ';' are directives:
    /// ';wrap' makes the level a horizontal ring (leaving one side enters the other);
    /// ';wave grunt=3 brute=1 ...' declares the level's next wave (see <see cref="WaveSpec.Parse"/>);
    /// a level without ';wave' lines uses the config's waves;
    /// ';hint x0 x1 id' declares a tutorial hint zone over the cell columns x0..x1 inclusive
    /// (see <see cref="HintZone"/>); ';training' marks the training level (no waves and no victory,
    /// see <see cref="RedlineSim"/> and <see cref="TrainingScript"/>). Other ';' lines are comments.
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

        /// <summary>The level's tutorial hint zones from ';hint' lines, in file order; empty when it has none.</summary>
        public IReadOnlyList<HintZone> Hints { get; }

        /// <summary>
        /// True for the training level (';training'): the sim spawns no waves and never reaches
        /// Victory; a <see cref="TrainingScript"/> drives it instead. Its ';wave' lines are rejected.
        /// </summary>
        public bool Training { get; }

        /// <summary>';endless': waves never run out; each one is bigger than the last (<see cref="WaveSpec.Endless"/>).</summary>
        public bool Endless { get; private set; }

        /// <summary>';randomspawn': enemies appear at random standing spots (<see cref="StandSpots"/>), not at 'S' cells.</summary>
        public bool RandomSpawn { get; private set; }

        /// <summary>';story': a story-mode level (checkpoints, lever, gates, fire, bridge, lift, placed enemies); null otherwise.</summary>
        public StoryLayout Story { get; private set; }

        /// <summary>Changes a cell at run time (story gates opening, bridge cells crumbling and coming back).</summary>
        public void Set(int x, int y, Tile tile)
        {
            if (y < 0 || y >= Height || x < 0 || x >= Width) return;
            _tiles[y * Width + x] = tile;
        }

        /// <summary>
        /// Bottom-centre of every cell an enemy can stand in: empty, the cell above empty, solid below
        /// (not a spike), in row-major order from the bottom. Used by <see cref="RandomSpawn"/> levels.
        /// </summary>
        public IReadOnlyList<Vec2> StandSpots { get; private set; } = Array.Empty<Vec2>();

        private TileMap(int width, int height, Tile[] tiles, Vec2 start, List<Vec2> spawns, bool wraps, List<WaveSpec> waves,
            List<HintZone> hints, bool training)
        {
            Training = training;
            Hints = hints;
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
            bool training = false;
            bool endless = false, randomSpawn = false, story = false;
            var waves = new List<WaveSpec>();
            var hintLines = new List<(string line, int number)>();
            int lineNumber = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNumber++;
                var line = raw.TrimEnd('\r');
                if (line.StartsWith(";"))
                {
                    var directive = line.Trim();
                    if (directive == ";wrap") wraps = true;
                    else if (directive == ";training") training = true;
                    else if (directive == ";endless") endless = true;
                    else if (directive == ";randomspawn") randomSpawn = true;
                    else if (directive == ";story") story = true;
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
                    else if (directive == ";hint" || directive.StartsWith(";hint ") || directive.StartsWith(";hint\t"))
                        hintLines.Add((directive, lineNumber));
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
            var layout = new StoryLayout();
            var gateCells = new List<(int x, int y)>();
            var liftCells = new List<(int x, int y)>();
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
                        case 'E': t = Tile.Shock; break;
                        case 'P':
                            if (start.HasValue) throw new FormatException("map has more than one 'P'");
                            start = new Vec2(x + 0.5f, y);
                            break;
                        case 'S': spawns.Add(new Vec2(x + 0.5f, y)); break;
                        // Story pieces (see StoryLayout; only used on a ';story' level).
                        case 'C': layout.Checkpoints.Add(new Vec2(x + 0.5f, y)); break;
                        case 'L': layout.Levers.Add(new Vec2(x + 0.5f, y)); break;
                        case 'B': layout.Boss = new Vec2(x + 0.5f, y); break;
                        case 'G': t = Tile.Solid; gateCells.Add((x, y)); break;
                        case '=': t = Tile.Solid; layout.BridgeCells.Add((x, y)); break;
                        case 'F': t = Tile.Solid; layout.FireCells.Add((x, y)); break;
                        case '|': liftCells.Add((x, y)); break;
                        default:
                            var kind = StoryLayout.KindFor(c);
                            if (kind.HasValue) layout.Spawns.Add(new StorySpawn(kind.Value, new Vec2(x + 0.5f, y)));
                            break;
                    }
                    tiles[y * width + x] = t;
                }
            }
            if (!start.HasValue) throw new FormatException("map has no player start 'P'");
            if (training && waves.Count > 0) throw new FormatException("a ';training' level cannot declare ';wave' lines");
            if (story) layout.Finish(gateCells, liftCells);
            if (spawns.Count == 0 && !randomSpawn && !story) throw new FormatException("map has no enemy spawn 'S'");
            if (endless && waves.Count > 0) throw new FormatException("an ';endless' level cannot declare ';wave' lines");
            var hints = new List<HintZone>(hintLines.Count);
            foreach (var (hintLine, number) in hintLines)
            {
                HintZone zone;
                try
                {
                    zone = HintZone.Parse(hintLine, width, wraps);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"line {number}: {ex.Message}", ex);
                }
                foreach (var other in hints)
                    if (other.Id == zone.Id)
                        throw new FormatException($"line {number}: hint id '{zone.Id}' is used twice in this level");
                hints.Add(zone);
            }
            var map = new TileMap(width, height, tiles, start.Value, spawns, wraps, waves, hints, training)
            {
                Endless = endless,
                RandomSpawn = randomSpawn,
                Story = story ? layout : null,
            };
            {
                // Standing spots on every map (';randomspawn' levels and the random-wave mode both spawn there).
                var spots = new List<Vec2>();
                for (int y = 1; y < height - 1; y++)
                    for (int x = 0; x < width; x++)
                        if (map.Get(x, y) == Tile.Empty && map.Get(x, y + 1) == Tile.Empty && map.Get(x, y - 1) == Tile.Solid)
                            spots.Add(new Vec2(x + 0.5f, y));
                if (randomSpawn && spots.Count == 0) throw new FormatException("a ';randomspawn' level has no cell to stand on");
                map.StandSpots = spots;
            }
            return map;
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

        /// <summary>True when the box overlaps the lower half of any shock-floor cell.</summary>
        public bool OverlapsShock(float minX, float minY, float maxX, float maxY)
        {
            for (int y = (int)MathF.Floor(minY); y <= (int)MathF.Floor(maxY); y++)
                for (int x = (int)MathF.Floor(minX); x <= (int)MathF.Floor(maxX); x++)
                    if (Get(x, y) == Tile.Shock && maxX > x && minX < x + 1 && maxY > y && minY < y + 0.5f) return true;
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
