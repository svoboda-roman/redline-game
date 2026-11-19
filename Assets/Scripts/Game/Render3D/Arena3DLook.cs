using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Look tunables for one level in the 3D-pixelated renderer: fog and haze, lamp and neon colours, the
    /// character rim light, rain, and the colour grade the camera rig applies. One preset per level via
    /// <see cref="For"/>.
    /// </summary>
    /// <remarks>
    /// Values follow art bible section 2 and 4: one dominant low-chroma hue per level (Loop01 ember amber,
    /// Loop02 teal-green, Loop03 oxblood), saturation only at light sources plus one accent, and one strong key
    /// light whose direction differs per level. Moon / MoonIntensity are the KEY light (name kept for callers).
    /// </remarks>
    /// <example>
    /// <code>
    /// var look = Arena3DLook.For("Loop02");
    /// look.FogDensity *= 1.2f; // per-level tweak before building
    /// var arena = new Arena3DRenderer(host, map, look);
    /// </code>
    /// </example>
    public sealed class Arena3DLook
    {
        /// <summary>Preset name (the level it was made for).</summary>
        public string Name = "Neutral";

        // --- Atmosphere -----------------------------------------------------------------------------------
        /// <summary>Scene fog colour. A bright fog colour reads as glowing haze.</summary>
        public Color FogColor = new Color(0.13f, 0.16f, 0.26f);
        /// <summary>Exponential fog density.</summary>
        public float FogDensity = 0.011f;
        /// <summary>Flat ambient light.</summary>
        public Color Ambient = new Color(0.05f, 0.07f, 0.11f);
        /// <summary>Tint of the mid-ground haze cards (blue-violet city haze).</summary>
        public Color HazeTint = new Color(0.42f, 0.42f, 0.75f);
        /// <summary>Overall strength of the haze cards (alpha scale).</summary>
        public float HazeStrength = 0.8f;
        /// <summary>Additive self-lit glow of the mid-ground haze (the "glowing" part of it).</summary>
        public float HazeGlow = 0.07f;
        /// <summary>Warm street-glow puffs low in the city (sodium bleeding into the fog).</summary>
        public Color CityGlow = new Color(0.55f, 0.3f, 0.14f);
        /// <summary>Strength of the city glow puffs.</summary>
        public float CityGlowStrength = 0.3f;
        /// <summary>Sky: horizon, zenith, glow band.</summary>
        public Color SkyHorizon = new Color(0.16f, 0.19f, 0.32f), SkyZenith = new Color(0.02f, 0.03f, 0.06f), SkyGlow = new Color(0.16f, 0.12f, 0.16f);
        /// <summary>Lit fraction of building windows.</summary>
        public float WindowsLit = 0.22f;
        /// <summary>Window emission multiplier.</summary>
        public float WindowGlow = 2.2f;
        /// <summary>Rain amount (0 = off, 1 = the prototype's downpour).</summary>
        public float Rain = 0.8f;

        // --- Lights ---------------------------------------------------------------------------------------
        /// <summary>Lamp (key) colour.</summary>
        public Color Sodium = new Color(1f, 0.58f, 0.24f);
        /// <summary>Accent neon colour (signs, slab trim, reflections of signs).</summary>
        public Color Neon = new Color(0.25f, 0.9f, 1.2f);
        /// <summary>Secondary accent (beacons, signals).</summary>
        public Color Signal = new Color(1f, 0.1f, 0.08f);
        /// <summary>Lamp spot intensity.</summary>
        public float LampSpotIntensity = 120f;
        /// <summary>Lamp point fill intensity.</summary>
        public float LampPointIntensity = 12f;
        /// <summary>Key light travel direction (world space; +x = camera-right, -z = toward the camera).</summary>
        public Vector3 KeyDir = new Vector3(0.5f, -0.6f, -0.3f);
        /// <summary>Floor and wall albedo tints (low chroma, dark).</summary>
        public Color DeckColor = new Color(0.30f, 0.30f, 0.32f), WallColor = new Color(0.20f, 0.20f, 0.22f);
        /// <summary>Bright far layer: the backlit haze/sky glow behind the city (the brightest large area of the frame).</summary>
        public Color FarGlow = new Color(0.4f, 0.42f, 0.48f);
        /// <summary>Strength of the unfogged far glow band behind the near city rows (Atmosphere3D; the far value step).</summary>
        public float FarLift = 0.55f;
        /// <summary>Strength of the god-ray cards leaning along the key light (0 = none).</summary>
        public float ShaftStrength = 1f;
        /// <summary>Dust / ash motes drifting in the key light (0 = none, 1 = ~70 visible).</summary>
        public float DustAmount = 0.6f;
        /// <summary>Ground mist hugging the deck (alpha scale).</summary>
        public float MistStrength = 1f;
        /// <summary>Darkening of the near deck below the floor line (the near value step).</summary>
        public float NearShade = 0.45f;
        /// <summary>Film grain intensity (bible: 3-4 %).</summary>
        public float FilmGrain = 0.04f;
        /// <summary>Key light (main directional, the only shadowed directional).</summary>
        public Color Moon = new Color(0.45f, 0.58f, 0.85f);
        /// <summary>Moon intensity.</summary>
        public float MoonIntensity = 2.2f;
        /// <summary>Character rim light colour (rendering layer 1 only).</summary>
        public Color Rim = new Color(0.55f, 0.75f, 1f);
        /// <summary>Character rim light intensity (each of the two back-side rims).</summary>
        public float RimIntensity = 1.6f;
        /// <summary>Strength of the wet-deck reflection streaks under lights.</summary>
        public float ReflectionIntensity = 1.6f;
        /// <summary>Sideways ripple of the reflection streaks.</summary>
        public float ReflectionRipple = 0.14f;
        /// <summary>Maximum number of lamps with real-time shadows at once (the ones nearest the view centre).</summary>
        public int MaxShadowedLamps = 4;
        /// <summary>
        /// Front fill: an unshadowed directional from the camera side that lights only the play-plane level
        /// (rendering layer <see cref="Arena3DRenderer.FillRenderingLayer"/>), lifting platform fronts the key misses.
        /// </summary>
        public Color FrontFill = new Color(0.6f, 0.62f, 0.7f);
        /// <summary>Front fill intensity (0 = off).</summary>
        public float FrontFillIntensity = 0.35f;
        /// <summary>Self-lit fill on level-box side faces turned away from the key (emission = fascia albedo × FrontFill × this).</summary>
        public float FasciaSideFill = 0.4f;
        /// <summary>Planar floor reflection: reflectance on the damp deck and in puddles / flood water.</summary>
        public float FloorReflect = 0.16f, PuddleReflect = 0.85f;
        /// <summary>Street puddle threshold (lower = more of the deck under water; 0.645 = scattered puddles).</summary>
        public float PuddleThreshold = 0.645f;
        /// <summary>Flood water behind the deck (z past DeckFar) at the floor height, mirroring the mid-ground.</summary>
        public bool FloodWater;

        // --- Neon street backdrop (Arena3DNeonStreet) -------------------------------------------------------
        /// <summary>Replaces the industrial city, ruins and rooftop props with a dense neon-sign street (variant A).</summary>
        public bool NeonStreet;
        /// <summary>Fraction of street signs that are dead: dark, unlit, no halo (0..1).</summary>
        public float DeadSigns = 0.2f;
        /// <summary>
        /// Platforms as black metal scaffolding: dark metallic fascia, slab tops, supports, crates and spikes, thin
        /// scaffold bracing, and pale steel edge lines on every solid box so hookable surfaces read at a glance.
        /// </summary>
        public bool MetalPlatforms;
        /// <summary>Colour of the platform edge lines (emissive; read against the neon street).</summary>
        public Color EdgeLine = new Color(0.55f, 0.68f, 0.85f);

        // --- Sunset backdrop (Arena3DSunset) --------------------------------------------------------------
        /// <summary>Builds the sunset backdrop: a big striped sun setting behind the city, its halo, and neon signs on the city.</summary>
        public bool Sunset;
        /// <summary>Sun gradient: top (yellow) and bottom (pink / red).</summary>
        public Color SunTop = new Color(1.3f, 0.95f, 0.35f), SunBottom = new Color(1.3f, 0.2f, 0.45f);
        /// <summary>Sun diameter (world units at its depth) and its centre relative to the camera follow point (x, y).</summary>
        public float SunSize = 150f;
        public Vector2 SunOffset = new Vector2(-30f, 32f);
        /// <summary>Neon sign colours on the city (picked at random per sign).</summary>
        public Color[] SignColors = { new Color(1.4f, 0.2f, 0.6f), new Color(1.4f, 0.95f, 0.25f), new Color(1.4f, 0.15f, 0.12f) };
        /// <summary>Neon signs per city building in the near rows (0..1).</summary>
        public float SignChance = 0.6f;
        /// <summary>Build the dense dark mid-ground trusses and girders (Arena3DDressing). Off for the open sunset look.</summary>
        public bool MidDressing = true;

        // --- Ruins and overgrowth (Arena3DRuins) -----------------------------------------------------------
        /// <summary>Overgrowth colour (creepers, vines, bushes): dark, in the level hue.</summary>
        public Color FoliageColor = new Color(0.07f, 0.08f, 0.06f);
        /// <summary>Snow / ash colour on ledges and ruins (alpha 0 = none).</summary>
        public Color AshColor = new Color(0.5f, 0.5f, 0.5f, 0f);
        /// <summary>Overgrowth amount (0 = none, 1 = heavy).</summary>
        public float Overgrowth = 0.3f;
        /// <summary>Value scale for mid-ground dressing and ruins (below 1 pushes the mid-ground toward silhouette).</summary>
        public float MidValue = 1f;
        /// <summary>Spacing of slab support columns and mid-ground ribs (world units).</summary>
        public float ColumnSpacing = 4.5f, RibSpacing = 6.2f;

        // --- Grade (applied by Pixel3DCamera.ApplyLook) ----------------------------------------------------
        /// <summary>Bloom threshold, intensity, scatter.</summary>
        public float BloomThreshold = 1.0f, BloomIntensity = 0.55f, BloomScatter = 0.6f;
        /// <summary>Bloom tint.</summary>
        public Color BloomTint = new Color(1f, 0.85f, 0.75f);
        /// <summary>Post exposure (EV).</summary>
        public float Exposure = 0.55f;
        /// <summary>Contrast and saturation (-100..100).</summary>
        public float Contrast = 22f, Saturation = -22f;
        /// <summary>Shadows/highlights lift (ShadowsMidtonesHighlights rgb + w).</summary>
        public Vector4 GradeShadows = new Vector4(0.85f, 0.95f, 1.2f, -0.05f), GradeHighlights = new Vector4(1.15f, 1f, 0.85f, 0f);
        /// <summary>Vignette intensity.</summary>
        public float Vignette = 0.25f;

        // --- Geometry -------------------------------------------------------------------------------------
        /// <summary>Deck extent toward (negative) and away from (positive) the camera.</summary>
        public float DeckNear = -16f, DeckFar = 14f;
        /// <summary>Seed for every procedural choice (props, city, clutter). Same seed = same level.</summary>
        public int Seed = 1234;

        /// <summary>Returns a new preset for a level name (Loop01, Loop02, Loop03, Arena01, Training); unknown names get Neutral.</summary>
        public static Arena3DLook For(string level)
        {
            switch (level)
            {
                case "Loop01": return Loop01();
                case "Loop02": return Loop02();
                case "Loop03": return Loop03();
                default: return Neutral(level);
            }
        }

        // Neon sunset look (all levels): a pink-to-yellow sky with a big striped sun setting behind a pink fogged city
        // full of pink / yellow / red neon signs, an open mid-ground (no dark truss wall), lifted ambient so little is
        // black, and wet reflective decks. The samurai's art is untouched; his own lights (Actors3DView) keep him readable.

        /// <summary>Loop01 rooftop plant: hot pink sky over a yellow horizon, the sun low on the left.</summary>
        public static Arena3DLook Loop01() => Streetize(new Arena3DLook
        {
            Name = "Loop01",
            KeyDir = new Vector3(0.62f, -0.42f, 0.32f),   // +z: shadows fall back onto the deck, not off its front lip
            Overgrowth = 0.35f, ColumnSpacing = 5.5f, RibSpacing = 7f,
            SunOffset = new Vector2(-35f, 30f),
            Rain = 0.25f,
            Seed = 1234,
        });

        /// <summary>Loop02 Overpass, flooded: a redder sunset mirrored in the flood water, the sun on the right.</summary>
        public static Arena3DLook Loop02() => Streetize(new Arena3DLook
        {
            Name = "Loop02",
            KeyDir = new Vector3(-0.62f, -0.70f, 0.26f),   // +z: shadows fall back onto the deck
            PuddleThreshold = 0.56f, FloodWater = true, // flooded
            Overgrowth = 0.6f, ColumnSpacing = 8.5f, RibSpacing = 10f,
            SunOffset = new Vector2(40f, 26f),
            SunBottom = new Color(1.4f, 0.12f, 0.2f),
            Rain = 0.4f,
            Seed = 2234,
        }, redder: true);

        /// <summary>Loop03 Gallery: a golden sunset, the sun centred and large.</summary>
        public static Arena3DLook Loop03() => Streetize(new Arena3DLook
        {
            Name = "Loop03",
            KeyDir = new Vector3(0.80f, -0.32f, 0.50f),
            Overgrowth = 0.25f, AshColor = new Color(0.9f, 0.6f, 0.6f, 1f),
            ColumnSpacing = 8.5f, RibSpacing = 10f,
            SunOffset = new Vector2(0f, 28f), SunSize = 175f,
            SunTop = new Color(1.4f, 1.1f, 0.4f),
            Rain = 0.1f,
            Seed = 3234,
        });

        /// <summary>Neutral sunset for Arena01 and Training.</summary>
        public static Arena3DLook Neutral(string name = "Neutral") => Streetize(new Arena3DLook
        {
            Name = name,
            KeyDir = new Vector3(0.45f, -0.6f, -0.35f),
            SunOffset = new Vector2(-20f, 30f),
            Rain = 0.2f,
            Seed = 4234,
        });

        /// <summary>
        /// Variant A, the neon street: a dense sign-covered street (Arena3DNeonStreet) under a deep violet night sky,
        /// pink-violet fog, warm street lamps, pink / yellow / red signs with cyan and white accents, wet reflective
        /// asphalt, lifted ambient (little pure black), strong bloom. No sun, industrial city, ruins or rooftop props.
        /// <paramref name="redder"/> pushes the signs and fog toward red.
        /// </summary>
        private static Arena3DLook Streetize(Arena3DLook l, bool redder = false)
        {
            Sunsetize(l, redder);
            l.Sunset = false;
            l.NeonStreet = true;
            l.SkyZenith = new Color(0.05f, 0.04f, 0.16f);
            l.SkyHorizon = redder ? new Color(0.45f, 0.1f, 0.22f) : new Color(0.4f, 0.12f, 0.38f);
            l.SkyGlow = new Color(0.35f, 0.08f, 0.25f);
            l.FogColor = redder ? new Color(0.36f, 0.1f, 0.18f) : new Color(0.32f, 0.12f, 0.3f);
            l.FogDensity = 0.011f;
            l.FarGlow = redder ? new Color(0.9f, 0.25f, 0.35f) : new Color(0.85f, 0.3f, 0.7f);
            l.HazeTint = l.FarGlow;
            l.HazeStrength = 0.28f;
            l.HazeGlow = 0.08f;
            l.CityGlowStrength = 0.3f;
            l.Ambient = new Color(0.16f, 0.1f, 0.16f);
            l.Moon = new Color(0.85f, 0.55f, 0.9f);
            l.MoonIntensity = 1.6f;
            l.Sodium = new Color(1.35f, 0.1f, 0.08f);   // red street lamps: red heads, red light, red reflections
            l.LampSpotIntensity = 120f;
            l.DeckColor = new Color(0.3f, 0.24f, 0.3f);
            l.WallColor = new Color(0.1f, 0.095f, 0.11f); // platforms: black metal
            l.MetalPlatforms = true;
            l.FrontFill = new Color(1f, 0.55f, 0.75f);
            l.FrontFillIntensity = 0.45f;
            l.ShaftStrength = 0f;
            l.DustAmount = 0.15f;
            l.MistStrength = 0.5f;
            l.FarLift = 0.4f;
            l.NearShade = 0.2f;
            l.FloorReflect = 0.32f;
            l.PuddleReflect = 0.95f;
            l.ReflectionIntensity = 2.4f;
            l.Exposure = 0.7f;
            l.Contrast = 22f;
            l.Saturation = 38f;          // saturated colour rather than glow
            l.BloomThreshold = 1.0f;
            l.BloomIntensity = 0.6f;
            l.BloomScatter = 0.65f;
            l.BloomTint = new Color(1f, 0.85f, 0.95f);
            l.Vignette = 0.2f;
            l.SignColors = redder
                ? new[] { new Color(1.4f, 0.14f, 0.14f), new Color(1.4f, 0.9f, 0.25f), new Color(1.4f, 0.22f, 0.6f), new Color(1.4f, 0.14f, 0.14f) }
                : new[] { new Color(1.4f, 0.22f, 0.62f), new Color(1.4f, 0.95f, 0.25f), new Color(1.4f, 0.16f, 0.14f), new Color(1.4f, 0.22f, 0.62f) };
            return l;
        }

        /// <summary>
        /// The shared neon sunset palette: pink zenith over an orange-yellow horizon, warm pink fog that turns the far
        /// city into pink silhouettes, yellow lamps, hot pink neon, red signals, lifted warm ambient (little pure black),
        /// saturated grade with strong bloom, wet reflective decks, no dark truss wall. <paramref name="redder"/> shifts
        /// the whole palette toward red.
        /// </summary>
        private static Arena3DLook Sunsetize(Arena3DLook l, bool redder = false)
        {
            l.Sunset = true;
            l.MidDressing = false;
            l.Moon = redder ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.6f, 0.5f);
            l.MoonIntensity = 3f;
            l.FogColor = redder ? new Color(0.62f, 0.16f, 0.24f) : new Color(0.68f, 0.24f, 0.4f);
            l.FogDensity = 0.012f;
            l.FarGlow = redder ? new Color(1.2f, 0.35f, 0.3f) : new Color(1.2f, 0.55f, 0.45f);
            l.Ambient = redder ? new Color(0.2f, 0.08f, 0.1f) : new Color(0.2f, 0.1f, 0.15f);
            l.HazeTint = redder ? new Color(1f, 0.3f, 0.35f) : new Color(1f, 0.4f, 0.6f);
            l.HazeStrength = 0.5f;
            l.HazeGlow = 0.12f;
            l.CityGlow = redder ? new Color(1.2f, 0.25f, 0.2f) : new Color(1.2f, 0.4f, 0.55f);
            l.CityGlowStrength = 0.45f;
            l.SkyZenith = redder ? new Color(0.45f, 0.06f, 0.2f) : new Color(0.5f, 0.1f, 0.42f);
            l.SkyHorizon = redder ? new Color(1f, 0.42f, 0.2f) : new Color(1f, 0.55f, 0.3f);
            l.SkyGlow = redder ? new Color(0.6f, 0.25f, 0.05f) : new Color(0.6f, 0.4f, 0.1f);
            l.WindowsLit = 0.4f;
            l.WindowGlow = 3.2f;
            l.Sodium = new Color(1.3f, 0.85f, 0.35f);
            l.Neon = new Color(1.4f, 0.2f, 0.6f);
            l.Signal = new Color(1.4f, 0.12f, 0.1f);
            l.LampSpotIntensity = 110f;
            l.DeckColor = new Color(0.42f, 0.3f, 0.34f);
            l.WallColor = new Color(0.3f, 0.2f, 0.24f);
            l.FrontFill = new Color(1f, 0.6f, 0.65f);
            l.FrontFillIntensity = 0.45f;
            l.FasciaSideFill = 0.55f;
            l.FoliageColor = new Color(0.2f, 0.1f, 0.12f);
            l.BloomThreshold = 0.9f;
            l.BloomIntensity = 0.9f;
            l.BloomScatter = 0.7f;
            l.BloomTint = new Color(1f, 0.8f, 0.8f);
            l.Exposure = 0.75f;
            l.Contrast = 18f;
            l.Saturation = 20f;
            l.GradeShadows = new Vector4(1.05f, 0.92f, 1.0f, 0.02f);
            l.GradeHighlights = new Vector4(1.1f, 1.0f, 0.9f, 0f);
            l.Vignette = 0.18f;
            l.FarLift = 0.9f;
            l.ShaftStrength = 0.5f;
            l.DustAmount = 0.3f;
            l.MistStrength = 0.8f;
            l.NearShade = 0.25f;
            l.MidValue = 1f;
            l.FloorReflect = 0.24f;
            l.PuddleReflect = 0.9f;
            if (!l.FloodWater) l.PuddleThreshold = 0.6f;
            l.ReflectionIntensity = 2f;
            l.SignColors = redder
                ? new[] { new Color(1.4f, 0.12f, 0.12f), new Color(1.4f, 0.9f, 0.25f), new Color(1.4f, 0.2f, 0.55f) }
                : new[] { new Color(1.4f, 0.2f, 0.6f), new Color(1.4f, 0.95f, 0.25f), new Color(1.4f, 0.15f, 0.12f) };
            return l;
        }
    }
}
