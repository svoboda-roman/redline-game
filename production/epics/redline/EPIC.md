# Epic: Redline MVP

> **Source**: `design/game-brief.md` (minimal tier — no GDD/ADRs)
> **Engine**: Unity 6.3 LTS

**Goal:** A 2D arena combat game where you run, slide, wall-jump and swing on a grappling hook — the faster you move, the harder you hit.

**Scope:** the brief's 7 MVP features — momentum movement, grappling hook, speed-scaled combat, grunt + brute, one arena, wave flow, HUD.

**Order:** the brief's Build order — movement → hook → combat + grunt → brute + arena → waves, HUD, end screens.

**Architecture note (minimal tier, no ADR):** all gameplay rules live in the engine-free `Redline.Core` assembly (`Assets/Scripts/Core/`, `noEngineReferences: true`) so they are unit-testable; the Unity layer (`Assets/Scripts/Game/`) only reads input and draws. Tuning values live in `Assets/Resources/RedlineConfig.json`.

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | Momentum movement and speed tiers | Logic | Ready | N/A |
| 002 | Grappling hook swing | Logic | Ready | N/A |
| 003 | Speed-scaled combat | Logic | Ready | N/A |
| 004 | Grunt and armored brute | Logic | Ready | N/A |
| 005 | Arena with hazards and spawn points | Integration | Ready | N/A |
| 006 | Wave flow, HP, game over, victory | Logic | Ready | N/A |
| 007 | HUD and end screens | UI | Ready | N/A |
| 008 | Visual polish — neon look and combat feedback | Visual/Feel | Ready | N/A |
