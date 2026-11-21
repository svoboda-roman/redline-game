# Story 001: Momentum movement and speed tiers

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief MVP feature 1`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Holding a direction on the ground accelerates the player toward run speed; releasing it brakes to a stop.
- [ ] Speed above run speed (from slide, wall-jump, hook, kills) decays slowly while the direction is held and is kept in the air.
- [ ] Jump with coyote time and jump buffering; releasing jump early cuts the jump short.
- [ ] Slide (ground, above a minimum speed) adds a speed boost, lowers the hitbox, and has a cooldown.
- [ ] Wall-jump off a wall while airborne, keeping the speed the player hit the wall with.
- [ ] Horizontal speed maps to tier 1/2/3 by configured thresholds; all values come from `RedlineConfig.json`.
- [ ] The player never passes through solid tiles.

## Implementation Notes

- Pure C# simulation in `Redline.Core`, fixed 60 Hz step; axis-separated AABB vs tile-grid collision.

## Out of Scope

- Story 002: grappling hook. Story 003: combat.

## Files

- `Assets/Scripts/Core/RedlineSim.cs`, `Assets/Scripts/Core/RedlineConfig.cs`, `Assets/Scripts/Core/CombatRules.cs`
- `Assets/Tests/EditMode/MovementTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Logic
**Required evidence**: `Assets/Tests/EditMode/MovementTests.cs` (advisory at `qa.level: minimal`)
**Status**: [ ] Not yet created

## Dependencies

- Depends on: None
- Unlocks: `production/epics/redline/story-002-grappling-hook.md`, `production/epics/redline/story-003-speed-scaled-combat.md`
