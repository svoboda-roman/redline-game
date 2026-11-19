# Redline - Audio Direction (Music and SFX Overhaul)

Status: DRAFT for audio-director review. Owner: sound-designer (proposal); sonic palette sign-off: audio-director.
Replaces: procedural chiptune (SynthClips, MusicSequencer). No sound bible exists yet; this file is the interim bible.
Companion: `sfx-spec.md`.

## 1. Sonic identity

One sentence: **a cinematic orchestral score for a lonely samurai walking through a neon-lit rain city, with a cold electronic underlayer.**

Pillars
1. **Epic orchestral core**: low strings (ostinato, spiccato), brass (horns, low brass swells), choir (oohs, no intelligible lyrics), big cinematic percussion.
2. **Japanese colour, as accent not wallpaper**: taiko (the main rhythmic engine), shakuhachi (solo melodic voice in calm states), koto/shamisen (plucked accents, tension), occasional temple bell. Never full "tourist Japan" pastiche; one or two Japanese voices per cue max.
3. **Dark cyberpunk underlayer**: low synth pads, sub-bass drones, filtered noise, rain. Analog, slow, dark. No bright arps, no sequenced 16th-note lead lines.
4. **Weight**: every action has a low-frequency body (thump 60-120 Hz) and a long-ish tail (reverb of a big wet city). Hits land heavy, not snappy-cute.
5. **Rain is the floor**: always present, always under everything.

Reference vibe (for search/briefing, not to copy): Ghost of Tsushima / Sekiro (Japanese colour, taiko), Blade Runner 2049 and Cyberpunk 2077 (dark synth+sub), Hans Zimmer / Two Steps From Hell style trailer orchestral, Katana ZERO/Ghostrunner (neon-samurai context, but we are more orchestral).

### Do / Don't

| DO | DON'T |
|---|---|
| Recorded or high-quality sampled acoustic sources: strings, brass, choir, taiko, metal impacts, real swords | Chiptune, square/saw/triangle bleeps, bitcrushed anything, 8-bit/16-bit style |
| Low-frequency weight, sub layers, long reverb tails on big events | Bright cartoon bleeps, "pew", "boing", "coin" pickups |
| Slow harmonic movement, minor / Phrygian / in-sen scale feel | Major-key bouncy arpeggios, "Mario" melodic hooks |
| Sparse silence and breath in calm states | Constant wall of sound at all times |
| Metal, water, leather, cloth, wet concrete textures for SFX | Comedic stingers, slide-whistles, rubber/spring sounds |
| Pitched-down, layered, designed impacts; real foley with processing | Raw stock "game SFX pack" noises with synthetic character |
| Choir as texture ("ah", "oo") | Lyrics, English vocals, speech |
| Tier-up reads as "the score rises to meet you" | Tier-up as a UI beep |
| Taiko hits that are tuned/dry/big (odaiko) | Cheesy "gong" stereotype on every hit |

Mood by level colour:
- Loop01 (rainy blue): melancholic, lonely, shakuhachi + strings + soft taiko. Cold.
- Loop02 (magenta neon): pulse and drive, synth bass + taiko ostinato, choir stabs. Seductive and dangerous.
- Loop03 (amber industrial): heavy, grinding, brass + low drones + metal percussion. Oppressive and climactic.

## 2. Music: states and adaptive rules

### 2.1 State table

Music is a set of **cue tracks**. Each gameplay level has two tracks: `CALM` and `INTENSE` (same key and tempo family, ideally same BPM; see 2.3 for sourcing). Everything else is a single track.

