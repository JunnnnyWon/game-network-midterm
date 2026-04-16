# ADR-0005: Battery Spawn and Score Pacing Model

## Status

Accepted

## Date

2026-04-16

## Last Verified

2026-04-16

## Decision Makers

User, Codex (architecture synthesis)

## Summary

Battery Rush Arena will use a **scarce-battery pacing model** tuned around the 2-player MVP: **3 active batteries**, **1 point per battery**, **3.0 second respawn delay**, and a **120 second timeout**. Spawn selection is deterministic and anti-camping aware, and 4-player pacing remains protocol-capable but disabled by default until later balancing.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3.10f1) |
| **Domain** | Core |
| **Knowledge Risk** | LOW — pacing rules are largely server-side and engine-agnostic |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/modules/physics.md` |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Verify server-side overlap resolution and spawn-point occupancy checks behave consistently with Unity client visuals. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001, ADR-0002 |
| **Enables** | ADR-0006, battery/scoring system GDD, playtest tuning |
| **Blocks** | Battery economy implementation, target pacing validation |
| **Ordering Note** | This ADR assumes ADR-0002’s timeout and simultaneous-event rules. |

## Context

### Problem Statement

The concept review flagged the battery economy as too vague to guarantee the intended fantasy. Without explicit spawn counts, respawn timing, contest rules, and pacing targets, the game could become either a dull route-memory race or a chaotic pickup spam match.

### Current State

The concept promises first-to-10 scoring and a short session, but it left timer length and spawn pacing unresolved. Architecture review also warned that 4-player scaling should not be treated as fully tuned MVP scope.

### Constraints

- MVP is tuned for 2-player play.
- The game must remain readable and demo-friendly.
- First-to-10 remains the core victory target.
- The timeout must prevent stalemates without regularly deciding every match.
- Spawns must avoid obvious camping behavior.

### Requirements

- Must define active battery count and respawn timing.
- Must define pickup value.
- Must define deterministic contested-pickup ordering.
- Must define timeout length.
- Must keep 2-player pacing in the intended short-session range.

## Decision

### Core pacing constants (2-player MVP)

- **Target score**: 10 points
- **Timeout**: 120 seconds
- **Point value per battery**: 1 point
- **Active batteries at once**: 3
- **Total spawn points in arena**: 8
- **Respawn delay after collection**: 3.0 seconds

### Spawn selection rules

When a battery respawns, the server picks a spawn point using this order:
1. Exclude currently occupied/active battery points.
2. Exclude the **last 2 spawn points used** if alternatives exist.
3. Exclude any spawn point within **2.0 world units** of a player if alternatives exist.
4. If multiple valid points remain, choose one using the server’s deterministic pseudo-random selection seeded from the match id.
5. If no points remain after filters, relax rule 3, then rule 2.

### Contested pickup resolution

If multiple players overlap the same battery during the same server tick:
1. Apply ADR-0002 effect resolution first.
2. Compare authoritative squared distance from each eligible player center to the battery center.
3. Closest player wins the pickup.
4. If distance is identical, lower session join index wins.

### Pacing expectations

- The design target is that a typical 2-player round reaches a decisive end in roughly **75–120 seconds**.
- Timeout should act as a fallback, not the dominant resolution path.
- If repeated local playtests show frequent timeout draws or extremely fast 10-point wins, the first tuning knobs to adjust are:
  1. respawn delay
  2. active battery count
  3. spawn exclusion radius

### 4-player stance

- **Architecture supports 4 players**, but pacing is **not considered fully tuned for 4-player mode yet**.
- If 4-player mode is enabled in a later milestone, the provisional formula is:
  - active batteries = `playerCount + 1` (capped at 5)
  - same 1-point battery value
  - same 120-second timeout
- This remains **feature-flagged stretch scope** until post-MVP balancing validates it.

### Architecture

```text
Server Tick
   -> validate active battery set
   -> resolve effects first
   -> resolve contested pickup order
   -> award 1 point
   -> queue respawn for collected battery at +3.0s
   -> evaluate 10-point win / timeout
```

### Key Interfaces

```csharp
public interface IBatterySpawnService {
    void InitializeSpawnPoints(IReadOnlyList<Vector2> spawnPoints);
    void Tick(int serverTick, float timeSeconds);
    bool TryResolvePickup(PlayerId playerId, BatteryId batteryId, int serverTick);
}

public record BatteryPacingConfig(
    int ActiveBatteryCount,
    float RespawnDelaySeconds,
    int TargetScore,
    float TimeoutSeconds,
    float AntiCampRadius);
