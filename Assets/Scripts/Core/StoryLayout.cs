using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>A vertical lift: a platform <see cref="MinX"/>..<see cref="MaxX"/> wide whose top runs between two heights.</summary>
    public readonly struct LiftTrack
    {
        public readonly float MinX, MaxX, Bottom, Top;
        public LiftTrack(float minX, float maxX, float bottom, float top) { MinX = minX; MaxX = maxX; Bottom = bottom; Top = top; }
    }

    /// <summary>An enemy placed in a story level (spawns when the player comes near, stays dead once killed).</summary>
    public readonly struct StorySpawn
    {
        public readonly EnemyKind Kind;
        public readonly Vec2 Position;
        public StorySpawn(EnemyKind kind, Vec2 position) { Kind = kind; Position = position; }
    }

    /// <summary>
    /// The story-mode pieces of a ';story' level (see <see cref="TileMap"/>): checkpoints ('C', in x order; the first one
    /// gives the hook), the lever ('L'), gates ('G' runs; gate 0 is the one nearest the lever, the others open when the
    /// lift reaches its top), fire traps ('F', wall cells that flare on and off), the collapsing bridge ('=' cells),
    /// lifts ('|' track cells), placed enemies (g grunt, b brute, d crow, s shooter, h shield, k kamikaze, u summoner)
    /// and the boss room marker ('B'). Gate, bridge and fire cells are solid in the tile grid; the sim opens gates and
    /// crumbles bridge cells by changing the tiles (<see cref="TileMap.Set"/>).
    /// </summary>
    public sealed class StoryLayout
    {
        public List<Vec2> Checkpoints { get; } = new List<Vec2>();
        /// <summary>Every lever ('L'), in x order.</summary>
        public List<Vec2> Levers { get; } = new List<Vec2>();

        /// <summary>The first lever, or null (kept for callers that only know one).</summary>
        public Vec2? Lever => Levers.Count > 0 ? Levers[0] : (Vec2?)null;

        /// <summary>For each lever, the index of the gate it opens (the nearest gate no earlier lever took; -1: none).</summary>
        public List<int> LeverGate { get; } = new List<int>();

        /// <summary>True when some lever opens gate <paramref name="gate"/> (otherwise a lift reaching its top opens it).</summary>
        public bool GateHasLever(int gate) => LeverGate.Contains(gate);
        public List<List<(int x, int y)>> Gates { get; } = new List<List<(int x, int y)>>();
        public List<(int x, int y)> BridgeCells { get; } = new List<(int x, int y)>();
        public List<(int x, int y)> FireCells { get; } = new List<(int x, int y)>();
        public List<LiftTrack> Lifts { get; } = new List<LiftTrack>();
        public List<StorySpawn> Spawns { get; } = new List<StorySpawn>();
        public Vec2? Boss { get; internal set; }

        /// <summary>Cells whose tile the sim changes at run time (gates, bridge): the static level mesh leaves them out.</summary>
        public HashSet<(int x, int y)> DynamicCells { get; } = new HashSet<(int x, int y)>();

        /// <summary>Enemy kind for a placed-enemy letter, or null.</summary>
        public static EnemyKind? KindFor(char c)
        {
            switch (c)
            {
                case 'g': return EnemyKind.Grunt;
                case 'b': return EnemyKind.Brute;
                case 'd': return EnemyKind.Drone;
                case 's': return EnemyKind.Shooter;
                case 'h': return EnemyKind.Shield;
                case 'k': return EnemyKind.Kamikaze;
                case 'u': return EnemyKind.Summoner;
                default: return null;
            }
        }

        /// <summary>Groups the raw marker cells into gates and lifts and orders things (called once by the parser).</summary>
        internal void Finish(List<(int x, int y)> gateCells, List<(int x, int y)> liftCells)
        {
            Checkpoints.Sort((a, b) => a.X.CompareTo(b.X));
            foreach (var group in Cluster(gateCells)) Gates.Add(group);
            Levers.Sort((a, b) => a.X.CompareTo(b.X));
            var taken = new HashSet<int>();
            foreach (var l in Levers)
            {
                int best = -1; float bestD = float.MaxValue;
                for (int g = 0; g < Gates.Count; g++)
                {
                    if (taken.Contains(g)) continue;
                    foreach (var c in Gates[g])
                    {
                        float d = Math.Abs(c.x + 0.5f - l.X) + Math.Abs(c.y + 0.5f - l.Y);
                        if (d < bestD) { bestD = d; best = g; }
                    }
                }
                if (best >= 0) taken.Add(best);
                LeverGate.Add(best);
            }
            foreach (var group in Cluster(liftCells, 2))   // a dotted track is still one lift
            {
                int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
                foreach (var c in group) { x0 = Math.Min(x0, c.x); x1 = Math.Max(x1, c.x); y0 = Math.Min(y0, c.y); y1 = Math.Max(y1, c.y); }
                Lifts.Add(new LiftTrack(x0, x1 + 1, y0, y1 + 1));
            }
            foreach (var g in Gates) foreach (var c in g) DynamicCells.Add(c);
            foreach (var c in BridgeCells) DynamicCells.Add(c);
        }

        private static List<List<(int x, int y)>> Cluster(List<(int x, int y)> cells, int reach = 1)
        {
            var left = new HashSet<(int x, int y)>(cells);
            var groups = new List<List<(int x, int y)>>();
            foreach (var start in cells)
            {
                if (!left.Remove(start)) continue;
                var group = new List<(int x, int y)> { start };
                for (int i = 0; i < group.Count; i++)
                {
                    var c = group[i];
                    for (int dx = -reach; dx <= reach; dx++)
                        for (int dy = -reach; dy <= reach; dy++)
                            if (left.Remove((c.x + dx, c.y + dy))) group.Add((c.x + dx, c.y + dy));
                }
                groups.Add(group);
            }
            return groups;
        }
    }
}
