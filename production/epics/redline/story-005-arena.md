# Story 005: Arena with hazards and spawn points

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief MVP feature 5`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] The arena is a text tile map (`Assets/Resources/Arena01.txt`): solid `#`, spikes `^`, player start `P`, enemy spawn `S`.
- [ ] The arena is enclosed (no way out), has a floor, platforms, walls usable for wall-jumps and ceilings reachable by the hook.
- [ ] Spikes damage the player and bounce them up.
- [ ] A scripted test confirms: the map parses, has one `P` and at least two `S`, and a hook fired from the floor reaches a ceiling.

## Implementation Notes

- `TileMap.Parse` turns text into the grid; row 0 of the text is the top of the arena.

## Out of Scope

- More than one arena.

## Files

- `Assets/Scripts/Core/TileMap.cs`, `Assets/Resources/Arena01.txt`
- `Assets/Tests/EditMode/ArenaTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Integration
**Required evidence**: `Assets/Tests/EditMode/ArenaTests.cs` (advisory at `qa.level: minimal`)
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-002-grappling-hook.md`, `production/epics/redline/story-004-grunt-and-brute.md`
- Unlocks: `production/epics/redline/story-006-wave-flow.md`
