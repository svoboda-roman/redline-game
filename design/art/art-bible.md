# Art Bible: Redline

> **Status**: Draft
> **Owned By**: art-director
> **Last Updated**: 2026-11-04
> **Art Director Sign-Off (AD-ART-BIBLE)**: SKIPPED 2026-11-04 — solo mode

Authored with `/art-bible` in autonomous mode (the user delegated decisions: "its up to you but fix
everything"). Sections 1–7 and 9 were drafted by art-director, section 8 by technical-artist. The orchestrator
resolved conflicts, listed under **Decisions log** at the end. Reference frames: `design/reference/replaced/`.

## 1. Visual Identity Statement

- **Rule:** *A dark, wet, one-colour-per-place city lit by one hard light, where the only saturation belongs to
  you, your enemies' eyes and what is about to hurt.*
- **Supersedes** the game brief's "clean neon-on-dark flat shapes; synthwave" line. The look is Replaced-style
  (Sad Cat Studios, 2026), techniques only, no copied assets: a real 3D scene rendered to a 480×270 target
  (40 texels/unit, FramingHeight 6.75), point-upscaled ×4 to 1080p, perspective camera, pitch 3°, FOV 30°.
  Audio direction is orchestral, not synthwave.
- **P1, speed is light** (serves "the faster you move, the harder you hit"): the tier colour is the only
  saturated hue not tied to a place, and it lives only on the katana. *Test: unsure whether
  something glows? It doesn't, unless it encodes tier, threat or a pickup.*
- **P2, depth before decoration** (serves the flow-state rush through a living city): every frame is layered
  and has one key light with a cast shadow. *Test: unsure whether to add a glow or a prop? Add an occluder or a
  mid-ground silhouette.*
- **P3, chunky and still:** shapes hold their texels from frame to frame. *Test: is a detail under 2 texels, or
  does it crawl in motion? Cut it or enlarge it.*

## 2. Mood & Atmosphere

All states: wet night, high contrast, film grain 3–4 %, vignette 25 %.

| State | Emotion | Lighting | Energy |
|---|---|---|---|
| Menu | held breath | locked-off rooftop, one warm spot on the ronin's back, slow rain, orange terminal text | 1/5 |
| Combat tier 1 | wary | level grade at full dark; katana cold blue | 2/5 |
| Combat tier 2 | hunting | key light +15 %, stronger reflections, katana amber | 3/5 |
| Combat tier 3 | lethal | key light +30 %, vignette 40 %, rain streaks read as speed lines, katana white-hot | 5/5 |
| Between waves | exhale | key light −40 %, saturation halved except puddle highlights, rain eases | 1/5 |
| Game over | cold | desaturate 70 %, one crimson practical, slow fade to black | 0/5 |
| Victory | dawn | grade lifts toward a warm low sun, shafts strongest | 2/5 |
| Upgrades | workshop | interior dark, one warm lamp, orange line-art cards | 1/5 |

- A tier change shifts the light temperature and intensity only, never the level hue.
- **Per level, mutually distinct.** *Test: the desaturated thumbnails still differ by key-light direction.*
  - **Loop01, rooftop plant, ember amber.** Low setting-sun backlight from the upper left behind the far city;
    shadows fall to camera-right. Ember orange, dusty rose, brown-black.
  - **Loop02 Overpass, teal-green.** Cold overhead sodium-green spot from the upper right at 45°; short, sharp
    shadows under the pillars. Teal-green, steel, wet black. Hot orange only in hazard lamps and brake lights.
  - **Loop03 Gallery, oxblood.** Low frontal key through tall windows from camera-left, long diagonal shafts;
    shadows across the floor to camera-right. Oxblood, brass, smoke, salmon haze.

## 3. Shape Language

- **Silhouette first:** every character reads as a solid dark silhouette at 480×270 with one trait.
  - Ronin: conical hat and long split coat.
  - Grunt: round hood.
  - Brute: wide square shoulders.
  - Drone: horizontal slab with two rotors.
  - Shooter: long side barrel.
  - Shield bearer: tall flat plane.
  - Kamikaze: hunched round body with a red pack.
  - Summoner: tall and thin with a floating halo.
- **Hero shapes:** the cone (hat) and the straight line (blade). Enemies are boxy and angular. The environment
  is heavy right angles and girders, with diagonals for depth.
- Threats use triangles and spikes; pickups use circles.
- UI: thin-line rectangles with 45° chamfers. No panels or fills.

## 4. Color System

Hex values are authored in sRGB.

- **World:**
  - ground black `#0B0A0C`
  - wet grey `#2A2A2E`
  - level hues: L1 ember `#C8622A`, L2 teal-green `#2F6B62`, L3 oxblood `#7A2A2A`
  - practical warm light `#FFB056`
  - enemy/danger crimson `#E5232E`
  - bone white `#E8E2D4`
- **Tier** (luminance rises with tier, so it doesn't depend on hue alone):
  - tier 1 cold blue `#9CC8FF`
  - tier 2 amber `#FFB347`
  - tier 3 white-hot `#FFF4E8`, the brightest value on screen
- **Enemy telegraph:** the eye dots ramp from dim `#7A1018` to `#FF2A2A` over the windup, then flash white for 2
  frames on the strike frame.
- **Danger zones:** crimson at 40 % as a ground decal, never a fill.
- **Pickups:** health green `#58E08A` with a plus glyph; speed cyan `#4FE3FF` with a chevron; hook gold
  `#FFD24A` with a ring.
- **Colourblind backups:**
  - Tier: HUD pip count 1/2/3 and a step in katana glow length.
  - Telegraph: a pose change plus an audio cue.
  - Pickups: the glyph shape.
  - Adjacent tiers differ by at least 25 % luminance.
- **UI palette:** monochrome orange `#F08A3C` for icons, text and lines; bone white for the active value; crimson
  only for low HP; no fills. The speed meter takes the tier colour (see section 7).

## 5. Character Design Direction

- **All characters are pre-rendered or hand-made pixel sprites lit in 3D** (albedo + normal + emissive, 40
  px/unit). The Tech Ronin is modelled and animated in 3D, then baked to sprite frames (Dead Cells method). This
  removes frame-to-frame shimmer by construction.
- **Ronin:** dark, with a near-black coat whose crimson lining `#7A1018` shows in motion. A low, forward-leaning
  stance. Two eye dots of 2×2 texels. Tier is shown on the katana only. The hat does not glow: its brim edge
  is a dark, non-emissive trim at least 2 texels thick.
- **Enemies ("corporate ronin"):** dark armour, crimson 2×2 eye dots, weapon indicators 2–3 texels. They are told
  apart by silhouette, not colour.
- **Hard rules at 480×270:**
  1. Minimum feature thickness is 2 texels. Nothing 1 texel wide on a character.
  2. Emissive accents are solid blocks of 2×2 to 3×3 texels. No dotted patterns or sparkle.
  3. The blade is at least 2 texels wide with a solid core; its glow comes from bloom, not extra pixels.
  4. No outlines, no white rims, no fresnel. Separation comes from value contrast and the scene's key light.
  5. Ronin 1.5 units tall (60 texels); enemies 1.3–1.6 units; brute 2.0.
  6. At least 35 texels of ground stay visible below the feet; the player rests about 62 % of the way down the
     visible band.
  7. A character's mean value is at least 35 % darker than the background directly behind it.
  8. Roots snap to the texel grid and flips happen on whole texels. Pulses use a 4-step ramp at 4 Hz or slower.
     Verify by diffing consecutive frames of a held idle: zero changed pixels within one animation frame.

## 6. Environment Design Language

- An industrial-decay city with a 1950s-tinged dystopia feel. Geometry carries the detail; textures are painted,
  stylised and low-res.
- **Layers** (world units from the play plane at Z = 0; the camera sits at about Z = −14):
  - **Foreground occluders, Z −4 to −1.5:** dark, blurred girders, cables, pipes and fences covering 8–12 % of
    the frame, at the edges only, never hiding the play area. 2–4 per screen.
  - **Play plane, Z 0:** ground, platforms and walls with clean silhouettes; 3–6 small props per screen; a wet,
    reflective floor.
  - **Mid-ground architecture, Z +3 to +12:** sharp and dense (girders, cranes, fences, signage, facades). At
    least 60 % of the frame behind the play plane holds structure, with 12–20 shapes per screen. **This is the
    biggest fix.**
  - **Far city, Z +25 to +60:** haze silhouettes in 3 value steps, with 20–40 windows as 2×2 texel lights.
  - **Sky:** a gradient in the level hue, rain, one light source.
- **Set dressing:**
  - Loop01: water tower, chimneys, crane, ember sky.
  - Loop02: elevated highway, concrete pillars, barriers, hazard stripes, green sodium spot.
  - Loop03: tall gallery, windows, balconies, banners, statues, dust shafts.
- **Lighting:** one key light per level with a real shadow map and long cast shadows; 3–5 volumetric shafts per
  frame; strong reflections on the wet ground.
- **Depth of field:** the focus band covers the play plane and mid-ground (Z 0 to +12) and stays fully sharp.
  Only the foreground occluders (strong) and the far city and sky (moderate) are blurred. Characters are never
  blurred.

## 7. UI/HUD Visual Direction

- Screen-space and Replaced-minimal: small monochrome orange line icons with no panels, frames or fills.
- **Letterbox: 2.0:1 during gameplay.** The visible band is 480×240 texels (6.0 units vertical), with 15-texel
  black bars at the top and bottom. Cinematic moments (menu, between waves, victory, game over) widen the bars
  to 2.4:1.
- **Speed + tier:** a thin line meter at the lower left with tick marks at the tier thresholds, and a tier
  numeral and pips beside it. The meter takes the tier colour.
- **HP:** a row of 8-texel diamond glyphs at the upper left: orange when full, hollow when lost, a crimson pulse
  at one left.
- **Wave + foes:** small orange monospace "WAVE 1/5" and "FOES 5" at the upper right, no panel.
- **Kill chain:** an "×7" counter under the speed meter with a thin decay line; it flashes bone white on each
  kill.
- **Typography:** pixel monospace with a terminal feel. Labels 8 texels, values 12, headings 16; nothing smaller
  than 8 texels at 480×270.
- **Menus:** orange text on near-black as a terminal list with a selection bar. Animation is instant or stepped
  (2–3 frame transitions), with no bloom on UI.
- **Conflict decided:** readability beats purity. The tier colour is allowed on the speed meter despite the
  otherwise orange HUD, because tier must read in peripheral vision.

## 8. Asset Standards

*Drafted by technical-artist 2026-11-04. The character rows are adapted to the pre-rendered ronin. Budgets are
proposals until profiled on the Mesa iGPU (`project.yaml` has no performance block).*

**Unit:** 1 texel = **2.5 cm** on the play plane (480×270, 40 texels/unit, ×4 point upscale to 1080p).

### Formats, naming, locations
- Meshes: FBX from Blender 5.2, 1 unit = 1 m, transforms applied. Every asset has a rebuild script in
  `tools/art/<name>/` (never imported, run with absolute paths); hand-edited FBX is rejected.
- Textures: PNG (no JPG). Colour textures are sRGB; normal and mask maps are linear. Sheets and hero textures
  are uncompressed with Point filtering.
- Locations:
  - props: `Assets/Render3D/Resources/Props3D/snake_case.fbx`
  - character sheets: `Assets/Resources/Art/Characters/<Kind>/` (PascalCase, ronin included)
  - ronin bake source (model, materials, Mixamo clips): `Assets/Art3D/Ronin/`
  - shaders: `Assets/Render3D/Resources/Shaders/`
  - keeper materials: `Assets/Render3D/Resources/ShaderVariants/Keep_<Lit|Cutout|Fade>_<n>.mat`
- Anything loaded by string or built at runtime lives under a `Resources/` folder.

### Characters are pre-rendered sprites
- All characters ship as sprite sheets at 40 px/unit: albedo + normal + emissive mask, Point filter, no mips.
  The ronin's 3D model is only a bake source (`RoninSpriteBaker`). Each frame is rendered orthographically at
  the in-game angle with no anti-aliasing.
- Loops are baked at about 12 fps; attacks at 20–24 fps, keeping the impact frame. Positions snap to the 1/40
  grid at runtime.
- Acceptance gate (pixel crawl): across 30 captured frames of a held idle, no silhouette or emissive pixel may
  change between frames that show the same animation frame.

### Low-res legibility (hard rules)
- **Minimum readable feature: 2 texels (5 cm)** in its thinnest on-screen dimension (blades, cables, railings,
  trims). Exaggerate the geometry if needed.
- **Minimum emissive feature: 3×3 texels**, or a line 2 texels wide and at least 6 long. No 1-texel emissive
  rings.
- Accent specks are at least 2×2 texels; anything smaller is folded into the base colour at low contrast.
- No fresnel rims or outlines on characters. No specular highlight smaller than 3 texels (bake sources use
  roughness ≥ 0.6).
- Prop texture density is at most 40–64 px/unit. Sizes: 64/128/256 for filler props, 512 maximum for a hero
  prop.
- Filtering:
  - sheets: Point, no mips
  - prop textures: Bilinear with mips, aniso 1 (mips are required, or dense textures shimmer)
  - render target: Point, no mips, no MSAA, with the 1-texel guard border

### Budgets (1080p/60, mid laptop iGPU)
| Category | Tris | Draw calls | Mat slots |
|---|---|---|---|
| Character sprite quad (player, enemy) | 2 | 1 | 1 |
| Hero prop | 2k | 1–2 | 2 |
| Filler prop | 500 | 1 (batched/instanced) | 1 |
| Foreground occluder | 800 | 1 | 1 |
| Far city card | 2 | 1 (shared atlas) | 1 |

- At most 2 material slots per object.
- Scene totals: ≤ 300 draw calls (SRP Batcher on), ≤ 150k triangles, ≤ 64 MB of textures.
- Frame time 16.6 ms (CPU 8, GPU 10). The post stack (Bokeh DOF, bloom, grade, grain) takes ≤ 1.5 ms.
- Transparent overdraw ≤ 3×.
- Merge props only within one depth layer. When over budget, cut shadowed lamps first.

### Lighting
- Shadowed lights per scene: 1 directional key (1 cascade, 2048) plus at most 4 shadowed lamps
  (`MaxShadowedLamps = 4`).
- Unshadowed point lights: at most 12 visible (Forward+), 4 per object, choosing those nearest the view centre.
- No dedicated per-character lights. Characters are lit by the scene key and the lamps through their normal
  maps.

### Shader variants and builds
- URP Lit keywords set at runtime are stripped from player builds. `Integration/ShaderVariantKeeper.cs` keeps
  the 12 `Keep_*` materials (Lit 0–7, Cutout 0–1, Fade 0–1).
- To add a variant:
  1. Duplicate a `Keep_*.mat`.
  2. Enable the keywords.
  3. Register it in the keeper.
  4. Verify it in a Linux64 player build with a screenshot; editor-only checks don't count.
- Every runtime keyword change needs a matching keeper. Targets: Linux and Windows desktop.

## 9. Reference Direction

1. **Replaced** (Sad Cat Studios, 2026), the primary reference.
   - Take: the low-res 3D scene, the dense mid-ground, one key light, wet ground, one grade per area, the orange
     minimal HUD, the red eye-dot enemies.
   - Avoid: its layouts, characters, logos and text.
2. **Blade Runner 2049.**
   - Take: single-colour environments (orange dust, teal fog), hard light shafts, scale.
   - Avoid: its empty, still frames; ours need density and motion.
3. **Samurai Jack.**
   - Take: bold silhouettes, stylised speed lines, flat colour for hero poses.
   - Avoid: flat outlined 2D rendering and a bright saturated palette.
4. **Ghost in the Shell (1995).**
   - Take: layered city haze, rain, quiet scale shots.
   - Avoid: its cyan-green-only palette; we keep a hue per level.
5. **Dead Cells** (Motion Twin).
   - Take: 3D models baked to pixel sprite frames with normal maps, which keeps animation fluid and pixels
     stable.
   - Avoid: its bright, saturated palette and thick readability outlines.

## Decisions log (orchestrator, 2026-11-04)

- **Pre-rendered ronin** (user: "yes" / "i dont care about my previous calls"). It replaces the live 3D
  character. The live path stays behind a toggle as a fallback.
- **Tier 3 is white-hot `#FFF4E8`, not crimson.** Crimson is reserved for enemies and danger, and luminance
  rises with tier.
- **Letterbox 2.0:1 in gameplay, not 2.4:1.** At 40 texels/unit, 2.4:1 leaves only 5 units of vertical view,
  too little for drones and hook swings. 2.0:1 keeps 6 units; 2.4:1 is used only in cinematic moments.
- **No FOV push at tier 3.** A FOV change breaks the 40 texels/unit grid, so tier 3 uses vignette and rain
  streaks instead.
- **Dead Cells replaces Hollow Knight** in section 9, to document the baking technique.
- **The hat no longer glows.** The baked ronin's emissive hat band read as a large outline ring around him (the
  light outline the user rejected three times). Tier now lives on the katana only.
- **The ronin's follow rim and fill lights are removed** (section 8: no per-character lights).
