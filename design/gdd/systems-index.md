# Systems Index: Battery Rush Arena

> **Status**: Draft
> **Created**: 2026-04-16
> **Last Updated**: 2026-04-16
> **Source Concept**: design/gdd/game-concept.md

---

## Overview

*Battery Rush Arena* is a small-scale competitive online game built around one short repeatable loop: join a room, start a match, collect batteries, disrupt opponents, resolve the winner on the server, and store the result in MySQL for leaderboard display. The game needs a small number of tightly-coupled systems rather than a wide feature set, so the design emphasis is on deterministic rules, authoritative match flow, and clear player-facing feedback.

The current architecture baseline now includes accepted ADR coverage for transport, room state, input runtime, scoring/pacing, disruption fairness, persistence, runtime UI, and audio-event routing. `Audio Feedback` remains a **Vertical Slice** system, while the other seven systems remain MVP-critical.

---

## Systems Enumeration

| # | System Name | Category | Priority | Status | Design Doc | Depends On |
|---|-------------|----------|----------|--------|------------|------------|
| 1 | Match Lifecycle & Room State | Core | MVP | Not Started | — | Network Session & Transport |
| 2 | Network Session & Transport | Foundation | MVP | Draft | design/gdd/network-session-and-transport.md | — |
| 3 | Player Controller & Input | Core | MVP | Not Started | — | Match Lifecycle & Room State |
| 4 | Arena Battery Economy & Scoring | Gameplay | MVP | Not Started | — | Match Lifecycle & Room State, Player Controller & Input |
| 5 | Slow Shot & Trap Interaction | Gameplay | MVP | Not Started | — | Player Controller & Input, Arena Battery Economy & Scoring |
| 6 | Results Persistence & Leaderboard | Persistence | MVP | Not Started | — | Match Lifecycle & Room State, Arena Battery Economy & Scoring |
| 7 | HUD, Results, and Ranking UI | UI | MVP | Not Started | — | Match Lifecycle & Room State, Arena Battery Economy & Scoring, Results Persistence & Leaderboard |
| 8 | Audio Feedback | Audio | Vertical Slice | Not Started | — | Match Lifecycle & Room State, Slow Shot & Trap Interaction |

---

## Categories

| Category | Description | Typical Systems |
|----------|-------------|-----------------|
| **Foundation** | Lowest-level technical systems everything else depends on | Networking, transport, protocol, shared authority seams |
| **Core** | Match-runtime systems that sit directly on top of the foundation | Room state, input, timing |
| **Gameplay** | The systems that make the match competitive | Battery scoring, trap logic, skill logic |
| **Persistence** | State that survives beyond a single round | Match results, rankings, profile stats |
| **UI** | Player-facing information displays | HUD, countdown, results screen, leaderboard |
| **Audio** | Sound feedback and atmosphere | Pickups, hits, alerts, victory sounds |

---

## Priority Tiers

| Tier | Definition | Target Milestone | Design Urgency |
|------|------------|------------------|----------------|
| **MVP** | Required for the core loop to function. Without these, you can't test the multiplayer match. | First playable prototype | Design FIRST |
| **Vertical Slice** | Required for a polished class demo and presentation. | Demo / 발표 build | Design SECOND |
| **Alpha** | Complete feature scope with rough implementation. | Expanded post-midterm build | Design THIRD |
| **Full Vision** | Stretch or polish features outside assignment scope. | Post-course polish | Design as needed |

---

## Dependency Map

### Foundation Layer (no dependencies)

1. **Network Session & Transport** — foundational because all room join, player presence, and authoritative state delivery depend on it.

### Core Layer (depends on foundation)

1. **Match Lifecycle & Room State** — depends on: Network Session & Transport
2. **Player Controller & Input** — depends on: Match Lifecycle & Room State

### Feature Layer (depends on core)

1. **Arena Battery Economy & Scoring** — depends on: Match Lifecycle & Room State, Player Controller & Input
2. **Slow Shot & Trap Interaction** — depends on: Player Controller & Input, Arena Battery Economy & Scoring
3. **Results Persistence & Leaderboard** — depends on: Match Lifecycle & Room State, Arena Battery Economy & Scoring

### Presentation Layer (depends on features)

1. **HUD, Results, and Ranking UI** — depends on: Match Lifecycle & Room State, Arena Battery Economy & Scoring, Results Persistence & Leaderboard
2. **Audio Feedback** — depends on: Match Lifecycle & Room State, Slow Shot & Trap Interaction

