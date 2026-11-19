using System;
using System.Collections.Generic;
using Redline.Core;
using UnityEngine;
using C = Redline.Game.Atmosphere.AtmosphereConfig;

namespace Redline.Game.Atmosphere
{
    /// <summary>
    /// Dresses a level with the rooftop prop atlas (<c>Assets/Resources/Art/Props</c>, built by
    /// <c>tools/art/props/make_props.sh</c>): props stand on exposed tile tops behind the terrain lip and
    /// the actors at mid-dark value, puddles lie on floor tops, cable bundles hang under overhangs, and a
    /// few large dark low-opacity occluders sit in front at the very bottom of the screen, below the floor
    /// top, so they never cover feet or spikes. Props never collide. Placement is deterministic per
    /// position; footprints keep clear of spikes and lamp posts and are spaced around the ring with
    /// <see cref="TileMap.Dx"/>. Lit signs earn a small 2D light, at most
    /// <see cref="AtmosphereConfig.PropLightMax"/> per level.
    /// </summary>
    /// <example>
    /// <code>
    /// var plan  = PropField.Plan(map, lampSpots);   // once per level
    /// var props = new PropField(copyRoot, plan);   // once per terrain copy
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Builds once; has no per-frame work.</remarks>
    public sealed class PropField
    {
        /// <summary>How a placed prop is drawn.</summary>
        public enum Kind : byte
        {
            /// <summary>Standing on a floor top, behind the terrain and the actors.</summary>
            Back,
            /// <summary>Flat puddle on a floor top, in front of the terrain, behind the actors.</summary>
            Puddle,
            /// <summary>Hanging from the underside of a solid tile.</summary>
            Hang,
            /// <summary>Large dark low-opacity silhouette in front, below the floor top.</summary>
            Occluder
        }

        /// <summary>One planned prop in map space.</summary>
        public struct Placement
        {
            /// <summary>Index into <see cref="Props"/>.</summary>
            public int Prop;
            /// <summary>Pivot position (map space): ground contact, hook, or occluder base.</summary>
            public Vector2 At;
            /// <summary>Draw mode.</summary>
            public Kind Kind;
            /// <summary>True when this placement carries one of the level's prop lights.</summary>
            public bool Lit;
        }

        /// <summary>One prop of the atlas.</summary>
        public sealed class PropInfo
        {
            /// <summary>Atlas name (e.g. <c>water_tank</c>).</summary>
            public string Name;
            /// <summary>Layer hint from the json: background-decor, foreground-occluder or hanging.</summary>
            public string Layer;
            /// <summary>Lit colour sprite with the <c>_NormalMap</c> secondary texture.</summary>
            public Sprite Color;
            /// <summary>Unlit emissive overlay, or null when the prop has no lit parts.</summary>
            public Sprite Emissive;
            /// <summary>Sprite size in world units.</summary>
            public Vector2 Size;
            /// <summary>Emissive texel count.</summary>
            public int EmissiveTexels;
            /// <summary>Emissive centroid relative to the pivot (world units).</summary>
            public Vector2 EmissiveCentroid;
            /// <summary>Average emissive colour.</summary>
            public Color EmissiveColor;
        }

        private static PropInfo[] _props;
        private static bool _loaded;

        /// <summary>Lights this field created.</summary>
        public int LightCount { get; }

        /// <summary>Props built in this copy.</summary>
        public int Count { get; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _props = null;
            _loaded = false;
        }

        /// <summary>The prop atlas, loaded on first use; empty when the atlas is missing.</summary>
        public static PropInfo[] Props
        {
            get
            {
                if (!_loaded) Load();
                return _props;
            }
        }

        // ------------------------------------------------------------------ planning

