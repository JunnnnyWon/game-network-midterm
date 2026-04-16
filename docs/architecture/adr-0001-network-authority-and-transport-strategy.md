# ADR-0001: Network Authority and Transport Strategy

## Status

Accepted

## Date

2026-04-16

## Last Verified

2026-04-16

## Decision Makers

User, Codex (architecture synthesis)

## Summary

Battery Rush Arena will use a **thin Unity client** connected to a **separate dedicated authoritative C# server** over a **custom TCP intent/snapshot protocol**. The Unity client is responsible only for input capture, presentation, and UI, while the server owns match state, score resolution, effect resolution, disconnect handling, and the only path to MySQL persistence. The locally controlled player will use **client-side input prediction plus server reconciliation** for responsiveness; remote players will remain interpolation-only, and transport liveness/version rules are fixed in this ADR.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3.10f1) |
| **Domain** | Networking |
| **Knowledge Risk** | HIGH — post-cutoff, must verify |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/current-best-practices.md`, `docs/engine-reference/unity/deprecated-apis.md`, `docs/engine-reference/unity/modules/networking.md`, `docs/engine-reference/unity/modules/input.md`, `docs/engine-reference/unity/modules/ui.md` |
| **Post-Cutoff APIs Used** | Unity Input System package; UI Toolkit runtime UI. Unity Netcode for GameObjects explicitly not chosen for the core match transport. |
| **Verification Required** | Verify background socket client stability in Unity 6.3 player builds, snapshot interpolation feel at target tick rate, disconnect handling, and that the chosen client library does not block the main thread. |

> **Knowledge-gap note:** Unity 6.3 is beyond the model cutoff. This ADR deliberately minimizes reliance on Unity-specific multiplayer packages by keeping the authoritative server external. Input System and UI Toolkit usage must still be checked against the pinned Unity 6.3 references.

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | None |
| **Enables** | ADR-0002 Match State Machine and Event Ordering; ADR-0003 Persistence Boundary and Leaderboard Formula; ADR-0004 Runtime UI Stack and Screen Flow |
| **Blocks** | First playable multiplayer prototype, networking system GDDs, persistence system implementation |
| **Ordering Note** | This ADR must be accepted before room-state, scoring, persistence, or UI ADRs can finalize their interfaces. |

## Context

### Problem Statement

The project needs one authoritative multiplayer architecture that is simple enough for a midterm assignment, visible enough for class demonstration, and strict enough to prevent score spoofing, duplicate pickups, or client-side cheating. Without this decision, every downstream system would guess who owns state, how the Unity client talks to the server, and how match results reach MySQL.

### Current State

The repo has a concept document and a master architecture draft, but there is no implementation yet. The concept already commits to:
- Unity client on PC with keyboard/mouse
- separate C# server
- MySQL persistence
- server-authoritative score, effects, and victory
- 2-player MVP with architecture that can scale to 4 players later

### Constraints

- The assignment explicitly requires a **Unity client**, **C# server**, and **MySQL**.
- The game is small-scale and demo-oriented; reliability and clarity matter more than large-player scalability.
- The Unity client should stay easy to debug and not become the authority for competitive state.
- Direct Unity-client-to-MySQL access is unsafe and unacceptable.
- The networking solution must be understandable enough to explain in a presentation.

### Requirements

- Must support 2 concurrent players in the MVP and leave clear seams for later 4-player support.
- Must keep score, pickups, trap effects, slow-shot effects, and victory authoritative on the server.
- Must allow the Unity client to feel responsive without letting the client decide outcomes.
- Must support deterministic result persistence and leaderboard queries.
- Must avoid deprecated Unity multiplayer assumptions such as UNet or legacy Input APIs.
- Must keep implementation complexity moderate for a class project.

## Decision

Battery Rush Arena will use a **dedicated external authoritative C# server** and a **custom TCP-based message protocol**. Unity will not use Netcode for GameObjects, Mirror, or Photon for the core match loop.

### Core approach

1. **Dedicated external server**
   - A standalone C# process owns rooms, match state, authoritative transforms, battery state, trap state, slow-shot state, timer, and result creation.
   - The server is the only component allowed to write match results or query/update MySQL.

2. **Thin Unity client**
   - Unity captures local keyboard/mouse input using the **new Input System**.
   - Unity sends **input intents** and **UI intents** to the server.
   - Unity renders snapshots from the server, using interpolation/correction for presentation.
   - Unity never computes authoritative score, trap hits, pickup success, or win/loss state.

3. **Custom TCP intent/snapshot protocol**
   - Chosen for implementation clarity, ordered delivery, and simplicity in a small-player-count assignment project.
   - Use a **length-prefixed framed protocol** over TCP so the Unity client can safely reconstruct messages from a byte stream.
   - Include a **protocol version** and **message type/version** in the handshake so client and server can reject incompatible builds cleanly.
   - Message categories:
     - `connect / join-room / leave-room`
     - `ready / rematch / leaderboard-refresh`
     - `input-frame`
     - `room-state-snapshot`
     - `match-event`
     - `result-persisted / result-failed`
   - The protocol is intentionally small and human-readable at the DTO level.

4. **Tick and snapshot model**
   - The server simulates at **20 ticks per second**.
   - Each client sends input frames tagged by local sequence/tick.
   - The server broadcasts **authoritative snapshots at 20 Hz** for the MVP.
   - The Unity client interpolates visible transforms for **remote entities** between the latest confirmed snapshots.
   - The locally controlled player predicts its own movement/fire intent immediately, then reconciles against the next authoritative server snapshot.
   - No rollback netcode will be used in the MVP.

5. **Authority boundaries**
   - **Client-owned**: local input capture, local aim vector derivation, local prediction of the owned avatar, rendering, UI state presentation, audio playback.
   - **Server-owned**: room membership, ready state, countdown, movement simulation outcome, collision/pickup resolution, projectile hit resolution, trap resolution, score totals, match end reason, disconnect handling, and persistence payload creation.
   - **Database-owned**: persisted records only, accessed through the server gateway.

6. **Transport liveness and compatibility are part of the decision**
   - The client sends a heartbeat whenever no input frame has been sent for **2 seconds**.
   - The server marks a session stale after **5 seconds** without heartbeat or input and resolves the match using the room-state ADR rules.
   - Reconnect to an active match is **not supported in the MVP**; reconnect is only allowed after returning to lobby.
   - The handshake must reject protocol-version mismatches with an explicit error before room join.
   - The server stores the **last processed client tick per session** and ignores duplicate or older input frames.

7. **No direct database access from Unity**
   - Unity receives leaderboard data only from the server.
   - The server is responsible for validation, idempotency, and retry-safe writes.

### Architecture

```text
+---------------------------+         TCP intent/snapshot         +------------------------------+
| Unity 6.3 Client          | <--------------------------------> | Dedicated C# Game Server     |
|---------------------------|                                      |------------------------------|
| Input System              | ---- input-frame / UI-intent ----> | Room State Machine           |
| Player Controller         | <--- room-state / match events ---- | Movement + Match Rules       |
| Snapshot Interpolator     |                                      | Pickup / Trap / Slow Resolve |
| UI Toolkit HUD / Results  | <--- leaderboard / persist status - | Persistence Gateway          |
| Audio / Presentation      |                                      | MySQL Write Queue            |
+---------------------------+                                      +---------------+--------------+
                                                                                  |
                                                                                  v
                                                                        +-------------------+
                                                                        | MySQL (ckgame)    |
                                                                        | match results     |
                                                                        | leaderboard rows  |
                                                                        +-------------------+
