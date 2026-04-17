# Story 004 — UI Toolkit Match Overlay

Status: In Progress
Type: Gameplay/UI slice
Epic: production/epics/epic-first-playable-network-core/EPIC.md

## Goal
Move the spike's core Active/results presentation from IMGUI into a real UI Toolkit runtime overlay while keeping the server-authoritative snapshot contract intact.

## Acceptance Criteria
- The spike renders an Active match HUD through UI Toolkit rather than relying only on IMGUI labels/cards.
- Results state is surfaced through UI Toolkit when the room reaches Ended/Saving/ResultsReady.
- Existing authoritative cooldown/debuff/results data remains correct.
- Existing world presentation and gameplay behavior remain verified.
- Findings are appended to `prototypes/network-session-spike.md`.