        /// <summary>Plans every prop of a level. Deterministic; allocates (call once per level).</summary>
        public static List<Placement> Plan(TileMap map, IReadOnlyList<LampField.Spot> lamps)
        {
            var plan = new List<Placement>();
            var props = Props;
            if (props.Length == 0) return plan;
            var floor = new List<int>();
            var occluders = new List<int>();
            int puddle = -1, hang = -1;
            for (int i = 0; i < props.Length; i++)
            {
                if (props[i].Name == "puddle") puddle = i;
                else if (props[i].Layer == "hanging") hang = i;
                else floor.Add(i);
                if (Array.IndexOf(C.OccluderProps, props[i].Name) >= 0) occluders.Add(i);
            }

            // Hero pieces (water tank, mast, stair hut, billboard, container): one every HeroGap tiles of
            // floor; small dressing (crates, barrels, vents, AC units...) everywhere else.
            var heroes = new List<int>();
            var small = new List<int>();
            foreach (int i in floor)
                (Array.IndexOf(C.HeroProps, props[i].Name) >= 0 ? heroes : small).Add(i);
            if (small.Count == 0) small = floor;
            if (heroes.Count > 0)
            {
                for (int y = 0; y < map.Height - 1; y++)
                {
                    int next = (int)(AtmoArt.Hash(y, 9, 61) % (uint)C.HeroGap);
                    for (int x = 0; x < map.Width; x++)
                    {
                        if (x < next || !ExposedTop(map, x, y)) continue;
                        uint h = AtmoArt.Hash(x, y, 41);
                        for (int attempt = 0; attempt < heroes.Count; attempt++)
                        {
                            if (!TryFloor(map, lamps, plan, heroes[(int)((h + (uint)attempt) % (uint)heroes.Count)], x, y, Kind.Back)) continue;
                            next = x + C.HeroGap;
                            break;
                        }
                    }
                }
            }

            // Lit props next to every lamp post (the sodium light picks them out): one each side if it fits.
            for (int li = 0; li < lamps.Count; li++)
            {
                var s = lamps[li];
                if (s.Kind != LampField.Kind.Post) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    bool done = false;
                    foreach (int off in C.LampPropOffsets)
                    {
                        int x = ((s.X + side * off) % map.Width + map.Width) % map.Width;
                        if (s.Y + 1 >= map.Height || !ExposedTop(map, x, s.Y)) continue;
                        for (int attempt = 0; attempt < 4 && !done; attempt++)
                            done = TryFloor(map, lamps, plan, small[(int)(AtmoArt.Hash(x, s.Y, 51 + attempt) % (uint)small.Count)], x, s.Y, Kind.Back);
                        if (done) break;
                    }
                }
            }

            for (int y = 0; y < map.Height - 1; y++)
            {
                int next = (int)(AtmoArt.Hash(y, 3, 71) % (uint)C.PropGapMax);
                for (int x = 0; x < map.Width; x++)
                {
                    if (x < next || !ExposedTop(map, x, y)) continue;
                    uint h = AtmoArt.Hash(x, y, 29);
                    bool placed = false;
                    if (puddle >= 0 && (h % 1000u) < C.PuddleChance * 1000f)
                        placed = TryFloor(map, lamps, plan, puddle, x, y, Kind.Puddle);
                    for (int attempt = 0; attempt < 5 && !placed; attempt++)
                    {
                        int pick = small[(int)(AtmoArt.Hash(x, y, 31 + attempt) % (uint)small.Count)];
                        placed = TryFloor(map, lamps, plan, pick, x, y, Kind.Back);
                    }
                    if (!placed) continue;
                    float w = props[plan[plan.Count - 1].Prop].Size.x;
                    next = x + Mathf.CeilToInt(w) + C.PropGapMin + (int)(h / 1000u % (uint)(C.PropGapMax - C.PropGapMin + 1));
                }
            }

            if (hang >= 0)
            {
                for (int y = 3; y < map.Height; y++)
                {
                    int gap = y == map.Height - 1 ? C.HangGap * 2 : C.HangGap;
                    int next = (int)(AtmoArt.Hash(y, 5, 73) % (uint)gap);
                    for (int x = 0; x < map.Width; x++)
                    {
                        if (x < next || map.Get(x, y) != Tile.Solid) continue;
                        if (map.Get(x, y - 1) != Tile.Empty || map.Get(x, y - 2) != Tile.Empty || map.Get(x, y - 3) != Tile.Empty) continue;
                        if (NearLamp(map, lamps, x, y, false)) continue;
                        var at = new Vector2(x + 0.5f, y);
                        if (Crowded(map, plan, props, at, props[hang].Size.x, Kind.Hang)) continue;
                        plan.Add(new Placement { Prop = hang, At = at, Kind = Kind.Hang });
                        next = x + gap;
                    }
                }
            }

            if (occluders.Count > 0)
            {
                int next = (int)(AtmoArt.Hash(0, 7, 79) % (uint)C.OccluderGap);
                for (int x = 0; x < map.Width; x++)
                {
                    if (x < next || !ExposedTop(map, x, 0)) continue;
                    int pick = occluders[(int)(AtmoArt.Hash(x, 0, 83) % (uint)occluders.Count)];
                    float hgt = props[pick].Size.y * C.OccluderScale;
                    plan.Add(new Placement { Prop = pick, At = new Vector2(x + 0.5f, C.OccluderTop - hgt), Kind = Kind.Occluder });
                    next = x + C.OccluderGap;
                }
            }

