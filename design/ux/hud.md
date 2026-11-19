# HUD Design

> **Status**: In Design (Phase 1b, restyle of the existing UI Toolkit HUD)
> **Author**: user + ux-designer
> **Last Updated**: 2026-11-04
> **Game**: Redline
> **Platform Targets**: Desktop (Linux/Windows), keyboard + gamepad. No touch. Min 1280x720 picture window, reference 1920x1080.
> **Related GDDs**: none located (game-concept, player-journey, interaction-patterns absent). Sources of truth: `Assets/Scripts/Game/RedlineHud.cs`, `TrainingOverlay.cs`, `TutorialOverlay.cs`.
> **Accessibility Tier**: **Standard (ASSUMED, not committed)**. Requires user/accessibility-specialist confirmation. Reduced-motion and text-scale are included here as if Comprehensive-lite because they are cheap in this design.
> **Style Reference**: `design/art/art-bible.md` section 4 (Color System) and section 7 (UI/HUD Visual Direction), binding.
> **Template**: HUD Design

---

## 1. HUD Philosophy

Redline's HUD is a quiet instrument cluster, in the manner of Replaced: thin orange line glyphs and pixel-terminal text sitting directly on the picture. No panels, frames, fills, brackets or drop shadows. (Today's HUD uses tinted panels, corner brackets, cyan/crimson panels, text shadows and 64+ px numerals. All of that is removed.) The player is chasing flow and must read the *speed tier* in peripheral vision and *HP* at a glance; everything else is secondary and small.

**Visibility principle**: SHOW the five survival facts always (speed/tier, HP, wave, foes, chain when active); HIDE everything else until it is contextually relevant (buffs, overspeed, armor-break hint, pickup toast). When in doubt, hide.

**Rule of Necessity**: A HUD element earns its place when the player would otherwise have to look away from their own character, or lose a run, to learn it.

**Pillar: never slow the player.** No HUD element animates in a way that blocks view or takes input; all are display-only.

---

## 2. Information Architecture

### 2.1 Full Information Inventory

Speed (numeric + meter), speed tier (1-3), tier thresholds, overspeed (above max, buffed air cap), "breaks armor" status (tier 3 hits armour), HP current/max, wave current/total, level number/total/name, foes alive, kill chain count, chain decay timer, speed-orb buff remaining, hook-orb buff remaining, pickup event (+HP / SPEED / HOOK), wave-start countdown ("GET READY", "FINAL WAVE"), game-over summary (wave reached, kills, restart prompt), victory summary (all levels cleared), training step/progress, first-sight hint cards and level hint zones, control prompts (labels from `ControlLabels`), enemy telegraphs (world, not HUD), points earned (shown in upgrades, not HUD).

### 2.2 Categorization

| Information | Always | Contextual | On demand | Hidden/diegetic | Reasoning |
|---|---|---|---|---|---|
| Speed + tier + meter | X | | | | Core skill feedback; tier gates damage |
| HP | X | | | | Survival |
| Wave n/total | X | | | | Pacing; minimal text |
| Foes alive | X | | | | Tells when wave ends |
| Level name/number | | X (first 3 s of level, then dim) | Pause screen | | Rarely needed mid-fight |
| Chain xN | | X (chain >= 2) | | | Noise at 0 |
| Chain decay line | | X (with chain) | | | Urgency of keeping chain |
| Overspeed strip | | X (above max speed) | | | Rare state |
| Armor-break tag | | X (tier 3 only) | | | Replaced by tier numeral + glyph; see 4.2 |
| Buff timers | | X (while active) | | | Hidden when none |
| Pickup toast | | X (1.0 s) | | | Event confirm; glyph + word |
| Wave countdown / banners | | X | | | Cinematic moments |
| Game-over/victory | | X | | | End of run |
| Telegraphs | | | | X | Art/audio, not HUD |
| Total points | | | Upgrade screen | | Reward after run |

---

## 3. Layout Zones

