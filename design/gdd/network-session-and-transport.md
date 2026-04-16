# System GDD: Network Session & Transport

> **Status**: Draft
> **Last Updated**: 2026-04-16
> **Category**: Foundation
> **Priority**: MVP
> **Primary ADRs**: `docs/architecture/adr-0001-network-authority-and-transport-strategy.md`, `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md`, `docs/architecture/adr-0007-player-controller-and-input-runtime-contract.md`
> **Source Requirements**: `TR-concept-003`, `TR-concept-007`, `TR-concept-012`, `TR-concept-014`, `TR-systems-001`

---

## 1. Overview

`Network Session & Transport` is the foundation system that lets the Unity client join a shared room, send only player/UI intents to a dedicated authoritative C# server, and receive the snapshots/events that every downstream system depends on. Its job is not to decide gameplay outcomes; its job is to guarantee that room membership, input delivery, liveness, and protocol compatibility are deterministic enough for the server to stay authoritative.

### In Scope
- protocol-version handshake before room join
- length-prefixed TCP framing and DTO serialization
- client session creation, room join/leave, and stale-session cleanup
- tick-tagged input frame delivery from the local player
- shared UI-intent delivery for `JoinRoom`, `Ready`, `Rematch`, and `RefreshLeaderboard`
- authoritative room snapshots and server event delivery back to clients
- heartbeat / keepalive behavior and duplicate-frame rejection

### Out of Scope
- room-state progression rules after a message is accepted (`Lobby`, `Countdown`, `Active`, `Ended`, `Saving`, `ResultsReady`)
- gameplay simulation, battery scoring, trap resolution, or victory logic
- MySQL persistence or leaderboard ranking formulas
- HUD/audio presentation logic beyond carrying authoritative payloads to those systems

### Design Goal
The player should feel that the match starts quickly, movement feedback is responsive, and all competitive outcomes are trustworthy because the server is always the single source of truth.

---

## 2. Player Fantasy

The transport layer supports the fantasy of entering a clean sci-fi training arena that “just works.” From the player perspective:
- joining a room should feel immediate and dependable, not like a fragile lab demo
- the other player should appear present in the same shared space with clear authoritative updates
- local movement should feel responsive enough to support route-racing and slow-shot timing
- disconnects, room failures, and mismatched builds should fail clearly instead of producing hidden desync

From the class-demo perspective, this system should make the client/server boundary visible: the Unity client shows responsive input and readable room state, while the server demonstrably owns match truth.

---

## 3. Detailed Rules

### 3.1 Authority Boundary
- The Unity client may send **intents only**.
- The dedicated external C# server owns room membership, match state, score, battery availability, trap outcomes, slow-shot outcomes, disconnect results, and persistence payload creation.
- The Unity client must never authoritatively compute score, pickup success, trap resolution, or match victory.
- The Unity client must never connect directly to MySQL.

### 3.2 Transport Topology
- MVP topology is **2 Unity desktop clients ↔ 1 authoritative C# server ↔ MySQL (server only)**.
- The protocol must stay architecturally ready for **4 players later**, but the first playable target assumes **2 connected players**.
- The Unity client is thin: it captures keyboard/mouse input, renders predicted local presentation, and renders authoritative server snapshots/events.

### 3.3 Session Lifecycle
1. Client enters a local `Menu / Connecting` wrapper state.
2. Client opens a TCP connection to the authoritative server.
3. Client and server complete a version handshake before any room action is accepted.
4. After handshake success, the client may send `Connect(playerName)` and `JoinRoom(roomCode)`.
5. Once the server accepts the player into a room, the room snapshot becomes the only shared-state source for lobby/countdown/active/results flow.
6. If the player leaves voluntarily before a match, the client sends `LeaveRoom` and returns to local menu flow.
7. If the transport becomes stale or closes unexpectedly, the server resolves the room using ADR-0002 disconnect rules.

### 3.4 Accepted Client → Server Message Families

| Message Family | Source | Used In | Payload Rules | Notes |
|---|---|---|---|---|
| `Connect` | UI/bootstrap | before room join | player display name + protocol version | must succeed before join is accepted |
| `JoinRoom` | UI intent | Lobby entry | room code / quick-join target | rejected on version mismatch or full room |
| `LeaveRoom` | UI intent | Lobby / ResultsReady | session id + room id | not used to escape an active MVP match |
| `Ready` | UI intent | Lobby | boolean intent edge | drives room-state service, not local UI logic |
| `Rematch` | UI intent | ResultsReady | boolean intent edge | valid only during rematch window |
| `RefreshLeaderboard` | UI intent | ResultsReady / leaderboard panel | optional limit / scope | server decides final query parameters |
| `InputFrame` | gameplay input | Active | sequence/tick + movement vector + aim + fire edge | the primary gameplay transport message |
| `Heartbeat` | transport keepalive | any shared server state | session id + latest known client tick | sent only when input silence crosses the keepalive threshold |

### 3.5 Accepted Server → Client Message Families

