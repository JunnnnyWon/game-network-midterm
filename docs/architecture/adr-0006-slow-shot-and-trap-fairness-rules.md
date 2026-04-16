# ADR-0006: Slow Shot and Trap Fairness Rules

## Status

Accepted

## Date

2026-04-16

## Last Verified

2026-04-16

## Decision Makers

User, Codex (architecture synthesis)

## Summary

Battery Rush Arena will keep crowd-control simple and fair: the **slow shot** is the player’s active disruption tool, while **map traps** are short route hazards. In MVP, both effects are **slow-only** (no score penalty), they do **not stack multiplicatively**, and a short **post-debuff immunity window** prevents chain-lock frustration.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3.10f1) |
| **Domain** | Core |
| **Knowledge Risk** | LOW — effect rules are server-side and only lightly coupled to Unity collision visuals |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/modules/physics.md`, `docs/engine-reference/unity/modules/ui.md` |
| **Post-Cutoff APIs Used** | None |
| **Verification Required** | Verify trap trigger shapes and projectile-hit presentation stay aligned with server effect state in Unity 6.3 builds. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001, ADR-0002, ADR-0005 |
| **Enables** | Skill/trap gameplay implementation, status-effect UI, fairness testing |
| **Blocks** | Slow shot implementation, trap implementation, debuff HUD work |
| **Ordering Note** | This ADR assumes ADR-0005 pacing and ADR-0002 tick ordering are already fixed. |

## Context

### Problem Statement

The concept promised both map traps and a player slow-shot, but review feedback warned that two disruption systems could easily become oppressive or redundant. Without explicit fairness rules, the game risks turning into repeated slow-lock frustration instead of readable competitive tension.

### Current State

The concept had not decided whether traps should reduce score or only slow movement, and it did not define cooldowns, durations, stacking, or feedback requirements.

### Constraints

- MVP needs only one active player skill and one trap behavior.
- Crowd control must remain understandable on a single PC display.
- The server must own effect timing and resolution.
- The gameplay should emphasize route denial and tempo shifts, not deep combat.

### Requirements

- Must define slow-shot values.
- Must define trap values.
- Must define stacking/immunity policy.
- Must avoid score-loss mechanics in MVP unless strictly necessary.
- Must provide enough clarity for HUD/status presentation.

## Decision

### Slow shot (player skill)
- **Input**: Left Mouse Button fires toward current mouse aim vector.
- **Cooldown**: 4.0 seconds
- **Projectile speed**: 14 units/second
- **Maximum lifetime**: 0.8 seconds
- **On hit effect**: 35% movement-speed reduction
- **Effect duration**: 1.25 seconds
- **Hit confirmation**: attacker receives hit marker; victim receives debuff icon + duration bar

### Map traps (arena hazard)
- **Trap type**: static floor hazard placed symmetrically in the arena
- **Trigger**: entering trap trigger collider
- **Effect**: 20% movement-speed reduction
- **Effect duration**: 0.75 seconds
- **Per-player retrigger cooldown for the same trap**: 1.5 seconds
- **Score penalty**: none in MVP

### Debuff interaction policy
- Effects never stack multiplicatively.
- Only the **strongest active movement slow** applies.
- If a weaker slow arrives while a stronger one is active, it is ignored.
- If an equally strong slow arrives while one is active, it does **not** refresh duration in MVP.
- After any movement slow ends, the player gets **0.5 seconds of debuff immunity** to prevent chain-lock behavior.

### Collision/fairness clarifications
- Player avatars do **not body-block** each other in MVP; player-vs-player collision is non-blocking.
- Traps are readable hazards, not hidden punishments.
- Neither traps nor slow shot may reduce score directly in MVP.
- The active disruption skill (slow shot) is the primary comeback tool; traps only shape route choice.

### Architecture

```text
Player fires slow shot -> server validates cooldown -> projectile exists on server
                                              |
                                              v
                               hit? apply strong slow (1.25s)

Player enters trap -> server validates per-trap retrigger cooldown
                    -> apply mild slow (0.75s)

Effect combiner -> strongest slow wins -> immunity window after expiry
```

### Key Interfaces

```csharp
public record SlowEffectState(
    float MoveMultiplier,
    float RemainingSeconds,
    string Source,
    bool Immune);