All positions are offsets from the **picture edge** (the area between the letterbox bars), never from the screen edge. HUD never draws on the bars. Gameplay picture is 2.0:1 (1920x960 at 1080p; bars 60 px each). Cinematic picture is 2.4:1 (~1920x800; bars ~140 px). The HUD is hidden or reduced during cinematic states (section 5), so the layout is authored for 2.0:1 and re-anchors automatically to the live picture rect (`Pixel3DCamera.LetterboxPixels`).

Unit: **T = 1 texel = 4 px at 1080p** (= picture width / 480). All sizes below are in T, so they scale with resolution. Safe margin **M = 8 T (32 px at 1080p)** from picture left/right/top/bottom (desktop; no TV overscan needed).

### 3.1 Zone Diagram

```
 [ letterbox bar, 60 px, black, empty ]
 +--------------------------------------------------------------+
 | M                                                        M   |
 | <> <> <> <>   (HP)         LEVEL 1/3 ... (3 s)  WAVE 2/5    |
 |                                                  FOES 7      |
 |                                                              |
 |                       (play area: nothing)                   |
 |                            +1 HP (toast near player)         |
 |                                                              |
 |  x7  (chain)                                                 |
 |  |||/|||/|||   (speed meter, tier colour)                     |
 |  2  ##  23.4  HOOK 4s  SPEED 3s                               |
 +--------------------------------------------------------------+
 [ letterbox bar ]
```

### 3.2 Zone Specification Table

| Zone | Position (from picture edge) | Elements | Max simultaneous | Notes |
|---|---|---|---|---|
| Top-left | left M, top M | HP diamonds | 1 row (max HP up to 10) | Read first by eye habit (Replaced) |
| Top-right | right M, top M, right-aligned | Level tag (transient), WAVE, FOES | 3 lines | Right-aligned text |
| Bottom-left | left M, bottom M | Speed meter, tier numeral + pips, speed value, buff chips; chain sits directly above meter | 6 | Primary peripheral read |
| Centre 40% | n/a | Nothing, except banners (cinematic) and pickup toast following the player | 1 | Keep clear |
| Bottom-centre | bottom M, centred | Training/hint cards (one at a time) | 1 | Max 2 lines |

Safe margins: desktop 32 px at 1080p (all sides, from picture edge). Windowed/ultrawide: the picture rect rules; HUD never spreads to the window edge.

---

## 4. HUD Element Specifications

Type: pixel monospace terminal font, **labels 8 T (32 px), values 12 T (48 px), headings 16 T (64 px)**; nothing smaller than 8 T. Colours: orange `#F08A3C` default; bone `#E8E2D4` active value; crimson `#E5232E` low HP only; tier colours on speed meter only. No text shadow. No background. Where text sits over bright scenery, a 1 T hard-edged near-black outline (pixel stroke, not blur) is allowed.

### 4.1 Element Overview Table

| Element | Zone | Always | Trigger | Data source | Update | Size | Min readable | Priority | Non-colour alt |
|---|---|---|---|---|---|---|---|---|---|
| HP diamonds | TL | Yes | n/a | Player HP | on change | 8 T glyph each, 3 T gap | 8 T | 1 | Filled vs hollow; flash + count text at low |
| Speed meter | BL | Yes | n/a | Sim speed | each frame | ~80 T wide x 2 T line | 2 T line + ticks 4 T | 2 | Ticks at tier thresholds, tier numeral, pips 1-3 |
| Tier numeral + pips | BL | Yes | n/a | Sim tier | on change | 12 T numeral | 12 T | 2 | Numeral and pip count |
| Speed value | BL | Yes | n/a | Sim speed | 10 Hz | 12 T | 12 T | 3 | Number |
| Chain | BL above meter | No | chain >= 2 | Sim chain | on kill | 12 T "x7" + 1 T decay line | 12 T | 4 | Number + shrinking line |
| Wave | TR | Yes | n/a | Sim wave | on change | label 8 T, value 12 T "2/5" | 8 T | 3 | Text |
| Foes | TR | Yes | n/a | Sim enemies | on change | 8 T label, 12 T value | 8 T | 3 | Text |
| Level tag | TR above wave | No | level start 3 s | Game level | on level | 8 T | 8 T | 6 | Text |
| Buff chips (SPEED, HOOK) | BL right of speed value | No | buff active | Orb timers | each frame, text 1 Hz | glyph 8 T + secs 8 T | 8 T | 5 | Glyph shape + seconds number (not colour) |
| Overspeed | BL under meter | No | speed above max | Sim | each frame | 1 T line + text 8 T "OVER" | 8 T | 5 | Text |
| Armor-break | BL | No | tier 3 | Sim | on change | folded into tier numeral glyph (see below) | | 6 | Glyph |
| Pickup toast | at player | No | orb pickup | Orb system | 1.0 s | 8-12 T | 8 T | 4 | Glyph (plus / chevron / ring) + word |
| Banner / sub-banner | centre | No | wave start, game over, victory | Sim state | on state | 16 T / 8 T | 8 T | 1 (transient) | Text |
| Hint card | bottom-centre | No | first sight / zone / training | Tutorial/Training | on trigger | 8 T text, max 2 lines | 8 T | 5 | Text + control label |

