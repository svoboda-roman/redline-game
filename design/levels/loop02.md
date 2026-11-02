# Loop02 — "Overpass"

> **Status**: Draft v1 (2026-11-03), not playtested
> **File**: `Assets/Resources/Loop02.txt` (ring, 112 × 24)
> **Order**: second level, after Loop01 and before Loop03
> **Introduces**: shooter (wave 1), shield bearer (wave 2)
> **Estimated play time**: 4–6 min for a clean run of five waves; one lap at run speed takes about 12 s

## 1. Identity and landmark

**The Overpass**: a 28-tile, two-tile-thick slab (x63–90) that splits the ring into two lanes
for a third of its length. It should be visible from anywhere on the runway.

- **Top lane** (y11): an exposed high road. A **pylon** (x76–77, two tall) sits in the middle and
  splits it into two guard posts. Above it hangs a **billboard** (x69–73, y17–18), the one anchor
  on the top lane.
- **Under it, the rail**: an 8-tile-high corridor whose ceiling is the slab underside (y9), so it
  is a hook anchor along its whole length. Players chain swings along it like monkey bars.

You recognise the level by reading its silhouette: lamp, open pocket, staircase, the long slab
with the pylon on top, then a plinth. Art should light the slab underside (the rail) and the
billboard so both read as hook surfaces.

Loop01 taught "a hook over a trench". Loop02 extends that to "a ceiling you can chain along" and
"a high anchor that launches you over an obstacle".

## 2. Section map

y counts up from the floor row (floor surface y1). Every standing surface has at least 3 tiles of
headroom.

```
x:   0        16       32         46        63                       91        104   111
     |runway  |gate    |shield     |climb    |      OVERPASS          |landing  |      |
y18                                                 [billboard]
y11                                         ____[==top lane==|pylon|==]
y9                                    _[s3] [########## slab ##########]
y7                                _[s2]     (rail: slab underside y9)
y5                            _[s1]
y3                      [bump]                                           [plinth]
y1   P......[lamp]^^^^......S...S..                S       S       S    S          S
```

| x range | Section | Intent | Intensity |
|---|---|---|---|
| 0–15 | **Runway** | Start and rest. Flat, no posts within 10 tiles of P (x4). Build tier 2 by sliding. | Rest |
| 16–31 | **Hook gate** | Launch line L1. Lamp anchor (x19–24, y8) over a 4-wide spike trench (x21–24). It can be jumped at run speed, so the hook is a bonus, not a gate. | Speed |
| 32–45 | **Shield pocket** | Fight. The L1 launch lands here, onto or over the shield's post (x32). Bump x44–45 is the brute crash wall and the first step of the climb. | Fight |
| 46–62 | **Climb** | Three floating steps (x48–50 top y5, x53–55 top y7, x58–60 top y9), each 2 up with a 2-tile gap; the floor lane runs under them (step 1 underside at y4). | Rest / setup |
| 63–90 | **Overpass split** | High lane: slower to reach, exposed to shooters, launch line L3. Low lane: the rail (launch line L2), fast but in the shooters' corridor. | Peak |
| 91–111 | **Landing pocket** | Both lanes come down here. Plinth x100–103 (2 tall) is the shooter perch and a crash wall. Then the runway again. | Fight → rest |

## 3. Hook launch lines

The hook fires at 60° in the facing direction from the player's centre (feet + 0.8), with range 11.
Against a ceiling whose underside is at height B, it hits at `dx = (B − centre) × 0.577` ahead of the player.

| # | Anchor | Fire from | Swing and launch | Reaches |
|---|---|---|---|---|
| **L1 Gate** | Lamp, underside y8, x19–24 | Floor, x≈15.5–21 facing right (dx≈3.6) | Swing under the lamp over the spikes, release on the upswing past x24 | Lands at x28–36 in the shield pocket: behind the shield, or on it from above |
| **L2 Rail** | Slab underside y9, x63–90 | Floor x≈59.5–86 (dx≈4.2); in the air at centre y≈5, dx≈2.3 | Chain 2–4 swings along the corridor, then a last swing off the slab end at x90 | Released at x≈90–93 at tier 3: over the plinth, into the landing pocket and on to the runway |
| **L3 Billboard** | Billboard underside y17, x69–73 | Top lane, x≈66–70 facing right (dx≈3.0) | Rope ≈5–6; the arc clears the pylon top (y13) by about 1.8 tiles at x75 | Lands at x80–90 on the top lane, onto post 1 (x86), at tier 3. This answers the top-lane brute. |

Unintended anchor that is acceptable: from the floor at x≈55–57, the ray hits the underside of step 3
(y8). That gives a short swing into the rail entrance. No other surface is within hook range of the
top lane (the ceiling row is at y23, beyond the 9.5-tile vertical reach).

## 4. Enemy posts

Posts are listed in file order, which is the order the round-robin fills.

