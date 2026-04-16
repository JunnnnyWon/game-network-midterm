# ADR-0003: Persistence Boundary and Leaderboard Formula

## Status

Accepted

## Date

2026-04-16

## Last Verified

2026-04-16

## Decision Makers

User, Codex (architecture synthesis)

## Summary

Battery Rush Arena will persist match data only through a **server-side MySQL gateway** inside the `ckgame` database. Match results will be written asynchronously with idempotent `match_id` protection, and the leaderboard will be ordered deterministically by **wins desc → best_score desc → total_matches asc → player_name asc**.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3.10f1) |
| **Domain** | Core |
| **Knowledge Risk** | LOW — persistence is server-side and only lightly coupled to Unity presentation |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/modules/ui.md` |
| **Post-Cutoff APIs Used** | UI Toolkit runtime UI is used only to display persistence status and leaderboard data on the client |
| **Verification Required** | Verify persistence status banners and leaderboard refresh behavior in Unity 6.3 results screens after async server writes. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001, ADR-0002 |
| **Enables** | ADR-0004 Runtime UI Stack and Screen Flow, results/leaderboard implementation, persistence GDD |
| **Blocks** | Match result storage, leaderboard implementation, submission artifacts showing DB integration |
| **Ordering Note** | Persistence writes rely on ADR-0002 end reasons and ADR-0001 server-only DB access. |

## Context

### Problem Statement

The assignment requires MySQL integration, but the project also needs safe authority boundaries and predictable leaderboard behavior. Without this ADR, developers could choose unsafe client-side DB access, blocking writes during match end, or ambiguous ranking formulas that are hard to explain and easy to break.

### Current State

ADR-0001 already bans direct Unity-to-MySQL access. The concept promises visible rankings and stored match results, but it does not define what tables exist, when writes happen, or how ties/draws are reflected in standings.

### Constraints

- Database name must be **`ckgame`**.
- The Unity client must never store DB credentials or open direct MySQL connections.
- Match completion must not block on DB success.
- Leaderboard ranking must be easy to explain in class and stable across repeated queries.
- MVP scope should use a minimal schema, not a live-service data model.

### Requirements

- Must persist one result record per completed match.
- Must maintain aggregate player stats used by the leaderboard.
- Must handle retries without duplicate leaderboard inflation.
- Must surface persistence success/failure to the UI.
- Must define how wins, draws, losses, and best score affect the ranking order.

## Decision

Battery Rush Arena will persist through a **server-only asynchronous persistence gateway** into the MySQL database **`ckgame`**.

### Database boundary

- The server is the only process that owns DB credentials.
- Unity clients request leaderboard data from the server only.
- Match results are queued asynchronously after ADR-0002 finalizes the end reason.
- The match UI transitions to `Saving`/`ResultsReady` independent of whether the DB write finishes immediately.

### Schema contract (MVP)

#### Table 1 — `match_results`
Stores one row per completed match.

Recommended columns:
- `match_id` (VARCHAR / UUID, PRIMARY KEY)
- `room_id`
- `ended_at`
- `end_reason`
- `winner_player_name` (nullable for draw/server abort)
- `player_count`
- `player_a_name`
- `player_a_score`
- `player_b_name`
- `player_b_score`
- `raw_payload_json` (optional audit/debug payload)

#### Table 2 — `player_stats`
Stores per-player aggregate ranking data.

Recommended columns:
- `player_name` (PRIMARY KEY)
- `wins`
- `draws`
- `losses`
- `best_score`
- `total_matches`
- `last_played_at`

### Write rules

1. When ADR-0002 transitions a match to `Ended`, the server creates a unique `match_id`.
2. The match result is queued to the persistence gateway.
3. The gateway inserts exactly one `match_results` row for that `match_id`.
4. If the insert succeeds, the gateway updates both players’ `player_stats` rows using upsert behavior.
5. If a retry occurs, `match_id` uniqueness prevents double-counting.
6. If the write fails after retries, the UI receives `PersistenceFailed`, but the match result remains final in memory.

### Leaderboard formula

Leaderboard ordering is deterministic and fixed as:
1. `wins` descending
2. `best_score` descending
3. `total_matches` ascending
4. `player_name` ascending

#### Result effects
- **Win**: `wins +1`, `total_matches +1`, `best_score = max(best_score, final_score)`
- **Loss**: `losses +1`, `total_matches +1`, `best_score = max(best_score, final_score)`
- **Draw**: `draws +1`, `total_matches +1`, `best_score = max(best_score, final_score)`
- **ServerAbort**: does **not** change wins/draws/losses; may optionally record match row for diagnostics only

### Failure policy

- Persistence attempts up to **3 retries** with backoff.
- Leaderboard queries return the last committed database state only.
- If the current match failed to persist, the results UI must say so explicitly.
- A failed write must never produce partial player-stat updates.

### Architecture

```text
ADR-0002 Ended state
        |
        v
Create MatchResultPayload (with unique match_id)
        |
        v
Async Persistence Gateway
   |                    | success          \ failure after retries
   v                   v
update player_stats    emit PersistenceFailed
insert match_results   keep in-memory result final
   |
   v