public interface IEffectRulesService {
    bool TryFireSlowShot(PlayerId playerId, Vector2 aimDirection, int serverTick);
    void TryTriggerTrap(PlayerId playerId, TrapId trapId, int serverTick);
    SlowEffectState GetEffectState(PlayerId playerId);
}
```

### Implementation Guidelines

- Keep cooldown and durations in config data, not hardcoded magic numbers in multiple files.
- Run all cooldown and duration timers on the server.
- Mirror only the resulting debuff state to the client.
- Use clear VFX/SFX cues, but keep visual noise low.
- If playtests show frustration, tune trap slowdown before changing slow-shot identity.

## Alternatives Considered

### Alternative 1: Score-reducing traps
- **Description**: traps reduce points when stepped on.
- **Pros**: big punishment, high tension.
- **Cons**: feels unfair, snowballs losing states, makes ranking less readable.
- **Estimated Effort**: Similar.
- **Rejection Reason**: too punishing for MVP and undermines the clean competition fantasy.

### Alternative 2: Fully stackable slows
- **Description**: trap + skill slows multiply or refresh repeatedly.
- **Pros**: stronger crowd control, dramatic disruption.
- **Cons**: high frustration, easy chain-lock, poor fairness perception.
- **Estimated Effort**: Low to implement, high cost to fun.
- **Rejection Reason**: directly contradicts review feedback on fairness.

### Alternative 3: Trap removed, skill only
- **Description**: remove map traps and keep only the slow shot.
- **Pros**: simpler ruleset.
- **Cons**: loses route-planning layer and one source of environmental tension.
- **Estimated Effort**: Lower.
- **Rejection Reason**: the chosen concept intentionally includes both active and passive disruption.

## Consequences

### Positive
- Crowd control remains readable and bounded.
- Slow shot and traps have distinct gameplay roles.
- The HUD can represent debuffs with one consistent state model.
- Ranking integrity is preserved because score cannot be stolen directly by crowd control.

### Negative
- No body-blocking means one possible PvP tactic is intentionally removed.
- Some players may want stronger punishment than the MVP design allows.
- Fairness relies on clean feedback and good arena placement.

### Neutral
- Future advanced skill variants remain possible, but this ADR intentionally keeps MVP simple.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| Slow shot still feels too strong at 35% / 1.25s | Medium | Medium | Tune duration before increasing cooldown burden |
| Traps feel irrelevant at 20% / 0.75s | Medium | Low | Tune trap placement before tuning raw effect values |
| Immunity window feels too forgiving | Low | Low | Revisit only after playtest evidence |

## Performance Implications
- **CPU**: negligible; only lightweight effect-state bookkeeping.
- **Memory**: tiny per-player effect state.
- **Load Time**: none.
- **Network**: minimal status-effect data in snapshots/events.

## Migration Plan

1. Implement slow-shot cooldown and projectile simulation on the server.
2. Implement trap trigger service with per-player retrigger cooldown.
3. Add debuff combiner and immunity-window logic.
4. Bind debuff state to HUD/status indicators.

**Rollback plan**: If traps and slow shot still overlap too much in playtests, write a superseding ADR that removes or redesigns one of them rather than layering exceptions into the same ruleset.

## Validation Criteria

- [ ] Slow-shot cooldown prevents repeated spam from one player.
- [ ] Trap and slow shot never stack into compounded movement reduction.
- [ ] Players regain normal movement after debuff duration plus immunity window exactly as specified.
- [ ] Trap hits never change score in MVP.
- [ ] Status UI clearly distinguishes “slowed” from normal state.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/game-concept.md` | Interference | **TR-concept-005** — Players can fire a slow-shot skill | Defines the slow-shot mechanical values and cooldown |
| `design/gdd/game-concept.md` | Hazards | **TR-concept-006** — Arena contains map traps that affect routing | Defines trap behavior as short route-shaping hazards |
| `design/gdd/game-concept.md` | Fairness | **TR-concept-013** — Competitive rules must avoid oppressive or unclear disruption | Uses strongest-effect-only logic, no score loss, and a post-debuff immunity window |
| `design/gdd/systems-index.md` | Slow Shot & Trap Interaction | MVP gameplay system | Turns the disruption concept into a bounded and testable rule set |

## Related

- `docs/architecture/adr-0001-network-authority-and-transport-strategy.md`
- `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md`
- `docs/architecture/adr-0005-battery-spawn-and-score-pacing-model.md`
- `docs/architecture/architecture.md`
- `design/gdd/game-concept.md`
- `design/gdd/systems-index.md`