| Message Family | Source System | Purpose | Client Behavior |
|---|---|---|---|
| `HandshakeAccepted / HandshakeRejected` | Network Session & Transport | version compatibility gate | proceed or fail clearly before join |
| `RoomSnapshot` | Match Lifecycle & Room State | authoritative room roster, phase, timer, and player state | render read-only state |
| `GameplayEvent` | gameplay systems through server event channel | battery pickup, hit confirmation, trap/debuff, countdown, end reason | show feedback only |
| `CorrectionSnapshot` | server simulation | authoritative local-player correction and remote-player transforms | reconcile/interpolate |
| `PersistenceStatus` | Results Persistence & Leaderboard | saving succeeded / failed | show results banner only |
| `LeaderboardResponse` | Results Persistence & Leaderboard | ranking rows | render the returned rows only |
| `Disconnect / RoomClosed / Error` | Network Session & Transport or room service | explicit failure reason | return to safe UI state and show reason |

### 3.6 Framing and Serialization Rules
- Transport uses **length-prefixed framed TCP messages** so partial byte reads can be reconstructed safely.
- Every message envelope includes:
  - payload length
  - protocol version
  - message type (and version if needed)
  - session identifier once the session exists
- DTO fields stay human-readable and debugging-friendly at the schema level.
- The transport wrapper must marshal parsed snapshot/event DTOs onto the Unity main thread before presentation systems consume them.

### 3.7 Tick, Prediction, and Delivery Rules
- The authoritative server simulates at **20 ticks per second**.
- The server broadcasts authoritative snapshots at **20 Hz** for MVP.
- The locally controlled player may predict presentation immediately after capturing input, but the next server correction always wins.
- Remote players are interpolation-only.
- Each `InputFrame` carries a local sequence/tick so the server can ignore duplicate or older frames.
- The transport layer must not fabricate missing gameplay inputs; silence is represented by either an idle `InputFrame` during active play or a `Heartbeat` if no input frame has been sent recently.

### 3.8 Liveness and Failure Rules
- If no input frame has been sent for **2 seconds**, the client sends a heartbeat.
- If the server sees no input or heartbeat for **5 seconds**, the session becomes stale.
- Reconnect to an **active** match is **not supported in the MVP**.
- A protocol-version mismatch must be rejected before room join with an explicit error.
- Duplicate or older client ticks are ignored without mutating authoritative state.
- Focus loss / alt-tab must not silently kill the connection; it still obeys the same heartbeat timeout path.

### 3.9 Explicit Prohibitions
- No client-authored competitive state.
- No direct Unity-to-MySQL path.
- No hidden transport bypass for rematch, ready, or leaderboard refresh.
- No “best effort” local win/timeout inference in the client when snapshots are delayed.

---

## 4. Formulas

### 4.1 Core Cadence
- `server_tick_rate_hz = 20`
- `server_tick_interval_ms = 1000 / 20 = 50`
- `snapshot_rate_hz = 20`
- `snapshot_interval_ms = 50`

### 4.2 Liveness Windows
- `heartbeat_required = (now - last_sent_input_frame_at) >= 2000 ms`
- `session_stale = (now - max(last_received_input_at, last_received_heartbeat_at)) >= 5000 ms`

### 4.3 Input Acceptance
- `accept_input_frame = protocol_version == supported_version AND client_tick > last_processed_tick[session] AND room_phase == Active`
- `reject_input_frame = NOT accept_input_frame`

### 4.4 Room Capacity
- `required_players_mvp = 2`
- `max_players_mvp_default = 2`
- `max_players_protocol_cap = 4`
- `can_start_shared_room = connected_players == required_players_mvp AND ready_players == connected_players`

### 4.5 Reconnect Policy
- `allow_reconnect_to_active_match = false`
- `allow_reconnect_post_match = room_phase IN {Lobby, ResultsReady} AND active_match == false`

### 4.6 Ownership Check
- `authoritative_write_allowed(state) = state.owner == server`
- `client_write_allowed(state) = false` for `score`, `battery_state`, `trap_state`, `slow_state`, `match_end_reason`, and `room_phase`

---

## 5. Edge Cases

| Scenario | Expected Resolution | Why It Matters |
|---|---|---|
| Client protocol version does not match server | reject before room join and show explicit compatibility error | prevents undefined parsing or mixed-rule matches |
| TCP packet arrives partially | framing layer buffers until full payload length is available | avoids corrupted DTO reads |
| Same `InputFrame` arrives twice | ignore if `client_tick <= last_processed_tick[session]` | prevents duplicate movement/fire side effects |
| Player disconnects during `Lobby` or `Countdown` | server returns room to `Lobby` using ADR-0002 | keeps ready/countdown state deterministic |
| Player disconnects during `Active` in MVP 2-player match | server issues `DisconnectForfeit` result for disconnected player | keeps authority and fairness explicit |
| Client alt-tabs and stops sending movement | heartbeats continue until the stale threshold is crossed | avoids silent dead sessions |
| Client tries to rejoin an active match after timeout | reject reconnect and force return to safe lobby/menu flow | MVP intentionally avoids live state restoration complexity |
| Snapshot arrives late after local prediction already moved the player | authoritative correction snapshot reconciles immediately | preserves responsiveness without weakening authority |
| Leaderboard refresh is clicked repeatedly | transport may send repeated UI intents, but server owns idempotent response behavior | keeps UI spam from mutating shared state |
| Room is full because 4-player seams are not enabled yet | reject join with explicit capacity error | keeps the 2-player MVP boundary obvious |

