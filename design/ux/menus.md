# UX Spec: Menus (all screens)

> **Status**: In Design (Phase 1b)
> **Author**: user + ux-designer
> **Last Updated**: 2026-11-04
> **Journey Phase(s)**: Boot -> Main menu -> Play; Pause; End of run -> Upgrades -> Continue; Settings; Credits. (player-journey doc absent.)
> **Screen / Flow Name**: `MainMenu`, `PauseMenu`, `SettingsScreen`, `UpgradeScreen`, `CreditsScreen`, `EndRunPrompt`, `ResetConfirm`, shared `MenuKit`
> **Platform Target**: Desktop; keyboard + gamepad (mouse hover/click supported but secondary).
> **Related GDDs**: none located. Code: `Assets/Scripts/Game/MenuScreen.cs`, `MenuKit.cs`, `SettingsScreen.cs`, `UpgradeScreen.cs`, `Audio/CreditsScreen.cs`.
> **Related ADRs**: none
> **Related UX Specs**: `design/ux/hud.md`
> **Accessibility Tier**: **Standard (ASSUMED, uncommitted)**
> **Style**: `design/art/art-bible.md` sections 4 and 7 (binding). Template: UX Spec.

---

## 1. Purpose & Player Need

Menus are a thin terminal between runs: get from "I died" to "I'm playing again" in the fewest presses, and let the player tune the game without fuss. For players chasing flow, every menu second is a flow break.

- **Player goal**: start, resume or retry within two inputs; spend points on upgrades in under 20 s.
- **Game goal**: capture settings and upgrade purchases, and keep the player in the loop.

## 2. Player Context on Arrival

| Question | Answer |
|---|---|
| Just doing | Dying/finishing (upgrades), pausing mid-fight, or cold boot |
| Emotional state | Adrenaline, mild frustration after death; calm at boot |
| Cognitive load | Low after death, but wants to restart fast |
| Known info | Their run result |
| Likely goal | Retry. Second: spend points. Rarely: change settings |
| Fear | Losing progress (reset), mis-pressing a destructive option, rebind conflicts |

**Emotional target**: a dark, quiet terminal that feels like Replaced: calm, in control, one step from the action.

## 3. Navigation Position

```
Boot (title splash, 2.4:1)
  Main Menu
    PLAY -> (training prompt on first run) -> Level 1 run
    CREDITS -> Credits
    SETTINGS -> Settings (Audio, Display, Graphics, Gameplay, Controls)
    QUIT
Run
  Pause (overlay, game paused)
    RESUME / RESTART / SETTINGS / MAIN MENU (mapped to existing actions)
  End of run (banner, HUD) -> Upgrades
    Upgrades: VITALITY / VELOCITY / GRAPPLE, BUY, RESET PROGRESS -> Reset confirm, CONTINUE (new run), MENU
```

Modal behaviour: Main, Settings, Upgrades, Credits are full-screen (opaque near-black). Pause is overlay (game paused; scene dimmed to 25%). Reset confirm is modal over Upgrades, dismiss with Back/Esc (= NO).

| Entry | Trigger | Notes |
|---|---|---|
| Main menu | Boot, MENU from Upgrades/Pause | |
| Pause | Esc / Start in run | Training included |
| Settings | Main menu or Pause | Returns to its caller |
| Upgrades | Restart/Select on end-of-run banner; menu optional | |
| Credits | Main menu | |

## 4. Entry & Exit Points

| Trigger | Source | Transition | Data in |
|---|---|---|---|
| Boot | App | Cut from black, 3 frames | Saved settings |
| PLAY | Main | Letterbox 2.4 -> 2.0 stepped | Level 1, upgrades |
| Pause | Run | Instant | Run state |
| Run end prompt | Banner | Cut | Points earned |

| Exit | Destination | Data saved |
|---|---|---|
| Back / Esc / B | Parent | Settings applied live and saved on change |
| CONTINUE | New run | Upgrades |
| MENU | Main | Upgrades |
| QUIT | Desktop | Settings |

Esc never quits the game from inside a screen. QUIT is only on Main and needs no confirm.

## 5. Layout Specification

