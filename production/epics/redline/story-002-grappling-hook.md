# Story 002: Grappling hook swing

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief MVP feature 2`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Pressing hook fires a line diagonally up-forward (configured angle and range); it attaches to the first solid tile it meets, or misses.
- [ ] While the hook button is held, the player swings as a pendulum: the rope never stretches beyond its attach length.
- [ ] Swinging converts falling speed into horizontal speed; releasing keeps the full velocity (launch).
- [ ] Holding a direction while swinging pumps the swing.
- [ ] Taking damage releases the hook.

## Implementation Notes

- Rope as a distance constraint on the predicted position; velocity recomputed from the constrained displacement (removes the outward radial component).

## Out of Scope

- Hooking enemies or pulling enemies.

## Files

- `Assets/Scripts/Core/RedlineSim.cs`, `Assets/Scripts/Core/TileMap.cs`
- `Assets/Tests/EditMode/HookTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Logic
**Required evidence**: `Assets/Tests/EditMode/HookTests.cs` (advisory at `qa.level: minimal`)
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-001-momentum-movement.md`
- Unlocks: `production/epics/redline/story-005-arena.md`
