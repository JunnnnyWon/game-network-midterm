# Results Persistence & Leaderboard

> **Status**: Draft
> **Author**: Codex
> **Last Updated**: 2026-04-16
> **Last Verified**: 2026-04-16
> **Implements Pillar**: Pillar 3 — Networking and Results Must Be Visible

## Summary

`Results Persistence & Leaderboard` stores final match outcomes in MySQL, maintains aggregate player stats, and returns leaderboard data to the client through the server-only persistence boundary. It exists so every finished match becomes a visible, persistent result instead of a temporary local memory.

> **Quick reference** — Layer: `Feature` · Priority: `MVP` · Key deps: `Match Lifecycle & Room State`, `Arena Battery Economy & Scoring`

## Overview

This system begins after a match is already decided. It receives the final result payload, writes it into the `ckgame` database, updates aggregate player records safely, and serves leaderboard data back to the UI. It must be reliable enough for class-demo persistence, but it must never block the match from ending on time.

## Player Fantasy

The player should feel that the game remembers what happened and that results matter beyond a single round. Seeing their name, wins, best score, and standings reflected after a match should make the session feel official and competitive.

## Detailed Rules

### Core Rules

1. Only the server may access MySQL.
2. The database name is **`ckgame`**.
3. Match completion is final before persistence success is known.
4. Persistence must not block the end-of-match flow.
5. Leaderboard data reaches the client only through the server.

### Match Result Storage Rules

1. Each completed match creates a unique `match_id`.
2. The server queues a `MatchResultPayload` after the room enters `Ended`.
3. Exactly one `match_results` row may exist per `match_id`.
4. A retry must not create duplicate stat inflation.
5. A result row may still exist even if aggregate player stats later fail, but the ideal path is atomic transaction protection.

### Aggregate Player Stats Rules

1. Aggregate stats live in `player_stats`.
2. For each player, tracked values include:
   - wins
   - draws
   - losses
   - best score
   - total matches
   - last played timestamp
3. Aggregate updates must happen transactionally with the match result write where possible.
4. `ServerAbort` is not a standard competitive stat outcome.

### Leaderboard Rules

1. Leaderboard ordering is deterministic:
   1. wins descending
   2. best score descending
   3. total matches ascending
   4. player name ascending
2. The leaderboard query should stay simple and explainable.
3. MVP does not require pagination or deep filtering; a top list is enough.

### Result Outcome Rules

1. Win:
   - wins +1
   - total matches +1
   - best score may update
2. Loss:
   - losses +1
   - total matches +1
   - best score may update
3. Draw:
   - draws +1
   - total matches +1
   - best score may update
4. ServerAbort:
   - should not silently count as a normal win/loss/draw without explicit later policy

### Failure Rules

1. Persistence retries up to **3 times** with backoff.
2. If persistence still fails:
   - the final match result is still shown to the player
   - persistence status becomes failed
   - leaderboard remains at last committed state
3. Failure visibility is required; silent failure is not acceptable.

## Formulas

### Leaderboard Sort Order

```text
sort_key = (-wins, -best_score, total_matches, player_name)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| wins | int | 0+ | player_stats | Primary competitive ordering metric |
| best_score | int | 0+ | player_stats | Higher score ranks above lower score on equal wins |
| total_matches | int | 0+ | player_stats | Fewer matches ranks above more on equal wins/score |
| player_name | string | non-empty | player_stats | Stable final tie-break |

**Expected output range**: deterministic total order
**Edge case**: Exact stat ties still sort predictably by player name.

### Best Score Update

```text
best_score = max(previous_best_score, final_match_score)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| previous_best_score | int | 0+ | player_stats | Player's current stored best score |
| final_match_score | int | 0-10+ | final match result | Score achieved in the just-finished match |

**Expected output range**: non-decreasing per player
**Edge case**: Draws and losses may still raise best score if the player scored higher than before.

### Retry Budget

```text
persistence_attempts <= 3
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| persistence_attempts | int | 1-3 | persistence service | Number of write attempts for a single result payload |

**Expected output range**: max 3 attempts
**Edge case**: After the final failed retry, the room still transitions to `ResultsReady` with failed persistence status.

## Edge Cases

| Scenario | Expected Behavior | Rationale |
|----------|------------------|-----------|
| DB connection fails temporarily | Retry up to 3 times, then surface failure | Keep UX moving while preserving visibility |
| Same `match_id` is retried | No duplicate stat inflation | Idempotency is mandatory |
| Match ended as `Draw` | Store result and increment draws for both players | Draw is a real competitive outcome |
| Match ended as `ServerAbort` | Do not silently award a competitive win/loss | Keep integrity of leaderboard stats |
| Persistence succeeds but leaderboard query is delayed | Results still show save success; leaderboard may refresh slightly later | Separate storage from presentation timing |
| Two players have identical visible stats | Sort by player name ascending | Final deterministic order |

## Dependencies

| System | Direction | Nature of Dependency |
|--------|-----------|---------------------|
| Match Lifecycle & Room State | This depends on final room outcome | Receives end reason and final score lock |
| Arena Battery Economy & Scoring | This depends on scoring outputs | Uses final authoritative score totals |
| HUD, Results, and Ranking UI | Other system depends on this | Needs persistence status and leaderboard rows |
| Network Session & Transport | Other system depends on this | Transports persistence/leaderboard results to the client |
| `docs/architecture/adr-0003-persistence-boundary-and-leaderboard-formula.md` | Design dependency | Governs DB boundary, schema, retries, and sorting |

## Tuning Knobs

| Parameter | Current Value | Safe Range | Effect of Increase | Effect of Decrease |
|-----------|--------------|------------|-------------------|-------------------|
| Retry count | 3 | 1-5 | More resilience, slower failure visibility | Faster failure reporting |
| Leaderboard row count shown | Top 10 | 5-20 | More visibility | Simpler UI |
| Persistence timeout/backoff aggressiveness | Moderate | conservative-aggressive | More patience with unstable DB | Faster fail-fast behavior |

## Acceptance Criteria

- [ ] Only the server talks to MySQL.
- [ ] Each completed match produces at most one stored result row per `match_id`.
- [ ] A retry cannot double-count wins, losses, draws, or total matches.
- [ ] Leaderboard ordering remains deterministic using the agreed formula.
- [ ] Persistence failure is visible to the player without changing the already-decided match result.
- [ ] The system remains consistent with ADR-0003 and the authoritative room-state flow.