```

### Key Interfaces

```csharp
public enum ClientMessageType {
    Connect,
    JoinRoom,
    LeaveRoom,
    ReadyState,
    RematchVote,
    InputFrame,
    RefreshLeaderboard
}

public enum ServerMessageType {
    RoomSnapshot,
    MatchEvent,
    MatchEnded,
    LeaderboardData,
    PersistenceStatus,
    Error
}

public record InputFrameDto(
    int ClientTick,
    float MoveX,
    float MoveY,
    float AimX,
    float AimY,
    bool FirePressed);

public record RoomSnapshotDto(
    int ServerTick,
    MatchState MatchState,
    float TimeRemaining,
    IReadOnlyList<PlayerSnapshotDto> Players,
    IReadOnlyList<BatterySnapshotDto> Batteries,
    IReadOnlyList<EffectSnapshotDto> Effects);

public interface IClientTransport {
    Task ConnectAsync(string playerName, string roomCode, CancellationToken ct);
    ValueTask SendInputAsync(InputFrameDto frame, CancellationToken ct);
    ValueTask SendUiIntentAsync(ClientMessageType type, string? payload, CancellationToken ct);
    event Action<RoomSnapshotDto> SnapshotReceived;
    event Action<ServerEventDto> EventReceived;
}

public interface IServerMatchGateway {
    void OnClientConnected(SessionId sessionId, string playerName);
    void OnClientDisconnected(SessionId sessionId);
    void OnInputFrame(SessionId sessionId, InputFrameDto frame);
    void OnUiIntent(SessionId sessionId, ClientMessageType type, string? payload);
}
```

### Implementation Guidelines

- Use the Unity **new Input System** for movement and aiming; do not use `Input.GetKey`, `Input.GetAxis`, or legacy mouse APIs.
- Build input around an **Input Actions asset** and generated C# wrapper or equivalent callback-based setup; do not poll legacy input in `Update()`.
- Keep the Unity network client on a background thread/task and marshal only parsed snapshot / event DTOs onto the main thread.
- Use compact DTOs and explicit enums instead of reflection-heavy or engine-coupled serialization.
- Keep the transport framing deterministic: fixed header, payload length, protocol version, and message type before the payload body.
- Make connect/reconnect/timeout behavior explicit, including heartbeat/keepalive and idempotent handling for repeated input frames.
- Keep all score, pickup, slow, and trap outcomes server-derived.
- Treat 4-player support as a protocol-capable stretch target, not an MVP promise.
- Use UI intents only for shared-state commands such as join, ready, rematch, and leaderboard refresh; keep local menu interactions client-only.

## Alternatives Considered

### Alternative 1: Unity Netcode for GameObjects + Host/Listen Server

- **Description**: Use NGO and let one Unity client act as host/server while the second client joins.
- **Pros**: Fast to prototype inside Unity; less custom serialization work; official Unity package.
- **Cons**: Host player gains authority complexity; presentation and authority become entangled in the Unity project; awkward fit for separate C# server + MySQL requirement; harder to explain as a clean client/server assignment architecture.
- **Estimated Effort**: Lower short-term setup, higher long-term architectural mismatch.
- **Rejection Reason**: The assignment and architecture already point to a separate C# server. Listen-server authority would weaken fairness and muddle the presentation of server-only result persistence.

### Alternative 2: Unity Netcode for GameObjects + Dedicated Unity Headless Server

- **Description**: Use NGO end-to-end with a Unity headless server build.
- **Pros**: Official Unity multiplayer workflow; no custom protocol needed; easier snapshot replication for GameObjects.
- **Cons**: Adds Unity-headless build/deployment complexity; still couples the authoritative server to Unity runtime concerns; unnecessary for a small top-down game with a separate server requirement; less aligned with “my own client-server-database program.”
- **Estimated Effort**: Medium to high.
- **Rejection Reason**: Overkill for the assignment and less aligned with the requirement to build a distinct C# server program.

### Alternative 3: Custom UDP Server Protocol

- **Description**: Use custom UDP for lower latency movement and snapshot delivery.
- **Pros**: Lower latency potential; avoids TCP head-of-line blocking; closer to typical realtime-game transport patterns.
- **Cons**: More engineering complexity for reliability, ordering, retransmission, and presentation-safe packet loss handling; higher bug risk for a short assignment.
- **Estimated Effort**: Higher than chosen approach.
- **Rejection Reason**: The MVP benefits more from clarity and reliability than raw transport performance. TCP is simpler to implement and explain for 2-player authoritative play.

## Consequences

### Positive

- The authority boundary is extremely clear: Unity presents, server decides.
- The architecture matches the assignment brief closely and is easy to explain in 발표 materials.
- MySQL access is safely isolated behind the server.
- The protocol stays small enough to debug manually during development.
- Future ADRs can depend on a stable server-owned room/match model.

### Negative

- TCP may introduce head-of-line blocking during packet bursts or poor connections.
- More custom networking code must be written than with NGO/Photon.
- Snapshot interpolation and server correction must be implemented manually.
- The Unity client cannot rely on built-in networked GameObject conveniences.

### Neutral

- 4-player scalability is structurally possible but intentionally deferred from MVP commitment.
- The project remains easier to reason about at the cost of some extra plumbing code.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| TCP latency spikes feel bad during movement | Medium | Medium | Keep payloads tiny, 20 Hz cadence, interpolate on client, test on real LAN/Wi-Fi |
| Background socket client mishandles thread-to-main-thread handoff in Unity | Medium | High | Use a single transport wrapper and explicit main-thread dispatch boundary |
| Custom protocol creates duplicate or out-of-order intent handling bugs | Medium | High | Tag messages with session + tick ids and treat server handlers as idempotent |
| 4-player scaling stresses snapshot size or room flow | Low | Medium | Treat 2-player as MVP and test 4-player later behind configuration |
| Developers accidentally add direct Unity-to-MySQL access | Medium | High | Ban client-side DB access in registry and future ADRs |

## Performance Implications
- **CPU**: Low impact on Unity client; moderate server simulation cost at 20 Hz.
- **Memory**: Low; snapshots and DTOs are small.
- **Load Time**: No major impact beyond room connection setup.
- **Network**: Target low-bandwidth play for MVP, aiming to stay well under **10 KB/s per client** at 2-player scale.

## Unity 6.3 Verification Backlog
- Verify background socket receive loops and shutdown behavior in actual Unity 6.3 desktop builds.
- Verify main-thread marshaling does not touch Unity objects from background tasks.
- Verify framed serialization survives partial TCP reads and reconnect attempts cleanly.
- Verify client-side prediction + reconciliation feels acceptable on LAN and typical campus Wi-Fi.
- Verify focus loss / alt-tab / application pause does not silently break heartbeat timing.

## Migration Plan

No migration from existing code is needed yet because implementation has not started.

1. Create the transport DTOs and transport wrapper in both Unity client and C# server.
2. Implement room join/ready/countdown lifecycle on the server.
3. Hook Unity client input and UI intents into the transport layer.
4. Implement server snapshots and client interpolation.
5. Integrate persistence gateway only after match-end events are stable.

**Rollback plan**: If the custom TCP approach becomes unstable, fall back to a simpler listen-server prototype only for internal experimentation, but keep this ADR as the authoritative target architecture unless explicitly superseded.

## Validation Criteria

- [ ] Two clients can connect to the server, join the same room, and complete a full match without authority ambiguity.
- [ ] The Unity client never directly mutates or invents score, battery, trap, or victory state.
- [ ] Disconnecting one client during an active match produces a deterministic server-owned end result.
- [ ] Leaderboard queries reach the client only through the server pathway.
- [ ] Movement and pickup feel acceptable at the chosen tick/snapshot cadence in local testing.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/game-concept.md` | Match Scale | **TR-concept-003** — MVP supports 2 players; architecture should scale to 4 | Chooses a dedicated authoritative server with room/session seams that can later admit more than 2 players without changing client authority rules |
| `design/gdd/game-concept.md` | Authority | **TR-concept-007** — Server authoritatively resolves score, effects, and victory | Makes the external C# server the sole writer of competitive match state and requires server-owned tick ordering |
| `design/gdd/game-concept.md` | Control Scheme | **TR-concept-012** — Game is played with keyboard and mouse on PC | Locks the Unity client to the new Input System and sends keyboard/mouse-derived intents over the transport boundary |
| `design/gdd/game-concept.md` | Results Visibility | **TR-concept-014** — Network and database outcomes must be visible to players | Ensures leaderboard and match-result data flow through the server-to-client snapshot/event path rather than hidden client-side state |
| `design/gdd/systems-index.md` | Network Session & Transport | Foundation-layer transport system | Establishes the dedicated server + custom framed TCP transport as the foundation contract for all later systems |
| `design/gdd/systems-index.md` | Match Lifecycle & Room State | Match-state system depends on transport | Explicitly enables downstream room-state and event-ordering ADR work while deferring end-state policy details to ADR-0002 |

## Related

- `docs/architecture/architecture.md`
- `design/gdd/game-concept.md`
- `design/gdd/systems-index.md`
- Enables ADR-0002 Match State Machine and Event Ordering
- Enables ADR-0003 Persistence Boundary and Leaderboard Formula
- Enables ADR-0004 Runtime UI Stack and Screen Flow
