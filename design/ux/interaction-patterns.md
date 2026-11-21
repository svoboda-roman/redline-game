# Interaction Pattern Library

> **Status**: Implemented (Phase 3, `/team-ui`), 2026-11-04
> **Style**: `design/art/art-bible.md` sections 4 and 7; Replaced-style terminal. Colours from `UiTheme`
> (orange `#F08A3C`, dim `#C37230`, bone `#E8E2D4`, ground `#0B0A0C`, crimson `#F0353F`).
> **Units**: T = picture width / 480 (4 px at 1080p). Menus use the fixed 1920x1080 panel (T = 4 px);
> the HUD derives T from `LetterboxInfo.PictureRect`.
> **Code**: `Assets/Scripts/Game/MenuKit.cs`, `UI/UiTheme.cs`, `UI/LetterboxInfo.cs`, `RedlineHud.cs`,
> `SettingsScreen.cs`, `UpgradeScreen.cs`, `MenuScreen.cs`, `Audio/CreditsScreen.cs`.

Every pattern: keyboard (W/S/arrows, A/D, Enter/Space, Esc, Backspace), gamepad (d-pad/stick, South, East,
North) and mouse (hover selects, click activates). Reduced Motion (`GameSettings.Current.ReducedMotion`)
makes every listed animation instant or steady. No blink is faster than 2 Hz.

## 1. Selection bar list
- **Use**: every menu list (main, pause, settings, upgrades, credits BACK, reset confirm).
- **Visual**: rows 16 T pitch, 12 T text, left-aligned at 24 T. Idle: orange text, no fill. Selected: solid
  orange bar behind the row, near-black text (the only fill in the UI). Disabled: 40 % orange plus a word
  (`NEED n`, `MAX`), never colour alone.
- **Input**: Up/Down move and wrap; Enter/South activate; Esc/East back; hover selects; click activates.
  The press that opened a screen is ignored (`_openedFrame`).
- **Code**: `MenuKit.MakeButton` + `MenuKit.StyleButton`; Settings uses `RowView` with the same look.

## 2. Stepped transition
- **Use**: screen enter/exit, selection move, HUD flashes, level tag fade, toast rise.
- **Rule**: no tweens or easing; values change in 2-4 discrete steps (e.g. level tag 100 / 75 / 50 / 25 / 0 %
  over 0.6 s; toast rises 4 T in 3 steps over 1.0 s; tier/foes/chain flashes last 2-3 frames).
- **Reduced Motion**: single cut; flashes limited to 1 frame at 50 %.

## 3. Rebind capture
- **Use**: Settings > Controls rows (two keyboard slots per action; gamepad is fixed and shown dim).
- **Flow**: A/D picks the slot (shown as `< KEY >`); Enter/click starts capture; the slot reads
  `PRESS A KEY` blinking 1 Hz (steady under Reduced Motion) and the status line names the action;
  next key binds. Esc/East cancels (`REBINDING CANCELLED`). Backspace/North clears a slot.
- **Results** (status line): `X BOUND TO A`, `SWAPPED: ...`, `MOVED FROM ... TO ...`,
  `REFUSED: X IS THE ONLY KEY FOR A ...`, `THAT KEY CANNOT BE BOUND`, `KEY CLEARED FROM A`.
  No action is ever left unbound. Navigation is disabled during capture.

## 4. Confirm modal
- **Use**: destructive actions (RESET PROGRESS).
- **Visual**: full-cover ground at 92 %, 16 T crimson question, 8 T consequence line, list `NO` / `YES`.
- **Input**: default focus NO; any direction toggles; Esc/East = NO; it traps focus and blocks clicks behind.

## 5. Toast / status line
- **HUD pickup toast**: glyph + word (`+1 HP`, `> SPEED`, `O HOOK`) above the player, 8 T orange, 1.0 s,
  3-step rise, vanish at the end; the same kind within 0.5 s merges (one label per kind).
- **Menu status line**: 8 T text under the list; bone for results, crimson for refusals and errors.
  Error state: a failed settings or progress write shows `SAVE FAILED - SETTINGS NOT KEPT`
  (Settings stays open once so the message is seen; a second BACK leaves).

## 6. Hint card
- **Use**: tutorial first-sight hints and zones (`TutorialOverlay`), training steps (`TrainingOverlay`).
- **Visual**: bottom-centre above the bar, no panel, 8 T orange body, dim title/step counter, key labels from
  `ControlLabels`; 2-frame stepped in/out. Training adds `STEP n/N`, the skip prompt and bar, and a 16 T bone
  `NICE` / `SKIPPED` word (no scale pop).

## 7. Meter
- **Speed meter** (HUD): 80 T x 2 T line, 30 % orange track, fill in the tier colour tracking speed directly
  (no lerp), 1 x 4 T ticks at the tier-2/3 thresholds; tier numeral (12 T) on the left and three 4 T pips on
  the right; a 1 T `OVER` line beneath above max speed. Tier is readable without colour (numeral, pips).
- **Block bar** (Settings volumes): `[||||||||..]  80`, ten cells plus the number; A/D steps 5, click/drag on
  the bar sets the value.
- **Chain decay**: 1 T orange line under `xN`, shrinking left with the chain timer.
