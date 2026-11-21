# Story 007: HUD and end screens

> **Epic**: Redline MVP
> **Status**: Ready
> **Layer**: Presentation
> **Type**: UI
> **Estimate**: S
> **Manifest Version**: N/A (minimal — no control manifest)
> **Last Updated**: —

## Context

**GDD**: `design/game-brief.md`
**Requirement**: `Brief MVP feature 7`

**ADR Governing Implementation**: N/A (minimal — no ADRs)
**ADR Decision Summary**: N/A (minimal — no ADRs)
**ADR Version**: N/A (minimal — no ADRs)

**Engine**: Unity 6.3 LTS | **Risk**: HIGH
**Engine Notes**: none (no ADR engine-compatibility analysis at minimal)

**Control Manifest Rules (this layer)**: N/A (minimal — no control manifest)

---

## Acceptance Criteria

- [ ] HUD shows a speed meter with tier markers and the current tier, HP, wave number (n/5) and enemies left.
- [ ] The player's color changes per speed tier.
- [ ] Wave-break banner ("WAVE n"), GAME OVER screen and VICTORY screen, each with the restart key shown.
- [ ] Controls are shown on screen at the start.

## Implementation Notes

- IMGUI (`OnGUI`) HUD — no UI package dependency.

## Out of Scope

- Menus, settings, pause screen.

## Files

- `Assets/Scripts/Game/RedlineHud.cs`, `Assets/Scripts/Game/RedlineGame.cs`

## QA Test Cases

*N/A — no qa-lead specs at this tier; implement against the Acceptance Criteria above*

## Test Evidence

**Story Type**: UI
**Required evidence**: retained screenshots of the HUD, GAME OVER and VICTORY screens in `production/qa/evidence/`
**Status**: [ ] Not yet created

## Dependencies

- Depends on: `production/epics/redline/story-006-wave-flow.md`
- Unlocks: None