Screen is drawn inside the 2.4:1 picture (cinematic bars, ~140 px at 1080p). Text unit T = 4 px at 1080p. Type: 16 T headings, 12 T values and list entries, 8 T labels and hints. Colour: orange `#F08A3C` list, bone `#E8E2D4` for the selected row's text, near-black `#0B0A0C` background. No panels or frames; the selection bar is a solid orange bar with near-black text? No: a **1 T-tall rule beneath is not enough**; the selected row is a **filled orange bar behind the row with near-black text inside** (Replaced terminal inverse selection, art-bible "selection bar"). This is the single permitted fill.

### 5.1 Wireframes

Main menu
```
[bar]
   REDLINE                                  (16 T, orange, left, 24 T from left)

   > PLAY      <- inverse selection bar
     SETTINGS
     CREDITS
     QUIT

   W/S - D-PAD  SELECT      ENTER - A  CONFIRM    (8 T, dim, bottom-left)
[bar]
```

Pause
```
   PAUSED   (16 T)
   > RESUME
     RESTART
     SETTINGS
     MAIN MENU
   [Esc] RESUME          (8 T hint)
```

Settings (one column list, section headers 8 T dim)
```
   SETTINGS
   AUDIO
     MASTER            [||||||||..]  80
     MUSIC             [||||||....]  60
     EFFECTS           [||||||||||] 100
   DISPLAY
     FULLSCREEN        < ON >
   GRAPHICS
     MODE              < CINEMATIC 3D >    (also CLASSIC 2D)
   GAMEPLAY
     TUTORIAL HINTS    < ON >
     (proposed) HUD TEXT SCALE, HUD OPACITY, REDUCED MOTION, HIGH CONTRAST
   CONTROLS  KEYBOARD - GAMEPAD IS FIXED
     JUMP              [SPACE]  [  --  ]    slot 1 / slot 2
     ...
     RESET CONTROLS
   BACK
   status line: ENTER REBIND - BACKSPACE CLEAR - A/D PICK SLOT OR CHANGE VALUE - ESC BACK
```
Volume bars use block characters (`|` and `.`) not graphic fills, with the number alongside (not colour or length alone).

Upgrades
```
   UPGRADES                             POINTS 12
   > VITALITY   LV 2/5   +1 HP        COST 4
     VELOCITY   LV 0/5   ...          COST 3
     GRAPPLE    LV 1/5   ...          COST 3
   BUY (ENTER)
   RESET PROGRESS
   CONTINUE
   MENU
   (empty) NO POINTS THIS RUN
```
Level shown as `LV 2/5` plus pip squares `##...`. Cost is always written. Unaffordable: dimmed to 40% with the label `NEED 4` (not colour alone). Maxed: `MAX`.

Reset confirm (centred, no frame)
```
   RESET ALL PROGRESS?
   ALL POINTS AND UPGRADES WILL BE LOST
     > NO        (default focus)
       YES
```

Credits: title, scrolling list (REDLINE, MUSIC and others), BACK; scroll W/S - D-pad, `ESC - B BACK`.

End-run prompt is the HUD banner (see hud.md); this spec's contract: `[R] / [START]` -> Upgrades. Button label from `ControlLabels`.

### 5.2 Layout zones

| Zone | Description | Size | Scroll | Overflow |
|---|---|---|---|---|
| Title | 16 T heading, left | top 12% of picture | No | Truncate never; 24 chars max |
| List | Rows 16 T pitch (12 T text), left-aligned at 24 T | ~70% | Yes (Settings, Credits) | Scrolls; selection kept in view with a 1-row margin; `...` arrows at edges |
| Hint line | 8 T dim | bottom 8 T | No | Wraps to 2 lines |

### 5.3 Component inventory

| Component | Type | Purpose | Reuse |
|---|---|---|---|
| MenuList + SelectionBar | List | All screens | `MenuKit` (restyle: remove brackets, panels, glows) |
| Slider row | Value row | Audio | Existing, restyle to block chars |
| Toggle/Choice row | `< A >` choice | Fullscreen, mode, hints | Existing |
| Keybind row (2 slots) | Row | Controls | Existing in `SettingsScreen` |
| Confirm dialog | Modal | Reset | `UpgradeScreen` |
| Status line | Text | Rebind feedback | Existing |
| Hint line | Text | Controls help | Existing; label text from `ControlLabels` |

