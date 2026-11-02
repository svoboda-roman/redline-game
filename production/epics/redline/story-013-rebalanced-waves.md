# Story 013: Rebalanced waves

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: S
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: 2026-11-02

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief Gameplay-pass item 5`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] Waves add drones from wave 3 and charging brutes from wave 4 (config arrays `waveDrones`, `bruteChargeFromWave`).
- [ ] The scripted bot, playing the shipped config, clears all waves of Arena01 (a clean run is possible).
- [ ] A bot that never jumps, hooks or slides dies before the last wave (sloppy play is punished).

## Implementation Notes

- Data-only tuning plus the bot's new routines (hook launch, drone handling, charge dodge).

## Out of Scope

- New arenas.

## Files

- `Assets/Resources/RedlineConfig.json`
- `Assets/Tests/EditMode/BotPlaythroughTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Integration
**Required evidence**: `Assets/Tests/EditMode/BotPlaythroughTests.cs`
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-010-kill-chain.md`, `production/epics/redline/story-011-charging-brute.md`, `production/epics/redline/story-012-drone.md`
- Unlocks: None
