using UnityEngine;

namespace Redline.Game.Atmosphere
{
    /// <summary>
    /// Every tuning value of the night-city atmosphere pass in one place: palette, parallax layers,
    /// rain, fog, lamps and lights. Colours are sRGB (the project renders in linear space; Unity
    /// converts sprite and light colours). Change a value here and every system that draws the
    /// world picks it up on the next level load.
    /// </summary>
    /// <example>
    /// <code>
    /// float speed = AtmosphereConfig.RainSpeed[2];          // near rain layer
    /// var sodium = AtmosphereConfig.SodiumLight;            // lamp colour
    /// </code>
    /// </example>
    /// <remarks>Read-only data; safe from any thread, but only the main thread uses it.</remarks>
    public static class AtmosphereConfig
    {
        // ------------------------------------------------------------------ view
        /// <summary>Half the view height the systems cover (RedlineGame's camera size is 8).</summary>
        public const float ViewHalfHeight = 8.5f;
        /// <summary>Half the view width covered: 8 × 2.4 supports up to a 21:9 window.</summary>
        public const float ViewHalfWidth = 20f;

        // ------------------------------------------------------------------ sky and city palette
        /// <summary>Sky at the top of the view: deep slate.</summary>
        public static readonly Color SkyTop = new Color(0.008f, 0.012f, 0.03f);
        /// <summary>Sky at the horizon: dim warm-grey city-glow haze.</summary>
        public static readonly Color SkyHorizon = new Color(0.15f, 0.13f, 0.12f);
        /// <summary>Ground haze the city layers fade into at their base (also their fill below).</summary>
        public static readonly Color GroundHaze = new Color(0.11f, 0.1f, 0.1f);
        /// <summary>Building silhouette colour per layer, far (0) to near (3). Far is nearly the sky.</summary>
        public static readonly Color[] BuildingColor =
        {
            new Color(0.105f, 0.105f, 0.115f),
            new Color(0.05f, 0.055f, 0.068f),
            new Color(0.03f, 0.035f, 0.045f),
            new Color(0.01f, 0.012f, 0.018f)
        };
        /// <summary>How much of each layer's base fades into the ground haze (far: all of it; near: little).</summary>
        public static readonly float[] LayerBaseHaze = { 1f, 0.7f, 0.3f, 0.1f };
        /// <summary>Solid foundation drawn under the level (y below 0): nothing of the city shows there.</summary>
        public static readonly Color Foundation = new Color(0.008f, 0.01f, 0.014f);
        /// <summary>Sorting order of the foundation: in front of every city layer and far rain, behind terrain.</summary>
        public const int FoundationOrder = -20;
        /// <summary>How much each layer's window and sign colours are pulled toward the haze.</summary>
        public static readonly float[] LayerFog = { 0.62f, 0.45f, 0.26f, 0.12f };
        /// <summary>Warm lit window.</summary>
        public static readonly Color WindowWarm = new Color(0.98f, 0.66f, 0.32f);
        /// <summary>Rare cool fluorescent window.</summary>
        public static readonly Color WindowCool = new Color(0.62f, 0.72f, 0.78f);
        /// <summary>Dim red sign bars on the nearer layers (blank, no text).</summary>
        public static readonly Color SignRed = new Color(0.78f, 0.16f, 0.12f);
        /// <summary>Aviation beacon on distant towers.</summary>
        public static readonly Color BeaconRed = new Color(1f, 0.12f, 0.08f, 0.9f);
        /// <summary>Hover-traffic lights crossing layer 1.</summary>
        public static readonly Color TrafficLight = new Color(1f, 0.85f, 0.65f, 0.75f);

