# Story 003: Speed-scaled combat

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief MVP feature 3`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Attack opens a short strike hitbox in front of the player, with a cooldown; each enemy is hit at most once per strike.
- [ ] Damage = base damage × tier multiplier (tier 1/2/3 → configured multipliers); knockback scales the same way.
- [ ] A kill refunds momentum in the facing direction (capped at max speed).
- [ ] An enemy whose minimum tier is above the player's current tier takes no damage and bounces the player back ("clang").
- [ ] Every hit, kill and clang is reported as an event so the game layer can show feedback.

## Implementation Notes

- `CombatRules` is a pure static helper (tier, damage, knockback, armor check) — unit-tested directly.

## Out of Scope

- Story 004: enemy behaviour.

## Files

- `Assets/Scripts/Core/CombatRules.cs`, `Assets/Scripts/Core/RedlineSim.cs`
- `Assets/Tests/EditMode/CombatTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Logic
**Required evidence**: `Assets/Tests/EditMode/CombatTests.cs` (advisory at `qa.level: minimal`)
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-001-momentum-movement.md`
- Unlocks: `production/epics/redline/story-004-grunt-and-brute.md`
