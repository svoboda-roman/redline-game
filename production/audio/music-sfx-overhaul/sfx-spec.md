# Redline - SFX Spec (Music and SFX Overhaul)

Status: DRAFT for audio-director review. Governed by `direction.md` (palette, mix, ducking, spatial rules). Source events: `SimEventType` in `Assets/Scripts/Core/SimTypes.cs`. Orb kinds: Health, Speed, Hook.

## 0. Conventions

- **ID naming**: `sfx_<area>_<name>[_t<tier>]`, lowercase snake case; stinger `stg_`, ambience `amb_`, UI `ui_`, music `mus_`. (No project audio naming guide exists; this is proposed, audio-director to confirm.)
- **Var** = number of distinct recorded variants. Round-robin: random variant, never the same twice in a row (and not the last 2 if Var >= 4).
- **Vol** = playback gain range in dB relative to the file normalised at -3 dBFS peak. A random value inside the range is chosen per play.
- **Pitch** = random multiplicative jitter, in semitones (+/-). Tier-scaled pitch is separate and noted.
- **Voices** = max simultaneous instances of that event; on overflow, steal the oldest, unless the new one has higher priority.
- **Prio**: 0 (highest) to 5. Voice-stealing across events: lower prio number wins. Priority groups: 0 = game over/victory; 1 = player hurt, explode, kill t3; 2 = player attack, hit, kill, clang, shield, windups/telegraphs; 3 = movement (jump, hook, land), shots; 4 = footsteps, slide loop, orb; 5 = UI repeats, ambience one-shots.
- **Space**: P = player (2D centre); W = world (pan + distance rolloff, direction.md section 4); A = audio-first world sound (extended range).
- **Rev**: reverb send level (0 = none, 1 low, 2 med, 3 high).
- **Dur** = file length. Max tail cap 2.5 s.
- **Vis**: the existing visual counterpart in game (to be verified by the programmer/art; entries marked CHECK were inferred from the event type, not inspected).
- **Avoid**: what must not be heard.
- File format: WAV 48 kHz 16-bit mono for one-shots (stereo only for explosions/stings/ambience), imported as Vorbis in Unity.

Search keyword conventions: sources = Freesound.org (filter CC0 / CC-BY), OpenGameArt.org, Sonniss GDC bundles (royalty-free, no credit), Zapsplat is NOT used (account-bound license), Kenney.nl (CC0, but his packs are mostly stylised; check each), Pixabay Sound Effects, BBC Sound Effects archive (non-commercial licence: NOT allowed), Mixkit (free licence, no attribution; acceptable).

## 1. Player actions

