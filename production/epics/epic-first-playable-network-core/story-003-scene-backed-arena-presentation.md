# Story 003 — Scene-Backed Arena Presentation

Status: In Progress
Type: Gameplay presentation slice
Epic: production/epics/epic-first-playable-network-core/EPIC.md

## Goal
Replace the spike's abstract arena-preview dependency with simple scene-backed authoritative player/battery/trap presentation so the first playable network core feels closer to a game than a debug dashboard.

## Acceptance Criteria
- The Unity spike renders players, active batteries, and trap zones as scene/world presentation rather than only as an IMGUI mini-preview.
- Local camera or viewport framing keeps the authoritative play space readable during Active play.
- Existing authoritative HUD/results/cooldown feedback remains visible and correct.
- Existing authoritative gameplay behavior remains verified.
- Findings are appended to `prototypes/network-session-spike.md`.
