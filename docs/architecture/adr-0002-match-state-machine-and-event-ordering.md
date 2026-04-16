# ADR-0002: Match State Machine and Event Ordering

## Status

Accepted

## Date

2026-04-16

## Last Verified

2026-04-16

## Decision Makers

User, Codex (architecture synthesis)

## Summary

Battery Rush Arena will use a **server-owned deterministic room/match state machine** with a fixed event ordering per server tick. The authoritative server will move every room through Lobby → Countdown → Active → Ended → Saving → ResultsReady, and all timeouts, ties, disconnects, and simultaneous score events will be resolved by explicit ordering rules rather than client inference.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3.10f1) |
| **Domain** | Core |
| **Knowledge Risk** | MEDIUM — Unity presentation is post-cutoff, but core state machine logic is mostly engine-agnostic |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/modules/networking.md`, `docs/engine-reference/unity/modules/ui.md`, `docs/engine-reference/unity/modules/input.md` |
| **Post-Cutoff APIs Used** | Unity Input System for ready/rematch intents; UI Toolkit for rendering room and results states |
| **Verification Required** | Verify room-state transitions stay in sync across clients, countdown/result transitions survive disconnects, and simultaneous scoring events resolve identically on all clients. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001 |
| **Enables** | ADR-0003, ADR-0004, gameplay system GDDs |
| **Blocks** | Match lifecycle implementation, room flow implementation, gameplay rules implementation |
| **Ordering Note** | Event ordering must be fixed before scoring, persistence, and UI documents can safely reference room states. |

## Context

### Problem Statement

The project needs one match lifecycle contract that every subsystem can trust. Without this ADR, the server, UI, persistence layer, and gameplay rules would all make different assumptions about when a room becomes active, when a match truly ends, and how ties or disconnects are resolved.

### Current State

ADR-0001 fixed the transport and authority boundary, but it intentionally deferred the exact room-state contract and end-of-match ordering. The concept review flagged missing tie rules, disconnect behavior, and unclear screen/state flow as blockers.

### Constraints

- The MVP is a **2-player authoritative online match**.
- 4-player support may exist later, but the room-state model must still be deterministic.
- The server must emit explicit room-state updates that the Unity client only renders.
- Match completion cannot wait on database success.
- Presentation and ranking flows must remain understandable in a short class demo.

### Requirements

- Must define a complete room lifecycle.
- Must define event ordering inside a server tick.
- Must define timeout, tie, disconnect, and simultaneous target-score behavior.
- Must allow a clean handoff from gameplay end → persistence → results UI.
- Must avoid client-side authority over match state.

## Decision

Battery Rush Arena will use a **single authoritative room-state machine** and a **single deterministic per-tick event ordering**.

### Authoritative Room States

```text
Menu (client-only) -> Connecting (client-only) -> Lobby -> Countdown -> Active -> Ended -> Saving -> ResultsReady -> Lobby
```

#### Server-owned shared states
- **Lobby**: players connected, room open, waiting for all required players to be present and ready.
- **Countdown**: all required players are ready; 3-second countdown running.
- **Active**: match timer and gameplay systems running.
- **Ended**: gameplay frozen; winner and end reason locked.
- **Saving**: result payload queued for persistence.
- **ResultsReady**: persistence outcome known and safe for results/leaderboard UI flow.

#### Client-only presentation states
- **Menu / Connecting** are local UI wrappers around the server-owned room states.
- The client may display transition animations, but it never creates or advances shared room state on its own.

### Match start and rematch rules

- A 2-player MVP room starts when **2 connected players are both marked Ready**.
- Countdown duration is **3 seconds**.
- If any ready player disconnects during Lobby or Countdown, the room returns to Lobby.
- A rematch requires **all currently connected players** to vote rematch within **15 seconds** after ResultsReady.
- If rematch consensus fails, the room returns to Lobby.

### Event ordering per server tick

The authoritative server processes each tick in this exact order:

1. **Transport intake**
   - accept join/leave/ready/rematch intents
   - reject stale/duplicate input frames
2. **Liveness checks**
   - heartbeat timeout / disconnect detection
3. **State-transition checks**
   - Lobby ↔ Countdown ↔ Active transitions
4. **Input application**
   - movement and aim intents applied to simulation state
5. **Ability and hazard resolution**
   - slow-shot hits, trap triggers, effect application/removal
6. **Pickup resolution**
   - battery collection claims resolved
7. **Score and victory evaluation**
   - score totals updated
   - target-score and timeout rules evaluated
8. **Snapshot/event emission**
   - room snapshot, match event, and end-state messages emitted
9. **Persistence handoff**
   - if the match ended this tick, emit payload to ADR-0003 persistence gateway

### End-of-match rules

- **Target-score win**: if one player alone reaches or exceeds 10 points during step 7, that player wins immediately and the room enters Ended.
- **Simultaneous target-score event**: if multiple players reach or exceed 10 on the same server tick, the player with the **higher final score after full tick resolution** wins. If still tied, the result is a **Draw**.
- **Timeout**: if the timer reaches zero before a target-score win locks, the player with the higher score wins.
- **Timeout tie**: if scores are equal at timer expiry, the result is a **Draw**.
- **Disconnect during Active**: in the MVP 2-player match, a disconnect causes **DisconnectForfeit** for the disconnected player and immediate win for the remaining connected player.
- **ServerAbort**: unrecoverable server room failure ends the match as `ServerAbort` and does not alter ranked win/loss totals until ADR-0003 persistence rules confirm safe behavior.

### Contested and simultaneous event rules

- Battery pickups are resolved only in the pickup phase.
- Ability/trap effects are applied before pickup resolution in the same tick.
- A player slowed in the current tick still uses the authoritative post-effect movement state for pickup eligibility.
- When two pickup claims target the same battery on the same tick, the scoring ADR's deterministic tie-break applies; if unresolved there, lower session join index wins the claim.

### Architecture

```text
Transport -> Room State Machine -> Simulation Phases -> End Reason Lock -> Persistence Queue -> Results UI
                    ^                    |
                    |                    v
             Ready / Rematch       Snapshot Broadcast
