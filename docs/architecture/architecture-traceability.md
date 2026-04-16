# Architecture Traceability Index

<!-- Living document — updated by /architecture-review after each review run.
     Do not edit manually unless correcting an error. -->

## Document Status

- **Last Updated**: 2026-04-16
- **Engine**: Unity 6.3 LTS (6000.3.10f1)
- **GDDs Indexed**: 2
- **ADRs Indexed**: 8
- **Last Review**: `docs/architecture/architecture-review-2026-04-16.md`

## Coverage Summary

## Tier A slice coverage

Covered Tier A systems:
- Network Session & Transport
- Match Lifecycle & Room State
- Player Controller & Input
- Arena Battery Economy & Scoring
- Results Persistence & Leaderboard
- HUD, Results, and Ranking UI


| Status | Count | Percentage |
|--------|-------|-----------|
| ✅ Covered | 22 | 100% |
| ⚠️ Partial | 0 | 0% |
| ❌ Gap | 0 | 0% |
| **Total** | **22** | **100%** |

---

## Traceability Matrix

| Req ID | GDD | System | Requirement Summary | ADR(s) | Status | Notes |
|--------|-----|--------|---------------------|--------|--------|-------|
| TR-concept-001 | game-concept.md | Match Goal | First player to 10 points wins immediately | ADR-0002, ADR-0005 | ✅ Covered | End-state and pacing ADRs both govern this rule. |
| TR-concept-002 | game-concept.md | Timeout Rule | If timer expires, highest score wins | ADR-0002, ADR-0005 | ✅ Covered | Timeout ordering and pacing constants are fixed. |
| TR-concept-003 | game-concept.md | Match Scale | MVP supports 2 players; architecture should scale to 4 | ADR-0001 | ✅ Covered | Dedicated authoritative server keeps 4-player seams available. |
| TR-concept-004 | game-concept.md | Core Loop | Players move in a top-down 2D arena and collect batteries | ADR-0005, ADR-0007 | ✅ Covered | Scoring/pacing and input/runtime contracts both participate. |
| TR-concept-005 | game-concept.md | Interference | Players can fire a slow-shot skill | ADR-0006 | ✅ Covered | Slow-shot mechanic and fairness rules are explicit. |
| TR-concept-006 | game-concept.md | Hazards | Arena contains map traps that affect routing | ADR-0006 | ✅ Covered | Trap behavior is explicit and bounded. |
| TR-concept-007 | game-concept.md | Authority | Server authoritatively resolves score, effects, and victory | ADR-0001, ADR-0002 | ✅ Covered | Authority boundary and match-state ordering both enforce this. |
| TR-concept-008 | game-concept.md | Persistence | Match results are stored in MySQL | ADR-0003 | ✅ Covered | Server-only MySQL persistence gateway is defined. |
| TR-concept-009 | game-concept.md | Ranking | Players can query and view leaderboard/ranking data | ADR-0003, ADR-0004 | ✅ Covered | Persistence schema and runtime screen flow both govern this. |
| TR-concept-010 | game-concept.md | Readability | Match state must remain instantly readable on a single PC display | ADR-0004 | ✅ Covered | HUD and panel structure lock the readability contract. |
| TR-concept-011 | game-concept.md | Session Length | Rounds should be short, repeatable, and demo-friendly | ADR-0002, ADR-0005 | ✅ Covered | Match lifecycle and pacing target window both align. |
| TR-concept-012 | game-concept.md | Control Scheme | Game is played with keyboard and mouse on PC | ADR-0001, ADR-0004, ADR-0007 | ✅ Covered | Transport, UI flow, and dedicated input ADR all align. |
| TR-concept-013 | game-concept.md | Fairness | Competitive rules must avoid oppressive or unclear disruption | ADR-0002, ADR-0005, ADR-0006, ADR-0007, ADR-0008 | ✅ Covered | Ordering, pacing, disruption, input, and presentation cues all support fairness. |
| TR-concept-014 | game-concept.md | Results Visibility | Network and database outcomes must be visible to players | ADR-0001, ADR-0003, ADR-0004, ADR-0008 | ✅ Covered | Transport, persistence, UI, and feedback cues all support visibility. |
| TR-systems-001 | systems-index.md | Network Session & Transport | Foundation transport system exists and anchors downstream modules | ADR-0001 | ✅ Covered | Directly addressed as the foundational transport ADR. |
| TR-systems-002 | systems-index.md | Match Lifecycle & Room State | Core lifecycle system exists and depends on transport | ADR-0002 | ✅ Covered | Directly addressed as the lifecycle ADR. |
| TR-systems-003 | systems-index.md | Player Controller & Input | Dedicated player-control/input architecture exists for the Unity client | ADR-0007 | ✅ Covered | Dedicated input/runtime contract now exists. |
| TR-systems-004 | systems-index.md | Arena Battery Economy & Scoring | Dedicated battery/scoring architecture exists | ADR-0005 | ✅ Covered | Directly addressed as the pacing/scoring ADR. |
| TR-systems-005 | systems-index.md | Slow Shot & Trap Interaction | Dedicated disruption-effect architecture exists | ADR-0006 | ✅ Covered | Directly addressed as the fairness/effect ADR. |
| TR-systems-006 | systems-index.md | Results Persistence & Leaderboard | Dedicated persistence/leaderboard architecture exists | ADR-0003 | ✅ Covered | Directly addressed as the persistence ADR. |
| TR-systems-007 | systems-index.md | HUD, Results, and Ranking UI | Dedicated runtime UI architecture exists | ADR-0004 | ✅ Covered | Directly addressed as the runtime UI ADR. |
| TR-systems-008 | systems-index.md | Audio Feedback | Audio system architecture exists | ADR-0008 | ✅ Covered | Presentation-only audio event contract now exists. |

