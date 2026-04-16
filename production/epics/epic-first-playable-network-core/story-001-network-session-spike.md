# Story 001 — Network Session Spike

Status: Ready for implementation
Type: Technical spike
Epic: production/epics/epic-first-playable-network-core/EPIC.md
TR-IDs:
- TR-concept-003
- TR-concept-007
- TR-systems-001

## Goal
Implement the narrowest runnable Unity client + external server bootstrap needed to prove session/transport viability.

## Acceptance Criteria
- Two local clients can connect and join the same room.
- Protocol mismatch rejects cleanly.
- Heartbeat timeout path is observable.
- One tick-aligned input frame path is observable.
- Findings are appended to prototypes/network-session-spike.md.