        // ------------------------------------------------------------------ parallax layers (index 0 = far)
        /// <summary>Number of building layers.</summary>
        public const int CityLayers = 4;
        /// <summary>Texture pixels per world unit per layer (far layers are coarser).</summary>
        public static readonly int[] LayerPixelsPerUnit = { 6, 8, 10, 12 };
        /// <summary>Horizontal repeat period in world units; must be ≥ 2 × ViewHalfWidth so two copies cover the view.</summary>
        public static readonly float[] LayerPeriod = { 64f, 56f, 50f, 44f };
        /// <summary>Tallest building per layer in world units (towers may reach it).</summary>
        public static readonly float[] LayerHeight = { 17f, 13f, 10f, 8f };
        /// <summary>Fraction of camera X motion a layer follows: 1 = glued to the camera (infinitely far).</summary>
        public static readonly float[] LayerFollowX = { 0.94f, 0.86f, 0.74f, 0.6f };
        /// <summary>Fraction of camera Y motion a layer follows.</summary>
        public static readonly float[] LayerFollowY = { 0.96f, 0.9f, 0.8f, 0.7f };
        /// <summary>Layer base height relative to the level's vertical middle (world units).</summary>
        public static readonly float[] LayerBaseY = { -5.5f, -7.5f, -9f, -10.5f };
        /// <summary>Probability that a window cell is lit, per layer.</summary>
        public static readonly float[] WindowLitChance = { 0.05f, 0.06f, 0.075f, 0.085f };
        /// <summary>Distant towers (with beacons) per far layer (0 and 1).</summary>
        public static readonly int[] TowerCount = { 3, 2, 0, 0 };
        /// <summary>Sorting order of each layer (sky is SkyOrder).</summary>
        public static readonly int[] LayerOrder = { -40, -36, -32, -28 };
        /// <summary>Sorting order of the sky.</summary>
        public const int SkyOrder = -45;
        /// <summary>Beacon blink period in seconds.</summary>
        public const float BeaconPeriod = 1.6f;
        /// <summary>Hover-traffic lights alive at once on layer 1.</summary>
        public const int TrafficCount = 5;
        /// <summary>Hover-traffic speed range (world units per second, in layer space).</summary>
        public const float TrafficSpeedMin = 1.2f, TrafficSpeedMax = 3.2f;

        // ------------------------------------------------------------------ fog
        /// <summary>Fog colour (cool grey) for bands and haze.</summary>
        public static readonly Color FogColor = new Color(0.42f, 0.48f, 0.54f);
        /// <summary>Haze bands drawn between city layers: alpha per band (after layer index 0, 1, 2, 3).</summary>
        public static readonly float[] HazeAlpha = { 0.17f, 0.12f, 0.08f, 0.05f };
        /// <summary>Haze band drift speed (world units per second).</summary>
        public static readonly float[] HazeDrift = { 0.15f, 0.22f, 0.3f, 0.4f };
        /// <summary>Ground fog in front of the terrain: alpha and drift.</summary>
        public const float GroundFogAlpha = 0.12f, GroundFogDrift = 0.35f;
        /// <summary>Ground fog band height in world units (centred on the level floor).</summary>
        public const float GroundFogHeight = 5f;
        /// <summary>Fog texture repeat period (world units).</summary>
        public const float FogPeriod = 48f;
        /// <summary>Sorting order of the ground fog (in front of terrain and props, behind actors).</summary>
        public const int GroundFogOrder = 5;