| State | Content | Loop? | Notes |
|---|---|---|---|
| Main menu | Slow, cinematic: shakuhachi + low pad + distant taiko hits, choir swell. 70-90 BPM or free-time. 60-120 s loop. | Loop | Rain ambience bed audible under it. Establishes the identity. |
| Training | Calm, sparse, instructional: koto/shamisen + soft pad + light taiko pulse. Non-threatening. Uses Loop01 CALM-style character. | Loop | No tier intensity crossfade (training is learning; keep it flat). Tier only adds +1.5 dB at tier 3 as a gentle reward, optional. |
| Loop01 (rainy blue) | CALM: shakuhachi, strings, pad, sparse taiko. INTENSE: strings ostinato + taiko driving + choir pad. | Loop | 90-110 BPM. |
| Loop02 (magenta neon) | CALM: dark synth pulse, sub, koto plucks. INTENSE: taiko ostinato, staccato strings, choir stabs, brass hits. | Loop | 110-130 BPM. |
| Loop03 (amber industrial) | CALM: drones, metal percussion, low brass. INTENSE: full brass + choir + taiko + strings. | Loop | 100-120 BPM. |
| Wave break (between waves) | Duck/filter the level music: drop INTENSE to 0, CALM stays at 60%, low-pass 4 kHz. Add a short sting on wave clear (see sfx-spec). No separate track. | - | Reads as "exhale". |
| Upgrade screen | Dedicated short ambient track: warm pad + koto + shakuhachi, low taiko heartbeat, ~60 s loop. Or the level CALM track low-passed to 2.5 kHz at -6 dB if no track is found (fallback). | Loop | Game time is paused; rain stays at 50%. |
| Victory | One-shot ~12-20 s: brass fanfare, taiko roll, choir ascending, resolving into shakuhachi tail. No loop; ends into silence + rain. | One-shot | Music ducks to 0 first (see 2.5). |
| Game over | One-shot ~6-10 s: low string drone, single taiko, reverse-cymbal-less. Dark, sorrowful. Choir fades out. | One-shot | Level music fades out in 0.4 s, rain low-passed. |

### 2.2 Tier intensity rules (core)

The player tier (1-3) from `sim.PlayerTier` drives an **intensity value** `I` in [0,1], smoothed. Two tracks per level play **simultaneously and in sync** (same start sample, same length); we crossfade volumes. Keeping both always playing keeps them in phase (required for the crossfade to work).

| Tier | CALM vol | INTENSE vol | INTENSE low-pass cutoff | Master music lift |
|---|---|---|---|---|
| 1 | 0 dB (ref) | -inf (mute, actually -60 dB) | 1.5 kHz (so it comes up muffled) | 0 dB |
| 2 | -3 dB | -4 dB | 6 kHz | 0 dB |
| 3 | -9 dB | 0 dB | 20 kHz (open) | +1.0 dB |

Rules
- **Enter tier up**: crossfade over **1.5 s** (equal-power curves). Also play the `mus_tier_up` sting (see sfx-spec, SFX bus) so the rise feels intentional and not a mix accident.
- **Leave tier (down)**: do not fall immediately. Reuse existing hold: `AudioMix.LayerHoldSeconds` (keep value; currently used for layer hold). After the hold, crossfade down over **3.0 s** (slower than up).
- **Hysteresis**: tier flapping (up/down within 2 s) must not retrigger the tier sting more than once per **4 s**.
- **Sync**: both tracks started with `AudioSource.PlayScheduled` on the same `dspTime`. Loop by `loop = true`. Tracks of one level MUST have identical length (trim/loop-edit in DAW; see 2.3).
- **Wave start**: intensity floor of tier 2 for the first 3 s of a wave, optional (tuning knob `waveStartFloorSeconds`, default 0 = off).
- **Level change**: crossfade previous level music to new over 2.0 s; new track pair starts at its beginning.
- **Pause**: music low-pass to 800 Hz and -6 dB (no stop), snapped back on resume. Keep existing `SetPaused`.
- **Menu to level**: menu music fades out 1.0 s; level CALM fades in over 1.5 s.

### 2.3 Sourcing approach for free full-mix tracks (no stems)

Assume no stems. Per level pick **two full mixes that are compatible** and make them loop-able:

