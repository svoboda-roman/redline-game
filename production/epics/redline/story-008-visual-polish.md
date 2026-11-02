# Story 008: Visual polish — neon look and combat feedback

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Presentation
> **Type**: Visual/Feel
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: 2026-11-02

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief — Art & audio direction` ("clean neon-on-dark flat shapes; the player glows brighter and shifts color per speed tier")

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Bloom makes the neon glow; a vignette frames the arena and pulses red when the player is hurt.
- [ ] Player, grunt and brute are readable pixel-art figures (not rectangles); the player faces its direction, has a slide pose, and glows brighter per tier.
- [ ] Brutes read as armored (steel) when the player is too slow and as breakable (red, pulsing glow) at tier 3.
- [ ] Arena: gradient backdrop, faint grid, drifting motes, bevelled terrain with neon edges, glowing spikes.
- [ ] Feedback: spark streaks on hits, debris on kills, dust on jump/land/slide, squash and stretch, speed lines at tier 3, slash arc on strikes, glowing rope, flash on tier-3 kills, shatter on game over.
- [ ] No per-frame allocations; gameplay rules unchanged (all Core tests still pass).

## Implementation Notes

- Procedural greyscale sprites tinted at draw time (`PixelArt`); renderers split by concern (`ArenaRenderer`, `ActorRenderer`, `EffectsRenderer`); URP Volume built in code (`PostFx`).

## Out of Scope

- Hand-drawn art, animation frames, audio.

## Files

- `Assets/Scripts/Game/PixelArt.cs`, `PostFx.cs`, `ArenaRenderer.cs`, `ActorRenderer.cs`, `EffectsRenderer.cs`, `RedlineGame.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Visual/Feel
**Required evidence**: retained screenshots in `production/qa/evidence/` + sign-off in `production/qa/evidence/visual-polish-evidence.md`
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-007-hud-and-end-screens.md`
- Unlocks: None