Primary focus on open: Main = PLAY; Pause = RESUME; Settings = first row (MASTER); Upgrades = first affordable upgrade else CONTINUE; Reset confirm = NO; Credits = BACK.

### 5.4 Information hierarchy
Title (where am I) -> list (what can I do) -> selected row's detail (what it does/costs) -> hint line. Upgrades: points first (top-right), then the list. After death, CONTINUE should be one press away: **Upgrades puts CONTINUE as the default-focus when points are 0 or nothing is affordable.**

## 6. States & Variants

| State | Trigger | Visual | Behaviour |
|---|---|---|---|
| Normal row | n/a | Orange text | |
| Selected | Nav/hover | Orange bar, near-black text | Stepped move: 2 frames |
| Disabled | Unaffordable/maxed | 40% orange + `NEED n` / `MAX` | Confirm plays "refuse" sound and nudges bar 1 T |
| Rebinding | Enter on bind row | Row reads `PRESS A KEY FOR <ACTION>` blinking 1 Hz | Esc cancels (`REBINDING CANCELLED`); next key press binds |
| Rebind conflict | Key used | Status: `SWAPPED: <A> <-> <B>` or `REFUSED: <reason>` | Never silently steals; reserved keys: `THAT KEY CANNOT BE BOUND` |
| Cleared | Backspace | `KEY CLEARED FROM <ACTION>`; refuse if it would leave an action unbound (`REFUSED: ...`) | |
| Reset controls | Select | `CONTROLS RESET TO DEFAULTS` | Defer confirm: single press OK (cheap, reversible by rebinding) |
| Empty upgrades | 0 points | `NO POINTS THIS RUN` | CONTINUE focused |
| Reset confirm | RESET PROGRESS | Modal | Default NO |
| Gamepad disconnected | any | `GAMEPAD DISCONNECTED` status line 3 s | Keyboard continues |

## 7. Interaction Map

### 7.1 Navigation

| Input | Action | Visual | Audio |
|---|---|---|---|
| W/S, Up/Down, D-pad up/down, left stick | Move selection | Bar steps 2 frames | tick |
| A/D, Left/Right, D-pad L/R | Change value / bind slot | Value steps | tick |
| Mouse hover | Select row | Same | tick |
| Scroll wheel | Scroll Settings/Credits | | |

### 7.2 Actions

| Input | Context | Action |
|---|---|---|
| Enter / Space / Pad A / click | Any row | Confirm: open, buy, begin rebind |
| Esc / Pad B / right-click | Any | Back (Reset confirm = NO; Rebind = cancel) |
| Backspace | Bind row | Clear slot |
| Held direction | Slider | Repeat after 350 ms at 12 Hz |
| R / Start | End-of-run | To Upgrades |
| Esc / Start | Run | Pause |
| Select | End-of-run | To Upgrades |

Gamepad bindings are fixed (documented as such on the Controls header); note this is a Standard-tier gap: gamepad rebinding is not offered (see open questions).

### 7.3 Restrictions
During transitions inputs queue 1 press; during rebind capture, navigation is disabled except Esc; Reset confirm blocks everything behind it.

## 8. Data Requirements

| Data | Source | Update | Owner | Missing handling |
|---|---|---|---|---|
| Volumes, fullscreen, mode, hints | `GameSettings` | On change | GameSettings/SettingsSave | Defaults |
| Key bindings | Input bindings | On rebind | Input system | Defaults |
| Points, upgrade levels, costs | Progress/Upgrades | On buy/run end | Progression | 0 / LV 0 |
| Run summary | Sim | On end | Sim | Dash |
| Control labels | `ControlLabels` | On device change | Input | Key name |

UI reads and fires events only.

## 9. Events Fired

| Action | Event | Payload | Receiver |
|---|---|---|---|
| Change setting | `SettingChanged` | key, value | GameSettings/SettingsSave (saved immediately) |
| Rebind | `BindingChanged` | action, slot, key, swapped? | Input |
| Reset controls | `ControlsReset` | | Input |
| Buy | `UpgradePurchased` | id, level | Progression |
| Reset progress | `ProgressReset` | | Progression (destructive, confirmed) |
| Continue / Menu / Play / Quit | `FlowRequested` | target | Game flow |

## 10. Transitions & Animations