1. Preferred: find ONE composer/pack with two versions of the same theme ("calm/ambient version" + "action/battle version"), same key and BPM.
2. Acceptable: two tracks from the same artist/album in the same key and tempo (or tempo within 3%). Time-stretch in DAW (Audacity/Reaper) to match BPM; pitch-match to the same key if needed. Trim to bars, cut on zero crossings, export OGG, 44.1 kHz, quality ~6 (~192 kbps) stereo.
3. Fallback when sync is poor: do NOT crossfade in sync. Do a **bar-quantised track switch**: wait for the next bar boundary (known BPM) then crossfade 1.0 s. Requires a BPM per track in the data file (`bpm`, `beatsPerBar`, `firstDownbeatOffsetSeconds`).
4. Intensity shaping with only DSP, per 2.2: low-pass the INTENSE track at low tiers, volume lifts, and a layered one-shot **taiko/brass hit loop** from SFX library (e.g. the "tier 3 war drum bed", looped, quiet) added only at tier 3 as free "percussion layer" when the INTENSE track is not intense enough.

Authoring requirements for all music files
- Format OGG Vorbis (or WAV for the short stings), 44.1/48 kHz stereo, loudness-normalised to **-16 LUFS integrated** for CALM and **-14 LUFS** for INTENSE (so crossfade levels in 2.2 hold), true peak <= -1 dBTP.
- Seamless loop: end tail must be cut, not decaying into silence (or the reverb tail is overlapped/wrapped into the start). Test with the loop in the editor for 10 loops.
- No vocals with lyrics. No tracks with a sustained melody hook that fights SFX (keep mid-range 1-4 kHz moderately recessed in the music; EQ notch -2 dB around 2-3 kHz is allowed).
- Streaming: Unity import settings = Streaming load type, Vorbis compressed, "Preload audio data" off for music; 2 streams per level playing simultaneously.

### 2.4 What to search for (music)

Search sites: OpenGameArt.org, Freesound (music-tagged), Pixabay Music, Free Music Archive (filter CC0 / CC-BY), Incompetech (CC-BY, Kevin MacLeod), Musopen, itch.io CC0 packs, Uppbeat is NOT allowed (account-bound license).

Keywords
- Menu: "cinematic ambient shakuhachi", "dark ambient japanese", "epic samurai ambient", "cyberpunk dark ambient cinematic"
- Loop01 calm: "rainy melancholic cinematic strings shakuhachi", "lofi dark orchestral rain", "sad samurai"
- Loop01 intense: "epic taiko strings battle", "dark orchestral action loop", "cinematic percussion tension strings"
- Loop02 calm: "dark synth pulse cinematic cyberpunk", "blade runner style ambient synth", "neon dark drone"
- Loop02 intense: "epic cyberpunk orchestral hybrid", "taiko synth hybrid action", "epic choir percussion hybrid"
- Loop03 calm: "industrial dark drone cinematic", "dark brass drone ambient"
- Loop03 intense: "epic brass choir battle", "boss battle orchestral epic", "dark choir war drums"
- Victory: "epic victory fanfare orchestral", "samurai victory cinematic"
- Game over: "dark sad orchestral defeat", "death cinematic sting strings"
- Upgrade: "peaceful japanese koto ambient", "calm koto shakuhachi loop"

Reject filter: any "8-bit", "chiptune", "retro", "arcade", "synthwave bouncy" track; any track with lyrics; any track with a hard intro/outro that cannot be cut.

Licensing rules
- CC0 preferred. CC-BY accepted: record in `production/audio/music-sfx-overhaul/credits.md` (title, author, URL, license, file name, date accessed) and surface in the in-game Credits screen. CC-BY-NC and CC-BY-SA/ND are rejected (commercial risk, derivative edits needed).
- Keep original download + licence page screenshot under `assets/audio/_sources/` (not shipped).

## 3. Mix targets

Unity mixer groups (AudioMixer) hierarchy:

```
Master
 +- Music      (Music_Level, Music_Stings)
 +- SFX
 |    +- SFX_Player   (swing, jump, slide, hook, footsteps, land)
 |    +- SFX_Impact   (hit, kill, clang, shield, hurt, explode)
 |    +- SFX_Enemy    (windups, shots, fuse, summon)
 |    +- SFX_Pickup   (orbs)
 |    +- SFX_Stinger  (wave start/clear, tier up/down, victory/gameover)
 +- UI
 +- Ambience   (Amb_Rain, Amb_City, Amb_OneShots)
```
Exposed parameters: `MasterVol, MusicVol, SfxVol, UiVol, AmbVol` (existing user sliders map to Master, Music, Sfx) plus `MusicLowpass`, `AmbLowpass`, and snapshot-style duck params below.

### 3.1 Level targets (at mixer output, user sliders at default 100%)

Measured as short-term loudness / peak at the group, relative values in dB under Master. Master limiter ceiling -1 dBFS (keep MasterLimiter).

| Group | Target avg | Peaks | Notes |
|---|---|---|---|
| Music (tier 1) | -20 LUFS-S | -10 dBFS | Quiet bed in calm |
| Music (tier 3) | -16 LUFS-S | -6 dBFS | Full, but SFX stay on top |
| SFX_Impact | peaks -6 dBFS | -3 dBFS on kills/explosions | The loudest things in the game after the music tier-3 swell |
| SFX_Player | -14 to -10 dBFS peaks | | Swing must read through music |
| SFX_Enemy telegraphs | -12 to -8 dBFS peaks | | Telegraphs must be audible over music at all tiers; they are gameplay information |
| UI | -14 dBFS peaks | | |
| Ambience rain | -34 LUFS-S (~ -26 dB under music tier 1) | -24 dBFS | Always under everything; audible when SFX is silent |
| Ambience city hum | -40 LUFS-S | | |
| Ambience one-shots (thunder/siren) | peak -18 dBFS | | Far, never competes |

Rule of thumb: Ambience < Music < SFX telegraphs <= SFX player < SFX impact, except during ducks.

### 3.2 Ducking (sidechain / snapshot style)

Implement as program-driven mixer parameter ramps (attack/hold/release), not true sidechain, so it is deterministic.

| Trigger | Ducks | Amount | Attack | Hold | Release |
|---|---|---|---|---|---|
| Kill, tier 1-2 | Music_Level | -2 dB | 20 ms | 80 ms | 400 ms |
| Kill, tier 3 | Music_Level, Amb | -4 dB music, -3 dB amb | 10 ms | 150 ms | 600 ms |
| Explode (kamikaze) | Music_Level -6 dB, Amb -4 dB, + low-pass music to 2 kHz | | 10 ms | 250 ms | 900 ms |
| PlayerHurt | Music_Level -5 dB + low-pass 1.2 kHz | | 10 ms | 300 ms | 700 ms |
| BruteCrash | Music_Level -3 dB | | 15 ms | 100 ms | 500 ms |
| WaveStart / Wave clear stinger | Music_Level -3 dB | | 50 ms | stinger length | 800 ms |
| Tier-up sting | none (sting rides the crossfade) | | | | |
| Game over | Music_Level to -inf over 0.4 s; Ambience low-pass 600 Hz, -6 dB; SFX_* -6 dB except the game over SFX | | 400 ms | until restart | 1.5 s on restart |
| Victory | Music_Level to -inf over 0.3 s, then victory cue | | 300 ms | cue length | 2.0 s |
| Low health (hp <= 1 of max, if exposed) | optional: low-pass music 3 kHz + heartbeat bed | | | | tuning knob, off by default |

Rules
- Ducks do not stack arithmetically: take the **deepest** active duck per target, with its own release.
- Max total music reduction from ducking: -9 dB (clamp).
- Rain ambience is never ducked below -8 dB total (it "always sits under", never vanishes) except during game over low-pass.