emit PersistenceSucceeded + allow leaderboard refresh
```

### Key Interfaces

```csharp
public record MatchResultPayload(
    string MatchId,
    string RoomId,
    MatchEndReason EndReason,
    DateTime EndedAtUtc,
    IReadOnlyList<PlayerResultDto> Players);

public record LeaderboardRow(
    string PlayerName,
    int Wins,
    int Draws,
    int Losses,
    int BestScore,
    int TotalMatches);

public interface IResultPersistenceService {
    Task QueueResultAsync(MatchResultPayload payload, CancellationToken ct);
    Task<PersistenceOutcome> TryPersistNextAsync(CancellationToken ct);
    Task<IReadOnlyList<LeaderboardRow>> QueryTopAsync(int limit, CancellationToken ct);
}
```

### Implementation Guidelines

- Use parameterized SQL only.
- Keep `match_id` globally unique and server-generated.
- Wrap aggregate stat updates in a transaction so `player_stats` changes remain atomic with the match insert.
- Never let persistence delay the end-of-match snapshot.
- Keep the leaderboard query simple and explainable for 발표 screenshots.

## Alternatives Considered

### Alternative 1: Unity client connects directly to MySQL
- **Description**: expose DB credentials to the Unity client and let it write/query directly.
- **Pros**: fewer server responsibilities; quick to prototype badly.
- **Cons**: insecure, violates authority model, easy to tamper with, poor architecture.
- **Estimated Effort**: Low initial effort, unacceptable risk.
- **Rejection Reason**: explicitly banned by ADR-0001.

### Alternative 2: Synchronous DB write before showing results
- **Description**: block the match until MySQL insert/update completes.
- **Pros**: simple reasoning about saved state.
- **Cons**: bad UX, stalls results on DB delay, couples gameplay end to infrastructure latency.
- **Estimated Effort**: Low.
- **Rejection Reason**: violates architecture principle that persistence must never block match conclusion.

### Alternative 3: Score-first leaderboard
- **Description**: rank primarily by raw best score.
- **Pros**: flashy numbers, easy to understand superficially.
- **Cons**: rewards outlier farming and unstable competition; does not reflect consistent wins.
- **Estimated Effort**: Similar.
- **Rejection Reason**: the game is a competitive match, so ranking should reflect wins first.

## Consequences

### Positive
- Safe server-only DB boundary.
- Exactly-once-ish result recording through `match_id` idempotency.
- Easy-to-explain leaderboard formula.
- Clear UI feedback path for persistence success/failure.

### Negative
- Requires server-side DB code and transaction handling.
- A failed DB connection can still leave the leaderboard stale after a match.
- MVP schema is intentionally simple and may need migration later.

### Neutral
- Draws remain visible in player history but do not dominate the leaderboard formula.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| Duplicate write attempts inflate stats | Medium | High | Unique `match_id` + transaction + retry-safe upsert |
| DB outage during demo | Medium | High | Keep results final in memory and show persistence failure banner |
| Leaderboard formula feels unintuitive to classmates | Low | Medium | Document wins-first ordering in PPT and results UI |

## Performance Implications
- **CPU**: negligible on client; low to moderate on server during writes.
- **Memory**: small queue of pending match results.
- **Load Time**: none during gameplay; only affects post-match leaderboard query latency.
- **Network**: minimal additional bandwidth for result/persistence status messages.

## Migration Plan

1. Create the `ckgame` schema and the `match_results` / `player_stats` tables.
2. Implement the server-side persistence queue and transaction logic.
3. Emit `PersistenceSucceeded` / `PersistenceFailed` statuses to the UI.
4. Wire leaderboard refresh into ADR-0004 UI flow.

**Rollback plan**: If aggregate `player_stats` updates prove fragile, keep writing `match_results` only and generate leaderboard data from a server-side aggregation query until a stronger schema replaces it.

## Validation Criteria

- [ ] Each completed match creates at most one `match_results` row.
- [ ] A retry after partial failure does not double-increment wins or total matches.
- [ ] Leaderboard ordering is deterministic for equal and non-equal player stats.
- [ ] Persistence failure is visible to the client without blocking results display.
- [ ] Unity never receives or stores raw MySQL credentials.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/game-concept.md` | Persistence | **TR-concept-008** — Match results are stored in MySQL | Defines server-only MySQL writes into `ckgame` through an async gateway |
| `design/gdd/game-concept.md` | Ranking | **TR-concept-009** — Players can query and view leaderboard/ranking data | Defines the leaderboard schema and deterministic sort formula |
| `design/gdd/game-concept.md` | Results Visibility | **TR-concept-014** — Network and database outcomes must be visible to players | Emits persistence status and leaderboard data through the server-to-client flow |
| `design/gdd/systems-index.md` | Results Persistence & Leaderboard | MVP persistence system | Defines schema, write timing, idempotency, and ranking contract for the persistence layer |

## Related

- `docs/architecture/adr-0001-network-authority-and-transport-strategy.md`
- `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md`
- `docs/architecture/architecture.md`
- `design/gdd/game-concept.md`
- `design/gdd/systems-index.md`