| Transition | Type | Duration | Easing | Interruptible | Reduced Motion |
|---|---|---|---|---|---|
| Screen enter/exit | 3-frame stepped dither/cut from black | ~50 ms | Stepped | Yes | Instant |
| Selection move | 2-frame step | 33 ms | Stepped | Yes | Instant |
| Letterbox 2.0 <-> 2.4 | 3-step | ~100 ms | Stepped | Yes | Instant |
| Text typing on title | none (instant) | 0 | | | n/a |
| Rebind prompt blink | 1 Hz | | | | Steady |

No bloom, glow, scale pops or blur on UI. Menu music/ambience carry the mood.

## 11. Input Method Completeness

**Keyboard**: [ ] every row reachable with W/S/arrows; [ ] A/D adjusts; [ ] Esc backs, never quits; [ ] selection always visible (bar). 
**Gamepad**: [ ] D-pad/stick navigate; [ ] A confirm, B back; [ ] no analogue requirement; [ ] disconnect handled (status line, keyboard fallback); [ ] rebinding pad is not supported (fixed layout, documented).
**Mouse**: [ ] hover selects; [ ] rows >= 12 T high (48 px) hit target; [ ] right-click = back; [ ] wheel scrolls.

## 12. Accessibility (assumed Standard)

Contrast: orange on `#0B0A0C` ~8:1 (pass); selected row near-black on orange ~8:1 (pass); dim hint text (50% orange) must be >= 4.5:1, so use 60% minimum, measure; disabled 40% is exempt but carries `NEED`/`MAX` text. Bone ~15:1.

Colour-blind: no information in colour only: selection = inverse bar, disabled = text, rebind state = text.

Focus order: top to bottom in the visible list; wrap at ends; Settings sections are headers (not focusable). Reset confirm traps focus.

Screen reader: Unity 6.3 desktop screen-reader support exists; announce screen title on open, row label + value on focus (`MASTER VOLUME 80 PERCENT`), status-line changes (rebind results). Needs ui-programmer feasibility confirmation.

Cognitive load: <= 8 rows per screen group; Settings grouped by section headers; Upgrades 3 items + 4 actions.

Proposed settings additions: HUD Text Scale, HUD Opacity, Reduced Motion, High Contrast (outline on), and "Menu Text Scale" (menus scale by integer pixel steps; at 150% rows reflow by dropping the hint line to two lines).

## 13. Localization

All strings from a table (currently hardcoded English; flag). Budget 40% expansion: row labels <= 20 chars at 12 T within a 90 T column; hint line wraps to 2 lines; pixel font must support target scripts (Latin-extended only unless decided). RTL not targeted.

## 14. Acceptance Criteria

- [ ] Main menu to gameplay in <= 2 inputs; death to new run (via Upgrades, nothing affordable) in <= 2 inputs.
- [ ] Every screen is fully operable with keyboard only and with gamepad only (except pad rebinding).
- [ ] No panel, bracket, glow, bloom; single fill is the selection bar.
- [ ] Text >= 8 T, headings 16 T, at 1280x720, 1920x1080, 3440x1440.
- [ ] Contrast >= 4.5:1 for all non-disabled text (measured).
- [ ] Reduced Motion makes all transitions instant.
- [ ] Rebinding: conflict swap/refuse messages shown; no action ever ends unbound; Reset Controls works; persists across restart.
- [ ] Reset progress needs confirm; default focus NO; Esc = NO.
- [ ] Settings persist; Esc never quits.
- [ ] Screenshots of every screen retained in `production/qa/evidence/` (Main, Pause, each Settings section, Upgrades normal/empty/maxed/reset-confirm, Credits, Rebinding states).

## 15. Open Questions

| Question | Owner | Resolution |
|---|---|---|
| Confirm Standard tier; commit accessibility-requirements.md | user | Open |
| Gamepad rebinding wanted? | user | Open (fixed today) |
| Pause menu items: is RESTART wanted (fast retry for flow players)? | game-designer | Open, recommended yes |
| Menu "Classic 2D" mode: art direction rejected 2D; keep or drop the toggle? | user/art-director | Open, recommend drop |
| Pixel font and licence | art-director | Open |
| Screen reader feasibility for runtime UI Toolkit | ui-programmer | Open |
| Replaced menu frames not inspected in this pass; spec follows art-bible text | ux-designer | Open |
