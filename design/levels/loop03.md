# Loop03 — "Gallery"

> **Status**: Draft v1 (2026-11-03), not playtested
> **File**: `Assets/Resources/Loop03.txt` (ring, 120 × 24)
> **Order**: third level, after Loop02
> **Introduces**: kamikaze (wave 1), summoner (wave 2); waves 3–5 mix all seven kinds
> **Estimated play time**: 6–8 min for a clean run; one lap at run speed takes about 13 s

## 1. Identity and landmark

**The Gallery**: three chandeliers (2 × 4 blocks, y9–10) hang in a row over three 4-wide spike strips,
with 4-wide safe islands between them. It is the first thing the player sees after the runway. The
expert line is three chained swings with no ground contact. The safe line is hopping from island
to island.

The second landmark is the **Broken Bridge** (x89–114), a top lane with a 4-tile **break**
(x100–103). A **lantern** anchor hangs above the break. Below the bridge, the **undercroft** is a
second hook rail with an **anvil** bump under the break.

Loop03's vocabulary is the rhythm of the hooks (lamp, lamp, lamp), not one big slab. The level asks
the player to keep moving: kamikazes in the open plaza and a fleeing summoner reward never stopping.

## 2. Section map

y counts up from the floor row (floor surface y1). Every standing surface has at least 3 tiles of
headroom.

```
x:   0       16  20  24  28  32  36  40   48      58      70      89        100  104       115 119
     |runway |  GALLERY                   |plaza          |climb  | BROKEN BRIDGE              |drop|
y17                                                                          [lantern]
y11                                                              [==bridge==]  break [==bridge==]
y9-10           [C1]    [C2]    [C3]          [halo]      _[s3]
y7                                                    _[s2]
y5                                                _[s1]
y3                                                [bump]               [anvil]
y1   P.......^^^^ isl ^^^^ isl ^^^^ ..S...S.....S.....S..   S   (undercroft) S  S   S
```

| x range | Section | Intent | Intensity |
|---|---|---|---|
| 0–15 | **Runway** | Start and rest. No posts within 10 tiles of P (x4). | Rest |
| 16–47 | **Gallery** | Launch lines L1–L3 in rhythm. Spikes at x20–23, x28–31, x36–39; islands at x24–27, x32–35; landing at x40–47. | Speed |
| 48–69 | **Plaza** | Open fight floor: room to keep moving against kamikazes. The halo anchor (x58–61, y10) is the way up to drones. | Fight |
| 70–88 | **Climb** | Bump x70–71 (brute crash wall), then steps x74–76 (top y5), x79–81 (top y7), x84–86 (top y9). The floor lane runs underneath. | Rest / setup |
| 89–114 | **Broken bridge / undercroft** | Split. High: bridge, the break, and lantern swing L5. Low: the undercroft rail L4, with the anvil as crash wall. | Peak |
| 115–119 | **Drop** | Short descent back onto the runway across the seam. | Rest |

## 3. Hook launch lines

The hook fires at 60° from the player's centre with range 11; it hits a ceiling of underside height B
at `dx = (B − centre) × 0.577` ahead. Chandeliers are 2 blocks thick, so a ray that is a little early
still hits their left face, which widens the fire window.

| # | Anchor | Fire from | Swing and launch | Reaches |
|---|---|---|---|---|
| **L1 Chandelier 1** | Underside y9, x20–23 | Runway floor, x≈14.5–19.5 | Swing over spikes x20–23 | Island 1, or a mid-air re-hook to C2 |
| **L2 Chandeliers 2 and 3** | x28–31 and x36–39, underside y9 | From an island floor: x≈24–26.8 for C2, x≈32–34.8 for C3. Mid-air (centre y≈5): about 2.3 tiles before the chandelier | Chain C1, C2, C3 with no ground contact | Release off C3 at tier 3, landing at x42–48 on post 4 (a kill and the start of a chain) |
| **L3 Halo** | Underside y10, x58–61 | Plaza floor x≈53.3–57.3 (dx≈4.7) | Swing up to drone height (about 6.5) | Strikes on drones; a dodge over a kamikaze rush |
| **L4 Undercroft rail** | Bridge underside y10, x89–99 and x104–114 | Floor x≥85 (the ray clears step 3 and hits the bridge); mid-air dx≈2.3 | Chain under the left span, cross the break with a release and re-hook (fire at x≥101.7 in the air) | Off the bridge end at x114 at tier 3, onto the runway |
| **L5 Lantern** | Underside y17, x100–103 | Bridge top x≈97–99.6 facing right (dx≈3.0) | Swing over the break (rope ≈5–6) | Lands on the right span x104–114, onto post 1 (x108), at tier 3. This answers a bridge brute. |

## 4. Enemy posts

Posts are listed in file order, which is the order the round-robin fills.

| # | Post (x, stands at y) | Ledge within 10 | Why here |
|---|---|---|---|
| 0 | x93, y11 left span | x89 (4 tiles) / break edge x99 (6 tiles) | Guards the top of the climb; drops toward the undercroft. A shooter here in wave 5 covers the plaza exit. |
| 1 | x108, y11 right span | Break edge x104 (4 tiles) / x114 (6 tiles) | Where L5 lands. The brute's post (waves 4–5). |
| 2 | x85, y9 step 3 | Step edge, 1–2 tiles | Contests the last jump onto the bridge. |
| 3 | x75, y5 step 1 | Step edge, 1–2 tiles | Contests the climb; drops to the plaza or the bump. |
| 4 | x44, y1 gallery landing | — | Where the L2 release lands: a reward kill at tier 3. |
| 5 | x52, y1 plaza | — | Kamikaze 1 (wave 1); summoner (wave 2). Long, open sightline to the gallery exit. |
| 6 | x58, y1 plaza | — | Kamikaze 2 (wave 1), under the halo, so L3 is the dodge. |
| 7 | x64, y1 plaza | — | Plaza, in front of the bump crash wall. |
| 8 | x80, y1 under the steps | — | Floor lane under the climb. |
| 9 | x94, y1 undercroft | — | Undercroft entrance. |
| 10 | x107, y1 undercroft | — | Right of the anvil (crash wall). |
| 11 | x112, y1 undercroft | — | Undercroft exit (12 tiles from P around the ring). |

