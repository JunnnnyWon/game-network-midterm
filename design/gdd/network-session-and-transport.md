# Network Session & Transport

> **Status**: Draft
> **Author**: Codex
> **Last Updated**: 2026-04-16
> **Last Verified**: 2026-04-16
> **Implements Pillar**: Pillar 3 — Networking and Results Must Be Visible

## Summary

`Network Session & Transport` is the foundation system that connects the Unity client to the external authoritative C# server, keeps both players in the same room, and moves all shared gameplay/UI intents and snapshots across a deterministic protocol. It exists so Battery Rush Arena can visibly demonstrate real client-server play without letting the client invent score, victory, or room state.

> **Quick reference** — Layer: `Foundation` · Priority: `MVP` · Key deps: `None`

## Overview

This system covers how a player enters the multiplayer flow at all: connecting from the Unity client, joining or creating a room, staying connected through heartbeat traffic, and receiving authoritative room and gameplay snapshots from the server. The player should never need to understand protocol details, but they should feel that room join, countdown start, match play, disconnect handling, and results transitions are fast, clear, and fair.

## Player Fantasy

The player should feel that the arena is a real synchronized space, not a fake local simulation. Joining a room should feel immediate and trustworthy, inputs should register quickly, and shared match events should appear as one consistent truth. The fantasy is: “I entered a real competitive training session, and the system is tracking both players honestly.”

## Detailed Rules

### Core Rules

1. **Client/server shape**
   - The Unity application is always a thin client.
   - The dedicated external C# server is always the authority for room membership, match state, score, pickups, debuffs, and victory.
   - The Unity client may only send intents and render authoritative responses.

2. **Connection flow**
   - The player enters a player name locally, then chooses create room or join room.
   - The client opens a TCP connection to the server and performs a protocol-version handshake before room entry.
   - If the protocol version is incompatible, the player is rejected before entering the lobby.
   - After a successful handshake, the client submits either:
     - `CreateRoom` intent, or
     - `JoinRoom(roomCode)` intent.

3. **Room membership rules**
   - MVP supports exactly **2 active players per room**.
   - A room enters ready-to-start behavior only when two connected players are present.
   - Room creation returns a room code that can be shared locally during the class demo.
   - A late third player cannot join an already full MVP room.

4. **Message categories**
   - Client → server:
     - `Connect`
     - `CreateRoom`
     - `JoinRoom`
     - `LeaveRoom`
     - `ReadyState`
     - `RematchVote`
     - `InputFrame`
     - `RefreshLeaderboard`
   - Server → client:
     - `RoomSnapshot`
     - `MatchEvent`
     - `MatchEnded`
     - `LeaderboardData`
     - `PersistenceStatus`
     - `Error`

5. **Snapshot and input cadence**
   - The server simulates and broadcasts authoritative snapshots at **20 Hz**.
   - The local client emits exactly **one input frame per transport tick** while the room is in `Active`.
   - Remote players are always rendered from authoritative snapshots with interpolation.
   - The local player may use presentation-only prediction, but transport still carries only intents, never authoritative state claims.

6. **Heartbeat and liveness**
   - If the client has not sent an input frame for **2.0 seconds**, it must send a heartbeat.
   - If the server receives no input frame or heartbeat from a connected session for **5.0 seconds**, that player is considered stale/disconnected.
   - If disconnection happens during an active match, downstream match rules resolve a disconnect forfeit.

7. **Duplicate and stale input handling**
   - Every `InputFrame` includes a client tick value.
   - The server stores the latest processed tick per session.
   - Input frames older than or equal to the latest processed tick are ignored.
   - The client never retries by inventing new outcome data; it only re-sends intents through normal transport flow if the connection is still alive.

8. **Reconnect policy**
   - Rejoining an already active match is **not supported in MVP**.
   - A disconnected player may reconnect only after returning to lobby flow.
   - The system should prefer explicit failure and return-to-lobby behavior over hidden reconnect complexity.

9. **Transport framing rules**
   - All protocol messages are length-prefixed and type-tagged.
   - Every message must include protocol version metadata in the initial handshake.
   - Transport payloads should stay compact and explicit; no reflection-heavy or engine-coupled transport format is allowed.

10. **Error visibility rules**
    - The client must surface at least these player-visible errors:
      - connection failed
      - room full
      - invalid room code
      - protocol mismatch
      - server disconnected
    - Error display must be clear and recoverable; the player should know whether to retry, rejoin, or return to menu.

### States and Transitions

| State | Entry Condition | Exit Condition | Behavior |
|-------|-----------------|----------------|----------|
| LocalIdle | App open, no active connection yet | Player submits create/join | Local menu only; no server session yet |
| Connecting | Client opens socket and sends handshake | Handshake succeeds or fails | Wait for protocol validation |
| RoomJoining | Handshake accepted, room create/join intent sent | Server accepts room entry or rejects | Await room snapshot or error |
| LobbyConnected | Room snapshot confirms room membership | Player leaves, disconnects, or room state advances to Countdown | Show room code, roster, ready state |
| MatchConnected | Room snapshot enters `Active` | Match ends or connection fails | Send `InputFrame`, receive live snapshots/events |
| PostMatchConnected | Match enters `Ended` / `Saving` / `ResultsReady` | Return to lobby or disconnect | Receive results/persistence/leaderboard data |
| Disconnected | Socket closed, timeout, or fatal protocol error | Player retries create/join | Show recoverable error or return path |

### Interactions With Other Systems

