using System.Collections.Generic;
using Redline.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using C = Redline.Game.Atmosphere.AtmosphereConfig;

namespace Redline.Game.Atmosphere
{
    /// <summary>
    /// Places sodium lamps and red signal lights procedurally along a level and builds them:
    /// a lit fixture sprite (lamp post on a floor, or a fixture under an overhang), an unlit halo
    /// and hot core, and a URP 2D spot light pointing down with Fast normal mapping. At most one
    /// fixture per <see cref="AtmosphereConfig.LampCellW"/>×<see cref="AtmosphereConfig.LampCellH"/>
    /// cell and none closer than <see cref="AtmosphereConfig.LampMinSpacing"/> (around the ring
    /// too); every <see cref="AtmosphereConfig.SignalEvery"/>th one is a blinking red signal.
    /// </summary>
    /// <example>
    /// <code>
    /// var spots = LampField.Plan(map);                  // once per level
    /// var lamps = new LampField(copyRoot, spots);       // once per terrain copy
    /// lamps.Update(Time.deltaTime);                     // blinks the signals
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Update does not allocate.</remarks>
    public sealed class LampField
    {
        /// <summary>Kind of fixture.</summary>
        public enum Kind
        {
            /// <summary>Lamp post standing on a floor tile.</summary>
            Post,
            /// <summary>Fixture hanging under a solid tile.</summary>
            Ceiling,
            /// <summary>Red signal box hanging under a solid tile.</summary>
            Signal
        }

        /// <summary>A planned fixture: the cell it attaches to and its kind.</summary>
        public struct Spot
        {
            /// <summary>Tile X of the attachment (the floor tile, or the tile it hangs under).</summary>
            public int X;
            /// <summary>Tile Y of the attachment.</summary>
            public int Y;
            /// <summary>Fixture kind.</summary>
            public Kind Kind;

            /// <summary>Where a figure standing beside a post stands (feet, map space); posts only.</summary>
            public Vector2 StandPoint => new Vector2(X + 0.5f, Y + 1f);
        }

        private readonly List<SpriteRenderer> _reflections = new List<SpriteRenderer>(32);
        private readonly List<float> _reflectionAlpha = new List<float>(32);
        private readonly List<float> _reflectionLength = new List<float>(32);
        private readonly Light2D[] _signals;
        private readonly SpriteRenderer[] _signalHalos;
        private float _time;

        /// <summary>Total lights this field created (lamps + signals).</summary>
        public int LightCount { get; }

        /// <summary>Plans fixture positions for a map. Deterministic; allocates (call once per level).</summary>
        public static List<Spot> Plan(TileMap map)
        {
            var spots = new List<Spot>();
            int cellsX = Mathf.Max(1, Mathf.CeilToInt(map.Width / (float)C.LampCellW));
            int cellsY = Mathf.Max(1, Mathf.CeilToInt(map.Height / (float)C.LampCellH));
            int placed = 0;
            for (int cy = 0; cy < cellsY; cy++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    float centreX = (cx + 0.5f) * C.LampCellW, centreY = (cy + 0.5f) * C.LampCellH;
                    float best = float.MaxValue;
                    var choice = default(Spot);
                    bool found = false;
                    for (int y = cy * C.LampCellH; y < Mathf.Min(map.Height, (cy + 1) * C.LampCellH); y++)
                    {
                        for (int x = cx * C.LampCellW; x < Mathf.Min(map.Width, (cx + 1) * C.LampCellW); x++)
                        {
                            if (map.Get(x, y) != Tile.Solid) continue;
                            float score = Mathf.Abs(x + 0.5f - centreX) + Mathf.Abs(y - centreY) * 0.5f
                                          + (AtmoArt.Hash(x, y, 5) % 100u) * 0.01f;
                            Kind kind;
                            if (IsPostSpot(map, x, y))
                            {
                                kind = Kind.Post;
                                // Prefer platform edges: a post at the lip lights the drop and the floor.
                                if (map.Get(x - 1, y + 1) == Tile.Empty && (map.Get(x - 1, y) != Tile.Solid || map.Get(x + 1, y) != Tile.Solid))
                                    score -= 3f;
                            }
                            else if (IsCeilingSpot(map, x, y))
                            {
                                kind = Kind.Ceiling;
                                // The level's top border is a ceiling everywhere: keep it as a last resort.
                                if (y == map.Height - 1) score += 6f;
                                else score -= 1.5f; // real overhangs are the interesting ones
                            }
                            else continue;
                            if (score >= best || TooClose(map, spots, x, y)) continue;
                            best = score;
                            choice = new Spot { X = x, Y = y, Kind = kind };
                            found = true;
                        }
                    }
                    if (!found) continue;
                    placed++;
                    if (choice.Kind == Kind.Ceiling && placed % C.SignalEvery == 0) choice.Kind = Kind.Signal;
                    spots.Add(choice);
                }
            }
            return spots;
        }