| ID | Event | Wanted sound | Var | Vol (dB) | Pitch (st) | Voices | Prio | Space/Rev | Dur | Search keywords |
|---|---|---|---|---|---|---|---|---|---|---|
| sfx_pl_jump | Jump | Short effort breath-less leather/cloth push + soft boot push-off on wet metal, a tiny coat flutter. Low thump body. | 4 | -10..-7 | +/-0.7 | 2 | 3 | P/1 | 0.2-0.3 s | "jump foley cloth leather", "boots push off wet metal", "coat movement", "parkour jump foley" |
| sfx_pl_walljump | WallJump | Boot kick/scuff against metal wall + sharper cloth snap + faint metallic resonance. Heavier than jump. | 3 | -8..-5 | +/-0.6 | 2 | 3 | P/1 | 0.3 s | "wall kick metal foley", "boot slap metal wall", "parkour wall run foley" |
| sfx_pl_slide_start | Slide (onset) | Hard scrape starting: wet boot/knee drag on metal with coat swoosh. | 3 | -9..-6 | +/-0.8 | 1 | 3 | P/1 | 0.35 s | "slide wet metal scrape", "knee slide concrete rain foley" |
| sfx_pl_slide_loop | Slide (held) | Looping wet-metal scrape with water spray, low rumble; pitch/vol by speed. Played as looping source while sliding (programmer: Slide event starts it; stop when sim stops sliding, see concerns). | 1 loop (+2 alt) | -14..-9 (by speed) | speed-mapped 0.9-1.15 | 1 | 4 | P/1 | loop 1.5-2 s | "metal scrape loop", "sliding on wet surface loop", "water spray scrape loop" |
| sfx_pl_swing_t1 | Attack tier 1 | Fast light blade swish, thin steel air cut, tiny cloth. No ring. | 4 | -9..-6 | +/-0.6 | 2 | 2 | P/1 | 0.25 s | "katana swing light", "sword whoosh fast", "sword swish foley" |
| sfx_pl_swing_t2 | Attack tier 2 | Heavier swing: deeper whoosh with a low body, a faint blade ring ending. | 4 | -8..-5 | +/-0.5 | 2 | 2 | P/2 | 0.35 s | "heavy sword swing", "katana whoosh heavy", "great sword swing" |
| sfx_pl_swing_t3 | Attack tier 3 | Resonant energy slash: bright blade shing + sub-heavy air cut + glowing resonant shimmer tail (processed, pitched, with a faint choir/synth swell). Reads as "the katana is charged". | 4 | -6..-3 | +/-0.4 | 2 | 1 | P/2 | 0.6-0.8 s | "energy sword slash", "laser sword whoosh cinematic", "katana slash resonance", "sword slash sci-fi heavy", "shing metal ring" |
| sfx_pl_hit_t1 | Hit (no kill) tier 1 | Short blade-on-body/armour impact: sharp slice + dull flesh/armour thud. | 5 | -8..-5 | +/-0.6 | 3 | 2 | W/1 | 0.2 s | "sword hit flesh", "blade impact body", "slash impact short" |
| sfx_pl_hit_t2 | Hit tier 2 | Heavier cut + thick thud + metal crunch. | 5 | -7..-4 | +/-0.5 | 3 | 2 | W/1 | 0.3 s | "heavy sword impact", "sword hit armor", "slash crunch" |
| sfx_pl_hit_t3 | Hit tier 3 | Deep impactful cut with sub boom 40-80 Hz layer, short energy crackle, glass-like shimmer. | 5 | -5..-2 | +/-0.4 | 3 | 1 | W/2 | 0.45 s | "cinematic hit impact", "sword impact boom", "heavy slash sub boom" |
| sfx_pl_kill_t1 | Kill tier 1 | Final cut + mechanical break: short crunch, electric fizzle, body drop thud. | 4 | -7..-4 | +/-0.5 | 3 | 2 | W/2 | 0.5 s | "cyborg death", "robot destroyed short", "electric break crunch" |
| sfx_pl_kill_t2 | Kill tier 2 | Bigger: metal rip, spark burst, thudding collapse, short boom. | 4 | -6..-3 | +/-0.5 | 3 | 2 | W/2 | 0.7 s | "robot explosion small", "metal tear spark", "cyborg shut down" |
| sfx_pl_kill_t3 | Kill tier 3 | Cinematic: slow-mo-feel boom + sub drop + glass/metal shatter + short reverse-swell + low taiko "dom" hit layered under. Duck per direction.md. | 4 | -4..0 | +/-0.3 | 2 | 1 | W/3 | 1.0-1.2 s | "cinematic impact boom", "taiko hit single", "metal shatter heavy", "sub drop impact", "epic hit" |
| sfx_pl_clang | Clang | Blade bounce off armour: sharp metallic ring, high partial 2-5 kHz, decaying 0.5 s. Sparks. | 5 | -8..-4 | +/-1.0 | 2 | 2 | W/2 | 0.6-0.8 s | "sword clash metal ring", "sword on armor clang", "metal ping ring", "katana clash" |
| sfx_pl_shieldblock | ShieldBlock | Dense energy-shield thud: low hum smack + digital-metal rattle. Distinct from clang (lower, more "wall"). | 4 | -8..-4 | +/-0.6 | 2 | 2 | W/2 | 0.5 s | "energy shield hit", "force field impact", "shield block metal thud sci-fi" |
| sfx_pl_hurt | PlayerHurt | Hard hit on the player: dull body impact, short grunt-less gasp (breath only, no voice acting; or voice-less "ugh" is OK at low level), electric zap crackle, ringing tinnitus-like tone 3 kHz fading 0.4 s. Duck. | 4 | -4..0 | +/-0.4 | 1 | 1 | P/2 | 0.7 s | "player hurt impact", "body hit cinematic", "tinnitus ring short", "electric shock hit" |
| sfx_pl_hook_fire | HookAttach (fire and travel) see note | Cable launch: pneumatic thunk + whip crack + thin metal cable zip. | 3 | -9..-6 | +/-0.6 | 1 | 3 | P/1 | 0.3 s | "grappling hook fire", "cable launch", "whip crack", "pneumatic launch" |
| sfx_pl_hook_latch | HookAttach (latch) | Hook head biting into metal/concrete: hard metallic clunk + short ring + cable tension twang. | 3 | -8..-5 | +/-0.5 | 1 | 3 | W/1 | 0.35 s | "metal clunk latch", "grappling hook hit metal", "cable twang tension" |
| sfx_pl_hook_release | HookRelease | Cable slack: tension release twang + rapid retract zip, light thud. | 3 | -11..-8 | +/-0.6 | 1 | 3 | P/1 | 0.3 s | "cable release", "rope snap release", "retract winch short" |
| sfx_pl_land_soft | Land, low fall speed | Light boot touch on wet metal, splash. | 4 | -14..-10 | +/-0.6 | 2 | 4 | P/1 | 0.2 s | "landing foley wet metal", "boots land puddle" |
| sfx_pl_land_med | Land, medium | Solid thud, boot slap + water splash + coat flap. | 4 | -10..-7 | +/-0.5 | 2 | 3 | P/1 | 0.3 s | "heavy landing foley", "land on metal roof" |
| sfx_pl_land_hard | Land, hard | Heavy impact: body slam + metal boom + gravel/water spray + sub thump. | 3 | -7..-3 | +/-0.4 | 1 | 2 | P/2 | 0.6 s | "hero landing impact", "superhero land", "heavy impact metal roof" |
| sfx_pl_step_walk / run / sprint | Footstep (by speed) | Boot on wet metal: slap + tiny splash + metal resonance. Walk (slow): soft; Run: firmer; Sprint (tier 2-3 speed): sharper with splash spray. Alternate L/R variants. | 6 per speed (18 total) | walk -20..-17, run -17..-13, sprint -14..-10 | +/-0.8 | 3 | 4 | P/1 | 0.15-0.25 s | "footsteps wet metal", "footstep puddle", "boots running rain", "footsteps metal roof rain" |
| sfx_pl_tier_up | Speed tier up | Rising swell: low taiko hit + short brass/string rise + shimmer; tier 2: smaller, tier 3: bigger with choir burst. (3 versions: t2, t3). | 2 per tier | t2 -10..-7, t3 -8..-4 | 0 | 1 | 1 | P/2 | 1.0-1.5 s | "cinematic riser short", "taiko hit with riser", "epic hit choir", "power up cinematic NOT 8-bit" |
| sfx_pl_tier_down | Speed tier down | Short downward energy decay: deep air release, reverse tail, "power down" low whoosh. Subtle. | 2 | -14..-10 | 0 | 1 | 4 | P/1 | 0.6 s | "power down cinematic", "low whoosh down", "energy fade" |

