# Story 011: Charging brute

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: M
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: 2026-11-02

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief Gameplay-pass item 3`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] A charging brute that sees the player on its level within range stops for a telegraph (`bruteWindupTime`), then charges in a straight line at `bruteChargeSpeed`.
- [ ] A charge that hits a wall crashes: the brute is dazed for `bruteCrashTime`, deals no contact damage, and its armor is down (any tier damages it).
- [ ] A charge that misses ends after `bruteChargeTime` and the brute recovers; a cooldown separates charges.
- [ ] Brutes only charge from wave `bruteChargeFromWave`; earlier brutes walk as before.
- [ ] Windup and crash are reported as events for the telegraph and stun visuals.

## Implementation Notes

- Brute state machine on `Enemy` (Walk, Windup, Charge, Recover, Crashed); effective armor read through `Enemy.ArmorTier`.

## Out of Scope

- Charging grunts.

## Files

- `Assets/Scripts/Core/Enemy.cs`, `Assets/Scripts/Core/RedlineSim.cs`, `Assets/Scripts/Game/ActorRenderer.cs`
- `Assets/Tests/EditMode/BruteChargeTests.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: Logic
**Required evidence**: `Assets/Tests/EditMode/BruteChargeTests.cs`
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-004-grunt-and-brute.md`
- Unlocks: `production/epics/redline/story-013-rebalanced-waves.md`