| System | Direction | Nature of Interaction |
|--------|-----------|-----------------------|
| Match Lifecycle & Room State | This system feeds it | Transport delivers join/leave/ready/rematch intents and authoritative room snapshots |
| Player Controller & Input | This system depends on it | Local `InputFrame` payloads originate from the input runtime contract |
| HUD, Results, and Ranking UI | UI depends on this system | UI reads connection state, room snapshots, errors, and persistence status through transport events |
| Results Persistence & Leaderboard | This system exposes its outputs | Transport forwards persistence status and leaderboard query results from the server |
| Slow Shot & Trap Interaction | This system carries its outcomes | Gameplay effect events and snapshot state travel through the same authoritative snapshot/event channel |

## Formulas

### Heartbeat Trigger

```text
send_heartbeat = (time_since_last_input_frame >= 2.0 seconds)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| time_since_last_input_frame | float seconds | 0+ | client runtime | Time since the last emitted gameplay input frame |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: In non-`Active` room states, the client may still send a keepalive/room intent, but gameplay input must remain disabled.

### Stale Session Detection

```text
session_is_stale = (time_since_last_received_input_or_heartbeat >= 5.0 seconds)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| time_since_last_received_input_or_heartbeat | float seconds | 0+ | server session tracker | Time since the server last heard from the client session |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: Once stale is true during `Active`, match rules must treat the player as disconnected rather than waiting indefinitely.

### Snapshot Budget

```text
snapshot_interval = 1 / 20 = 0.05 seconds
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| server_tick_rate | int Hz | fixed at 20 | ADR-0001 | Number of authoritative simulation/snapshot updates per second |

**Expected output range**: 0.05 seconds between snapshots in normal conditions.
**Edge case**: If a snapshot is delayed, the client must continue interpolation from the last valid snapshot rather than inventing match outcomes.

### Room Capacity Rule

```text
can_join_room = (connected_players < 2)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| connected_players | int | 0-2 in MVP | authoritative room state | Number of active players currently occupying the room |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: Spectators are not supported in MVP; a room at capacity is simply full.

## Edge Cases

| Scenario | Expected Behavior | Rationale |
|----------|------------------|-----------|
| Client sends input before handshake finishes | Ignore gameplay input until room state is `Active` | Prevent invalid or premature gameplay state |
| Player enters an invalid room code | Server rejects join and client shows explicit recoverable error | Keeps demo flow understandable |
| Duplicate `InputFrame` arrives | Server ignores it using session tick tracking | Prevents double-processing |
| Player alt-tabs or stops moving during match | Client sends heartbeat after 2 seconds of no gameplay input | Prevents false disconnects for idle-but-connected players |
| Network drop during active match | Server marks session stale after 5 seconds and downstream rules resolve disconnect forfeit | Keeps the match deterministic |
| Player tries to reconnect mid-match | Reconnect is rejected for MVP active match flow | Avoids scope creep and fairness ambiguity |
| Room is full | Join request is rejected with a room-full error | MVP is fixed at 2 active players |
| Snapshot arrives late | Client keeps rendering from the last authoritative state with interpolation/correction | Presentation may soften jitter but never invent results |

## Dependencies

| System | Direction | Nature of Dependency |
|--------|-----------|---------------------|
| Match Lifecycle & Room State | Other system depends on this | Needs room membership, ready/rematch intents, and live connection state |
| Player Controller & Input | This system depends on Player Controller & Input | Input frames are built from local input-runtime output |
| Results Persistence & Leaderboard | Other system depends on this system | Persistence status and leaderboard responses return through transport |
| HUD, Results, and Ranking UI | Other system depends on this system | UI must show room code, errors, connection state, and authoritative results |
| `docs/architecture/adr-0001-network-authority-and-transport-strategy.md` | Design dependency | Governs protocol, authority, cadence, and reconnect policy |
| `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md` | Design dependency | Governs room-state transitions and disconnect handling |
| `docs/architecture/adr-0007-player-controller-and-input-runtime-contract.md` | Design dependency | Governs payload source and local prediction boundary |

## Tuning Knobs

| Parameter | Current Value | Safe Range | Effect of Increase | Effect of Decrease |
|-----------|--------------|------------|-------------------|-------------------|
| Server tick / snapshot rate | 20 Hz | 15-30 Hz | Smoother remote updates, more bandwidth/CPU | Less bandwidth, more visible latency/jitter |
| Heartbeat silence threshold | 2.0 s | 1.0-3.0 s | More aggressive keepalive traffic | Longer idle gaps before keepalive |
| Stale-session timeout | 5.0 s | 3.0-8.0 s | More tolerant of brief hiccups, slower disconnect resolution | Faster forfeit resolution, more false disconnect risk |
| Room capacity | 2 players | 2-4 players | Supports stretch-scale matches | Keeps MVP simpler and easier to demo |
| Error banner visibility duration | 2.0 s minimum or until dismissed | 1.5-5.0 s | Easier to notice errors | Faster return to interaction, higher miss risk |

## Acceptance Criteria

- [ ] A player can create or join a room only after a successful protocol-version handshake.
- [ ] A full room rejects additional join attempts with a clear recoverable error.
- [ ] The server receives at most one gameplay `InputFrame` per client transport tick while the room is `Active`.
- [ ] If no gameplay input is sent for 2 seconds, the client emits a heartbeat instead of silently disappearing.
- [ ] If the server receives no input or heartbeat for 5 seconds, the session is marked stale and downstream disconnect handling can proceed deterministically.
- [ ] Duplicate or stale client ticks do not create duplicate movement/effect processing on the server.
- [ ] Reconnect-to-active-match is explicitly unsupported in MVP and returns the player to a safe lobby/menu path.
- [ ] UI receives enough transport state to show connection failures, room join failures, and room membership changes clearly.
- [ ] All transport rules remain consistent with ADR-0001, ADR-0002, and ADR-0007.