### Polish Layer (depends on everything)

1. **Presentation polish and tuning pass (inferred)** — depends on: all MVP systems

---

## Recommended Design Order

| Order | System | Priority | Layer | Agent(s) | Est. Effort |
|-------|--------|----------|-------|----------|-------------|
| 1 | Network Session & Transport | MVP | Foundation | systems-designer, network-programmer | M |
| 2 | Match Lifecycle & Room State | MVP | Core | game-designer, systems-designer | M |
| 3 | Player Controller & Input | MVP | Core | game-designer, gameplay-programmer | S |
| 4 | Arena Battery Economy & Scoring | MVP | Feature | game-designer, systems-designer | M |
| 5 | Slow Shot & Trap Interaction | MVP | Feature | game-designer, systems-designer | M |
| 6 | Results Persistence & Leaderboard | MVP | Feature | systems-designer, analytics-engineer | M |
| 7 | HUD, Results, and Ranking UI | MVP | Presentation | ux-designer, ui-programmer | M |
| 8 | Audio Feedback | Vertical Slice | Presentation | audio-director, sound-designer | S |

---

## Architecture Baseline (Accepted ADR Coverage)

| System | Governing ADR(s) | Notes |
|--------|------------------|-------|
| Network Session & Transport | ADR-0001 | Foundation contract for dedicated server transport and authority boundary |
| Match Lifecycle & Room State | ADR-0002 | Fixed authoritative room-state machine and end-of-match ordering |
| Player Controller & Input | ADR-0007 | Unity Input System action maps, tick-aligned input frames, client prediction boundary |
| Arena Battery Economy & Scoring | ADR-0005 | Battery pacing, contested pickup ordering, target score contract |
| Slow Shot & Trap Interaction | ADR-0006 | Slow-shot/trap fairness, debuff stacking, immunity window |
| Results Persistence & Leaderboard | ADR-0003 | Server-only persistence gateway, idempotent writes, leaderboard sort contract |
| HUD, Results, and Ranking UI | ADR-0004 | Runtime UI Toolkit stack and screen/state flow |
| Audio Feedback | ADR-0008 | Vertical Slice presentation-only cue routing from authoritative events + local UI cues |

---

## Circular Dependencies

- **HUD, Results, and Ranking UI ↔ Match Lifecycle & Room State**: UI needs room state to render, while room state transitions depend on player ready/rematch input. **Proposed resolution**: room state remains authoritative; UI sends intent events only.
- **Arena Battery Economy & Scoring ↔ Slow Shot & Trap Interaction**: scoring needs debuff/trap results to resolve fair pickups, while trap/slow outcomes may depend on score-phase context. **Proposed resolution**: route all contested outcomes through a single server-side match rules service.

---

## High-Risk Systems

| System | Risk Type | Risk Description | Mitigation |
|--------|-----------|-----------------|------------|
| Network Session & Transport | Technical | Unity client + separate C# authoritative server requires a clear protocol and sync model | Freeze protocol and authority rules before coding |
| Match Lifecycle & Room State | Design / Technical | Undefined edge cases can break match flow, ties, or disconnect handling | Write explicit state machine and event ordering |
| Results Persistence & Leaderboard | Technical | Duplicate writes, DB failures, or ambiguous ranking formulas can corrupt class-demo results | Use async write queue + deterministic ranking formula + idempotency key |
| 4-player scalability | Scope | Supporting up to 4 players can expand balancing, HUD, and network work beyond MVP | Treat 2-player as MVP, 4-player as architecture-ready stretch target |

---

## Progress Tracker

| Metric | Count |
|--------|-------|
| Total systems identified | 8 |
| Design docs started | 2 |
| Design docs reviewed | 1 |
| Design docs approved | 0 |
| Systems with accepted ADR coverage | 8/8 |
| MVP systems designed | 2/7 |
| Vertical Slice systems designed | 0/1 |

---

## Next Steps

- [ ] Review and approve this systems enumeration
- [ ] Re-run `/architecture-review` so the generated traceability docs reflect ADR-0007 and ADR-0008
- [ ] Design MVP-tier systems first (use `/design-system [system-name]`)
- [ ] Run `/design-review` on each completed GDD
- [ ] Create the master architecture document (`/create-architecture`)
- [ ] Prototype the highest-risk system early (network session + room state)
