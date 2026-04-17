# Story 007 — Canonical UI Toolkit Flow

Status: In Progress
Type: Gameplay/UI slice
Epic: production/epics/epic-first-playable-network-core/EPIC.md

## Goal
Make UI Toolkit the canonical player-facing runtime flow for the spike and reduce the remaining IMGUI shell to minimal debug diagnostics.

## Acceptance Criteria
- The normal pre-match, active, and results flow can be used through UI Toolkit without relying on the IMGUI shell.
- The IMGUI layer is reduced to a bounded debug/diagnostic surface only.
- Existing authoritative scene/HUD/results behavior remains verified.
- Findings are appended to `prototypes/network-session-spike.md`.
