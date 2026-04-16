# ADR-0007: Player Controller and Input Runtime Contract

## Status

Accepted

## Date

2026-04-16

## Last Verified

2026-04-16

## Decision Makers

User, Codex (architecture synthesis)

## Summary

Battery Rush Arena will use the **Unity Input System** with explicit **Gameplay** and **UI** action maps, and the Unity client will publish exactly one **tick-aligned input frame** for the locally controlled player. The client may predict local locomotion and aim presentation for responsiveness, but the server remains authoritative for transforms, debuffs, pickups, score, and every other competitive outcome.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3.10f1) |
| **Domain** | Input |
| **Knowledge Risk** | HIGH — Unity 6.3 input behavior is post-cutoff and must be verified in the pinned build |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/current-best-practices.md`, `docs/engine-reference/unity/deprecated-apis.md`, `docs/engine-reference/unity/modules/input.md`, `docs/engine-reference/unity/modules/ui.md` |
| **Post-Cutoff APIs Used** | Input System package action maps, generated C# action wrapper, `InputAction.ReadValue<Vector2>()`, `Keyboard.current`, `Mouse.current` |
| **Verification Required** | Verify keyboard/mouse bindings, gameplay-vs-UI map switching, fire-edge capture, and local prediction/reconciliation feel in actual Unity 6.3 desktop builds. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001, ADR-0002, ADR-0004 |
| **Enables** | Player-controller implementation, movement prediction tuning, gameplay system GDDs that depend on input capture |
| **Blocks** | Player Controller & Input implementation, transport/input integration work |
| **Ordering Note** | Transport cadence, room-state semantics, and runtime UI flow must stay fixed while this ADR narrows the client-side input/runtime contract. |

## Context

### Problem Statement

The architecture review found that `Player Controller & Input` was only described in `architecture.md` and had no dedicated ADR. Without a dedicated contract, implementation could drift between legacy `Input.*` polling, ad hoc UI/gameplay bindings, or overly optimistic client prediction that conflicts with the authoritative server.

### Current State

The concept commits the game to keyboard and mouse on PC, ADR-0001 fixes the thin-client/server-authority split, and ADR-0004 fixes the runtime screen flow. What remained unspecified was how the Unity client samples local controls, packages them into transport-safe input frames, and limits prediction so that responsiveness improves without letting the client invent game state.

### Constraints

- Unity 6.3 should use the new Input System, not legacy `Input.*` APIs.
- The project is keyboard + mouse only for MVP.
- The server tick cadence is already fixed by ADR-0001.
- Match state, score, pickups, effects, and debuffs remain server authoritative.
- The project must stay simple enough for a small class-team implementation.

### Requirements

- Must convert keyboard/mouse input into deterministic, transport-safe input frames.
- Must separate gameplay actions from menu/UI confirm-back behavior.
- Must support responsive local movement presentation without moving authority to the client.
- Must tolerate authoritative correction when slows, traps, or collisions change movement outcome.
- Must prevent legacy-input or mixed-input-stack drift.

## Decision

Battery Rush Arena will use a **single generated Input Actions asset** with two action maps:

- **Gameplay**
  - `Move` (`Vector2`) — WASD
  - `AimScreenPosition` (`Vector2`) — mouse position
  - `Fire` (`Button`) — left mouse button
- **UI**
  - `Confirm` (`Button`) — Enter / primary submit
  - `Back` (`Button`) — Escape / cancel
  - `Point` (`Vector2`) — mouse position
  - `Click` (`Button`) — left mouse button

### Runtime ownership rules

- Only the **local player** has an active player-controller input adapter.
- Remote players never run local input logic; they render authoritative snapshots only.
- The client samples device state every render frame, caches the latest values, and emits **one `InputFrame` per local prediction/network tick**.
- The client sends gameplay input frames only while the room is in `Active`. During lobby/results flow, the UI map publishes UI intents instead.

### Input-frame contract

Each emitted `InputFrame` contains:

- `Tick` — client-local sequence matched to the next transport send
- `MoveX`, `MoveY` — normalized/clamped movement vector in range `[-1, 1]`
- `AimX`, `AimY` — normalized world-space aim direction derived from current mouse position relative to the locally rendered player anchor
- `FirePressed` — rising-edge-only fire bit latched until included in a sent frame

Rules:

- Diagonal movement must be normalized to avoid faster diagonal speed.
- If the current mouse position produces a zero-length aim vector, reuse the last valid non-zero aim direction.
- `FirePressed` is an **edge**, not a held-state auto-fire flag.
- Gameplay input does not include score, position, battery, trap, or debuff claims.

### Prediction + reconciliation contract

- The Unity client predicts **only** local transform-facing presentation, camera follow, and immediate crosshair feedback.
- Prediction uses the latest known authoritative movement modifiers (for example the current slow multiplier); it does **not** predict pickups, trap triggers, hit confirmation, or debuff expiry beyond the last authoritative state.
- The server may correct local position/velocity at any snapshot. The client smooth-corrects small errors and snaps only when divergence exceeds a visible threshold set during tuning.

### Architecture

```text
Keyboard/Mouse
      |
      v
Unity Input System action maps
      |
      v
PlayerControllerInputAdapter
      |                    \
      |                     -> local movement/camera prediction (presentation only)
      v
Tick-aligned InputFrame
      |
      v
ADR-0001 transport -> authoritative server simulation -> correction snapshot
```

### Key Interfaces

```csharp
public record InputFrame(
    int Tick,
    float MoveX,
    float MoveY,
    float AimX,
    float AimY,
    bool FirePressed);

