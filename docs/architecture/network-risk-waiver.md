# Network Risk Waiver

- Date: 2026-04-16
- Decision: ACCEPTED WAIVER
- Reviewer: Codex (Tier A gate owner)

## Why the live spike is deferred
A meaningful transport spike requires the first runnable Unity client bootstrap and external C# server skeleton. That runnable bootstrap is the first implementation slice itself, not a documentation artifact.

## Accepted risk
The project is beginning implementation with transport risk still unproven in runtime, specifically:
- socket lifecycle behavior in Unity desktop builds
- handshake path under actual client/server execution
- heartbeat timeout behavior in a live room
- measured tick-aligned input transport under two local clients

## Why this is still bounded
- ADR-0001 already constrains the transport contract tightly.
- The first implementation story is explicitly the network session spike.
- Later stories must not assume transport is solved until the spike records pass/fail observations.

## Bounded limitation on the first coding slice
Until the spike story is executed, implementation remains limited to the narrow bootstrap path needed to prove session/transport viability. No broader gameplay networking claims should be treated as validated.