        // ------------------------------------------------------------------ rain (index 0 = far)
        /// <summary>Streaks per rain layer. Total = the particle budget (220).</summary>
        public static readonly int[] RainCount = { 100, 75, 45 };
        /// <summary>Fall speed per layer (world units per second).</summary>
        public static readonly float[] RainSpeed = { 14f, 19f, 26f };
        /// <summary>Streak length per layer (world units).</summary>
        public static readonly float[] RainLength = { 0.35f, 0.6f, 1.05f };
        /// <summary>Streak width per layer (world units).</summary>
        public static readonly float[] RainWidth = { 0.03f, 0.04f, 0.055f };
        /// <summary>Streak alpha per layer.</summary>
        public static readonly float[] RainAlpha = { 0.16f, 0.22f, 0.3f };
        /// <summary>How strongly each layer counter-scrolls against camera motion (parallax).</summary>
        public static readonly float[] RainParallax = { 0.15f, 0.0f, -0.25f };
        /// <summary>Sorting order per rain layer: behind terrain, in front of terrain, in front of actors.</summary>
        public static readonly int[] RainOrder = { -24, 4, 14 };
        /// <summary>Slant from vertical in degrees (wind from the left).</summary>
        public const float RainSlant = 12f;
        /// <summary>Rain colour (cool, desaturated).</summary>
        public static readonly Color RainColor = new Color(0.68f, 0.76f, 0.84f);
        /// <summary>Splash flickers alive at once (pooled).</summary>
        public const int SplashPool = 28;
        /// <summary>Splashes spawned per second near the camera.</summary>
        public const float SplashRate = 55f;
        /// <summary>Splash lifetime in seconds.</summary>
        public const float SplashLife = 0.14f;
        /// <summary>Splash alpha.</summary>
        public const float SplashAlpha = 0.55f;

        // ------------------------------------------------------------------ terrain
        /// <summary>Tile texture resolution (pixels per world unit / per tile): the characters' texel density.</summary>
        public const int TilePixels = 40;
        /// <summary>Pixels per unit of the hand-drawn lamp/signal fixture sprites (authored at 24).</summary>
        public const int FixturePixels = 24;
        /// <summary>Stencil paint on tiles (worn off-white) and hazard paint (yellow, black).</summary>
        public static readonly Color StencilPaint = new Color(0.5f, 0.5f, 0.46f);
        /// <summary>Worn hazard yellow and its black stripe.</summary>
        public static readonly Color HazardYellow = new Color(0.47f, 0.38f, 0.14f), HazardBlack = new Color(0.07f, 0.07f, 0.065f);
        /// <summary>Rust stain and oil stain colours.</summary>
        public static readonly Color Rust = new Color(0.3f, 0.17f, 0.1f), Oil = new Color(0.06f, 0.065f, 0.075f);
        /// <summary>Tile tint (multiplied into the generated metal; lights do the rest).</summary>
        public static readonly Color TileTint = new Color(1f, 1f, 1f);
        /// <summary>Cold specular wet-sheen line on exposed tops (unlit).</summary>
        public static readonly Color WetSheen = new Color(0.72f, 0.84f, 0.95f, 0.55f);
        /// <summary>Sheen line thickness in world units (one tile pixel).</summary>
        public const float WetSheenThickness = 1f / TilePixels;
        /// <summary>Spike colour (hazard: stays saturated) and its glow alpha.</summary>
        public static readonly Color SpikeColor = new Color(0.95f, 0.14f, 0.2f);
        /// <summary>Spike glow alpha.</summary>
        public const float SpikeGlowAlpha = 0.32f;

