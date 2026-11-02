# Evidence: Story 007 — HUD and end screens

**Story**: `production/epics/redline/story-007-hud-and-end-screens.md`
**Build**: Linux64 player, Unity 6000.3.26f1, branch `feat/redline-mvp`, 2026-11-02
**How captured**: `Builds/Linux64/Redline.x86_64 -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -redlineCapture <dir>`
(`Assets/Scripts/Game/RedlineCapture.cs` — dev-only tour, inert without the flag)

| Screenshot | Shows | AC |
|---|---|---|
| `hud-wave1.png` | Speed meter with T2/T3 markers, TIER 1 (cyan player), 6 HP pips, WAVE 1/5, ENEMIES 3, grunts closing in, controls hint | HUD, controls hint |
| `wave2-brute-armored.png` | Wave 2: brute in steel colour while the player is tier 1 (armored) | tier readability |
| `hud-tier3.png` | TIER 3 · BREAKS ARMOR, yellow player with afterimage trail, brute now red (breakable) | player colour per tier |
| `gameover.png` | GAME OVER banner, reached wave + kills, "Press R / Start to retry", empty HP pips | GAME OVER screen |
| `victory.png` | VICTORY banner, "All 5 waves cleared", "Press R / Start to play again" | VICTORY screen |

The tour forces states (sets HP to 0, clears waves), so kill counts on the end screens read 0.
The "WAVE n" break banner is visible in the tour's first frames and sits above the arena's platforms.

**Observed issues fixed before this capture**: the wave banner overlapped the centre platform (moved up);
the first capture fired before wave 1 spawned (tour now waits for the fight).

**Code review round (2026-11-02)**, recaptured after each fix:
- HUD text raised to ≥24 px at 1080p reference (T2/T3 marks were ~11 px at 720p); translucent panels behind HUD text.
- Tier label and HP pips share one row; camera reserves 4.8 tiles of headroom so HUD panels never cover the arena.
- Controls hint moved from the bottom (it covered the player in wave 1) to the top centre between the panels.
- Wave banner now refreshes when restarting during a wave break (it kept the old wave number).

**HUD restyle (2026-11-02)**, recaptured: neon panels (speed panel border takes the tier colour), zoned speed meter with a
live speed readout, tier badge + pulsing "BREAKS ARMOR" at tier 3, HP pips that flash when lost and pulse at low HP, a
centred CHAIN badge with a draining timer, a wave panel with progress dots and a foes count, a pop-in letter-spaced
banner with underline and a pre-wave countdown, and a highlighted controls hint. Font: Source Code Pro (SIL OFL 1.1,
licence bundled in `Assets/Resources/Fonts/`). Camera headroom raised to 5.6 tiles so no panel covers the arena.

**Sign-off**: pending — lead review of the look (review mode `solo`: the developer signs off).
