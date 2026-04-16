# Battery Rush Arena — Master Architecture

## Document Status
- Version: 0.1-draft
- Last Updated: 2026-04-16
- Engine: Unity 6.3 LTS (6000.3.10f1)
- GDDs Covered: design/gdd/game-concept.md, design/gdd/systems-index.md
- ADRs Referenced: adr-0001-network-authority-and-transport-strategy.md, adr-0002-match-state-machine-and-event-ordering.md, adr-0003-persistence-boundary-and-leaderboard-formula.md, adr-0004-runtime-ui-stack-and-screen-flow.md, adr-0005-battery-spawn-and-score-pacing-model.md, adr-0006-slow-shot-and-trap-fairness-rules.md

## Engine Knowledge Gap Summary
- Unity 6.3 LTS exact build is **post-cutoff / high-risk** for version-sensitive API details.
- Follow the existing engine reference docs before relying on runtime API assumptions.
- Highest-risk domains for this project:
  - **Networking**: Unity's official Netcode exists, but this project intentionally uses a **separate C# authoritative server**, so Unity networking packages are not the primary architecture driver.
  - **Input**: use the **new Input System**, not legacy `Input.*` APIs.
  - **UI**: UI Toolkit is production-ready in Unity 6, and is the preferred runtime UI stack for this project.
  - **Rendering**: URP + 2D Renderer is the intended render pipeline.
- Low-risk/contained domains:
  - Physics2D for local collision and trigger detection
  - simple URP 2D sprite rendering
  - Unity Test Framework for EditMode/PlayMode coverage

## Technical Requirements Baseline

| Req ID | GDD | System | Requirement | Domain |
|--------|-----|--------|-------------|--------|
| TR-concept-001 | game-concept.md | Match Goal | First player to 10 points wins immediately | Gameplay |
| TR-concept-002 | game-concept.md | Timeout Rule | If timer expires, highest score wins | Gameplay |
| TR-concept-003 | game-concept.md | Match Scale | MVP supports 2 players; architecture should scale to 4 | Networking |
| TR-concept-004 | game-concept.md | Core Loop | Players move in a top-down 2D arena and collect batteries | Core |
| TR-concept-005 | game-concept.md | Interference | Players can fire a slow-shot skill | Gameplay |
| TR-concept-006 | game-concept.md | Hazards | Arena contains map traps that affect routing | Gameplay |
| TR-concept-007 | game-concept.md | Authority | Server authoritatively resolves score, effects, and victory | Networking |
| TR-concept-008 | game-concept.md | Persistence | Match results are stored in MySQL | Persistence |
| TR-concept-009 | game-concept.md | Ranking | Players can query and view leaderboard/ranking data | UI/Persistence |
| TR-concept-010 | game-concept.md | Readability | Match state must remain instantly readable on a single PC display | UI |
| TR-concept-011 | game-concept.md | Session Length | Rounds should be short, repeatable, and demo-friendly | Gameplay |
| TR-concept-012 | game-concept.md | Control Scheme | Game is played with keyboard and mouse on PC | Input |
| TR-concept-013 | game-concept.md | Fairness | Competitive rules must avoid oppressive or unclear disruption | Gameplay |
| TR-concept-014 | game-concept.md | Results Visibility | Network and database outcomes must be visible to players | UI/Networking |

## System Layer Map

| Layer | Module | Owns | Notes |
|------|--------|------|------|
| Foundation | Network Session & Transport | socket connections, client identity, message framing, server tick dispatch | Custom protocol between Unity client and C# server; no direct Unity-to-MySQL access |
| Foundation | Match Persistence Gateway | async result write queue, leaderboard query contract, idempotency keys | Runs on the server side and is the only layer that touches MySQL |
| Core | Match Lifecycle & Room State | lobby, ready, countdown, active, ended, saving, results-ready states | Server authoritative state machine |
| Core | Player Controller & Input | local input capture, local movement intent, aim direction, local camera focus | Client-owned input, server-approved simulation outcome |
| Feature | Arena Battery Economy & Scoring | battery spawn table, pickup resolution, score totals, contested pickup ordering | Server owns authoritative scoring |
| Feature | Slow Shot & Trap Interaction | projectile state, debuff application, trap triggers, effect durations | Server resolves hits and debuffs; client renders feedback |
| Presentation | HUD, Results, and Ranking UI | match HUD, countdown, score display, results screen, leaderboard screen | UI Toolkit runtime UI |
| Presentation | Audio Feedback | pickup SFX, hit SFX, countdown cues, win/loss cues | Optional polish layer; no gameplay authority |

## Module Ownership

### Foundation

