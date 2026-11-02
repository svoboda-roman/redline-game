# Story 004: Grunt and armored brute

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief MVP feature 4`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Grunt: walks toward the player on the ground (gravity, collides with terrain), dies to any hit (HP from config).
- [ ] Brute: slow, heavy, armored below the configured minimum tier, dies to one top-tier hit.
- [ ] Touching a living enemy damages the player (unless invulnerable) and knocks them away, killing their momentum.
- [ ] Enemies are knocked back by hits (scaled by tier) and never pass through solid tiles.

## Implementation Notes

- Enemies are plain data (`Enemy` class) moved by the same tile collision as the player.

## Out of Scope

- Ranged or flying enemies.

## Files

- `Assets/Scripts/Core/Enemy.cs`, `Assets/Scripts/Core/RedlineSim.cs`
- `Assets/Tests/EditMode/EnemyTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Logic
**Required evidence**: `Assets/Tests/EditMode/EnemyTests.cs` (advisory at `qa.level: minimal`)
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-003-speed-scaled-combat.md`
- Unlocks: `production/epics/redline/story-005-arena.md`
