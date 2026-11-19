using System.Collections.Generic;
using Redline.Core;
using Redline.Game.Atmosphere;
using UnityEngine;
using C = Redline.Game.Atmosphere.AtmosphereConfig;

namespace Redline.Game
{
    /// <summary>
    /// Draws the level as a rainy night city: a parallax skyline with fog (<see cref="CitySkyline"/>),
    /// screen-covering rain (<see cref="RainField"/>), wet industrial-metal terrain (lit, normal-mapped,
    /// varied by position) with a cold sheen on exposed tops, glowing spikes, and procedurally placed
    /// sodium lamps and red signals (<see cref="LampField"/>) under one dim cool global 2D light.
    /// On a ring level three copies of the terrain (and its lamps) sit side by side and follow the
    /// camera a whole level at a time, so the seam never shows; the backdrop and rain follow the
    /// camera continuously. Rooftop props (<see cref="PropField"/>) dress every copy the same way.
    /// </summary>
    /// <example>
    /// <code>
    /// var arena = new ArenaRenderer(worldRoot, sim.Map);
    /// arena.Update(Time.deltaTime, camera.transform.position);
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Builds its objects once; Update does not allocate.</remarks>
    public sealed class ArenaRenderer
    {
        private const float SpikeGlowWidth = 1.8f, SpikeGlowHeight = 1.2f;
        private const float SpikeWidth = 0.9f, SpikeHeight = 0.6f;

        private readonly TileMap _map;
        private readonly Transform _terrain;
        private readonly CitySkyline _city;
        private readonly RainField _rain;
        private readonly List<LampField> _lamps = new List<LampField>(3);
        private readonly List<PropField> _propFields = new List<PropField>(3);
        private readonly List<LampField.Spot> _spots;
        private readonly List<PropField.Placement> _props;

        /// <summary>Planned lamp and signal positions in map space (the same in every ring copy).</summary>
        public IReadOnlyList<LampField.Spot> LampSpots => _spots;

        /// <summary>Planned rooftop props in map space (the same in every ring copy).</summary>
        public IReadOnlyList<PropField.Placement> PropPlan => _props;

        /// <summary>2D lights this arena created: the global light plus every lamp, signal and lit sign in every copy.</summary>
        public int LightCount { get; }

        /// <summary>Builds the arena under a parent transform.</summary>
        public ArenaRenderer(Transform parent, TileMap map)
        {
            _map = map;
            var root = new GameObject("Arena").transform;
            root.SetParent(parent, false);
            Lights2D.Global(root, C.GlobalLight, C.GlobalIntensity);

            var backdrop = new GameObject("Backdrop").transform;
            backdrop.SetParent(root, false);
            _city = new CitySkyline(backdrop, map.Height);
            _spots = LampField.Plan(map);
            _props = PropField.Plan(map, _spots);
            _rain = new RainField(backdrop, map, LampField.ConeApexes(_spots));

            _terrain = new GameObject("Terrain").transform;
            _terrain.SetParent(root, false);
            if (map.WrapsX)
            {
                for (int copy = -1; copy <= 1; copy++) BuildCopy(_terrain, copy * map.Width);
            }
            else
            {
                BuildCopy(_terrain, 0f);
            }
            int lights = 1;
            for (int i = 0; i < _lamps.Count; i++) lights += _lamps[i].LightCount;
            for (int i = 0; i < _propFields.Count; i++) lights += _propFields[i].LightCount;
            LightCount = lights;
        }

        private void BuildCopy(Transform parent, float offsetX)
        {
            var copy = new GameObject("Copy").transform;
            copy.SetParent(parent, false);
            copy.localPosition = new Vector3(offsetX, 0f, 0f);
            BuildTerrain(copy);
            _lamps.Add(new LampField(copy, _spots, _map));
            _propFields.Add(new PropField(copy, _props));
        }

        /// <summary>Moves the backdrop and rain with the camera, keeps the ring copies under it, blinks signals.</summary>
        public void Update(float dt, Vector3 camera)
        {
            if (_map.WrapsX) _terrain.localPosition = new Vector3(_map.Width * Mathf.Floor(camera.x / _map.Width), 0f, 0f);
            _city.Update(camera, dt);
            _rain.Update(camera, dt);
            for (int i = 0; i < _lamps.Count; i++) _lamps[i].Update(dt);
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
            bool top = Open(x, y + 1), bottom = Open(x, y - 1), left = Open(x - 1, y), right = Open(x + 1, y);
            var sprite = AtmoArt.Tile(AtmoArt.TileMask(top, bottom, left, right), AtmoArt.TileVariant(x, y));
            var block = SpriteFactory.CreateLit("Tile", root, sprite, C.TileTint, 0);
            block.transform.localPosition = new Vector3(x + 0.5f, y + 0.5f, 0f);
            if (!top) return;
            // Cold wet sheen: a one-pixel unlit line just under the top lip.
            var sheen = SpriteFactory.Create("WetSheen", root, SpriteFactory.Square, C.WetSheen, 2);
            sheen.transform.localPosition = new Vector3(x + 0.5f, y + 1f - C.WetSheenThickness * 0.5f, 0f);
            sheen.transform.localScale = new Vector3(1f, C.WetSheenThickness, 1f);
        }

        private static void BuildSpike(Transform root, int x, int y)
        {
            var glowColor = C.SpikeColor;
            glowColor.a = C.SpikeGlowAlpha;
            var glow = SpriteFactory.Create("SpikeGlow", root, PixelArt.Glow, glowColor, 1);
            Place(glow.transform, x + 0.5f, y + 0.35f, SpikeGlowWidth, SpikeGlowHeight, PixelArt.Glow);
            var spike = SpriteFactory.Create("Spike", root, SpriteFactory.Triangle, C.SpikeColor, 2);
            Place(spike.transform, x + 0.5f, y + 0.3f, SpikeWidth, SpikeHeight);
        }

        /// <summary>A face is exposed when the neighbour is not solid. Above/below the grid is never exposed; the ring wraps.</summary>
        private bool Open(int x, int y)
        {
            if (y < 0 || y >= _map.Height) return false;
            if (!_map.WrapsX && (x < 0 || x >= _map.Width)) return true;
            return _map.Get(x, y) != Tile.Solid;
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