Notes player actions
- **HookAttach**: SimEvent HookAttach fires on latch. If the fire is not a separate event, play `sfx_pl_hook_fire` on input via the existing hook input path (programmer to confirm; see concerns). Until then, HookAttach plays latch only, fire is optional.
- **Tier pitch for swings**: replace existing `SwingPitchPerTier` with separate samples (above). Remove tier-pitch.
- **Hit/Kill tier**: from `ev.Tier` clamped to 1..3 (existing).
- **Landing**: `PlayLand(fallSpeed)`: map to soft/med/hard by two thresholds (config `landSoftMax`, `landHardMin`; defaults 8 and 16 world units/s - programmer to adjust to real values), gain additionally scaled by `Clamp01(fallSpeed / landHardMin)` within +/-3 dB.
- **Footsteps**: no sim event. Audio drives them from `Tick`: while grounded and |vx| > 0.5, step interval = clamp(0.42 - 0.025 * speed, 0.17, 0.42) s (config; tune by ear to the run animation, ideally frame-synced to animation events if available). Speed class: walk < run threshold, run, sprint = tier >= 2. Never step when sliding/airborne.
- **Slide loop**: started by Slide event, tracked via `sim.Player` state in Tick (see concerns).

## 2. Enemies and projectiles

| ID | Event | Wanted sound | Var | Vol (dB) | Pitch (st) | Voices | Prio | Space/Rev | Dur | Search keywords |
|---|---|---|---|---|---|---|---|---|---|---|
| sfx_en_brute_windup | BruteWindup | Heavy mechanical charge-up: servo winding + low growl-like engine rev + metal stomp-scrape. Rising pitch contour; reads "something heavy is coming". | 3 | -10..-6 | +/-0.5 | 2 | 2 | A/2 | 0.8-1.0 s | "heavy mech power up", "robot charge up", "engine rev low", "bull snort" (organic layer), "servo wind up heavy" |
| sfx_en_brute_charge_loop | (optional, during charge, see concerns) | Rolling heavy footfalls/rumble | 1 | -14..-10 | speed | 1 | 3 | A/1 | loop | "heavy footsteps robot loop" |
| sfx_en_brute_crash | BruteCrash | Massive impact into a wall: concrete/metal crash + debris + sub boom + daze rattle. | 3 | -6..-2 | +/-0.4 | 1 | 1 | A/3 | 1.0 s | "metal crash heavy", "wall impact boom", "cinematic crash", "concrete smash" |
| sfx_en_drone_windup | DroneWindup | High whine ramping up + servo lock + short alert ping (soft, not cartoon beep). Dive: sound transitions to a descending Doppler-like whoosh. | 3 | -12..-8 | +/-0.5 | 3 | 2 | A/1 | 0.7 s | "drone whine rising", "quadcopter rev", "servo lock", "drone dive whoosh" |
| sfx_en_drone_dive | Drone dive (no sim event; see concerns) | Fast dive whoosh with falling pitch. If no event: trigger at end of windup using the windup duration config. | 3 | -10..-6 | +/-0.6 | 3 | 3 | W/1 | 0.5 s | "dive bomb whoosh", "drone fast pass by", "doppler whoosh" |
| sfx_en_shooter_windup | ShooterWindup | Charging energy weapon: rising whine + capacitor buzz + mechanical aim click. | 3 | -12..-8 | +/-0.4 | 2 | 2 | W/1 | 0.6 s | "plasma charge up", "rifle charge", "capacitor charge" |
| sfx_en_shoot | Shoot | Low-calibre energy bolt: punchy crack + low thump + thin zap. NOT laser-pew. Weight and a small tail. | 4 | -10..-6 | +/-0.6 | 3 | 3 | W/2 | 0.4 s | "sci-fi rifle shot heavy", "plasma shot", "energy weapon fire low", "futuristic gun shot" |
| sfx_pl_reflect | ProjectileReflect | Katana deflects the bolt: bright metallic "ting" + energy ricochet zip + small spark crackle. Satisfying. | 4 | -8..-4 | +/-1.0 | 2 | 2 | W/2 | 0.5 s | "bullet deflect sword", "ricochet energy", "sword parry spark" |
| sfx_en_proj_hit | ProjectileHit | Small bolt impact: sizzle + short thump on metal/wall/body (on hit target of player: layer with hurt). | 4 | -13..-9 | +/-0.8 | 3 | 4 | W/1 | 0.3 s | "energy impact small", "sizzle hit", "bullet impact metal" |
| sfx_en_fuse | KamikazeFuse | Rising pressurised hiss + low-level electric buzz with a slowly accelerating pulse (heartbeat-like ticking, muted pitch). Pitch/pulse rate goes up over the fuse duration. Must be audible off-screen. NOT a cartoon beep/alarm. | 3 (loopable bodies of ~1.2 s with rising automation done at runtime: pitch 0.9 -> 1.4, pulse rate x2) | -9..-5 | start +/-0.3 | 3 | 2 | A/1 | duration of fuse (config; programmer: loop + pitch ramp, stop at Explode) | "fuse burning hiss", "pressure hiss rising", "countdown low tension pulse", "bomb timer ticking low", "electric charge rising" |
| sfx_en_explode | Explode | Big cinematic explosion: sub boom, mid crack, debris, metal shards, long low tail (<= 2.2 s), distant echo from the city. Layered stereo. | 4 | -4..0 | +/-0.3 | 2 | 1 | A/3 | 2.0 s | "cinematic explosion", "big explosion sub boom", "explosion debris rooftop", "bomb blast city" |
| sfx_en_summon_cast | SummonerCast | Dark ritual-tech charge: whispery choir-like pad swell + electric crackle + low drone rise. | 3 | -12..-8 | +/-0.4 | 1 | 2 | W/2 | 1.0 s | "dark magic charge", "summon cast sci-fi", "portal charge", "choir swell short dark" |
| sfx_en_summon | Summon | Drone materialises: hard teleport-like "thup" + glitch-tech shimmer + servo start. | 3 | -10..-6 | +/-0.5 | 2 | 3 | W/1 | 0.5 s | "teleport in", "materialize tech", "glitch appear" |