### 4.2 Element Detail Blocks

**HP diamonds**
- Visual: row of 8 T diamond line glyphs (rotated square, 1 T stroke). Full = orange outline with orange fill-free centre dot? No fills: full = solid orange line diamond with a 2 T centre pixel; lost = hollow outline, 40% orange. Spacing 3 T.
- Data: current/max HP; max changes with upgrades (up to cap; if > 10, wrap to a second row of 10).
- Update: lost diamond flashes bone 3 frames, then hollow (stepped). Heal: diamond fills in 2 frames.
- Urgency: at 1 HP left the last full diamond turns crimson and blinks at 2 Hz (stepped on/off) plus the numeral "1" appears right of the row (non-colour alt). Reduced Motion: no blink; crimson steady plus numeral.
- Interaction: display-only. Customization: global HUD opacity and text scale.

**Speed meter, tier numeral and pips**
- Visual: a 2 T line, tick marks 4 T tall at tier-2 and tier-3 thresholds (extending 1 T above and below); the filled portion is the same line in the current tier colour. Left of it, tier numeral "1/2/3" in 12 T in tier colour; to the right of the meter end, three pips (4 T squares): lit = tier count, remaining hollow. Speed value right below as bone "23.4" (12 T).
- The cyan/orange full-height tier-coloured panel and "TIER 1" tag are removed. Hit-armour: at tier 3 a small blade glyph (8 T) appears beside the numeral instead of the text "BREAKS ARMOR"; the first time in a run it is accompanied by a one-off hint card.
- Update: fill tracks speed directly (no lerp lag; lag would mislead); tier change steps the colour and flashes the numeral bone for 3 frames, with the tick crossed.
- Colour-blind: tier shown by numeral, pip count and threshold position; adjacent tiers differ >= 25% luminance (art bible).
- Interaction: display-only.

**Chain**
- "x7" in 12 T, bone, directly above the speed meter (left-aligned to it) with a 1 T decay line under it in crimson-free orange that shrinks to the left. Flashes bone for 2 frames on each kill; no scale pop (a stepped +1 T nudge is allowed unless Reduced Motion). Disappears with a 2-frame step when the chain breaks. The caption "CHAIN" is dropped (the "x" suffices).

**Wave / Foes**
- Right-aligned: `WAVE 2/5` (label 8 T orange, "2/5" 12 T bone) and `FOES 7` below. Wave progress dots are removed (the n/total says it). FOES value flashes bone 2 frames when it changes; at 0 it reads `FOES 0` until the next wave starts.
- Level tag: `LEVEL 1/3 EMBER` 8 T dim orange (50%), top of the right column, shown for 3 s from level start then fades by a 3-step stepped fade to nothing (stays in pause screen).
- Training: wave panel hidden (existing behaviour).

**Buff chips**
- Glyph (speed chevron, hook ring, 8 T) + seconds ("4S") in 8 T. Orange default; the orb colour may tint the glyph only, never alone (glyph shape differs). Under 2 s remaining: seconds blink, stepped, bone.

**Overspeed**
- A 1 T line under the meter beyond the max speed mark, orange, and the text `OVER` 8 T at the line end while active. Else hidden.

