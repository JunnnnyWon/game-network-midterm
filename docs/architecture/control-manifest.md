# Control Manifest

Manifest Version: 2026-04-16
Source: ADR-0001 through ADR-0008

## Foundation
### Required
- Keep Unity as a thin client and the external C# server as authority.
- Use the custom framed TCP protocol with explicit version handshake.
- Keep MySQL server-only.

### Forbidden
- Direct Unity-to-MySQL access
- Client-authored competitive state
- Reconnect-to-active-match scope creep in MVP

## Core
### Required
- Use the Unity Input System only.
- Keep room-state transitions server-owned.
- Treat `Draw` as a first-class match end reason.

### Forbidden
- Legacy `Input.*` in production gameplay flow
- Client-side room-state advancement
- Hidden tie-breaking rules outside ADR-0002

## Gameplay
### Required
- Score changes only through authoritative battery collection.
- Slow/trap effects remain movement-only in MVP.
- Strongest slow wins; no multiplicative stacking.

### Forbidden
- Score penalties from traps in MVP
- Body-blocking as an ad hoc addition
- Variable battery values in MVP without a superseding decision

## Persistence
### Required
- Idempotent `match_id` writes
- Deterministic leaderboard ordering
- Persistence must not block end-of-match lock

### Forbidden
- Synchronous DB writes in the gameplay end path
- Non-deterministic leaderboard ordering

## Presentation
### Required
- Runtime UI uses UI Toolkit
- Audio remains presentation-only
- Results/persistence visibility must stay explicit to the player

### Forbidden
- UI computing match outcomes locally
- Audio implying unconfirmed competitive outcomes