public interface IInputFrameBuilder {
    InputFrame BuildForTick(int tick, Vector3 localPlayerWorldPosition);
}

public interface ILocalPredictionController {
    void ApplyPredictedInput(InputFrame frame, float moveSpeedScale);
    void Reconcile(AuthoritativePlayerSnapshot snapshot);
}
```

### Implementation Guidelines

- Generate the Input System C# wrapper from one shared `.inputactions` asset; do not hand-roll duplicated bindings in multiple scripts.
- Keep gameplay-map enable/disable logic explicit and tied to room/UI state transitions.
- Treat UI `Confirm`/`Back` as intent sources only; UI buttons still own the visible flow from ADR-0004.
- Quantize/clamp outgoing vectors in one place before transport serialization.
- Do not use legacy `Input.GetKey*`, `Input.mousePosition`, or mixed-input-stack fallbacks in production code.

## Alternatives Considered

### Alternative 1: Direct device polling only (`Keyboard.current` / `Mouse.current`)
- **Description**: poll raw devices directly in gameplay scripts without a shared action-map asset.
- **Pros**: fast to prototype, fewer assets up front.
- **Cons**: weaker separation between gameplay and UI bindings, more duplicated binding logic, harder future rebinding/testing.
- **Estimated Effort**: Slightly lower initial effort.
- **Rejection Reason**: the project needs one explicit input contract across gameplay + UI, not scattered device reads.

### Alternative 2: Legacy Input Manager / `Input.*`
- **Description**: use the deprecated Input Manager and `Input.GetAxis`/`Input.GetKeyDown`.
- **Pros**: familiar from older Unity tutorials.
- **Cons**: deprecated in Unity 6, weaker future support, conflicts with pinned engine guidance.
- **Estimated Effort**: Similar.
- **Rejection Reason**: directly violates the Unity 6 reference guidance.

### Alternative 3: No local prediction
- **Description**: wait for the server snapshot before moving the local player at all.
- **Pros**: simplest authority story, minimal reconciliation logic.
- **Cons**: noticeably worse responsiveness for a real-time route-racing game, especially in demos.
- **Estimated Effort**: Slightly lower code complexity, higher UX cost.
- **Rejection Reason**: the assignment still needs the game to feel responsive on keyboard + mouse.

## Consequences

### Positive
- Player input now has a dedicated ADR-level contract instead of only a prose note in the master architecture doc.
- Gameplay and UI bindings are separated cleanly.
- Local responsiveness improves without weakening server authority.
- Fire-intent and aim-direction semantics are explicit for transport and gameplay teams.

### Negative
- Input-map state switching and fire-edge buffering add a little runtime complexity.
- Prediction/reconciliation still needs real feel-tuning in desktop builds.
- Generated Input System assets/classes add one more Unity asset to keep in sync.

### Neutral
- This ADR fixes the runtime/input seam; it does not define gameplay values such as projectile cooldowns or slow durations, which remain governed elsewhere.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| Fire edge is missed between render frames and transport ticks | Medium | Medium | Latch the edge until it is serialized into one outgoing frame |
| Prediction drifts when a debuff arrives mid-move | Medium | Medium | Use last authoritative movement modifier and reconcile on every snapshot |
| UI/gameplay maps overlap and double-handle Enter/Escape | Low | Medium | Switch maps explicitly based on room/UI state and test all transitions |

## Performance Implications

- **CPU**: low; action reads and local prediction are lightweight compared to rendering/networking.
- **Memory**: low; one action asset, one local prediction buffer, one local adapter.
- **Load Time**: negligible; input actions load with the client scene/bootstrap.
- **Network**: small and bounded; one compact input frame per transport tick for the local player only.

## Migration Plan

1. Create the shared Input Actions asset and generated wrapper for gameplay + UI maps.
2. Implement one local `PlayerControllerInputAdapter` that caches device state and emits tick-aligned `InputFrame` payloads.
3. Bind the outgoing frames to ADR-0001 transport and add local prediction/reconciliation hooks.
4. Verify gameplay/menu transitions, fire-edge capture, and debuff correction feel in desktop builds.

**Rollback plan**: If the chosen action-map layout or prediction threshold proves unworkable, write a superseding ADR instead of mixing legacy input paths or bypassing the authority model ad hoc.

## Validation Criteria

- [ ] The client emits exactly one normalized `InputFrame` per transport tick for the local player while the room is active.
- [ ] Keyboard/mouse gameplay controls and menu confirm/back controls do not fight each other during screen transitions.
- [ ] Local movement remains responsive while authoritative correction can still override the client cleanly.
- [ ] Fire input is represented as an edge and cannot spam from a held button alone.
- [ ] No production gameplay code depends on legacy `Input.*` APIs.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/game-concept.md` | Core Loop | **TR-concept-004** — Players move in a top-down 2D arena and collect batteries | Defines the client-side movement/aim input contract that feeds the authoritative loop |
| `design/gdd/game-concept.md` | Control Scheme | **TR-concept-012** — Game is played with keyboard and mouse on PC | Locks keyboard + mouse bindings to explicit Gameplay/UI action maps |
| `design/gdd/systems-index.md` | Player Controller & Input | **TR-systems-003** — Dedicated player-control/input architecture exists for the Unity client | Turns the player-controller seam into an accepted ADR with runtime, transport, and prediction rules |

## Related

- `docs/architecture/adr-0001-network-authority-and-transport-strategy.md`
- `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md`
- `docs/architecture/adr-0004-runtime-ui-stack-and-screen-flow.md`
- `docs/architecture/architecture.md`
- `design/gdd/game-concept.md`
- `design/gdd/systems-index.md`