| # | Post (x, stands at y) | Ledge within 10 | Why here |
|---|---|---|---|
| 0 | x67, y11 top lane | Left end x63, 4 tiles away | Guards the top of the climb. The brute's post from wave 3: the pylon is its crash wall and L3 is the tier-3 answer. |
| 1 | x86, y11 top lane | Right end x90, 4 tiles away | Past the pylon, where L3 lands. Drops into the landing pocket toward the player. |
| 2 | x59, y9 step 3 | Step edge, 1–2 tiles away | Contests the last jump onto the overpass. |
| 3 | x49, y5 step 1 | Step edge, 1–2 tiles away | Contests the first step; it drops into the shield pocket. |
| 4 | x101, y3 plinth | Plinth edges, 1–2 tiles away | Shooter perch (wave 1): watches both lanes exiting the overpass. Reachable with a jump from the floor. |
| 5 | x32, y1 | — | Where L1 lands: the shield bearer's post (wave 2). |
| 6 | x38, y1 | — | Shield pocket, in front of the crash bump. |
| 7 | x68, y1 rail | — | Rail entrance. |
| 8 | x77, y1 rail | — | Middle of the rail. |
| 9 | x86, y1 rail | — | Rail exit. Shooters here fire along the corridor. |
| 10 | x93, y1 | — | Landing pocket, left of the plinth. |
| 11 | x106, y1 | — | Landing pocket, right of the plinth (10 tiles from P around the ring). |

## 5. Wave plan

Spawn order assumed: brutes, grunts, drones (the current code), then shooters, shields, kamikazes
and summoners in directive key order. **The wave parser must confirm this order.** Post = k mod 12;
the second pass offsets by 0.8.

```
;wave grunt=4 brute=0 drone=0 shooter=1 shield=0 kamikaze=0 summoner=0
;wave grunt=5 brute=0 drone=0 shooter=0 shield=1 kamikaze=0 summoner=0
;wave grunt=5 brute=1 drone=2 shooter=2 shield=1 kamikaze=0 summoner=0
;wave grunt=5 brute=2 drone=2 shooter=2 shield=2 kamikaze=0 summoner=0
;wave grunt=7 brute=2 drone=3 shooter=2 shield=2 kamikaze=0 summoner=0
```

| Wave | Total | Where the new or key enemies land | Lesson |
|---|---|---|---|
| 1 | 5 | Grunts on posts 0–3 (top lane and steps); **shooter alone on the plinth** (4) | Shooter intro: one shooter, in the open, with grunts as filler. |
| 2 | 6 | Grunts on 0–4; **shield alone at x32**, where L1 lands | Shield intro: the gate launch is the answer (over it, or onto it). |
| 3 | 11 | Brute on top lane x67 (not charging yet); drones 6–7; shooters on the rail (8, 9); shield at x93 | First mix: shooters and shields together; the brute rewards L3. |
| 4 | 13 | Charging brutes on both top-lane posts, either side of the pylon (the crash wall); shooters x86 rail and x93; shields x106 and x67 | Charging brutes on the high road; the low road is a shooter corridor. |
| 5 | 16 | Brutes on the top lane; grunts on 2–8; drones 9–11; **shooters back on the top lane** (second pass); shields on both steps | Finale: high road guarded from above, climb blocked by shields, so take the rail. |

Difficulty over Loop01 (3, 6, 9, 9, 13 enemies): 5, 6, 11, 13, 16, with the new kinds on top.

## 6. Pacing chart

```
intensity
 high |                         ___         /\   ___/\
      |              /\        /   \       /  \_/
  mid |      /\     /  \      /     \_   _/
      |     /  \___/    \____/        \_/
  low |____/
      +------------------------------------------------ one lap
       runway gate shield  climb   overpass   landing runway
```

Per lap: speed build, launch, fight, breath on the climb, a choice of peak (top lane or rail), then
a landing fight and the runway to reset. Across waves the peak moves: waves 1–2 fight in the open
pockets; waves 3–5 put the fight on the overpass.

## 7. Narrative and audio beats

No story (the brief rules out story). Environmental read only: the billboard and pylon make it a
city overpass at night. Audio direction is still to come. Suggested cues for audio-director: a
filter sweep opening up while the player is on the rail, and a hit on the release off the slab end.

## 8. Risks and what playtest or bot must check

1. **The L1 trench** is 4 wide; with a jump at run speed 9 the player covers about 6.2 tiles. Check
   that a standing-start jump from x≈20 also clears it.
2. **Rail chaining**: confirm a second hook fired mid-air (centre y≈4–6) attaches ahead on the slab
   and that 3+ chained swings reach tier 3 by x90.
3. **L3 over the pylon**: confirm the swing from the billboard clears the pylon (needs some
   approach speed or pumping) and lands on the top lane, not off the end.
4. **Top-lane drop** at x90 lands on the floor at x≈96–101 at speed 9–12 and often hits the
   plinth's left side (a speed loss). Decide whether that hop is a good beat or move the plinth.
5. **Spawn order** for shooter and shield is an assumption (see §5).
6. **Shooters on the rail** (waves 3–4) fire under a ceiling. Check that shots and reflects read
   clearly, and that a shooter's line of sight is blocked by the slab for top-lane players (cover).
7. **Brute on the top lane**: when the player is under it, it drops off the left end (x63) to the
   floor. Check it does not stand on step 3 or pace the edge.
8. **Wave 5 shields on the 3-wide steps**: check that a shield's body fits there and that the climb
   is still passable by jumping over it.
9. Not verified by hand: the swing arcs (they depend on the rope reel-in and pump code), grunt
   pathing across the pylon, and the line of sight of drones under the slab (they hover about 6.5,
   below the y9 ceiling).
