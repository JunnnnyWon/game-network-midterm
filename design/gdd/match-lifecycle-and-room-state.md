# Match Lifecycle & Room State

> **Status**: Draft
> **Author**: Codex
> **Last Updated**: 2026-04-16
> **Last Verified**: 2026-04-16
> **Implements Pillar**: Pillar 1 — Instantly Readable Competition

## Summary

`Match Lifecycle & Room State` defines the authoritative flow of a Battery Rush Arena session from lobby entry through countdown, live play, match end, result saving, leaderboard visibility, and return to lobby. It exists so every player sees one clear shared match truth, especially when ties, disconnects, rematches, and persistence delays happen.

> **Quick reference** — Layer: `Core` · Priority: `MVP` · Key deps: `Network Session & Transport`, `Results Persistence & Leaderboard`, `HUD, Results, and Ranking UI`

## Overview

This system is the server-owned state machine that determines where a room currently is, what players are allowed to do, when the match begins, when it ends, and what happens after the final score is locked. It translates transport-level connectivity into visible multiplayer flow: waiting in lobby, becoming ready, counting down, playing, resolving win/loss/draw/disconnect outcomes, saving results, showing results, and optionally rematching.

## Player Fantasy

The player should feel that the match is a fair, controlled competitive event with no ambiguity about what phase it is in. Countdown start should feel decisive, the match end should feel immediate and final, and post-match flow should feel official rather than improvised. The emotional goal is: “The arena clearly knows when the competition starts, when it ends, and why I won, lost, or drew.”

## Detailed Rules

### Core Rules

1. **Authority ownership**
   - Only the server may change shared room state.
   - Clients may send room-related intents (`Ready`, `Rematch`, `BackToLobby` equivalents routed through UI/transport), but clients never advance room state on their own.
   - Shared room state is visible to clients through authoritative snapshots/events only.

2. **Authoritative shared states**
   - `Lobby`
   - `Countdown`
   - `Active`
   - `Ended`
   - `Saving`
   - `ResultsReady`

3. **Lobby rules**
   - A room enters `Lobby` after successful room creation/join and before a match has started.
   - Lobby requires exactly **2 connected players** in MVP before it can advance.
   - Each player may independently toggle ready state.
   - The room advances from `Lobby` to `Countdown` only when both connected players are present and marked ready.

4. **Countdown rules**
   - Countdown duration is **3 seconds**.
   - Countdown is authoritative and shared; both clients see the same countdown phase.
   - If either player disconnects or loses ready eligibility during countdown, the room returns to `Lobby`.
   - Gameplay inputs may be suppressed or ignored until `Active` begins.

5. **Active match rules**
   - `Active` begins immediately after countdown finishes.
   - During `Active`, gameplay systems may resolve movement, abilities, pickups, debuffs, score changes, and timer decay.
   - The room remains `Active` until one of the explicit end conditions locks the match:
     - target score reached
     - timer expiry
     - disconnect forfeit
     - server abort

6. **End lock rules**
   - The first authoritative tick that resolves a valid end condition moves the room to `Ended`.
   - Once `Ended` is entered, no later gameplay score/effect updates may change the outcome.
   - `Ended` must always include a final `MatchEndReason` and final score state.

7. **Saving rules**
   - After `Ended`, the room enters `Saving` while final match data is handed to persistence.
   - Match completion must not wait for DB success to be considered final.
   - `Saving` exists so the player can see that results are being finalized, even if persistence is delayed.

8. **Results-ready rules**
   - The room enters `ResultsReady` when persistence outcome is known or sufficiently surfaced for the result flow.
   - `ResultsReady` exposes final scores, end reason, and persistence status to the results/leaderboard UI flow.
   - A room may remain reusable after `ResultsReady`; it does not need to destroy/recreate transport state for rematch.

9. **Rematch rules**
   - A rematch requires **all currently connected players** to vote rematch within **15 seconds** after `ResultsReady`.
   - If both players vote rematch in time, the room returns to `Countdown` using the same room context.
   - If rematch consensus fails, the room returns to `Lobby`.

10. **Disconnect rules**
    - If a player disconnects during `Lobby`, the room remains valid but cannot start until two eligible players are present again.
    - If a player disconnects during `Countdown`, countdown is canceled and the room returns to `Lobby`.
    - If a player disconnects during `Active`, the disconnected player loses by `DisconnectForfeit` and the remaining player wins immediately.
    - Rejoin to an active match is unsupported in MVP.

