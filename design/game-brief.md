# Game Brief: Redline

**One-sentence pitch:** A 2D arena combat game where you run, slide, wall-jump and swing on a grappling hook — the faster you move, the harder you hit.

## Core loop
- Build speed around the arena: run, slide, wall-jump, and swing on the grappling hook (release at the bottom of a swing to launch). Ground running and sliding top out at tier 2; **tier 3 must be earned** with a hook launch, a wall-jump or a kill chain.
- Strike enemies — damage and knockback scale with your current speed tier; a kill refunds momentum, and **kills in quick succession build a chain** that refunds more.
- Armored brutes only break at top tier, and **charge at you**; drones hover out of ground reach, so you have to go vertical.
- Clear the wave, the next one comes harder; die and retry instantly.

## Player goal & fail state — what "working" looks like
- Goal: survive and clear all 5 waves of the arena.
- Fail: HP reaches 0 (enemy contact, spikes) → game over → instant restart from wave 1. Getting hit costs HP and kills your momentum.

## MVP — what must exist to be the game
*(v1 — built, stories 001–008; kept as the traceability base: stories cite `Brief MVP feature N`)*
1. Momentum movement: run acceleration, jump, slide, wall-jump; horizontal speed mapped to 3 speed tiers.
2. Grappling hook: fire diagonally up-forward, attach to solid terrain, pendulum swing that keeps momentum, release to launch.
3. Speed-scaled combat: strike damage and knockback = base × tier multiplier; kills refund momentum.
4. Two enemies: grunt (chases the player, dies to any hit) and armored brute (tanky, breakable only at top tier).
5. One arena: floor, platforms, walls for wall-jumps, hook ceilings, spike strips, enemy spawn points.
6. Wave flow: 5 waves defined in config, player HP, game over → instant restart, victory screen.
7. HUD: speed meter + tier, HP, wave number, enemies left.

## Gameplay pass — v1.1 (stories cite `Brief Gameplay-pass item N`)
*Why: in v1 tier 3 is one button (run 9 + slide boost 3.5 = 12.5 ≥ tier-3 threshold 12, every 0.4 s on any flat floor), the hook has no combat role, both enemies just walk at you, and kills have no streak value. Numbers below are first guesses to tune by playing.*
1. **Earned tier 3:** slide boost cut so run + slide tops out at tier 2 (~10.5); tier 3 (≥12) is reachable only via hook launch, wall-jump speed, or a kill refund on top of a tier-2 slide. Test: a ground-only run cannot reach tier 3 on a flat floor; the scripted bot must use the hook to clear brutes.
2. **Kill chain:** each kill within ~1.5 s of the last adds a stacking refund bonus (capped); a hit taken or the timer running out resets it. HUD shows the chain count.
3. **Charging brute:** ~0.5 s telegraph, then a straight charge; crashing into a wall stuns it and drops its armor for ~1.5 s (a lure-it-into-a-wall answer for a slower player).
4. **Drone:** hovers high above the floor and dives at the player; dies to any hit, unreachable from the ground — forces jumping, wall-jumping or the hook.
5. **Rebalanced waves:** drones from wave 3, charging brutes from wave 4, heal and HP retuned so a clean run is possible but sloppy play ends by wave 4.

## Out of scope — not building this
- No timer, score, rank, racing or rival runners.
- No multiplayer (local versus is a later candidate).
- No items/power-ups, no upgrade tree, no bosses.
- No ranged enemies or projectiles, no second arena (the pass tunes what exists).
- One arena only; no save system; no story, cutscenes or dialogue.
- Audio is still missing (the art direction asks for synthwave) — a separate candidate, not part of this pass.

## Build order
1. Movement + speed tiers (prove running feels good on its own).
2. Grappling hook swing (prove the hook builds speed).
3. Speed-scaled strikes + grunt (prove "faster = stronger" is fun).
4. Brute, arena layout and hazards.
5. Wave flow, HUD, game-over and victory screens.
6. *Gameplay pass, riskiest first:* earned tier 3 (changes the core feel; the bot must learn the hook) → kill chain → charging brute → drone → rebalanced waves.

---
**Who it's for / what they feel:** Action players who want the flow-state rush of swinging, sliding and smashing through enemies without ever slowing down.

**Art & audio direction:** Clean neon-on-dark flat shapes; the player glows brighter and shifts color per speed tier; driving synthwave.

**Reference game:** Momentum platformers with a grappling hook (e.g. SpeedRunners' hook-and-run movement) — keeps only the run/slide/wall-jump/hook movement feel; the arena wave combat and speed-scaled damage are our own.