## 5. Wave plan

Spawn order assumed: brute, grunt, drone, shooter, shield, kamikaze, summoner. **The wave parser
must confirm this order.** Post = k mod 12; the second pass offsets by 0.8.

```
;wave grunt=5 brute=0 drone=0 shooter=0 shield=0 kamikaze=2 summoner=0
;wave grunt=5 brute=0 drone=0 shooter=0 shield=0 kamikaze=0 summoner=1
;wave grunt=5 brute=1 drone=2 shooter=2 shield=1 kamikaze=2 summoner=0
;wave grunt=5 brute=2 drone=2 shooter=2 shield=2 kamikaze=2 summoner=1
;wave grunt=6 brute=2 drone=3 shooter=2 shield=2 kamikaze=3 summoner=2
```

| Wave | Total | Where the new or key enemies land | Lesson |
|---|---|---|---|
| 1 | 7 | Grunts on 0–4 (bridge, steps, gallery landing); **two kamikazes alone in the plaza** (x52, x58) | Kamikaze intro: open floor, seen from far, the halo to dodge. Keep moving. |
| 2 | 6 + summons | Grunts on 0–4; **summoner alone in the plaza** (x52) | Summoner intro: chase it around the ring before its drones pile up. |
| 3 | 13 | Brute on the left span (x93, not charging); drones in the plaza; shooters at x80 and x94; shield in the undercroft; kamikazes at x112 and the bridge | First full mix, still without a charging brute. |
| 4 | 16 | Charging brutes on both bridge spans; shooters in the undercroft; shields x112 and the bridge; kamikazes on the bridge and step 3; summoner on step 1 | The bridge is the fortress; L5 and the undercroft rail are the two ways through. |
| 5 | 20 + summons | Brutes on the bridge; grunts 2–7; drones 8–10; shooters x112 and the bridge; shields on the bridge and step 3; kamikazes on step 1, the gallery landing and the plaza; two summoners in the plaza | Finale. Watch the total: see risk 6. |

Rise over Loop02 (5, 6, 11, 13, 16): 7, 6 plus summons, 13, 16, 20.

## 6. Pacing chart

```
intensity
 high |       /\/\/\                            /\  /\
      |      /      \       /\                 /  \/  \
  mid |     /        \_    /  \     __        /        \
      |    /           \__/    \___/  \______/          \__
  low |___/
      +--------------------------------------------------------- one lap
       runway  gallery    plaza    climb    bridge / undercroft  drop
```

The gallery is a high-skill, low-threat speed burst (spikes, no posts on it). The plaza is the open
fight; the climb is the breath; the bridge is the peak. The drop and the runway reset the lap.

## 7. Narrative and audio beats

No story. Environmental read: a ruined hall (chandeliers) opening onto a broken bridge. Suggested
cues for audio-director: a rising three-note arpeggio, one note per chandelier hook in the gallery
(rewards the chained line), and a low pulse in the plaza while a kamikaze is rushing.

## 8. Risks and what playtest or bot must check

1. **Gallery fire windows**: from island 1 only about x24–26.8 hits C2 when standing. Check that the
   mid-air re-hook (about 2.3 tiles ahead at centre y≈5) is learnable. Widen the chandeliers to 5 if
   it is not.
2. **Island hopping** (4-wide spikes, 4-wide islands) must work from a near standstill on an island
   (groundAccel 60 reaches 9 within about 0.7 tiles). Check that a spike bounce
   (spikeBounceSpeed 15, 30% speed kept) does not chain several hits across a strip.
3. **Kamikaze and summoner near spikes**: the post at x44 is 5 tiles from the last spike strip. Check
   their AI does not rush or flee into the gallery spikes (a spike death is fine; a stuck enemy is
   not).
4. **The summoner flees on a ring**: it can run laps. Confirm the player at tier 2 or more catches it
   within about one lap, and that it cannot flee onto a ledge it cannot leave.
5. **Brutes on the bridge**: a charging brute aimed at the player can charge off a span end or into the
   break and fall. Confirm it re-posts on the floor (anvil x101–102 and bump x70–71 are the crash walls).
6. **Wave 5 load**: 20 enemies plus summoned drones. With 6 HP and a heal of 2 per wave this may be
   too much. Trim grunts first.
7. **L5 lantern**: confirm the swing from the left span crosses the break and lands on the right span
   rather than dropping through it. Without approach speed it may fall short; falling through is a
   safe outcome (onto the anvil or the floor).
8. **Undercroft rail across the break**: check that the release and re-hook over x100–103 is possible
   and that the anvil (top y3) does not snag a low swing.
9. **Spawn order** for the four new kinds is an assumption (see §5).
10. Not verified by hand: the swing arcs, kamikaze or shield bodies on the 3-wide steps, and drone line
    of sight under the bridge (they hover about 6.5, below the y10 bridge).