## Known Gaps

None.

---

## Cross-ADR Conflicts

No unresolved ADR conflicts were found in the latest review.

---

## ADR → GDD Coverage (Reverse Index)

| ADR | Title | GDD Requirements Addressed | Engine Risk |
|-----|-------|---------------------------|-------------|
| ADR-0001 | Network Authority and Transport Strategy | TR-concept-003, TR-concept-007, TR-concept-012, TR-concept-014, TR-systems-001 | HIGH |
| ADR-0002 | Match State Machine and Event Ordering | TR-concept-001, TR-concept-002, TR-concept-007, TR-concept-011, TR-concept-013, TR-systems-002 | MEDIUM |
| ADR-0003 | Persistence Boundary and Leaderboard Formula | TR-concept-008, TR-concept-009, TR-concept-014, TR-systems-006 | LOW |
| ADR-0004 | Runtime UI Stack and Screen Flow | TR-concept-009, TR-concept-010, TR-concept-012, TR-concept-014, TR-systems-007 | HIGH |
| ADR-0005 | Battery Spawn and Score Pacing Model | TR-concept-001, TR-concept-002, TR-concept-004, TR-concept-011, TR-concept-013, TR-systems-004 | LOW |
| ADR-0006 | Slow Shot and Trap Fairness Rules | TR-concept-005, TR-concept-006, TR-concept-013, TR-systems-005 | LOW |
| ADR-0007 | Player Controller and Input Runtime Contract | TR-concept-004, TR-concept-012, TR-systems-003 | HIGH |
| ADR-0008 | Audio Feedback Event Contract | TR-systems-008 | MEDIUM |

---

## Superseded Requirements

None in this review run.

---

## How to Use This Document

**When writing a new ADR**: add it to the reverse index and update any requirement rows it covers.

**When approving a GDD change**: scan the matrix for requirements from that GDD and verify whether an accepted ADR needs revision.

**When running `/architecture-review`**: this document should be refreshed from the current GDD + ADR state.

**Gate check**: the Pre-Production gate requires this document to exist and to have zero Foundation Layer Gaps.
