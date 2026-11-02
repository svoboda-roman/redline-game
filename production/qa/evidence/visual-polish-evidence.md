# Evidence: Story 008 — Visual polish

**Story**: `production/epics/redline/story-008-visual-polish.md`
**Build**: Linux64 player, Unity 6000.3.26f1, branch `feat/redline-mvp`, 2026-11-02
**How captured**: `Builds/Linux64/Redline.x86_64 -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -redlineCapture <dir>`

| Screenshot | Shows | AC |
|---|---|---|
| `hud-wave1.png` | Bloom on neon edges and characters; gradient backdrop, grid, motes; bevelled tiles; glowing spikes; pixel-art player (visor, facing right) and grunts | bloom, figures, arena |
| `wave2-brute-armored.png` | Brute in steel while the player is tier 1 (armored) | brute states |
| `hud-tier3.png` | Tier-3 player: yellow glow, afterimage trail, speed lines; brute red with pulsing glow (breakable) | tier glow, brute states, speed lines |
| `fx-kill.png` | Tier-3 kill feedback at the brute: spark streaks and flash (dev-tour preview of the kill effect) | feedback |
| `gameover.png` | Player shattered into debris; GAME OVER banner | shatter on game over |
| `victory.png` | VICTORY screen with the new look | — |

| `wave3-drones.png` | Gameplay pass (story 012): purple drones heading for their hover points | drone readability |
| `wave4-charge-telegraph.png` | Gameplay pass (story 011): brute flashing yellow while telegraphing a charge; second brute steel | charge telegraph |

**Not visible in stills (verify in play)**: dust on jump/land/slide, squash and stretch, slash arc, glowing rope, red vignette pulse on damage, hit pop on enemies.

**Regression check**: gameplay rules unchanged — 77/77 tests pass in Unity EditMode and `dotnet test tools/core-tests`.

**Sign-off**: pending — playtest of feel by the developer (review mode `solo`).