### 3.3 Frequency masking notes
- Music low band (<120 Hz) is shared by sub-bass pads, taiko and SFX impact bodies. Keep music high-passed at 30 Hz and the impact SFX sub (50-90 Hz) mono. Music INTENSE at tier 3 gets a -2 dB shelf below 100 Hz (a mixer EQ on Music group) when a kill/explode body plays (part of the duck above).
- Swing/slash air (2-8 kHz) competes with strings/choir: music has a gentle -2 dB dip around 3-5 kHz (static EQ on Music group).
- Rain (broadband 2-10 kHz) is high-passed at 300 Hz (remove mud) and low-passed at 9 kHz so it does not mask clang/ring.
- Telegraph cues (windups, fuse) sit at 1-4 kHz, narrow, with a distinctive pitch contour; they are tuned not to overlap with the music's tonic (tune the fuse rising hiss to avoid sitting in the key of the music is NOT required; contour distinctiveness is enough).

## 4. Spatial rules (shared)

The game is 2.5D side-view, so use 2D-ish positioning with simple rules:
- Player SFX: non-positional (2D), centred.
- World SFX (enemies, explosions, orbs, projectiles): pan by `(worldX - cameraX) / halfScreenWidth` clamped to [-0.8, 0.8]; volume rolloff: linear from full at 0 to 0.6 of the volume at screen edge, to 0.25 at 1.5x screen width; no sound beyond 2.0x screen width EXCEPT those flagged "audio-first" (fuse, explosion, brute charge) which are heard to 2.5x with a low-pass (distance simulated: cutoff from 20 kHz down to 3 kHz).
- High-frequency rolloff with distance: low-pass cutoff = lerp(20 kHz, 4 kHz, distance01).
- Reverb: one shared "wet city rooftop" send (Master-level reverb return): decay 1.6-2.2 s, pre-delay 25 ms, high damping, send levels per event in sfx-spec. Cap the reverb tail at 2.5 s (no long wash on fast actions).

## 5. Implementation notes (for the programmer)

Behind the existing API (Init, Process(sim, events), Tick, PlayLand; plus SetVolumes, SetPaused, StopEffects, PlayPreview):
- Replace `SynthClips` with a `SfxLibrary` ScriptableObject: `SfxId -> AudioClip[] variants + volume range + pitch jitter + voice limit + priority + mixer group + reverb send`. Data-driven; no hardcoded values (project coding standards).
- Replace `MusicSequencer` with `MusicDirector`: owns per-level pairs of AudioSource (CALM/INTENSE), one stinger source, one-shot source. Exposes `SetState(MusicState)`, `SetTier(int)`, and a `Duck(DuckId)` API. `Tick` calls `SetTier(sim.PlayerTier)` with the existing hold timer.
- Keep `AudioMix` as the central constants file (tier crossfade times, hold seconds, duck table). Values in section 2.2 and 3.2 are the defaults; make them config.
- Addressables or Resources: audio assets in `Assets/Audio/{Music,Sfx,Ambience,Ui}/`, loaded by Addressables groups per level (music streamed). Keep a guard: if a clip is missing, log once and play nothing (no synth fallback).
- Voice budget total: 32 SFX voices, 2 music streams (+1 stinger), 3 ambience, 4 UI. Steal the lowest priority, oldest voice.
- Pitch jitter is multiplicative on `AudioSource.pitch`. Round-robin = pick random variant not equal to the last (see sfx-spec).
- Music memory: streaming only. SFX: Decompress on Load for clips < 200 KB, Compressed In Memory for ambient loops and long tails.

## 6. Open questions for audio-director / producer
1. Confirm the music licensing policy (CC-BY allowed with credits; CC0 preferred).
2. Is a credits screen planned? CC-BY needs one.
3. Is the tier stinger acceptable (SFX bus) or should the tier change be silent beyond the crossfade?
4. Should the training level music be flat (as proposed) or follow tier?
5. Low-health heartbeat feature: wanted? (Needs an HP exposure to audio.)
