using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Draws the static arena: a gradient backdrop with a faint grid and drifting motes,
    /// bevelled terrain with neon outlines on every exposed face, and glowing spikes.
    /// </summary>
    /// <example>
    /// <code>
    /// var arena = new ArenaRenderer(worldRoot, sim.Map);
    /// arena.Update(Time.deltaTime);   // drifts the motes
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Builds its objects once; Update does not allocate.</remarks>
    public sealed class ArenaRenderer
    {
        private const int MoteCount = 48;
        private const float Margin = 12f;

        private static readonly Color BackdropTop = new Color(0.10f, 0.05f, 0.20f);
        private static readonly Color BackdropBottom = new Color(0.01f, 0.01f, 0.04f);
        private static readonly Color GridColor = new Color(0.35f, 0.45f, 1f, 0.05f);
        private static readonly Color MoteColor = new Color(0.45f, 0.65f, 1f, 0.35f);
        private static readonly Color TileTint = new Color(0.17f, 0.19f, 0.36f);
        private static readonly Color TopEdge = new Color(0.3f, 0.75f, 1f);
        private static readonly Color SideEdge = new Color(0.22f, 0.45f, 0.9f, 0.8f);
        private static readonly Color SpikeColor = new Color(1f, 0.22f, 0.42f);

        private readonly TileMap _map;
        private readonly SpriteRenderer[] _motes = new SpriteRenderer[MoteCount];
        private readonly Vector2[] _moteVelocity = new Vector2[MoteCount];
        private float _time;

        /// <summary>Builds the arena under a parent transform.</summary>
        public ArenaRenderer(Transform parent, TileMap map)
        {
            _map = map;
            var root = new GameObject("Arena").transform;
            root.SetParent(parent, false);
            BuildBackdrop(root);
            BuildTerrain(root);
            BuildMotes(root);
        }

        /// <summary>Drifts the background motes upward, wrapping at the top.</summary>
        public void Update(float dt)
        {
            _time += dt;
            for (int i = 0; i < MoteCount; i++)
            {
                var t = _motes[i].transform;
                var p = t.localPosition;
                p.x += (_moteVelocity[i].x + Mathf.Sin(_time * 0.7f + i) * 0.15f) * dt;
                p.y += _moteVelocity[i].y * dt;
                if (p.y > _map.Height + 1f) p.y = -1f;
                t.localPosition = p;
            }
        }

        private void BuildBackdrop(Transform root)
        {
            float w = _map.Width + Margin * 2f;
            float h = _map.Height + Margin * 2f;
            var top = SpriteFactory.Create("BackdropBase", root, SpriteFactory.Square, BackdropBottom, -12);
            Place(top.transform, _map.Width * 0.5f, _map.Height * 0.5f, w, h);
            var gradient = SpriteFactory.Create("BackdropGradient", root, PixelArt.Gradient, BackdropTop, -11);
            Place(gradient.transform, _map.Width * 0.5f, _map.Height * 0.5f + 2f, w, _map.Height + 4f, PixelArt.Gradient);

            for (int x = 0; x <= _map.Width; x += 2)
            {
                var line = SpriteFactory.Create("GridV", root, SpriteFactory.Square, GridColor, -10);
                Place(line.transform, x, _map.Height * 0.5f, 0.05f, _map.Height);
            }
            for (int y = 0; y <= _map.Height; y += 2)
            {
                var line = SpriteFactory.Create("GridH", root, SpriteFactory.Square, GridColor, -10);
                Place(line.transform, _map.Width * 0.5f, y, _map.Width, 0.05f);
            }
        }

        private void BuildTerrain(Transform root)
        {
            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    var tile = _map.Get(x, y);
                    if (tile == Tile.Solid) BuildSolid(root, x, y);
                    else if (tile == Tile.Spike) BuildSpike(root, x, y);
                }
            }
        }

        private void BuildSolid(Transform root, int x, int y)
        {
            var block = SpriteFactory.Create("Tile", root, PixelArt.Tile, TileTint, 0);
            Place(block.transform, x + 0.5f, y + 0.5f, 1f, 1f, PixelArt.Tile);
            if (Open(x, y + 1)) Edge(root, x + 0.5f, y + 0.95f, 1f, 0.1f, TopEdge, 2);
            if (Open(x, y - 1)) Edge(root, x + 0.5f, y + 0.03f, 1f, 0.06f, SideEdge, 1);
            if (Open(x - 1, y)) Edge(root, x + 0.03f, y + 0.5f, 0.06f, 1f, SideEdge, 1);
            if (Open(x + 1, y)) Edge(root, x + 0.97f, y + 0.5f, 0.06f, 1f, SideEdge, 1);
        }

        private void BuildSpike(Transform root, int x, int y)
        {
            var glowColor = SpikeColor;
            glowColor.a = 0.35f;
            var glow = SpriteFactory.Create("SpikeGlow", root, PixelArt.Glow, glowColor, 1);
            Place(glow.transform, x + 0.5f, y + 0.35f, 1.8f, 1.2f, PixelArt.Glow);
            var spike = SpriteFactory.Create("Spike", root, SpriteFactory.Triangle, SpikeColor, 2);
            Place(spike.transform, x + 0.5f, y + 0.3f, 0.9f, 0.6f);
        }

        private void BuildMotes(Transform root)
        {
            for (int i = 0; i < MoteCount; i++)
            {
                var c = MoteColor;
                c.a *= Random.Range(0.4f, 1f);
                var mote = SpriteFactory.Create("Mote", root, PixelArt.Glow, c, -9);
                float size = Random.Range(0.12f, 0.3f);
                Place(mote.transform, Random.Range(1f, _map.Width - 1f), Random.Range(0f, _map.Height), size, size, PixelArt.Glow);
                _moteVelocity[i] = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.2f, 0.6f));
                _motes[i] = mote;
            }
        }

        private bool Open(int x, int y) =>
            x >= 0 && y >= 0 && x < _map.Width && y < _map.Height && _map.Get(x, y) != Tile.Solid;

        private static void Edge(Transform root, float cx, float cy, float w, float h, Color color, int order)
        {
            var edge = SpriteFactory.Create("Edge", root, SpriteFactory.Square, color, order);
            Place(edge.transform, cx, cy, w, h);
        }

        /// <summary>Positions a unit-sized sprite (or a PixelArt sprite, sized by its bounds) to cover w×h.</summary>
        private static void Place(Transform t, float cx, float cy, float w, float h, Sprite sizedBy = null)
        {
            t.localPosition = new Vector3(cx, cy, 0f);
            var unit = sizedBy != null ? (Vector2)sizedBy.bounds.size : Vector2.one;
            t.localScale = new Vector3(w / unit.x, h / unit.y, 1f);
        }
    }
}