        private static bool IsPostSpot(TileMap map, int x, int y) =>
            y < map.Height - 4 && map.Get(x, y + 1) == Tile.Empty && map.Get(x, y + 2) == Tile.Empty
            && map.Get(x, y + 3) == Tile.Empty && map.Get(x + 1, y + 3) == Tile.Empty;

        private static bool IsCeilingSpot(TileMap map, int x, int y) =>
            y >= 3 && map.Get(x, y - 1) == Tile.Empty && map.Get(x, y - 2) == Tile.Empty && map.Get(x, y - 3) == Tile.Empty;

        private static bool TooClose(TileMap map, List<Spot> spots, int x, int y)
        {
            for (int i = 0; i < spots.Count; i++)
            {
                float dx = map.Dx(spots[i].X, x), dy = spots[i].Y - y;
                if (dx * dx + dy * dy < C.LampMinSpacing * C.LampMinSpacing) return true;
            }
            return false;
        }

        /// <summary>Where a lamp's light (and cone apex) sits, in map space. Signals: the lens.</summary>
        public static Vector2 Head(Spot spot)
        {
            switch (spot.Kind)
            {
                case Kind.Post: return new Vector2(spot.X + 0.3f, spot.Y + 1f) + AtmoArt.LampPostHead;
                case Kind.Ceiling: return new Vector2(spot.X + 0.5f, spot.Y) + AtmoArt.CeilingLampHead;
                default: return new Vector2(spot.X + 0.5f, spot.Y) + AtmoArt.SignalLens;
            }
        }

        /// <summary>Cone apexes (map space) of every sodium lamp in <paramref name="spots"/>, for the rain to glint in.</summary>
        public static Vector2[] ConeApexes(List<Spot> spots)
        {
            int n = 0;
            for (int i = 0; i < spots.Count; i++) if (spots[i].Kind != Kind.Signal) n++;
            var apexes = new Vector2[n];
            n = 0;
            for (int i = 0; i < spots.Count; i++) if (spots[i].Kind != Kind.Signal) apexes[n++] = Head(spots[i]);
            return apexes;
        }

