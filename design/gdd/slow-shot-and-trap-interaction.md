# Slow Shot & Trap Interaction

> **Status**: Draft
> **Author**: Codex
> **Last Updated**: 2026-04-16
> **Last Verified**: 2026-04-16
> **Implements Pillar**: Pillar 2 — Short Matches, Real Tension

## Summary

`Slow Shot & Trap Interaction` defines the game’s two disruption tools: a player-fired slow shot and map-based trap hazards. It exists so interference adds route tension and comeback opportunities without turning the match into frustrating crowd-control spam.

> **Quick reference** — Layer: `Feature` · Priority: `MVP` · Key deps: `Player Controller & Input`, `Arena Battery Economy & Scoring`

## Overview

This system governs how players interfere with each other’s movement and routing during a match. The slow shot is the active player skill, while traps are passive environmental pressure. Both are server-authoritative, both affect movement only, and both are tuned for readability and fairness rather than heavy punishment.

## Player Fantasy

The player should feel disruptive and tactical, not abusive. Landing a slow shot should feel like stealing momentum at the perfect time, and trap routes should create meaningful danger zones without feeling like invisible or unfair punishment.

## Detailed Rules

### Core Rules

1. MVP includes exactly:
   - one active player skill: **slow shot**
   - one map hazard type: **trap slow**
2. Both effects are **movement slow only** in MVP.
3. Neither effect directly reduces score.
4. The server owns all cooldowns, durations, hit checks, trap trigger checks, and final effect state.
5. Clients may only render confirmed effect state.

### Slow Shot Rules

1. Input is `Fire` from the gameplay input map.
2. Cooldown is **4.0 seconds**.
3. Projectile speed is **14 units/second**.
4. Maximum lifetime is **0.8 seconds**.
5. On confirmed hit, target movement speed is reduced by **35%**.
6. Slow-shot effect duration is **1.25 seconds**.
7. The attacker may get a local fire cue immediately, but hit confirmation is authoritative.

### Trap Rules

1. Traps are static floor hazards placed symmetrically in the arena.
2. Trap trigger occurs on authoritative entry into the trap trigger region.
3. Trap effect is **20% movement-speed reduction**.
4. Trap duration is **0.75 seconds**.
5. The same trap cannot re-trigger on the same player for **1.5 seconds**.
6. Trap triggers affect routing but not score directly.

### Stacking and Immunity Rules

1. Slows never stack multiplicatively.
2. Only the strongest active slow currently applies.
3. If a weaker slow arrives while a stronger slow is active, it is ignored.
4. If an equally strong slow arrives while a same-strength slow is active, it does not refresh duration in MVP.
5. After any movement slow ends, the player receives **0.5 seconds** of debuff immunity.

### Collision and Fairness Rules

1. Players do **not** body-block each other in MVP.
2. Traps must be visually readable hazards.
3. Disruption exists to change routes/timing, not to lock the player out of the game.
4. Any adjustment that increases punishment should be treated as a tuning change requiring explicit validation.

### Ordering Rules

1. Effects resolve before contested pickup resolution in the same server tick.
2. The latest authoritative effect state is what downstream systems consume.
3. If a player is slowed before pickup resolution in the same tick, pickup eligibility uses the post-effect authoritative state.

## Formulas

### Slow Shot Multiplier

```text
move_multiplier = 1.0 - 0.35 = 0.65
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| base_move_multiplier | float | fixed at 1.0 | movement baseline | Normal movement multiplier |

**Expected output range**: 0.65 while active
**Edge case**: If a stronger or equal effect policy blocks refresh/override, the active multiplier remains unchanged.

### Trap Multiplier

```text
move_multiplier = 1.0 - 0.20 = 0.80
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| base_move_multiplier | float | fixed at 1.0 | movement baseline | Normal movement multiplier |

**Expected output range**: 0.80 while active
**Edge case**: Trap slow is ignored if a stronger active slow is already applied.

### Strongest-Slow Rule

```text
applied_slow = min(all_active_move_multipliers)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| all_active_move_multipliers | list<float> | 0.0-1.0 | authoritative effect state | All currently eligible slow multipliers |

**Expected output range**: one active multiplier only
**Edge case**: If no slow exists, multiplier returns to 1.0; after expiry, immunity begins.

### Immunity Window

```text
immune_until = slow_end_time + 0.5 seconds
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| slow_end_time | timestamp | current match time | authoritative effect expiry | Time when active slow ends |

**Expected output range**: 0.5 seconds post-slow protection
**Edge case**: New slow attempts during immunity are ignored.

## Edge Cases

| Scenario | Expected Behavior | Rationale |
|----------|------------------|-----------|
| Player is hit by slow shot while already trap-slowed | Stronger effect applies; no stacking | Prevent oppressive stacking |
| Player steps on the same trap repeatedly | Same trap is blocked by retrigger cooldown | Avoid trap spam |
| Player is slowed and reaches a battery on the same tick | Pickup resolution uses post-effect authoritative state | Keep ordering deterministic |
| Fire button is pressed during cooldown | No new projectile is created | Cooldown remains authoritative |
| Equal-strength slow arrives during active slow | Duration does not refresh in MVP | Keep behavior simple and bounded |
| Slow expires and another slow lands immediately | Immunity blocks the new slow for 0.5 s | Prevent chain-lock frustration |
| Trap is visually unclear in the map layout | Treat as implementation/placement bug, not a rules exception | Readability is part of fairness |

## Dependencies

| System | Direction | Nature of Dependency |
|--------|-----------|---------------------|
| Player Controller & Input | This depends on it | Fire intent comes from input/runtime contract |
| Arena Battery Economy & Scoring | Mutual dependency | Effect timing influences pickup outcomes and route control |
| HUD, Results, and Ranking UI | Other system depends on this | Needs debuff state, cooldown state, and readable feedback |
| Audio Feedback | Other system depends on this | Hit/trap/debuff cues consume authoritative effect transitions |
| `docs/architecture/adr-0006-slow-shot-and-trap-fairness-rules.md` | Design dependency | Governs all core values and fairness rules |
| `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md` | Design dependency | Governs effect-before-pickup ordering |

## Tuning Knobs

| Parameter | Current Value | Safe Range | Effect of Increase | Effect of Decrease |
|-----------|--------------|------------|-------------------|-------------------|
| Slow shot cooldown | 4.0 s | 3.0-6.0 s | Less spam, lower pressure frequency | More frequent disruption |
| Slow shot duration | 1.25 s | 0.75-1.5 s | Stronger route denial | Softer impact |
| Slow shot strength | 35% slow | 20%-40% | More punishment | Less impact |
| Trap duration | 0.75 s | 0.5-1.25 s | Longer route denial | More forgiving traps |
| Trap strength | 20% slow | 10%-30% | Stronger hazard pressure | More ignorable traps |
| Post-debuff immunity | 0.5 s | 0.25-1.0 s | More anti-chain-lock protection | More frequent CC chaining |

## Acceptance Criteria

- [ ] Slow shot uses a 4.0-second cooldown and cannot be spammed while cooling down.
- [ ] Trap and slow-shot effects never stack into compounded movement reduction.
- [ ] The strongest active slow is the only movement modifier that applies.
- [ ] Players gain 0.5 seconds of immunity after slow expiry.
- [ ] Trap and slow shot never change score directly in MVP.
- [ ] Effect ordering remains consistent with ADR-0006 and ADR-0002.
- [ ] The system stays readable and fair enough to support route-based competition rather than crowd-control abuse.