Notes enemies
- Windup/fuse/cast are **telegraphs**, so they must stay clear of masking (direction.md 3.1) and not be ducked below -4 dB.
- **Audio-first (A)**: KamikazeFuse, BruteWindup/Crash, Explode, DroneWindup may be audible off-screen. The fuse's position comes from the event Position (verify it exists for KamikazeFuse; see concerns).
- Existing enum has no separate "reflect Hit/Kill" events beyond ProjectileReflect; use reflect sound only.

## 3. World, flow and pickups

| ID | Event | Wanted sound | Var | Vol (dB) | Pitch (st) | Voices | Prio | Space/Rev | Dur | Search keywords |
|---|---|---|---|---|---|---|---|---|---|---|
| stg_wave_start | WaveStart | Tense low taiko double-hit + low brass swell + faint siren/alert in the distance. Not an alarm blare. | 3 | -8..-4 | 0 | 1 | 1 | P/3 | 1.5-2.0 s | "taiko hit double", "cinematic tension hit", "low brass swell", "alert hit cinematic" |
| stg_wave_clear | Wave clear (not a SimEventType; see concerns) | Release/exhale: single temple bell or shakuhachi phrase + soft taiko + low string resolve. | 3 | -9..-5 | 0 | 1 | 1 | P/3 | 2.0-2.5 s | "temple bell single", "shakuhachi short phrase", "cinematic resolve", "gong soft hit" |
| stg_victory | Victory | See music cue. Additional SFX layer: single big taiko hit + choir "ah" rise, 3 s. | 1 | -6..-3 | 0 | 1 | 0 | P/3 | 3 s | "epic victory hit", "choir ah rise", "taiko roll finale" |
| stg_gameover | GameOver | Low boom + descending drone + glassy break + silence-ish decay. Music cue per direction.md. | 1 | -5..-2 | 0 | 1 | 0 | P/3 | 3-4 s | "game over cinematic", "defeat low boom", "death drone", "failure sting dark" |
| sfx_orb_spawn_health | OrbSpawn Health | Soft warm tonal pop: glass-bell "plink" (organic, bell/koto pluck) + subtle shimmer. Not a coin. | 3 | -16..-12 | +/-0.5 | 3 | 4 | W/1 | 0.5 s | "koto pluck single", "glass bell soft", "crystal drop" |
| sfx_orb_spawn_speed | OrbSpawn Speed | Whoosh-edged shimmer with a rising electric hum, light taiko tick. | 3 | -16..-12 | +/-0.5 | 3 | 4 | W/1 | 0.5 s | "energy whoosh small", "electric shimmer rising" |
| sfx_orb_spawn_hook | OrbSpawn Hook | Metallic ping with a cable-twang overtone. | 3 | -16..-12 | +/-0.5 | 3 | 4 | W/1 | 0.5 s | "metal ping soft", "twang resonant", "shamisen pluck single" |
| sfx_orb_pick_health | OrbPickup Health | Warm swelling chime + low pad lift (healing feel): koto/bell chord, 0.8 s. | 3 | -10..-6 | +/-0.3 | 2 | 3 | P/2 | 0.8 s | "healing chime soft", "bell chord warm", "koto chord" |
| sfx_orb_pick_speed | OrbPickup Speed | Rising whoosh + electric thrum + mini sub pulse (energy absorb). | 3 | -10..-6 | +/-0.3 | 2 | 3 | P/2 | 0.7 s | "energy absorb", "power up whoosh cinematic" |
| sfx_orb_pick_hook | OrbPickup Hook | Metallic ratchet + cable ring + bright ping. | 3 | -10..-6 | +/-0.3 | 2 | 3 | P/2 | 0.6 s | "ratchet metal power up", "cable ring", "tech charge up short" |
| sfx_orb_expire | OrbExpire | Soft fading flicker/fizzle. Very quiet (optional). | 2 | -20..-16 | +/-0.5 | 2 | 5 | W/0 | 0.3 s | "fizzle out soft", "energy fade small" |