            // A few lit signs earn a light: the brightest kinds first, spaced around the ring.
            int lights = 0;
            for (int i = 0; i < plan.Count && lights < C.PropLightMax; i++)
            {
                var p = plan[i];
                if (p.Kind != Kind.Back || props[p.Prop].Name != C.FlickerProp || props[p.Prop].Emissive == null) continue;
                p.Lit = true;
                plan[i] = p;
                lights++;
            }
            for (int pass = 0; pass < 2 && lights < C.PropLightMax; pass++)
            {
                for (int i = 0; i < plan.Count && lights < C.PropLightMax; i++)
                {
                    var p = plan[i];
                    if (p.Kind != Kind.Back || props[p.Prop].EmissiveTexels < C.PropLightMinTexels) continue;
                    if (pass == 0 && props[p.Prop].EmissiveTexels < C.PropLightMinTexels * 4) continue;
                    bool spaced = true;
                    for (int j = 0; j < plan.Count; j++)
                        if (plan[j].Lit && Mathf.Abs(map.Dx(plan[j].At.x, p.At.x)) < 12f) spaced = false;
                    if (!spaced || p.Lit) continue;
                    p.Lit = true;
                    plan[i] = p;
                    lights++;
                }
            }
            return plan;
        }

        private static bool ExposedTop(TileMap map, int x, int y) =>
            map.Get(x, y) == Tile.Solid && map.Get(x, y + 1) == Tile.Empty;

        private static bool TryFloor(TileMap map, IReadOnlyList<LampField.Spot> lamps, List<Placement> plan, int prop,
            int x, int y, Kind kind)
        {
            var info = Props[prop];
            // Footprint: the visible base is narrower than the padded sprite.
            float half = Mathf.Max(0.3f, info.Size.x * 0.5f - 0.15f);
            float cx = x + 0.5f;
            int x0 = Mathf.FloorToInt(cx - half), x1 = Mathf.FloorToInt(cx + half - 0.001f);
            int clear = kind == Kind.Puddle ? 1 : Mathf.CeilToInt(info.Size.y - 0.1f);
            for (int fx = x0; fx <= x1; fx++)
            {
                if (!ExposedTop(map, fx, y)) return false;
                for (int k = 1; k <= clear; k++)
                    if (map.Get(fx, y + k) != Tile.Empty) return false;
            }
            for (int fx = x0 - C.PropSpikeClear; fx <= x1 + C.PropSpikeClear; fx++)
                if (map.Get(fx, y + 1) == Tile.Spike) return false;
            if (kind != Kind.Puddle && NearLamp(map, lamps, x, y, true)) return false;
            var at = new Vector2(cx, y + 1f);
            if (Crowded(map, plan, Props, at, info.Size.x, kind)) return false;
            plan.Add(new Placement { Prop = prop, At = at, Kind = kind });
            return true;
        }

        private static bool NearLamp(TileMap map, IReadOnlyList<LampField.Spot> lamps, int x, int y, bool posts)
        {
            for (int i = 0; i < lamps.Count; i++)
            {
                var s = lamps[i];
                if (s.Y != y || (s.Kind == LampField.Kind.Post) != posts) continue;
                if (Mathf.Abs(map.Dx(s.X, x)) <= C.PropLampClear) return true;
            }
            return false;
        }