        // ------------------------------------------------------------------ lighting
        /// <summary>Global light: dim cool night.</summary>
        public static readonly Color GlobalLight = new Color(0.55f, 0.64f, 0.78f);
        /// <summary>Global light intensity.</summary>
        public const float GlobalIntensity = 0.36f;
        /// <summary>Sodium lamp colour.</summary>
        public static readonly Color SodiumLight = new Color(1f, 0.6f, 0.26f);
        /// <summary>Lamp light intensity, outer radius, inner radius.</summary>
        public const float LampIntensity = 5f, LampRadius = 7f, LampInnerRadius = 0.4f;
        /// <summary>Lamp cone (degrees): outer and inner angle, pointing down.</summary>
        public const float LampOuterAngle = 140f, LampInnerAngle = 60f;
        /// <summary>Lamp falloff intensity (0 = linear, 1 = sharp).</summary>
        public const float LampFalloff = 0.6f;
        /// <summary>Normal-map distance for lamps (how far "in front" of the sprites the light sits).</summary>
        public const float LampNormalDistance = 2.2f;
        /// <summary>Halo around each lamp head (unlit glow sprite): colour/alpha and diameter.</summary>
        public static readonly Color LampHalo = new Color(1f, 0.62f, 0.3f, 0.32f);
        /// <summary>Halo diameter in world units.</summary>
        public const float LampHaloSize = 2.6f;
        /// <summary>Hot core of the lamp glass.</summary>
        public static readonly Color LampCore = new Color(1f, 0.86f, 0.62f, 0.95f);
        /// <summary>Volumetric cone under each lamp (unlit, alpha-blended): colour/alpha, length, half-angle (degrees).</summary>
        public static readonly Color LampCone = new Color(1f, 0.66f, 0.34f, 0.25f);
        /// <summary>Cone length in world units.</summary>
        public const float LampConeLength = 4.2f;
        /// <summary>Cone half-angle in degrees (matches about half the light's outer angle).</summary>
        public const float LampConeHalfAngle = 32f;
        /// <summary>Rain streak colour inside a lamp cone (warm, brighter).</summary>
        public static readonly Color RainInCone = new Color(1f, 0.8f, 0.55f, 0.55f);
        /// <summary>Wet-floor reflection under a lamp: colour/alpha, width, depth below the floor top (world units).</summary>
        public static readonly Color FloorReflection = new Color(1f, 0.64f, 0.3f, 0.5f);
        /// <summary>Reflection streak width.</summary>
        public const float ReflectionWidth = 0.42f;
        /// <summary>Reflection streak length down the floor face.</summary>
        public const float ReflectionDepth = 1.1f;
        /// <summary>Signal reflections relative to lamp reflections: alpha and length factors.</summary>
        public const float SignalReflectionAlpha = 0.35f, SignalReflectionLength = 0.5f;
        /// <summary>Red signal light colour, radius, and intensity range (blinks).</summary>
        public static readonly Color SignalLight = new Color(1f, 0.14f, 0.1f);
        /// <summary>Signal light radius.</summary>
        public const float SignalRadius = 2.8f;
        /// <summary>Signal intensity at its dim and bright moment.</summary>
        public const float SignalMin = 0.25f, SignalMax = 1.3f;
        /// <summary>Signal blink period in seconds.</summary>
        public const float SignalPeriod = 1.1f;
        /// <summary>Lamp placement grid cell (tiles): at most one lamp per cell.</summary>
        public const int LampCellW = 10, LampCellH = 7;
        /// <summary>Minimum distance between two lamps (tiles).</summary>
        public const float LampMinSpacing = 5f;
        /// <summary>Every Nth placed fixture becomes a red signal light instead of a lamp.</summary>
        public const int SignalEvery = 4;
        /// <summary>Lamp-post / fixture metal tint (lit sprite).</summary>
        public static readonly Color FixtureTint = new Color(0.55f, 0.58f, 0.62f);