Notes
- **Orb pickup** mapping: `Detail = (int)OrbKind` -> Health, Speed, Hook. Do not use a pickup coin-style sound; all orb sounds follow DO list in direction.md.
- **PlayPreview**: the settings-slider volume preview used `OrbPickup`. Replace with `sfx_orb_pick_health`, or better a neutral `ui_slider_tick` with a soft bell.

## 4. UI

| ID | Event | Wanted sound | Var | Vol (dB) | Pitch (st) | Voices | Prio | Dur | Search keywords |
|---|---|---|---|---|---|---|---|---|---|
| ui_move | Menu move/focus | Very soft wooden click / tonal "tok" (hinoki wood block) with a short reverb; low. | 3 | -20..-16 | +/-0.4 | 2 | 5 | 0.12 s | "wood block soft click", "ui wood tick", "bamboo tap" |
| ui_confirm | Confirm | Warm koto/shamisen pluck + small low thump. | 2 | -14..-10 | +/-0.3 | 1 | 4 | 0.4 s | "koto pluck short", "shamisen pluck", "ui confirm organic" |
| ui_back | Back | Soft low thud + muted pluck down a step. | 2 | -16..-12 | +/-0.3 | 1 | 4 | 0.3 s | "ui back soft", "muted pluck", "wood thud soft" |
| ui_slider_tick | Slider tick | Tiny bamboo tap; fast repeat allowed, pitch walks by slider position (+/- 3 st across the range). | 3 | -22..-18 | slider mapped | 1 | 5 | 0.08 s | "bamboo click", "wood tick tiny" |
| ui_upgrade_buy | Upgrade chosen (optional if UpgradeScreen has a confirm) | Koto chord + soft taiko hit + shimmer. | 2 | -12..-8 | 0 | 1 | 3 | 0.8 s | "koto chord", "soft taiko" |

