# Story 006: Wave flow, HP, game over, victory

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief MVP feature 6`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] 5 waves are defined in config (grunt and brute counts per wave); a wave spawns at the spawn points after a short break.
- [ ] When every enemy of a wave is dead, the next wave starts; after the last wave the state becomes Victory.
- [ ] The player has HP from config; damage grants brief invulnerability; HP 0 → GameOver.
- [ ] From GameOver or Victory, the restart input starts again from wave 1 with full HP; restart is also available at any time.

## Implementation Notes

- State machine in `RedlineSim`: WaveBreak → Fighting → (WaveBreak | Victory), any → GameOver.

## Out of Scope

- Score, timer, rank, persistent saves.

## Files

- `Assets/Scripts/Core/RedlineSim.cs`, `Assets/Scripts/Core/RedlineConfig.cs`
- `Assets/Tests/EditMode/WaveTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Logic
**Required evidence**: `Assets/Tests/EditMode/WaveTests.cs` (advisory at `qa.level: minimal`)
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-005-arena.md`
- Unlocks: `production/epics/redline/story-007-hud-and-end-screens.md`