        // ------------------------------------------------------------------ props (PropField)
        /// <summary>Resources path of the prop atlas json (tools/art/props/make_props.sh writes it).</summary>
        public const string PropAtlasPath = "Art/Props/props";
        /// <summary>Background props: lit tint (mid-dark, behind the actors) and sorting order (behind the terrain).</summary>
        public static readonly Color PropTint = new Color(1f, 1f, 1f);
        /// <summary>Background prop sorting order (terrain is 0, so the terrain lip hides the prop's foot).</summary>
        public const int PropOrder = -3;
        /// <summary>Puddles lie on the floor top, in front of the terrain and behind the actors.</summary>
        public const int PuddleOrder = 1;
        /// <summary>Puddle tint (lit; the water ramp catches lamp light).</summary>
        public static readonly Color PuddleTint = new Color(0.85f, 0.9f, 1f, 0.85f);
        /// <summary>Emissive overlay (unlit) colour multiplier: signs glow but stay below the lamps.</summary>
        public static readonly Color PropEmissive = new Color(0.8f, 0.8f, 0.8f, 0.85f);
        /// <summary>Gap between floor props in tiles (min, max): several per screen, not cluttered.</summary>
        public const int PropGapMin = 3, PropGapMax = 7;
        /// <summary>Tiles kept clear of props on each side of a spike and of a lamp post.</summary>
        public const int PropSpikeClear = 2, PropLampClear = 1;
        /// <summary>Large mid-ground "hero" props, placed at most one per <see cref="HeroGap"/> tiles of floor.</summary>
        public static readonly string[] HeroProps = { "water_tank", "antenna_mast", "stair_hut", "billboard", "container" };
        /// <summary>Tiles between hero props (a 6.75 half-height 16:9 view is 24 tiles wide: about two per screen).</summary>
        public const int HeroGap = 11;
        /// <summary>Tile offsets from a lamp post tried for the lit prop on each side (nearest first).</summary>
        public static readonly int[] LampPropOffsets = { 2, 3, 4 };
        /// <summary>Foreground silhouettes along the screen bottom (recognisable shapes only, no mounds).</summary>
        public static readonly string[] OccluderProps = { "railing", "fence" };
        /// <summary>The prop that flickers like a dim neon screen, and its spill light intensity and radius.</summary>
        public const string FlickerProp = "billboard";
        /// <summary>Billboard spill light: small cyan.</summary>
        public const float SignLightIntensity = 0.55f, SignLightRadius = 3.2f;
        /// <summary>Glow multiplier during a flicker dropout.</summary>
        public const float SignDropout = 0.35f;
        /// <summary>Chance a planned floor slot gets a puddle instead of a standing prop.</summary>
        public const float PuddleChance = 0.18f;
        /// <summary>Spacing of hanging props under overhangs (tiles), and the top border's extra spacing factor.</summary>
        public const int HangGap = 9;
        /// <summary>Lit-sign props (billboard, neon, door) that get a small Light2D: at most this many per level.</summary>
        public const int PropLightMax = 3;
        /// <summary>Prop light intensity, radius and the minimum emissive texels for a prop to earn one.</summary>
        public const float PropLightIntensity = 0.9f, PropLightRadius = 2.4f;
        /// <summary>Minimum emissive texel count for a prop light.</summary>
        public const int PropLightMinTexels = 40;
        /// <summary>Foreground occluders: spacing along the bottom floor (tiles), scale, colour (near black, low alpha), order.</summary>
        public const int OccluderGap = 15;
        /// <summary>Foreground occluder scale.</summary>
        public const float OccluderScale = 1.6f;
        /// <summary>Foreground occluder colour: dark silhouette at low opacity.</summary>
        public static readonly Color OccluderColor = new Color(0.012f, 0.014f, 0.02f, 0.92f);
        /// <summary>Foreground occluder top edge (world y): stays below the floor top so it never covers feet or spikes.</summary>
        public const float OccluderTop = 0.85f;
        /// <summary>Foreground occluder sorting order (in front of the near rain).</summary>
        public const int OccluderOrder = 16;

        // ------------------------------------------------------------------ player light
        /// <summary>Player key/rim light: intensity, radius, inner radius, falloff.</summary>
        public const float PlayerLightIntensity = 0.6f, PlayerLightRadius = 3.2f, PlayerLightInner = 0.2f, PlayerLightFalloff = 0.75f;
        /// <summary>Rim offset behind (against facing) and above the player's centre.</summary>
        public const float PlayerLightBack = 0.55f, PlayerLightUp = 0.45f;
        /// <summary>Player body colour (neutral: the scene lights it; the tier shows on sword and rim).</summary>
        public static readonly Color PlayerBody = new Color(0.46f, 0.48f, 0.53f);
    }
}