```

### Key Interfaces

```csharp
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
    Draw,
    DisconnectForfeit,
    ServerAbort
}

public interface IMatchStateService {
    MatchState CurrentState { get; }
    MatchEndReason? EndReason { get; }
    void Tick(float deltaTime);
    void SetReady(PlayerId playerId, bool isReady);
    void RegisterDisconnect(PlayerId playerId);
    bool TryQueueRematch(PlayerId playerId);
}

public interface IMatchEventOrdering {
    void ProcessTransportIntake(int serverTick);
    void ProcessLivenessChecks(int serverTick);
    void ProcessStateTransitions(int serverTick);
    void ProcessInput(int serverTick);
    void ProcessEffects(int serverTick);
    void ProcessPickups(int serverTick);
    void ProcessScoringAndVictory(int serverTick);
    void EmitSnapshotAndEvents(int serverTick);
}
```

### Implementation Guidelines

- Keep the room-state enum server-side and treat client copies as read-only.
- Freeze gameplay logic the moment the room enters Ended; no late score changes after that state.
- Emit the `MatchEndReason` explicitly with the final snapshot so the UI never infers why the match ended.
- In MVP, do not support rejoin-to-match complexity.
- Draw is a real end state and must be displayed explicitly in UI and persistence.

## Alternatives Considered

### Alternative 1: Client-driven room flow with server validation only
- **Description**: let clients locally move through countdown/result states and only validate major transitions with the server.
- **Pros**: less server bookkeeping; simpler local UX code.
- **Cons**: divergent state risk, race conditions on disconnect, ambiguous end reasons.
- **Estimated Effort**: Lower initial effort, much higher bug/debug effort.
- **Rejection Reason**: violates the thin-client authority model from ADR-0001.

### Alternative 2: Ad-hoc events without explicit state machine
- **Description**: drive match logic through a set of loosely ordered events and booleans.
- **Pros**: quick to prototype.
- **Cons**: hard to audit, easy to break with edge cases, impossible to explain cleanly in docs and QA.
- **Estimated Effort**: Low initial effort, high maintenance cost.
- **Rejection Reason**: the design-review feedback specifically demanded deterministic state and event ordering.

### Alternative 3: Allow reconnect into active matches
- **Description**: let disconnected players restore state and continue in the same match.
- **Pros**: player-friendly in unstable networks.
- **Cons**: more complexity in authoritative state restoration, timer rollback, and exploit prevention.
- **Estimated Effort**: High.
- **Rejection Reason**: beyond MVP scope for a class project.

## Consequences

### Positive
- Every subsystem gets the same authoritative lifecycle.
- Timeout/tie/disconnect behavior is explicit and testable.
- Results UI and persistence layers can rely on stable end reasons.
- QA can write deterministic test cases for simultaneous events.

### Negative
- Draw handling adds one more results path to UI and DB logic.
- Strict state machine work adds up-front design effort.
- Reconnect convenience is intentionally sacrificed in MVP.

### Neutral
- 4-player support remains possible, but the disconnect/rematch rules are currently tuned around 2-player MVP.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| Hidden edge case still bypasses state machine | Medium | High | Keep all transitions in one room-state service and test every end reason |
| Designers later want reconnect support | Medium | Medium | Add a superseding ADR instead of widening MVP behavior ad hoc |
| Draw frequency is higher than expected | Low | Medium | Revisit pacing in ADR-0005 after prototype playtests |

## Performance Implications
- **CPU**: negligible additional cost beyond explicit per-tick phase ordering.
- **Memory**: minimal; state machine is compact.
- **Load Time**: none.
- **Network**: explicit end-state payloads add tiny overhead but improve clarity.

## Migration Plan

No code migration required yet.

1. Implement room-state enum and transition service on the server.
2. Integrate transport intents from ADR-0001.
3. Wire scoring/effect phases to the ordered tick pipeline.
4. Emit explicit end-reason snapshots for UI and persistence.

**Rollback plan**: If the state machine proves too strict during prototyping, revise via a superseding ADR rather than adding hidden bypasses.

## Validation Criteria

- [ ] Two-player room cannot enter Active until both players are connected and ready.
- [ ] Countdown cancels correctly if a player leaves before match start.
- [ ] Simultaneous target-score and timeout events resolve identically across repeated runs.
- [ ] Disconnect during Active always yields the same DisconnectForfeit result.
- [ ] Results UI receives explicit MatchEndReason values instead of deriving them locally.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/game-concept.md` | Match Goal | **TR-concept-001** — First player to 10 points wins immediately | Defines the authoritative target-score win state and simultaneous resolution policy |
| `design/gdd/game-concept.md` | Timeout Rule | **TR-concept-002** — If timer expires, highest score wins | Defines timeout winner and timeout draw rules |
| `design/gdd/game-concept.md` | Authority | **TR-concept-007** — Server authoritatively resolves score, effects, and victory | Makes room state and end reasons server-owned |
| `design/gdd/game-concept.md` | Session Length | **TR-concept-011** — Rounds should be short, repeatable, and demo-friendly | Uses a fast lobby/countdown/rematch loop and immediate end-state lock |
| `design/gdd/game-concept.md` | Fairness | **TR-concept-013** — Competitive rules must avoid oppressive or unclear disruption | Uses deterministic event ordering and explicit tie/disconnect rules |
| `design/gdd/systems-index.md` | Match Lifecycle & Room State | Core lifecycle system | Defines the room-state and event-order contract the rest of the architecture depends on |

## Related

- `docs/architecture/adr-0001-network-authority-and-transport-strategy.md`
- `docs/architecture/architecture.md`
- `design/gdd/game-concept.md`
- `design/gdd/systems-index.md`
