# Game Brief: Redline

**One-sentence pitch:** A 2D arena combat game where you run, slide, wall-jump and swing on a grappling hook — the faster you move, the harder you hit.

## Core loop
- Build speed around the arena: run, slide, wall-jump, and swing on the grappling hook (release at the bottom of a swing to launch).
- Strike enemies — damage and knockback scale with your current speed tier; a kill refunds momentum so you keep flying.
- Armored brutes only break at top tier: slow down and you cannot hurt them.
- Clear the wave, the next one comes harder; die and retry instantly.

## Player goal & fail state — what "working" looks like
- Goal: survive and clear all 5 waves of the arena.
- Fail: HP reaches 0 (enemy contact, spikes) → game over → instant restart from wave 1. Getting hit costs HP and kills your momentum.

## MVP — what must exist to be the game
1. Momentum movement: run acceleration, jump, slide, wall-jump; horizontal speed mapped to 3 speed tiers.
2. Grappling hook: fire diagonally up-forward, attach to solid terrain, pendulum swing that keeps momentum, release to launch.
3. Speed-scaled combat: strike damage and knockback = base × tier multiplier; kills refund momentum.
4. Two enemies: grunt (chases the player, dies to any hit) and armored brute (tanky, breakable only at top tier).
5. One arena: floor, platforms, walls for wall-jumps, hook ceilings, spike strips, enemy spawn points.
6. Wave flow: 5 waves defined in config, player HP, game over → instant restart, victory screen.
7. HUD: speed meter + tier, HP, wave number, enemies left.

## Out of scope — not building this
- No timer, score, rank, racing or rival runners.
- No multiplayer (local versus is a later candidate).
- No items/power-ups, no upgrade tree, no bosses.
- One arena only; no save system; no story, cutscenes or dialogue.

## Build order
1. Movement + speed tiers (prove running feels good on its own).
2. Grappling hook swing (prove the hook builds speed).
3. Speed-scaled strikes + grunt (prove "faster = stronger" is fun).
4. Brute, arena layout and hazards.
5. Wave flow, HUD, game-over and victory screens.

---
**Who it's for / what they feel:** Action players who want the flow-state rush of swinging, sliding and smashing through enemies without ever slowing down.

**Art & audio direction:** Clean neon-on-dark flat shapes; the player glows brighter and shifts color per speed tier; driving synthwave.

**Reference game:** Momentum platformers with a grappling hook (e.g. SpeedRunners' hook-and-run movement) — keeps only the run/slide/wall-jump/hook movement feel; the arena wave combat and speed-scaled damage are our own.