#### Network Session & Transport
- **Owns**: TCP connection lifecycle, session tokens, room join/leave messages, heartbeat, serialization envelope, protocol version handshake, 20 Hz server tick + snapshot cadence, duplicate-frame rejection, stale-session timeout rules
- **Exposes**:
  - `Connect(playerName)`
  - `JoinRoom(roomCode)`
  - `SendInput(InputFrame frame)`
  - `SendUIIntent(UIIntent intent)`
- **Consumes**: nothing above it; it is the base transport
- **Engine/API use**:
  - Unity client: Input System and background network client wrapper in C#
  - Server: .NET sockets / async networking
- **Boundary rule**: clients may submit intents only; they never submit authoritative score, trap, or victory state. Transport sends a heartbeat after 2 seconds of silence, marks sessions stale after 5 seconds without heartbeat/input, and does not allow reconnect to an active match in the MVP.

#### Match Persistence Gateway
- **Owns**: MySQL connection access, result insert/update logic, leaderboard query logic, retry-safe write operations
- **Exposes**:
  - `PersistMatchResult(MatchResultPayload payload)`
  - `QueryLeaderboard(LeaderboardScope scope, int limit)`
- **Consumes**: final match result emitted by Match Lifecycle & Room State
- **Boundary rule**: this module is server-only; Unity clients never connect to MySQL directly

### Core

#### Match Lifecycle & Room State
- **Owns**: room roster, ready state, countdown, match timer, win reason, rematch state, disconnect handling
- **Exposes**:
  - `TryStartCountdown()`
  - `BeginMatch()`
  - `EndMatch(EndReason reason)`
  - `HandleDisconnect(PlayerId playerId)`
- **Consumes**: validated join/leave events from Network Session; score/victory events from Arena Battery Economy
- **Boundary rule**: all clients render the state they receive; they do not infer match state on their own

#### Player Controller & Input
- **Owns**: local key/mouse mapping, movement vector generation, aim direction, fire intent, menu confirm/back intent
- **Exposes**:
  - `BuildInputFrame()`
  - `BuildAimSnapshot()`
- **Consumes**: match state updates, debuff/trap feedback, authoritative transform corrections
- **Boundary rule**: client predicts presentation only; final position and movement-affecting debuffs come from server resolution

### Feature

#### Arena Battery Economy & Scoring
- **Owns**: battery spawn points, active battery set, respawn cadence, point values, score totals, contested pickup ordering
- **Exposes**:
  - `ResolvePickupAttempt(PlayerId playerId, BatteryId batteryId, ServerTick tick)`
  - `RespawnDueBatteries(ServerTick tick)`
  - `GetScoreboardSnapshot()`
- **Consumes**: player overlap/pickup intents, room state, player count scaling constants
- **Boundary rule**: batteries are collected on server-validated overlap only; duplicate pickup attempts are ignored idempotently

#### Slow Shot & Trap Interaction
- **Owns**: projectile lifetime, cooldown timer, slow debuff duration, trap trigger tables, immunity windows, effect stacking policy
- **Exposes**:
  - `FireSlowShot(PlayerId playerId, AimVector aim)`
  - `ResolveProjectileHits(ServerTick tick)`
  - `ResolveTrapTriggers(ServerTick tick)`
- **Consumes**: movement positions from Match state / player transforms
- **Boundary rule**: effect stacking is capped and deterministic; server emits explicit status transitions to clients

### Presentation

#### HUD, Results, and Ranking UI
- **Owns**: title/menu flow, room join screen, ready prompt, countdown view, in-match HUD, end-of-match panel, leaderboard view, persistence error banners
- **Exposes**: UI intents only (`Ready`, `Rematch`, `RefreshLeaderboard`, `BackToLobby`)
- **Consumes**: authoritative room state, scoreboard snapshots, debuff status, persistence status, leaderboard query results
- **Boundary rule**: UI never computes match outcomes; it only renders provided data

#### Audio Feedback
- **Owns**: one-shot audio cues and ambience
- **Consumes**: safe presentation events from HUD and gameplay event bus
- **Boundary rule**: audio must not be a source of authority or hidden state

## Data Flow

### 1. Room Start Flow
1. Unity client captures player name and room action in UI Toolkit.
2. Network Session performs protocol-version handshake, then sends `Connect` / `JoinRoom` request to the C# server.
3. Server validates room state and emits authoritative room snapshot.
4. HUD updates to show waiting/ready state.
5. When all required players are ready, Match Lifecycle enters `Countdown` and broadcasts countdown events.

### 2. Live Match Frame Flow
1. Unity client reads keyboard/mouse via the new Input System.
2. Player Controller builds an `InputFrame` (move vector, aim vector, fire intent).
3. Network Session sends input intent to server.
4. Server simulates player movement, battery overlaps, slow-shot and trap outcomes, then updates room state.
5. Server broadcasts authoritative snapshot at 20 Hz: positions, scores, active effects, timer, battery state.
6. Client applies interpolation/correction and renders HUD/audio updates.

