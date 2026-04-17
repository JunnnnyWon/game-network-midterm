# Story 005 — UI Toolkit Lobby and Countdown Flow

Status: In Progress
Type: Gameplay/UI slice
Epic: production/epics/epic-first-playable-network-core/EPIC.md

## Goal
Move player-name entry, create/join room, lobby readiness, and countdown presentation into UI Toolkit so the spike no longer depends on IMGUI for the core pre-match flow.

## Acceptance Criteria
- Player name entry and create/join room controls are available through UI Toolkit.
- Lobby state (room code, members, ready state) is visible through UI Toolkit.
- Countdown is visibly rendered through UI Toolkit before Active play begins.
- Existing authoritative Active HUD/results flow remains intact.
- Findings are appended to `prototypes/network-session-spike.md`.
