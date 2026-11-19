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

        /// <summary>Loop01 rooftop plant, ember amber: low setting sun behind the far city from the upper left; shadows fall camera-right.</summary>
        public static Arena3DLook Loop01() => new Arena3DLook
        {
            Name = "Loop01",
            KeyDir = new Vector3(0.62f, -0.42f, 0.32f),   // +z: shadows fall back onto the deck, not off its front lip
            Moon = new Color(1f, 0.56f, 0.30f), MoonIntensity = 4f,
            FogColor = new Color(0.62f, 0.36f, 0.26f), FogDensity = 0.014f, FarGlow = new Color(1.0f, 0.56f, 0.30f),
            Ambient = new Color(0.05f, 0.035f, 0.035f),
            HazeTint = new Color(0.62f, 0.36f, 0.28f),
            CityGlow = new Color(0.7f, 0.34f, 0.18f), CityGlowStrength = 0.3f,
            SkyHorizon = new Color(0.80f, 0.40f, 0.22f), SkyZenith = new Color(0.14f, 0.07f, 0.08f), SkyGlow = new Color(0.9f, 0.42f, 0.2f),
            Sodium = new Color(1f, 0.6f, 0.3f), Neon = new Color(0.9f, 0.42f, 0.16f), Signal = new Color(1f, 0.15f, 0.08f),
            Rim = new Color(0.4f, 0.6f, 1f),
            DeckColor = new Color(0.34f, 0.28f, 0.27f), WallColor = new Color(0.22f, 0.17f, 0.16f),
            BloomTint = new Color(1f, 0.82f, 0.7f),
            GradeShadows = new Vector4(1.0f, 0.95f, 0.95f, -0.04f), GradeHighlights = new Vector4(1.12f, 1f, 0.88f, 0f),
            Saturation = -20f, Rain = 0.5f,
            FarLift = 0.7f, ShaftStrength = 1.1f, DustAmount = 0.85f, MistStrength = 0.9f, NearShade = 0.45f, // ash in the low sun
            FrontFill = new Color(1f, 0.62f, 0.45f), FrontFillIntensity = 0.3f,
            FoliageColor = new Color(0.09f, 0.075f, 0.045f), Overgrowth = 0.35f, ColumnSpacing = 5.5f, RibSpacing = 7f,
            Seed = 1234,
        };

        /// <summary>Loop02 Overpass, teal-green: cold overhead sodium-green spot from the upper right at 45 degrees; short sharp shadows.</summary>
        public static Arena3DLook Loop02() => new Arena3DLook
        {
            Name = "Loop02",
            KeyDir = new Vector3(-0.62f, -0.70f, 0.26f),   // +z: shadows fall back onto the deck
            Moon = new Color(0.55f, 1f, 0.72f), MoonIntensity = 3.6f,
            FogColor = new Color(0.20f, 0.34f, 0.30f), FogDensity = 0.015f, FarGlow = new Color(0.50f, 0.75f, 0.66f),
            Ambient = new Color(0.035f, 0.06f, 0.06f),
            HazeTint = new Color(0.25f, 0.5f, 0.45f),
            CityGlow = new Color(0.12f, 0.45f, 0.4f), CityGlowStrength = 0.3f,
            SkyHorizon = new Color(0.12f, 0.27f, 0.24f), SkyZenith = new Color(0.01f, 0.03f, 0.03f), SkyGlow = new Color(0.1f, 0.3f, 0.25f),
            Sodium = new Color(1f, 0.52f, 0.14f),   // hot orange only in hazard lamps
            Neon = new Color(1.2f, 0.45f, 0.08f), Signal = new Color(1f, 0.12f, 0.05f),
            Rim = new Color(0.4f, 1f, 0.9f),
            DeckColor = new Color(0.25f, 0.30f, 0.30f), WallColor = new Color(0.16f, 0.2f, 0.21f),
            BloomTint = new Color(0.8f, 1f, 0.9f),
            GradeShadows = new Vector4(0.92f, 1.0f, 1.0f, -0.05f), GradeHighlights = new Vector4(1.0f, 1.08f, 0.95f, 0f),
            Saturation = -18f, Rain = 0.7f,
            FarLift = 0.5f, ShaftStrength = 0.9f, DustAmount = 0.45f, MistStrength = 1.3f, NearShade = 0.5f, // wet: mist over dust
            FrontFill = new Color(0.55f, 0.9f, 0.8f), FrontFillIntensity = 0.3f,
            FloorReflect = 0.22f, PuddleReflect = 0.9f, PuddleThreshold = 0.56f, FloodWater = true, // flooded
            FoliageColor = new Color(0.035f, 0.10f, 0.07f), Overgrowth = 1f, ColumnSpacing = 8.5f, RibSpacing = 10f,
            Seed = 2234,
        };

        /// <summary>Loop03 Gallery, oxblood: low frontal key through tall windows from camera-left; long diagonal shadows to camera-right.</summary>
        public static Arena3DLook Loop03() => new Arena3DLook
        {
            Name = "Loop03",
            KeyDir = new Vector3(0.80f, -0.32f, 0.50f),
            Moon = new Color(1f, 0.66f, 0.5f), MoonIntensity = 3.0f,
            // Far bright / near dark: a darker fog and lighter veils pull the mid-ground down, the far band lifts the back.
            // Mid-ground pulled to dark silhouettes: a darker, thinner fog (less mid-grey lift), darker dressing (MidValue).
            FogColor = new Color(0.27f, 0.11f, 0.095f), FogDensity = 0.013f, FarGlow = new Color(1.2f, 0.66f, 0.5f),
            Ambient = new Color(0.07f, 0.042f, 0.04f),
            HazeTint = new Color(0.55f, 0.28f, 0.25f), HazeGlow = 0.06f, HazeStrength = 0.62f,
            CityGlow = new Color(0.6f, 0.2f, 0.12f), CityGlowStrength = 0.3f,
            SkyHorizon = new Color(0.42f, 0.16f, 0.13f), SkyZenith = new Color(0.07f, 0.03f, 0.03f), SkyGlow = new Color(0.5f, 0.2f, 0.14f),
            WindowsLit = 0.12f,
            Sodium = new Color(1f, 0.72f, 0.32f),   // brass
            Neon = new Color(1.1f, 0.35f, 0.2f), Signal = new Color(1f, 0.1f, 0.05f),
            Rim = new Color(1f, 0.7f, 0.5f),
            DeckColor = new Color(0.17f, 0.13f, 0.13f), WallColor = new Color(0.12f, 0.08f, 0.08f),
            BloomTint = new Color(1f, 0.78f, 0.66f),
            GradeShadows = new Vector4(1.05f, 0.94f, 0.94f, -0.04f), GradeHighlights = new Vector4(1.15f, 0.97f, 0.85f, 0f),
            Saturation = -15f, Rain = 0.2f,
            FarLift = 1.4f, ShaftStrength = 1.35f, DustAmount = 0.9f, MistStrength = 0.6f, NearShade = 0.6f, // dust in window light
            // Platform fronts face away from the frontal-left key: a warm fill lifts them without touching the mid-ground.
            FrontFill = new Color(1f, 0.6f, 0.48f), FrontFillIntensity = 0.85f, MidValue = 0.6f,
            FoliageColor = new Color(0.085f, 0.035f, 0.035f), AshColor = new Color(0.62f, 0.54f, 0.52f, 1f), Overgrowth = 0.25f,
            ColumnSpacing = 8.5f, RibSpacing = 10f,
            Seed = 3234,
        };

        /// <summary>Neutral cool night for Arena01 and Training.</summary>
        public static Arena3DLook Neutral(string name = "Neutral") => new Arena3DLook
        {
            Name = name,
            KeyDir = new Vector3(0.45f, -0.6f, -0.35f),
            Moon = new Color(0.7f, 0.78f, 0.95f), MoonIntensity = 2f,
            FogColor = new Color(0.14f, 0.15f, 0.18f),
            HazeTint = new Color(0.45f, 0.48f, 0.55f),
            CityGlow = new Color(0.45f, 0.35f, 0.25f),
            SkyHorizon = new Color(0.17f, 0.18f, 0.22f),
            Neon = new Color(0.6f, 0.9f, 1.1f),
            Rim = new Color(0.75f, 0.85f, 1f),
            GradeShadows = new Vector4(0.95f, 0.97f, 1.05f, -0.04f), GradeHighlights = new Vector4(1.05f, 1f, 0.95f, 0f),
            Rain = 0.4f,
            Seed = 4234,
        };
    }
}