### 3. Scoring and Victory Flow
1. Server resolves battery pickup order for the current tick.
2. Scoreboard updates on server only.
3. If a player reaches 10 points, Match Lifecycle ends immediately with `TargetScoreReached`.
4. If timer expires first, Match Lifecycle compares final scores and resolves timeout winner or tie rule.
5. End-of-match snapshot is broadcast to all clients before persistence begins.

### 4. Persistence and Leaderboard Flow
1. Match Lifecycle creates a `MatchResultPayload` with unique match id.
2. Match Persistence Gateway writes result asynchronously to MySQL.
3. On success, server emits `PersistenceSucceeded`; on failure, emits `PersistenceFailed` and logs retry-safe diagnostics.
4. UI displays results screen first, then leaderboard screen using queried leaderboard data.
5. Leaderboard ordering uses deterministic sort: wins desc, best score desc, total matches asc, player name asc.

### 5. Disconnect Flow
1. Transport heartbeat failure (5 seconds without input/heartbeat) or socket close notifies server.
2. Match Lifecycle marks disconnected player as forfeited if match is active; reconnect to the active match is not supported in MVP.
3. Server finalizes result with `DisconnectForfeit` end reason.
4. Persistence Gateway stores single final match record.
5. Remaining clients see disconnect result and can return to lobby.

## API Boundaries

```csharp
public record InputFrame(
    int Tick,
    float MoveX,
    float MoveY,
    float AimX,
    float AimY,
    bool FirePressed);

public enum MatchState {
    Lobby,
    Countdown,
    Active,
    Ended,
    Saving,
    ResultsReady
}

public enum MatchEndReason {
    TargetScoreReached,
    TimeExpired,
    DisconnectForfeit,
    ServerAbort
}

public interface IRoomStateService {
    MatchState CurrentState { get; }
    bool TryAddPlayer(string playerName);
    void SetReady(string playerId, bool isReady);
    void Tick(float deltaTime);
}

public interface IScoringService {
    bool TryCollectBattery(string playerId, string batteryId, int serverTick);
    IReadOnlyDictionary<string, int> GetScores();
}

public interface IEffectService {
    bool TryFireSlowShot(string playerId, Vector2 aimDirection, int serverTick);
    void ResolveServerTick(int serverTick);
}

public interface IResultPersistenceService {
    Task PersistMatchResultAsync(MatchResultPayload payload, CancellationToken ct);
    Task<IReadOnlyList<LeaderboardRow>> QueryLeaderboardAsync(int limit, CancellationToken ct);
}
```

**Invariants**
- Only the server can mutate score, effect state, battery availability, or match state.
- Only `MatchPersistenceGateway` may write to or query MySQL.
- UI and Audio modules consume snapshots/events and never own competitive state.
- Duplicate client messages must be safe to ignore or deduplicate by tick + player id.

## ADR Audit
- Existing ADRs:
  - ADR-0001 Network Authority and Transport Strategy
  - ADR-0002 Match State Machine and Event Ordering
  - ADR-0003 Persistence Boundary and Leaderboard Formula
  - ADR-0004 Runtime UI Stack and Screen Flow
  - ADR-0005 Battery Spawn and Score Pacing Model
  - ADR-0006 Slow Shot and Trap Fairness Rules
- Architecture conflicts with existing ADRs: none
- Traceability status: all foundation/core/presentation requirements identified in the current baseline now have ADR coverage

## Required ADRs

All architecture-critical ADRs identified for the MVP are now written:
- ADR-0001 Network Authority and Transport Strategy
- ADR-0002 Match State Machine and Event Ordering
- ADR-0003 Persistence Boundary and Leaderboard Formula
- ADR-0004 Runtime UI Stack and Screen Flow
- ADR-0005 Battery Spawn and Score Pacing Model
- ADR-0006 Slow Shot and Trap Fairness Rules

No remaining blocking ADR gaps are known at the architecture level before MVP system GDD authoring.

## Architecture Principles
1. **Server authority over all competitive outcomes** — score, pickups, effects, and victory never originate from the client.
2. **Thin Unity client, explicit external server** — Unity handles rendering, input, and UI; the separate C# server owns match truth and MySQL access.
3. **Deterministic room state before polish** — match lifecycle clarity matters more than feature breadth.
4. **Async persistence, never blocking match conclusion** — database writes must not stall the match flow.
5. **2-player MVP, 4-player-ready seams** — MVP implementation is optimized for 2-player delivery while protocol and UI structures leave room for future scaling.

## Open Questions
- None at the architecture level. Remaining adjustments should happen through system GDD review or superseding ADRs if the design changes.

