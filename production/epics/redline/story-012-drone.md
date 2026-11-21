# Story 012: Drone

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: 2026-11-02

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief Gameplay-pass item 4`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] A drone ignores gravity and hovers `droneHoverHeight` above the player's feet, out of reach of a standing strike or a plain jump strike.
- [ ] Above the player it telegraphs (`droneWindupTime`), then dives at where the player was; after the dive it climbs back to hover height.
- [ ] It never passes through terrain, dies to any hit, and deals contact damage like other enemies.
- [ ] Hitting it is possible by jumping off a wall, swinging on the hook, or counter-hitting during its dive.

## Implementation Notes

- New `EnemyKind.Drone` with its own steering; shares collision, combat and contact rules.

## Out of Scope

- Projectiles.

## Files

- `Assets/Scripts/Core/Enemy.cs`, `Assets/Scripts/Core/RedlineSim.cs`, `Assets/Scripts/Game/PixelArt.cs`, `Assets/Scripts/Game/ActorRenderer.cs`
- `Assets/Tests/EditMode/DroneTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Logic
**Required evidence**: `Assets/Tests/EditMode/DroneTests.cs`
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-004-grunt-and-brute.md`
- Unlocks: `production/epics/redline/story-013-rebalanced-waves.md`