UI: no beeps, no "blip", no sine-wave tones. UI sounds go to the UI mixer group, unaffected by gameplay ducking, but affected by the user SFX slider (as UiVol = SfxVol by default).

## 5. Ambience

| ID | Layer | Description | Format | Level | Search keywords |
|---|---|---|---|---|---|
| amb_rain_metal | Base 1 (always on, all gameplay and menus) | Dense steady rain on metal rooftops and puddles; sparse drips from gutters; no thunder. Loop seamless 45-90 s, 2 variants alternating every loop to hide repetition (crossfade 4 s). High-pass 300 Hz / low-pass 9 kHz in the mixer. | stereo OGG, stream or compressed in memory | direction.md 3.1 (-34 LUFS-S), never ducked below -8 dB | "rain on metal roof loop", "heavy rain tin roof", "rain puddles city night loop", "rain on corrugated iron" |
| amb_city_hum | Base 2 | Distant city: low traffic rumble, ventilation, HVAC, electric buzz of neon, a faint distant crowd. Loop 60-120 s. | stereo OGG | -40 LUFS-S | "city night ambience distant", "urban rooftop ambience", "ventilation hum low", "neon buzz hum" |
| amb_neon_buzz (Loop02 only) | Detail | Neon sign electric buzz and flicker, low. Fades in by level. | loop 20 s | -42 LUFS-S | "neon sign buzz", "fluorescent hum flicker" |
| amb_industrial (Loop03 only) | Detail | Machinery drone, steam release, metal creak, distant clanks. | loop 60 s | -38 LUFS-S | "industrial ambience factory distant", "steam vent hiss", "metal creak far" |
| amb_thunder_far | One-shot | Distant rolling thunder. Random every 25-60 s (config), random pan, low-passed 2 kHz. Loop01 most often, Loop02 rare, Loop03 off. Var 6. | stereo OGG | peak -18 dBFS | "distant thunder rumble", "far thunder rolling", "thunder low rain" |
| amb_siren_far | One-shot | Distant sirens/horns (non-western neutral); random every 40-90 s, pan sweeps, heavy low-pass 3 kHz + reverb. Var 4. | stereo OGG | peak -22 dBFS | "distant siren city night", "far police siren", "air raid distant NOT close" |
| amb_city_oneshots | One-shot | Distant metal clank, a far drone/aircraft pass, dog bark far (optional, low). | | peak -24 dBFS | "distant clang", "helicopter far pass", "city distant oneshots" |

