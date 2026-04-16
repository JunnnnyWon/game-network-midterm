# Arena Battery Economy & Scoring

> **Status**: Reviewed
> **Author**: Codex
> **Last Updated**: 2026-04-16
> **Last Verified**: 2026-04-16
> **Implements Pillar**: Pillar 2 — Short Matches, Real Tension

## Summary

`Arena Battery Economy & Scoring` governs how batteries appear, how players earn points, how contested pickups resolve, and how match pacing stays within the intended short-session window. It exists so the score race feels competitive and readable rather than random, empty, or spammy.

> **Quick reference** — Layer: `Feature` · Priority: `MVP` · Key deps: `Match Lifecycle & Room State`, `Player Controller & Input`

## Overview

This system controls the heart of the competitive loop: batteries spawn into the arena, players race to collect them, scores rise toward the target of 10, and the pacing of that race stays consistent enough to create tension without becoming chaotic. The server owns all battery availability, pickup resolution, score updates, and timeout/target-score evaluation inputs used by the room-state system.

## Player Fantasy

The player should feel clever and opportunistic: reading routes, cutting off opponents, and making fast movement decisions around scarce high-value opportunities. Every pickup should feel meaningful because it moves the score race forward and changes the pressure in the arena.

## Detailed Rules

### Core Rules

1. MVP target score is **10 points**.
2. Each collected battery is worth **1 point**.
3. The arena maintains **3 active batteries at once** in 2-player MVP mode.
4. The arena contains **8 total spawn points** for battery placement.
5. Collected batteries respawn after **3.0 seconds**.
6. The server is the only authority allowed to:
   - mark a battery as active/inactive
   - award points
   - resolve contested pickups
   - determine whether target-score or timeout conditions are met
7. Clients may show battery visuals and score UI only from authoritative snapshots/events.
8. 4-player support is architecture-ready only; MVP pacing is tuned for 2 players first.

### Spawn Rules

1. When a battery needs to respawn, the server builds a candidate list of spawn points.
2. Spawn points already occupied by active batteries are excluded.
3. The last **2 used spawn points** are excluded if alternatives exist.
4. Any spawn point within **2.0 world units** of a player is excluded if alternatives exist.
5. If multiple candidates remain, the server chooses deterministically from a seeded pseudo-random selection.
6. If no candidate remains, the server relaxes exclusion rules in this order:
   - relax player-distance exclusion first
   - relax recent-spawn exclusion second
7. Batteries never spawn on top of another active battery.

### Pickup Resolution Rules

1. Pickup resolution happens only during the pickup phase of the authoritative server tick.
2. If one player alone overlaps a battery validly, that player gets the pickup.
3. If multiple players overlap the same battery in the same tick:
   - resolve effects first (from the effect system)
   - compare authoritative squared distance from each eligible player center to the battery center
   - closest player wins
   - if distance is identical, lower session join index wins
4. A consumed battery is removed immediately from the active set and queued for respawn.

### Score Rules

1. Score updates occur only after authoritative pickup resolution.
2. Scores may only increase via confirmed battery collection in MVP.
3. Traps and slow shots do **not** directly change score in MVP.
4. Scoreboard updates are emitted to clients after the authoritative score change is locked.
5. When a player reaches 10 points, the room-state system may end the match immediately according to authoritative ordering rules.

### Pacing Rules

1. The design target for a typical 2-player match is roughly **75–120 seconds**.
2. Timeout should act as a fallback, not the dominant outcome.
3. The first tuning knobs for pacing are:
   - respawn delay
   - active battery count
   - anti-camp spawn radius
4. Variable battery values are not part of MVP.

### 4-Player Readiness Rules

1. 4-player mode is not tuned for MVP.
2. If later enabled, provisional scaling is:
   - active batteries = `playerCount + 1`, capped at 5
   - same 1-point value
   - same 120-second timeout
3. No 4-player tuning is assumed complete until later balance validation.

## Formulas

### Score Increment

