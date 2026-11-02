# Story 010: Kill chain

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: S
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: 2026-11-02

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief Gameplay-pass item 2`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Each kill within `chainWindow` seconds of the previous one raises the chain by 1; the kill refund grows by `chainBonusPerKill` per chain step, capped at `chainMaxBonus`.
- [ ] The chain resets when the window runs out or the player takes damage.
- [ ] The HUD shows the chain count while it is 2 or more.

## Implementation Notes

- `Player.Chain` and `Player.ChainTimer` in Core; values in `RedlineConfig.json`.

## Out of Scope

- Score or rank from chains.

## Files

- `Assets/Scripts/Core/RedlineSim.cs`, `Assets/Scripts/Core/Player.cs`, `Assets/Scripts/Game/RedlineHud.cs`
- `Assets/Tests/EditMode/ChainTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Logic
**Required evidence**: `Assets/Tests/EditMode/ChainTests.cs`
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-009-earned-tier3.md`
- Unlocks: `production/epics/redline/story-013-rebalanced-waves.md`