11. **Draw rules**
    - If multiple players reach the target score on the same server tick and remain tied after full tick resolution, the result is `Draw`.
    - If the timer expires and final scores are equal, the result is `Draw`.
    - Draw is a real end state and must be visible in UI, persistence payloads, and leaderboard/result handling.

12. **Server-abort rules**
    - If an unrecoverable room/server error prevents fair continuation, the room may end as `ServerAbort`.
    - `ServerAbort` is not a normal competitive outcome and should not silently masquerade as a win/loss.

13. **Event ordering dependency**
    - This system assumes ADR-0002’s tick order remains fixed:
      1. transport intake
      2. liveness checks
      3. state-transition checks
      4. input application
      5. effect resolution
      6. pickup resolution
      7. scoring/victory evaluation
      8. snapshot/event emission
      9. persistence handoff
    - Match lifecycle must never bypass this ordering with ad hoc transition shortcuts.

### States and Transitions

| State | Entry Condition | Exit Condition | Behavior |
|-------|-----------------|----------------|----------|
| Lobby | Room created/joined successfully; no live match currently running | Both players ready → `Countdown` | Show roster, room code, ready state; allow room-related intents only |
| Countdown | Both connected players are ready in Lobby | Countdown completes → `Active`; readiness loss/disconnect → `Lobby` | Freeze competitive play; show 3-2-1 shared countdown |
| Active | Countdown finishes successfully | End condition resolved → `Ended` | Gameplay and timer run; match outcome still undecided |
| Ended | Target score, timeout, disconnect forfeit, draw, or server abort locks outcome | Persistence handoff begins → `Saving` | Freeze gameplay; final scores and end reason become immutable |
| Saving | `Ended` hands final payload to persistence layer | Persistence outcome becomes visible → `ResultsReady` | Show saving state; competitive outcome already final |
| ResultsReady | Persistence status/final result flow is ready for presentation | Rematch consensus → `Countdown`; otherwise return → `Lobby` | Show results, leaderboard, rematch window, and persistence status |

### Interactions with Other Systems

| System | Direction | Nature of Interaction |
|--------|-----------|-----------------------|
| Network Session & Transport | This system depends on it | Receives join/leave/ready/rematch intents and connection liveness information |
| Arena Battery Economy & Scoring | This system consumes it | Reads target-score and final-score conditions to determine win/lose/draw transitions |
| Results Persistence & Leaderboard | This system feeds it | Hands final match result payload into persistence after end lock |
| HUD, Results, and Ranking UI | UI depends on this system | Exposes current room phase, countdown, end reason, and rematch window |
| Player Controller & Input | Input depends on this system | Determines when gameplay input is active vs suppressed |
| Audio Feedback | Audio depends on this system | Countdown, match start, match end, and persistence-failure cues follow room-state transitions |

## Formulas

### Countdown Completion

```text
countdown_complete = (countdown_remaining_seconds <= 0)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| countdown_remaining_seconds | float seconds | 0.0-3.0 | authoritative room timer | Shared countdown time remaining before gameplay begins |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: If a player becomes ineligible before the timer reaches zero, the room returns to `Lobby` instead of entering `Active`.

### Active Match Timeout

```text
timeout_reached = (match_time_remaining_seconds <= 0)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| match_time_remaining_seconds | float seconds | 0.0-120.0 in MVP | authoritative room timer | Remaining competitive match time |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: If target score is also reached on the same authoritative tick, ADR-0002 simultaneous-resolution rules decide the final outcome.

### Rematch Consensus

```text
rematch_granted = (connected_players_with_vote == connected_players_total) AND (time_since_results_ready <= 15.0 seconds)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| connected_players_with_vote | int | 0-2 in MVP | room rematch tracker | Number of connected players who voted rematch |
| connected_players_total | int | 0-2 in MVP | room roster | Number of currently connected players |
| time_since_results_ready | float seconds | 0+ | room post-match timer | Time elapsed since the room entered `ResultsReady` |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: If one player disconnects after results, consensus is recalculated against currently connected players only.

### Ready-to-Start Rule

```text
can_start_countdown = (connected_players_total == 2) AND (ready_players_total == 2)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| connected_players_total | int | 0-2 in MVP | room roster | Number of connected players in the room |
| ready_players_total | int | 0-2 in MVP | ready-state tracker | Number of players who have toggled ready |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: Ready flags are meaningful only for currently connected players; a disconnected player’s ready state cannot keep the room eligible.