Ambience rules
- Ambience starts at scene load and keeps playing across waves, wave breaks, upgrade (rain at 50%), pause (low-pass 800 Hz, still audible). Stops only on quitting to desktop; menu uses same rain at 70% of level volume.
- One-shot scheduler: no one-shot within 6 s of a kill t3/explode; not two one-shots overlapping.
- Level flavour: Loop01 rain heavier (+3 dB) + thunder; Loop02 rain normal + neon buzz; Loop03 rain lighter (-3 dB) + industrial.

## 6. Voice budget summary

Total SFX 32 voices: Player 6, Impact 8, Enemy 8, Pickup 4, Stinger 2, Footsteps 2 (included in Player), spare 2. Music: 2 level + 1 stinger + 1 one-shot. Ambience: 3 loops + 1 one-shot. UI: 4.

## 7. Information carried first by audio / visual help needed

| Event | Audio-first? | Visual status | Need more visual help? |
|---|---|---|---|
| KamikazeFuse off-screen | Yes: heard before seen if the kamikaze is out of screen | CHECK: on-screen fuse flash exists presumably | Yes. Add an off-screen edge indicator (small red/orange chevron at screen edge pointing to the kamikaze, pulsing with the fuse pulse rate). Audio alone with pan only is ambiguous in a side-scroller. |
| BruteWindup off-screen | Partly | CHECK: windup pose/flash | Recommend the same edge indicator or a warning flash. |
| ShooterWindup off-screen | Partly | CHECK: charge glow | Recommend edge indicator if shooters can fire from off-screen. |
| DroneWindup / dive | Partly | CHECK: windup tint | Optional. |
| SummonerCast | No | CHECK: cast VFX | No. |
| Speed tier up/down | No (duplicate with HUD/tier VFX) | HUD tier indicator CHECK | No. |
| Footsteps, ambience | Audio only by nature | n/a | n/a |
| Orb expire | Audio supports the visual blink | CHECK: orb blinking before expire | Verify orb blink exists, else add. |
| Slide / wall-jump | No | animation | No. |
| Wave clear (derived) | Audio+HUD | CHECK HUD "wave clear" text | Ensure a visual exists. |
All "CHECK" items: sound designer did not inspect the render code; the programmer/art owner to confirm.

## 8. Concerns and gaps (to resolve before implementation)
1. `SimEventType` has **no WaveClear**, **no tier up/down**, **no Slide-end**, **no drone dive**, **no footstep/land** events. The spec handles them as derived events in `Tick`/`Process` (state diffs: PlayerTier changes, enemies remaining -> 0, sim player state). Programmer to confirm what state is readable or add events.
2. HookAttach is the only hook-start event; "fire" and "latch" may need two events (add `HookFire`) or a time-offset (play fire on input, latch on attach).
3. `KamikazeFuse` needs a persistent Position/identity to run a looping, rising fuse sound that stops at Explode (or the kamikaze death by other means: killed before explode must stop the fuse). Needs enemy id on the event.
4. Reverb send and low-pass-by-distance require an AudioMixer asset; the current setup is custom (AudioMix constants + MasterLimiter). Mixer adoption is a programmer/tech-director decision.
5. All sample choices are placeholders until licensed assets are found; the final per-file credits go into `credits.md`.