```

### Implementation Guidelines

- Keep spawn and pickup resolution fully server-side.
- Mirror the active battery set to clients via snapshots only.
- Do not add battery rarity tiers in MVP.
- Tune the arena layout around 8 spawn points so routing choices matter without flooding the map.

## Alternatives Considered

### Alternative 1: High-density spawn model
- **Description**: keep 5-6 batteries active in 2-player mode with very short respawns.
- **Pros**: constant action, easier to always find something to collect.
- **Cons**: weakens routing tension, makes 10 points too fast, reduces the value of disruption.
- **Estimated Effort**: Similar.
- **Rejection Reason**: undermines the intended “smart, quick, disruptive” pacing.

### Alternative 2: Single-battery chase model
- **Description**: only one battery active at a time.
- **Pros**: very easy to read and synchronize.
- **Cons**: becomes too linear and predictable, weakens route-choice gameplay.
- **Estimated Effort**: Lower.
- **Rejection Reason**: too simplistic for the fantasy and too easy to camp.

### Alternative 3: Variable-value batteries
- **Description**: use 1-point and 2-point battery variants.
- **Pros**: more excitement and comeback potential.
- **Cons**: increases balance complexity, UI complexity, and ranking volatility.
- **Estimated Effort**: Higher.
- **Rejection Reason**: unnecessary for MVP clarity.

## Consequences

### Positive
- The round has a concrete pacing target.
- Spawn rules actively reduce simple camping behavior.
- Score pacing supports the 10-point race without flooding the arena.
- 4-player support is acknowledged without pretending it is fully solved.

### Negative
- 2-player tuning may need several playtest passes.
- Deterministic pseudo-random spawn selection adds a small bit of server-side bookkeeping.
- 4-player behavior remains provisional until tested.

### Neutral
- Timeout is now fixed at 120 seconds, which may later be tuned but is no longer ambiguous.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| 3 active batteries feels too sparse in early playtests | Medium | Medium | Adjust respawn delay before changing core score target |
| Anti-camp spawn radius causes awkward clumping elsewhere | Medium | Medium | Tune radius after observing actual arena layout |
| 4-player provisional settings produce chaotic pacing | High | Low for MVP | Keep 4-player disabled by default |

## Performance Implications
- **CPU**: negligible; battery spawn checks are tiny.
- **Memory**: minimal active battery state.
- **Load Time**: none.
- **Network**: small snapshot payload for 3 active batteries in MVP.

## Migration Plan

1. Implement spawn-point data and active battery tracking on the server.
2. Hook contested pickup resolution into the match tick order.
3. Mirror active batteries to the client HUD/arena snapshots.
4. Validate timeout and target-score pacing through MVP playtests.

**Rollback plan**: If pacing feels wrong, adjust `RespawnDelaySeconds` or `ActiveBatteryCount` in configuration before revisiting the broader rules.

## Validation Criteria

- [ ] Typical 2-player matches usually finish in the 75–120 second window.
- [ ] Battery spawns do not repeatedly appear on the same point when alternatives exist.
- [ ] Contested pickups resolve identically across repeated deterministic test runs.
- [ ] Timeout is a fallback, not the dominant outcome in normal local testing.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/game-concept.md` | Match Goal | **TR-concept-001** — First player to 10 points wins immediately | Keeps the 10-point target and defines pacing around it |
| `design/gdd/game-concept.md` | Timeout Rule | **TR-concept-002** — If timer expires, highest score wins | Fixes timeout to 120 seconds and ties into ADR-0002 end-state handling |
| `design/gdd/game-concept.md` | Core Loop | **TR-concept-004** — Players move in a top-down 2D arena and collect batteries | Defines active battery count, spawn points, and contest resolution |
| `design/gdd/game-concept.md` | Session Length | **TR-concept-011** — Rounds should be short, repeatable, and demo-friendly | Sets the intended 75–120 second pacing window |
| `design/gdd/game-concept.md` | Fairness | **TR-concept-013** — Competitive rules must avoid oppressive or unclear disruption | Anti-camp spawn selection and deterministic contested pickup rules reduce unfair outcomes |
| `design/gdd/systems-index.md` | Arena Battery Economy & Scoring | MVP gameplay system | Turns the battery economy into fixed, tunable server-side rules |

## Related

- `docs/architecture/adr-0001-network-authority-and-transport-strategy.md`
- `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md`
- `docs/architecture/architecture.md`
- `design/gdd/game-concept.md`
- `design/gdd/systems-index.md`