**Pickup toast**
- Appears above the player, 8 T glyph + word (`+1 HP`, `SPEED`, `HOOK`) in orange (bone for the number), rises 4 T in 3 steps over 1.0 s then vanishes in 2 frames. Orb world colour identifies the pickup in the world; the toast uses glyph + word, so colour is redundant.

**Banners (cinematic)**
- `GET READY - 3` / `FINAL WAVE - 3`: 16 T bone, centred, with 8 T sub-line. These appear during the 2.4:1 wide-bar state; HUD corners fade out meanwhile.
- Game over: 16 T `RUN ENDED`, 8 T `WAVE 4 - 31 KILLS`, then prompt `[R] / [START] UPGRADES` (see menus.md; label comes from `ControlLabels`). The prompt must pulse neither fast nor flash; stepped 1 Hz blink is allowed (off under Reduced Motion).
- Victory: `ALL 3 LEVELS CLEARED` / kills / same prompt.

**Hint cards (tutorial / training)**
- Bottom-centre, 8 T orange text, key label in bone inside square brackets `[SPACE]`; max 2 lines of 48 characters; no panel. Training adds `STEP 3/8` in 8 T dim above, and the result words `NICE` / `SKIPPED` 1 s. The skip prompt is always shown in the card for training.

---

## 5. HUD States by Gameplay Context

| Context | Shown | Hidden | Modified | Transition |
|---|---|---|---|---|
| Combat (gameplay, 2.0:1) | HP, speed block, wave/foes, chain, contextual items | Banners | none | n/a |
| Between waves / countdown (2.4:1) | HP, speed block | Wave/foes (dimmed 50%), chain | Banner centre | Bars widen in 3 steps (~100 ms); skip under Reduced Motion |
| Level intro | + level tag | | | Level tag 3 s |
| Training | HP, speed block, hint card | Wave/foes, level tag | Step counter | Instant |
| Pause | all HUD dimmed to 40%, frozen | | Pause menu overlay | Instant |
| Game over / victory (2.4:1) | Banner and summary only | All gameplay HUD | | Instant, stepped bar widen |
| Upgrades/menus | None (screens own the UI) | HUD | | n/a |

---

## 6. Information Hierarchy

| Element | Tier | Reasoning | If hidden |
|---|---|---|---|
| HP | MUST KEEP | Survival | Screen-edge vignette is NOT an option here (pixel purity); audio low-HP cue |
| Speed meter + tier numeral/pips | MUST KEEP | Core skill, damage gate | Katana glow length (art) |
| Wave / Foes | SHOULD KEEP | Progress | Banner at wave end |
| Chain | SHOULD KEEP | Score flow | Audio pitch ladder |
| Buff chips | SHOULD KEEP | Timed power | Orb world glow on player |
| Overspeed | CAN HIDE | Rare | none |
| Level tag | CAN HIDE | Transient | Pause screen |
| Speed numeric value | CAN HIDE | Meter conveys | none |
| Wave dots, "CHAIN" caption, "TIER" word, HP caption, panels, brackets | ALWAYS HIDE | Removed by restyle | |

---

## 7. Visual Budget

| Constraint | Limit | Method | Estimate | Status |
|---|---|---|---|---|
| Max simultaneous elements | 9 (HP, meter, tier, value, wave, foes, chain, 2 buff chips); toast and hint extra | Count visible | ~9 | To verify |
| Screen area, combat | <= 6% of picture | Pixel area of text/glyph bounds | est 3-4% | To verify |
| Centre 40% | 0% (banners/toast/hints only) | | | To verify |
| Contrast, orange `#F08A3C` on `#0B0A0C` | >= 4.5:1 (est. ~8:1); bone ~15:1; tier 1 `#9CC8FF` ~11:1 | Measure on bright level 1 backgrounds too, using the 1 T outline | | To verify |
| Crimson `#E5232E` on near-black | est ~4.4:1, below 4.5:1 | Redundant with numeral + blink; use only for large glyph | | CONCERN |
| Panel opacity | 0 (none) | | | Compliant |
| Min size | 8 T = 32 px at 1080p, 17 px at 720p | | | Compliant |