        /// <summary>Builds the fixtures and lights of <paramref name="spots"/> under <paramref name="root"/> (map space).</summary>
        public LampField(Transform root, List<Spot> spots, TileMap map)
        {
            int signals = 0;
            for (int i = 0; i < spots.Count; i++) if (spots[i].Kind == Kind.Signal) signals++;
            _signals = new Light2D[signals];
            _signalHalos = new SpriteRenderer[signals];
            int s = 0;
            var down = Quaternion.Euler(0f, 0f, 180f);
            for (int i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                Vector2 head;
                switch (spot.Kind)
                {
                    case Kind.Post:
                    {
                        var foot = new Vector2(spot.X + 0.3f, spot.Y + 1f);
                        var post = SpriteFactory.CreateLit("LampPost", root, AtmoArt.LampPost, C.FixtureTint, 1);
                        post.transform.localPosition = foot;
                        head = foot + AtmoArt.LampPostHead;
                        break;
                    }
                    case Kind.Ceiling:
                    {
                        var top = new Vector2(spot.X + 0.5f, spot.Y);
                        var fixture = SpriteFactory.CreateLit("CeilingLamp", root, AtmoArt.CeilingLamp, C.FixtureTint, 1);
                        fixture.transform.localPosition = top;
                        head = top + AtmoArt.CeilingLampHead;
                        break;
                    }
                    default:
                    {
                        var top = new Vector2(spot.X + 0.5f, spot.Y);
                        var box = SpriteFactory.CreateLit("SignalBox", root, AtmoArt.SignalBox, C.FixtureTint, 1);
                        box.transform.localPosition = top;
                        var lens = top + AtmoArt.SignalLens;
                        var halo = C.SignalLight;
                        halo.a = 0.5f;
                        _signalHalos[s] = Glow(root, lens, halo, 1.4f, 3);
                        Glow(root, lens, new Color(1f, 0.4f, 0.35f, 0.95f), 0.22f, 3);
                        _signals[s] = Lights2D.Point(root, "SignalLight", lens, C.SignalLight, C.SignalMax,
                            C.SignalRadius, 0.1f, 360f, 360f, 0.7f, C.LampNormalDistance);
                        var red = C.SignalLight;
                        red.a = C.FloorReflection.a * C.SignalReflectionAlpha;
                        AddReflection(root, map, lens, red, C.SignalReflectionLength);
                        s++;
                        continue;
                    }
                }
                Glow(root, head, C.LampHalo, C.LampHaloSize, 3);
                Glow(root, head + new Vector2(0f, 0.02f), C.LampCore, 0.32f, 3);
                var light = Lights2D.Point(root, "LampLight", head, C.SodiumLight, C.LampIntensity, C.LampRadius,
                    C.LampInnerRadius, C.LampOuterAngle, C.LampInnerAngle, C.LampFalloff, C.LampNormalDistance);
                light.transform.localRotation = down;
                // Volumetric cone: apex at the glass, opening downward.
                var cone = SpriteFactory.Create("LampCone", root, AtmoArt.LightCone, C.LampCone, 3);
                cone.transform.localPosition = head;
                float coneW = 2f * C.LampConeLength * Mathf.Tan(C.LampConeHalfAngle * Mathf.Deg2Rad);
                cone.transform.localScale = new Vector3(coneW, C.LampConeLength, 1f);
                AddReflection(root, map, head, C.FloorReflection, 1f);
            }
            LightCount = spots.Count;
        }

        /// <summary>A warm broken reflection smear on the first floor top below <paramref name="head"/>.</summary>
        private void AddReflection(Transform root, TileMap map, Vector2 head, Color color, float length)
        {
            int x = Mathf.FloorToInt(head.x);
            for (int y = Mathf.FloorToInt(head.y) - 1; y >= 0; y--)
            {
                if (map.Get(x, y) != Tile.Solid) continue;
                if (map.Get(x, y + 1) == Tile.Solid) return;
                var r = SpriteFactory.Create("FloorReflection", root, AtmoArt.Reflection, color, 3);
                r.transform.localPosition = new Vector3(head.x, y + 1f, 0f);
                r.transform.localScale = new Vector3(C.ReflectionWidth, C.ReflectionDepth * length, 1f);
                _reflections.Add(r);
                _reflectionAlpha.Add(color.a);
                _reflectionLength.Add(length);
                return;
            }
        }

        private static SpriteRenderer Glow(Transform root, Vector2 at, Color color, float size, int order)
        {
            var g = SpriteFactory.Create("LampHalo", root, PixelArt.Glow, color, order);
            g.transform.localPosition = at;
            g.transform.localScale = new Vector3(size, size, 1f);
            return g;
        }

        /// <summary>Blinks the red signals.</summary>
        public void Update(float dt)
        {
            _time += dt;
            for (int i = 0; i < _reflections.Count; i++)
            {
                // Rain on the puddle: a gentle, irregular shimmer.
                var c = _reflections[i].color;
                float baseA = _reflectionAlpha[i];
                c.a = baseA * (0.7f + 0.2f * Mathf.Sin(_time * 7.3f + i * 2.1f) + 0.1f * Mathf.Sin(_time * 17.9f + i));
                _reflections[i].color = c;
                var t = _reflections[i].transform;
                t.localScale = new Vector3(C.ReflectionWidth * (0.9f + 0.1f * Mathf.Sin(_time * 5.1f + i)), C.ReflectionDepth * _reflectionLength[i], 1f);
            }
            for (int i = 0; i < _signals.Length; i++)
            {
                float phase = Mathf.Repeat(_time / C.SignalPeriod + i * 0.29f, 1f);
                float on = phase < 0.5f ? 1f : 0f;
                _signals[i].intensity = Mathf.Lerp(C.SignalMin, C.SignalMax, on);
                var c = C.SignalLight;
                c.a = 0.15f + 0.4f * on;
                _signalHalos[i].color = c;
            }
        }
    }
}