        private static bool Crowded(TileMap map, List<Placement> plan, PropInfo[] props, Vector2 at, float width, Kind kind)
        {
            for (int i = 0; i < plan.Count; i++)
            {
                var p = plan[i];
                if (Mathf.Abs(p.At.y - at.y) > 0.01f || (p.Kind == Kind.Puddle) != (kind == Kind.Puddle)) continue;
                float need = (props[p.Prop].Size.x + width) * 0.5f;
                if (Mathf.Abs(map.Dx(p.At.x, at.x)) < need) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ building

        /// <summary>Builds the planned props under <paramref name="root"/> (map space).</summary>
        public PropField(Transform root, List<Placement> plan)
        {
            var props = Props;
            int lights = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                var p = plan[i];
                var info = props[p.Prop];
                SpriteRenderer sr;
                switch (p.Kind)
                {
                    case Kind.Occluder:
                        sr = SpriteFactory.Create("PropOccluder", root, info.Color, C.OccluderColor, C.OccluderOrder);
                        if (AtmoArt.Hash((int)p.At.x, 1, 89) % 2u == 0u) sr.flipX = true;
                        sr.transform.localScale = new Vector3(C.OccluderScale, C.OccluderScale, 1f);
                        sr.transform.localPosition = p.At;
                        continue;
                    case Kind.Puddle:
                        sr = SpriteFactory.CreateLit("Puddle", root, info.Color, C.PuddleTint, C.PuddleOrder);
                        break;
                    default:
                        sr = SpriteFactory.CreateLit("Prop_" + info.Name, root, info.Color, C.PropTint, C.PropOrder);
                        break;
                }
                sr.transform.localPosition = p.At;
                SpriteRenderer glowSr = null;
                if (info.Emissive != null)
                {
                    glowSr = SpriteFactory.Create("PropGlow", sr.transform, info.Emissive, C.PropEmissive, sr.sortingOrder + 1);
                    glowSr.transform.localPosition = Vector3.zero;
                }
                bool sign = info.Name == C.FlickerProp;
                UnityEngine.Rendering.Universal.Light2D light = null;
                if (p.Lit)
                {
                    light = Lights2D.Point(root, "PropLight", p.At + info.EmissiveCentroid, info.EmissiveColor,
                        sign ? C.SignLightIntensity : C.PropLightIntensity, sign ? C.SignLightRadius : C.PropLightRadius,
                        0.1f, 360f, 360f, 0.7f, C.LampNormalDistance);
                    lights++;
                }
                if (sign && glowSr != null)
                    sr.gameObject.AddComponent<SignFlicker>().Init(glowSr, light, p.At.x);
            }
            LightCount = lights;
            Count = plan.Count;
        }

        // ------------------------------------------------------------------ atlas

        private static void Load()
        {
            _loaded = true;
            _props = Array.Empty<PropInfo>();
            var json = Resources.Load<TextAsset>(C.PropAtlasPath);
            string dir = C.PropAtlasPath.Substring(0, C.PropAtlasPath.LastIndexOf('/') + 1);
            var color = Resources.Load<Texture2D>(dir + "props_color");
            var normal = Resources.Load<Texture2D>(dir + "props_normal");
            var emissive = Resources.Load<Texture2D>(dir + "props_emissive");
            if (json == null || color == null)
            {
                Debug.LogWarning("PropField: prop atlas missing at Resources/" + C.PropAtlasPath + " (run tools/art/props/make_props.sh)");
                return;
            }
            var data = JsonUtility.FromJson<AtlasJson>(json.text);
            var secondary = normal != null
                ? new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normal } }
                : Array.Empty<SecondarySpriteTexture>();
            _props = new PropInfo[data.props.Length];
            for (int i = 0; i < data.props.Length; i++)
            {
                var p = data.props[i];
                var rect = new Rect(p.rect.x, p.rect.y, p.rect.w, p.rect.h);
                var pivot = new Vector2(p.pivot.x, p.pivot.y);
                var info = new PropInfo
                {
                    Name = p.name,
                    Layer = p.layer,
                    Color = Sprite.Create(color, rect, pivot, data.ppu, 0, SpriteMeshType.FullRect, Vector4.zero, false, secondary),
                    Size = new Vector2(p.rect.w / data.ppu, p.rect.h / data.ppu),
                    EmissiveTexels = p.emissive
                };
                if (p.emissive > 0 && emissive != null)
                {
                    info.Emissive = Sprite.Create(emissive, rect, pivot, data.ppu, 0, SpriteMeshType.FullRect);
                    info.EmissiveCentroid = new Vector2(p.emissive_centroid.x, p.emissive_centroid.y);
                    if (!ColorUtility.TryParseHtmlString(p.emissive_color, out info.EmissiveColor)) info.EmissiveColor = Color.white;
                }
                _props[i] = info;
            }
        }

        // JsonUtility DTOs: field names match tools/art/props/pack_props.py's json.
        [Serializable] private sealed class AtlasJson { public float ppu; public PropJson[] props; }
        [Serializable] private sealed class PropJson { public string name; public string layer; public string anchor; public RectJson rect; public Vec2Json pivot; public int emissive; public Vec2Json emissive_centroid; public string emissive_color; }
        [Serializable] private sealed class RectJson { public int x; public int y; public int w; public int h; }
        [Serializable] private sealed class Vec2Json { public float x; public float y; }
    }

    /// <summary>
    /// Dim neon flicker for the billboard: a slow hum plus rare short dropouts, applied to the unlit
    /// glow's alpha and the sign's spill light. Per-frame cost: one sine and two property writes.
    /// </summary>
    internal sealed class SignFlicker : MonoBehaviour
    {
        private SpriteRenderer _glow;
        private UnityEngine.Rendering.Universal.Light2D _light;
        private float _alpha, _intensity, _seed;

        /// <summary>Binds the glow sprite and optional light; <paramref name="seed"/> de-syncs signs.</summary>
        public void Init(SpriteRenderer glow, UnityEngine.Rendering.Universal.Light2D light, float seed)
        {
            _glow = glow;
            _light = light;
            _alpha = glow.color.a;
            _intensity = light != null ? light.intensity : 0f;
            _seed = seed * 0.137f;
        }

        private void Update()
        {
            float t = Time.time + _seed;
            float k = 0.88f + 0.08f * Mathf.Sin(t * 2.3f) + 0.04f * Mathf.Sin(t * 17f);
            float drop = Mathf.Repeat(t * 0.31f, 1f);
            if (drop < 0.025f || (drop > 0.04f && drop < 0.05f)) k *= C.SignDropout;
            var c = _glow.color;
            c.a = _alpha * k;
            _glow.color = c;
            if (_light != null) _light.intensity = _intensity * k;
        }
    }
}