```text
new_score = current_score + 1
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| current_score | int | 0-10+ | authoritative scoreboard | Player score before confirmed pickup |

**Expected output range**: integer score increment by 1
**Edge case**: If a winning score is reached, room-state may lock the match immediately after the score change.

### Active Battery Count (2-player MVP)

```text
active_battery_count = 3
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| active_battery_count | int | fixed at 3 in MVP | pacing configuration | Number of simultaneously available batteries |

**Expected output range**: constant 3 in 2-player MVP
**Edge case**: During spawn delay windows, live active count may temporarily drop below 3 until respawn completes.

### 4-Player Provisional Scaling

```text
active_battery_count = min(player_count + 1, 5)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| player_count | int | 2-4 future-ready | room roster | Number of connected competitors |

**Expected output range**: 3 to 5
**Edge case**: This formula is future-ready only and should not be treated as MVP tuned behavior.

### Contested Pickup Tie-Break

```text
winner = min_by(distance_to_battery, join_index)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| distance_to_battery | float | 0+ | server overlap resolution | Squared distance from player center to battery center |
| join_index | int | 0-1 in MVP | room membership order | Stable fallback order if distances tie |

**Expected output range**: exactly one pickup winner
**Edge case**: If one contender is invalidated by effect/state rules earlier in the tick, only eligible contenders remain in the comparison.

## Edge Cases

| Scenario | Expected Behavior | Rationale |
|----------|------------------|-----------|
| Two players touch the same battery on the same tick | Closest eligible player wins; join index breaks exact ties | Prevent ambiguous pickup results |
| A battery would respawn near a player but alternatives exist | Use another valid point first | Reduce obvious spawn camping |
| No spawn point satisfies all exclusions | Relax exclusion order deterministically | Ensure respawn still happens |
| Match timer expires while no one has reached 10 | Final score stands; room-state resolves winner or draw | Economy feeds room-state, not vice versa |
| A player is slowed when trying to reach a battery | Effect resolution happens before pickup resolution | Keeps fairness consistent with ADR ordering |
| A battery respawn is due on the same tick as a match end | Match end wins; no post-end score changes | Prevent after-the-buzzer scoring drift |
| 4-player mode is toggled on later | Use provisional scaling and treat it as untuned | Preserve future-ready seam without promising balance |

## Dependencies

| System | Direction | Nature of Dependency |
|--------|-----------|---------------------|
| Match Lifecycle & Room State | This depends on room state | Match must be `Active` for pickups/score progression |
| Player Controller & Input | This system consumes its consequences | Player movement and route choice determine pickup opportunities |
| Slow Shot & Trap Interaction | Mutual dependency | Effects can change who reaches a pickup first |
| HUD, Results, and Ranking UI | Other system depends on this | Needs authoritative scores and active battery state for display |
| `docs/architecture/adr-0005-battery-spawn-and-score-pacing-model.md` | Design dependency | Governs all pacing, spawn, and scoring constants |
| `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md` | Design dependency | Governs when pickup and scoring resolution happen in the tick |

## Tuning Knobs

| Parameter | Current Value | Safe Range | Effect of Increase | Effect of Decrease |
|-----------|--------------|------------|-------------------|-------------------|
| Active battery count | 3 | 2-5 | More targets, faster score race | Scarcer pickups, slower match pace |
| Respawn delay | 3.0 s | 1.5-5.0 s | Slower pacing, more route commitment | Faster pacing, more arena clutter |
| Anti-camp radius | 2.0 units | 1.0-3.0 units | Reduces nearby respawns more strongly | Makes nearby respawns more common |
| Recent-spawn memory | last 2 points | 1-3 points | More spawn variety | More repeated spawn patterns |
| Timeout | 120.0 s | 90.0-150.0 s | More comeback time | Faster match conclusion |

## Acceptance Criteria

- [ ] Exactly 3 batteries are active at once in 2-player MVP whenever respawn delays allow.
- [ ] Every confirmed battery pickup awards exactly 1 point.
- [ ] Contested pickups resolve deterministically on the server.
- [ ] Spawn selection avoids occupied, recently used, and near-player points when alternatives exist.
- [ ] Score can only change through confirmed battery collection in MVP.
- [ ] Battery economy and score progression remain consistent with ADR-0005 and ADR-0002.
- [ ] Match pacing can be tuned through clearly identified knobs without rewriting core rules.