---

## 8. Feedback & Notification Systems

| Type | Trigger | Position | Duration | Animation | Max | Priority | Queue | Dismissible |
|---|---|---|---|---|---|---|---|---|
| Pickup toast | Orb collected | Above player | 1000 ms | 3-step rise, 2-frame vanish | 2 | Low | Same kind within 500 ms merges | No |
| Hint card | First sight, hint zone | Bottom-centre | 4000 ms or until done | 2-frame in/out | 1 | Med | Latest replaces; none during banners | Yes (any hint toggle in settings) |
| Tier change | Tier crossing | Speed block | 150 ms | Bone flash 3 frames | 1 | Med | Not queued | No |
| Low HP | HP == 1 | HP | persistent | blink 2 Hz | 1 | Critical | Bypasses everything | No |
| Banner | Wave/level/end | Centre | countdown or until input | stepped | 1 | High | Preempts hints | End screens: by input |

Rules: (1) hint cards never appear during a banner or at 1 HP; (2) identical toasts within 500 ms merge; (3) low HP and game-over are never queued.

---

## 9. Platform Adaptation

| Platform | Safe zone | Resolution | Input | Notes |
|---|---|---|---|---|
| Desktop | M = 8 T | 1280x720 to 3840x2160, any window; picture rect 2.0:1 | KB + gamepad | 720p: T = 2.67 px, 8 T text = 21 px; legible but near limit; do not go below 1280x720 |
| Steam Deck (if tested) | M = 8 T | 1280x800 -> 1280x640 picture | Gamepad | Same |

Control prompts adapt to last-used device (`ControlLabels`). Repositioning: **not required** (desktop only, five elements, fixed positions); revisit if console is added.

---

## 10. Accessibility (assumed Standard)

### 10.1 Colour-blind
| Element | Risk | Fix |
|---|---|---|
| Tier colour | Tier 1 blue vs 2 amber vs 3 white | Tier numeral, pips, tick position, luminance steps |
| HP crimson | Low-HP only by colour | Blinking diamond + numeral "1" |
| Buff orb colours | Colour meaning | Glyph shape + seconds number |
| Foes crimson (old) | Removed; foes are orange text | Text |

### 10.2 Text scaling
Setting: HUD Text Scale 100 / 125 / 150% (proposed; not yet in `GameSettings`, see concerns). At 150%, labels become 12 T and values 18 T (snapped to integer multiples of the pixel font to avoid blurry scaling: use 100% and 150% = 1.5 only if font is rendered at 12 T natively, else 100/200). Right column right-aligns so it grows leftwards; bottom-left grows upwards; chain stacks above the meter; buff chips wrap beneath the speed value.

| Element | 100% | 125% | 150% | Overflow |
|---|---|---|---|---|
| All | TBD | TBD | TBD | Wrap / stack, never overlap, hints may drop to 3 lines |

### 10.3 Motion sensitivity
| Animation | Severity | Reduced Motion | Replacement |
|---|---|---|---|
| Letterbox widen/narrow | Mild | Yes | Instant |
| Toast rise | Mild | Yes | Static, fade-out in 2 frames |
| HP blink / prompt blink | Moderate | Yes | Steady crimson / steady |
| Bone flashes on kill/tier | Moderate | Yes (limit to 1 frame, 50% intensity) | Steady colour change |
| Screen shake / postfx | Out of scope (game) | n/a | n/a |

No flash > 3 per second anywhere in the HUD (blinks are <= 2 Hz). Motion Reduction flag proposed in `GameSettings`.

### 10.4 Subtitles
No voiced dialogue currently; no subtitles required. If voice/diegetic text is added, spec: default ON, bottom-centre inside the picture, 8 T, max 42 chars, 2 lines, speaker name prefix.

### 10.5 Controls
HUD Opacity 40-100% (default 100%), HUD Text Scale, Motion Reduction, Tutorial Hints (exists), High Contrast (draws the 1 T outline on all HUD text; default OFF). All proposed except Tutorial Hints; add to the Settings screen (menus.md).

---

