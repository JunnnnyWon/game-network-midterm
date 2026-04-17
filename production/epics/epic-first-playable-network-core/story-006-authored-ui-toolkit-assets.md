# Story 006 — Authored UI Toolkit Assets

Status: In Progress
Type: Gameplay/UI slice
Epic: production/epics/epic-first-playable-network-core/EPIC.md

## Goal
Replace the spike's runtime-built UI Toolkit scaffolding with authored UXML/USS assets so the player-facing flow rests on a real UI asset structure rather than code-only construction.

## Acceptance Criteria
- The spike loads its core UI Toolkit layout from authored UXML/USS assets.
- Pre-match, Active HUD, and results presentation still reflect authoritative snapshot data correctly.
- Existing scene-backed world presentation remains intact.
- Existing gameplay behavior remains verified.
- Findings are appended to `prototypes/network-session-spike.md`.