---

## 6. Dependencies

### Upstream Inputs
- `design/gdd/game-concept.md` for 2-player MVP, authoritative-server, and results-visibility requirements
- `design/gdd/systems-index.md` for system priority, dependency order, and foundation-layer role
- `docs/architecture/adr-0001-network-authority-and-transport-strategy.md` for transport topology, framing, tick cadence, heartbeats, and reconnect policy
- `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md` for room states, disconnect handling, ready/rematch windows, and end-state ownership
- `docs/architecture/adr-0007-player-controller-and-input-runtime-contract.md` for tick-aligned `InputFrame` structure and local prediction boundary
- `docs/registry/architecture.yaml` for forbidden patterns and cross-system ownership contracts
- `.claude/docs/technical-preferences.md` for Unity 6.3, C#, Input System, and testing expectations

### Downstream Consumers
- `Match Lifecycle & Room State` depends on transport to accept join/ready/rematch intents and emit authoritative room snapshots.
- `Player Controller & Input` depends on transport to serialize gameplay input at the fixed tick cadence.
- `Arena Battery Economy & Scoring` and `Slow Shot & Trap Interaction` depend on deduplicated, ordered authoritative tick delivery.
- `Results Persistence & Leaderboard` depends on transport to return persistence and leaderboard responses to clients.
- `HUD, Results, and Ranking UI` depends on transport for all shared-state visibility.

### External Runtime Dependencies
- Unity Input System on the client
- .NET async sockets / TCP networking on the server
- a shared DTO/message schema contract between client and server

---

## 7. Tuning Knobs

| Knob | Default | Safe Range / Options | Why Tune It |
|---|---|---|---|
| `server_tick_rate_hz` | 20 | 15-30 during prototype only | balance responsiveness vs implementation/debug complexity |
| `snapshot_rate_hz` | 20 | match server tick for MVP | keep correction feel and payload cadence readable |
| `heartbeat_interval_ms` | 2000 | 1000-2500 | reduce silent disconnects without flooding the wire |
| `stale_timeout_ms` | 5000 | 4000-6000 | trade off resilience vs fast disconnect resolution |
| `required_players_mvp` | 2 | fixed at 2 for first playable | maintain delivery scope and room-state clarity |
| `max_players_protocol_cap` | 4 | 2 or 4 depending on milestone | preserve the architecture-ready seam without forcing MVP UI/testing scope |
| `room_join_error_copy` | concise explicit reason | version mismatch / room full / room closed | improve demo readability when connection fails |
| `prediction_correction_policy` | immediate authoritative snap or short smoothing on client | tune during prototype | keep local feel responsive without hiding authority |

### Locked MVP Decisions
- custom length-prefixed TCP transport is locked for MVP
- reconnect-to-active-match is off for MVP
- direct client database access is permanently forbidden
- competitive state stays server-owned even if local prediction is smoothed visually

---

## 8. Acceptance Criteria

### Traceability Coverage
- [ ] `TR-concept-003` is satisfied: the design explicitly supports a 2-player MVP while preserving 4-player-ready protocol seams.
- [ ] `TR-concept-007` is satisfied: the Unity client sends intents only and the server is the sole writer of competitive match state.
- [ ] `TR-concept-012` is satisfied: keyboard/mouse-derived input frames are the expected client gameplay transport input.
- [ ] `TR-concept-014` is satisfied: room snapshots, disconnect results, persistence status, and leaderboard responses are visible to the client through the server pathway.
- [ ] `TR-systems-001` is satisfied: this system is explicitly defined as the foundation transport that all downstream multiplayer systems depend on.

### Functional Design Checks
- [ ] The GDD states that transport uses a dedicated authoritative C# server and custom length-prefixed TCP framing.
- [ ] The GDD fixes the MVP cadence at 20 Hz server ticks and 20 Hz authoritative snapshots.
- [ ] The GDD includes explicit formulas for heartbeat and stale-session timeout behavior.
- [ ] The GDD forbids reconnecting to an active match in MVP.
- [ ] The GDD names the only allowed shared UI intents: join, ready, rematch, and leaderboard refresh.
- [ ] The GDD calls out duplicate/older input-frame rejection.
- [ ] The GDD stays consistent with ADR-0001, ADR-0002, ADR-0007, `architecture.md`, and `docs/registry/architecture.yaml`.

### Verification Targets
- [ ] Two clients can complete handshake, join the same room, and receive the same authoritative room phase without client-derived match state.
- [ ] Disconnecting one client during an active 2-player MVP match yields the same `DisconnectForfeit` outcome on the remaining client.
- [ ] Partial TCP reads can be reconstructed safely by the framing layer.
- [ ] Focus loss / idle input still follows the heartbeat → stale-session path instead of silently hanging the room.
- [ ] Repeated leaderboard refresh or duplicate input messages do not create duplicate authoritative state changes.