## Edge Cases

| Scenario | Expected Behavior | Rationale |
|----------|------------------|-----------|
| One player is connected but never receives an opponent | Room stays in `Lobby` indefinitely or until the player leaves | Match cannot start without two players |
| A player un-reads during countdown | Countdown is canceled and room returns to `Lobby` | Start conditions must remain authoritative and explicit |
| Both players reach 10 on the same tick | Resolve by ADR-0002 simultaneous scoring rules; may end as `Draw` | Prevent hidden tie-breaking drift |
| Timer expires with equal scores | End as `Draw` | The result must remain explicit and fair |
| Persistence is slow but not failed | Room enters `Saving` and later `ResultsReady` without changing competitive outcome | DB latency must not alter who won |
| Persistence fails after retries | Match still reaches `ResultsReady`, but persistence status is surfaced as failed | Match result should remain visible even when DB write fails |
| One player disconnects during countdown | Room returns to `Lobby` | Countdown is only valid when both players remain eligible |
| One player disconnects during active play | End immediately with `DisconnectForfeit` | Avoid ambiguous half-live matches |
| Server room crashes after match is already locked | Preserve final end reason if already in `Ended`; otherwise use `ServerAbort` | Prevent contradictory outcomes |
| Players do nothing on results screen | After rematch window ends, return to `Lobby` | Keeps flow moving without hidden deadlock |

## Dependencies

| System | Direction | Nature of Dependency |
|--------|-----------|---------------------|
| Network Session & Transport | This depends on transport | Needs room membership, ready/rematch intents, and liveness signals |
| Arena Battery Economy & Scoring | This depends on gameplay scoring | Needs final score and target-score conditions |
| Results Persistence & Leaderboard | Other system depends on this | Receives final match payload after end lock |
| HUD, Results, and Ranking UI | Other system depends on this | Needs authoritative room phase, countdown, end reason, and rematch window |
| Player Controller & Input | Other system depends on this | Gameplay input availability follows room phase |
| `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md` | Design dependency | Governs the room-state machine, end reasons, and tick order |
| `docs/architecture/adr-0001-network-authority-and-transport-strategy.md` | Design dependency | Governs transport/liveness/reconnect policy |
| `docs/architecture/adr-0003-persistence-boundary-and-leaderboard-formula.md` | Design dependency | Governs saving/results-ready persistence handoff behavior |
| `docs/architecture/adr-0004-runtime-ui-stack-and-screen-flow.md` | Design dependency | Governs how room phases are surfaced in UI |

## Tuning Knobs

| Parameter | Current Value | Safe Range | Effect of Increase | Effect of Decrease |
|-----------|--------------|------------|-------------------|-------------------|
| Countdown duration | 3.0 s | 2.0-5.0 s | More anticipation, slower restarts | Faster restarts, less drama/readiness clarity |
| Match timeout | 120.0 s | 90.0-150.0 s | More comeback time, longer rounds | Faster resolution, more timeout pressure |
| Rematch vote window | 15.0 s | 8.0-20.0 s | More time to decide rematch | Faster return to lobby |
| Results-ready auto-return delay (if adopted later) | Manual/none in MVP baseline | 0-10.0 s | More automated flow | More player control |
| Disconnect stale threshold | 5.0 s | 3.0-8.0 s | More tolerant of hiccups | Faster forfeit resolution |

## Acceptance Criteria

- [ ] A room cannot enter `Countdown` until two connected players are both marked ready.
- [ ] If a player disconnects during `Countdown`, the room returns to `Lobby` instead of starting the match.
- [ ] A room entering `Active` does so only after the full shared countdown completes.
- [ ] Once `Ended` is reached, the final score and `MatchEndReason` no longer change.
- [ ] Timeout ties and simultaneous target-score ties can resolve as `Draw` and remain visible through UI/persistence flow.
- [ ] A disconnect during `Active` produces `DisconnectForfeit` deterministically.
- [ ] Persistence handoff occurs after end lock and does not change the competitive result.
- [ ] `ResultsReady` supports both rematch consensus and return-to-lobby flow.
- [ ] All lifecycle rules remain consistent with ADR-0002, ADR-0001, ADR-0003, and ADR-0004.
