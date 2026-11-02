# Story 009: Earned tier 3

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: 2026-11-02

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief Gameplay-pass item 1`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Running then sliding on a flat floor tops out at tier 2 (never ≥ tier-3 threshold).
- [ ] A hook swing (jump, hook near the apex, swing through the bottom) reaches tier 3.
- [ ] A wall-jump off a wall hit at slide speed reaches tier 3.
- [ ] A kill during a tier-2 slide pushes the player to tier 3 (refund on top of slide speed).
- [ ] The scripted bot clears Arena01 using the hook to reach tier 3 for brutes.

## Implementation Notes

- Tuning only where possible (`slideBoost`); the bot gains a hook-launch routine.

## Out of Scope

- Story 010: chain bonus.

## Files

- `Assets/Resources/RedlineConfig.json`, `Assets/Scripts/Core/RedlineConfig.cs`
- `Assets/Tests/EditMode/TierEarningTests.cs`, `Assets/Tests/EditMode/BotPlaythroughTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Logic
**Required evidence**: `Assets/Tests/EditMode/TierEarningTests.cs`
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-001-momentum-movement.md`, `production/epics/redline/story-002-grappling-hook.md`
- Unlocks: `production/epics/redline/story-010-kill-chain.md`