## 11. Tuning Knobs

| Parameter | Value | Range | Player adjustable | Notes |
|---|---|---|---|---|
| Margin M | 8 T | 4-12 T | No | Data-driven constant |
| Level tag duration | 3 s | 1-5 | No | |
| Toast duration | 1.0 s | 0.6-1.5 | No | |
| Hint duration | 4 s | 2-8 | No | |
| HUD opacity | 100% | 40-100 | Yes | |
| Text scale | 100% | 100-150 | Yes | |
| Low-HP blink | 2 Hz | 1-2 | No | |
| Buff blink threshold | 2 s | 1-3 | No | |
| Letterbox step time | ~100 ms (3 steps) | 0-250 | Reduced Motion -> 0 | |

---

## 12. Acceptance Criteria

**Layout**
- [ ] No HUD pixel renders on the letterbox bars at 2.0:1 or 2.4:1 at 1280x720, 1920x1080, 2560x1440, 3440x1440.
- [ ] All elements are anchored by offsets from the picture edge; changing the bar height moves them with no overlap.
- [ ] No panel, bracket, fill or text shadow exists in the HUD (art bible section 7).
- [ ] Nothing smaller than 8 T; at 1080p labels are 32 px, values 48 px.
- [ ] Centre 40% is empty except banner, toast, hint.
- [ ] Each context in section 5 shows exactly the listed elements.

**Accessibility**
- [ ] Removing colour (greyscale screenshot): tier, HP state, buffs all still discernible.
- [ ] Orange and bone text >= 4.5:1 on the lightest level background (L1 ember lit areas) with or without the outline.
- [ ] Reduced Motion removes every animation in 10.3; no element blinks faster than 2 Hz.
- [ ] Text scale 150% causes no overlap.
- [ ] Settings persist between sessions.

**Notifications**
- [ ] Same pickup within 500 ms merges; hint never appears during a banner; low HP never delayed.

**Screenshot evidence** (project rule): retained screenshots of combat, tier 3, chain, 1 HP, between-waves, game-over, training in `production/qa/evidence/`.

---

## 13. Open Questions

| Question | Owner | Deadline | Resolution |
|---|---|---|---|
| Confirm Standard accessibility tier (and whether text scale / Reduced Motion are required) | user + accessibility-specialist | before implementation | Open |
| Pixel font choice with licence (needs 8/12/16 T crisp) | art-director | Phase 2 | Open |
| Max HP cap after upgrades (diamond row wrap) | game-designer | Phase 2 | Open |
| Whether 720p (8 T = 21 px) is a supported minimum | user | Phase 2 | Open |
| Does Replaced reference show hearts as filled or outlined? Verified visually: not yet checked, spec follows art bible text | ux-designer | Phase 2 | Open |

### 13.1 Logged gaps (deferred by decision, 2026-11-04)

| Gap | Status | Note |
|---|---|---|
| HUD Opacity (40-100 %) | Deferred | Not in settings data; HUD renders at 100 % (40 % only while paused, via `RedlineHud.SetPaused`). |
| High Contrast (1 T outline on all HUD text) | Deferred | No outline is drawn today; measure L1 ember backgrounds before shipping. |
| Gamepad rebinding | Deferred | Gamepad layout is fixed and labelled as such on the Controls header. |
| Screen reader | Deferred | Unity 6.3 desktop screen-reader support not wired to the runtime UI Toolkit screens. |

### 13.2 Resolved in implementation (2026-11-04)

- Font: Departure Mono v1.500 (SIL OFL 1.1), `Assets/Resources/Fonts/DepartureMono-Regular.otf`; Source Code Pro is the in-engine fallback (VT323 not bundled).
- Low-HP crimson: `#E5232E` measures 4.34:1 on `#0B0A0C`; implemented as `#F0353F` (4.97:1), used only on the low-HP diamond and its numeral.
- Reduced Motion and HUD Text Scale (100/125/150 %) exist as optional fields in `SettingsData` and on the Settings screen.
- "CLASSIC 2D / CINEMATIC 3D" row removed from Settings; the `-render2d` flag and the stored `classic2d` value still work.
