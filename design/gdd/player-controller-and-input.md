# Player Controller & Input

> **Status**: Reviewed
> **Author**: Codex
> **Last Updated**: 2026-04-16
> **Last Verified**: 2026-04-16
> **Implements Pillar**: Pillar 2 — Short Matches, Real Tension

## Summary

`Player Controller & Input` defines how the local player reads keyboard and mouse input, converts it into tick-aligned network-safe intents, and presents movement/aim feedback without taking authority away from the server. It exists so the game feels responsive on the player’s machine while still preserving fair, synchronized multiplayer outcomes.

> **Quick reference** — Layer: `Core` · Priority: `MVP` · Key deps: `Match Lifecycle & Room State`, `Network Session & Transport`

## Overview

This system owns the local player control surface: movement input, aiming, fire input, and the small amount of presentation-only local prediction that makes the arena feel responsive. It sits between the raw Unity Input System and the transport layer, ensuring that controls are deterministic, keyboard/mouse only, and cleanly split between gameplay and UI behavior. The player should feel sharp control, but never gain client-side authority over score, pickups, debuffs, or victory.

## Player Fantasy

The player should feel quick, precise, and fully in control of their own avatar without feeling like the game is lagging behind their hands. Moving through the arena should feel snappy, aiming should feel immediate, and pressing fire should feel deliberate. The emotional target is: “My character responds instantly to me, but the match still feels fair and official.”

## Detailed Rules

### Core Rules

1. **Input stack rules**
   - The MVP uses only **keyboard + mouse**.
   - The system must use the **Unity Input System**, not legacy `Input.*` APIs.
   - One shared generated Input Actions asset is the only approved gameplay input source.

2. **Action maps**
   - **Gameplay map**
     - `Move` → WASD / 2D vector
     - `AimScreenPosition` → current mouse position
     - `Fire` → left mouse button
   - **UI map**
     - `Confirm` → Enter
     - `Back` → Escape
     - `Point` → mouse position
     - `Click` → left mouse button
   - Gameplay map is active only when the room is in `Active`.
   - UI map is active during menu, lobby, results, and leaderboard flow.

3. **Local-player ownership**
   - Only the local player instance reads live input devices.
   - Remote player avatars never run input collection; they only render authoritative snapshots.
   - The system may not create a second authoritative player object on the client.

4. **Movement rules**
   - Movement input is read as a 2D vector.
   - Diagonal movement must be normalized so diagonal travel is not faster than cardinal travel.
   - Movement intent is represented only as direction input, not as final position claims.
   - If the room is not `Active`, gameplay movement intent must not be sent.

5. **Aim rules**
   - Aim is derived from mouse position relative to the local player anchor.
   - Aim direction is normalized before being packed into an outgoing `InputFrame`.
   - If the mouse produces a zero-length aim vector, the system reuses the last valid non-zero aim direction.

6. **Fire rules**
   - Fire input is treated as an **edge**, not continuous held auto-fire.
   - When fire is pressed, the edge is latched until included in exactly one outgoing frame.
   - Holding the button without a fresh edge must not create repeated fire intents by itself.

7. **Input-frame packaging rules**
   - The system builds one `InputFrame` per local prediction/network tick while the room is `Active`.
   - Each frame contains:
     - `Tick`
     - `MoveX`
     - `MoveY`
     - `AimX`
     - `AimY`
     - `FirePressed`
   - The controller system never adds score, trap, hit, debuff, or pickup claims to its payload.

8. **Prediction rules**
   - The client may predict only presentation-facing local behavior:
     - local avatar locomotion feel
     - camera follow behavior
     - local crosshair feedback
   - The client may not predict authoritative score gain, pickup success, debuff expiration, hit confirmation, or match results beyond the last server-confirmed state.
   - Small divergence from authoritative movement may be smoothed; large divergence may be snapped based on tuning thresholds.

9. **Debuff interaction rules**
   - Predicted movement must use the latest authoritative movement modifier known to the client.
   - If a trap/slow effect is received from the server, predicted movement must reconcile to that new multiplier on the next update.
   - The player controller does not decide debuff timing; it only consumes the latest authoritative effect state.

10. **Camera/control feel rules**
    - Camera should follow the local player smoothly enough to preserve readability but not lag so much that aiming becomes imprecise.
    - Crosshair/aim feedback should respond immediately to mouse movement.
    - Movement should feel arcade-responsive rather than simulation-heavy.

11. **Lifecycle rules**
    - During `Lobby` and `Countdown`, the system may still render orientation/cursor locally, but gameplay movement/fire payloads must remain disabled.
    - During `ResultsReady`, the UI map takes precedence and gameplay actions are disabled.

12. **Authority boundary rules**
    - Final position, movement-affecting debuffs, score, trap outcomes, hit confirmation, and match state remain server authoritative.
    - The player controller is allowed to make the local avatar feel responsive, not to decide competitive truth.

### States and Transitions

| State | Entry Condition | Exit Condition | Behavior |
|-------|-----------------|----------------|----------|
| InputIdle | App not yet in active gameplay or local player not instantiated | Local player becomes controllable | No gameplay input frames emitted |
| UiOnly | Room state is Menu/Lobby/Countdown/Results flow | Room enters `Active` | UI action map active; gameplay map disabled |
| GameplayActive | Room state is `Active` and local player exists | Room leaves `Active` | Gameplay map active; emit one `InputFrame` per tick |
| Reconciling | Authoritative correction arrives while gameplay is active | Correction applied | Smooth or snap toward authoritative state within tuning rules |
| Disabled | Local player lost ownership, disconnected, or scene/session ended | Local ownership restored or new session starts | No local device input consumed for gameplay |

### Interactions with Other Systems

| System | Direction | Nature of Interaction |
|--------|-----------|-----------------------|
| Match Lifecycle & Room State | This system depends on it | Determines when gameplay actions are active vs UI-only |
| Network Session & Transport | This system feeds it | Sends tick-aligned `InputFrame` payloads and UI intents |
| Slow Shot & Trap Interaction | This system is constrained by it | Consumes authoritative debuff state; fire edges request slow-shot use |
| HUD, Results, and Ranking UI | UI depends on this system | UI map provides confirm/back/click behavior and room-state-appropriate control switching |
| Audio Feedback | Audio depends partly on this system | May play local fire cue or UI confirm/back cue, but not authoritative result cues |

## Formulas

### Diagonal Movement Normalization

```text
if length(raw_move_vector) > 1.0:
    normalized_move = raw_move_vector / length(raw_move_vector)
else:
    normalized_move = raw_move_vector
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| raw_move_vector | Vector2 | each axis -1.0 to 1.0 | gameplay action map | Raw local movement input before normalization |
| normalized_move | Vector2 | vector length 0.0 to 1.0 | controller runtime | Safe outgoing movement vector |

**Expected output range**: vector length `<= 1.0`
**Edge case**: Zero vector remains zero and does not need previous-direction fallback.

### Tick-Aligned Input Emission

```text
emit_input_frame = (room_state == Active) AND (local_player_owned == true)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| room_state | enum | Lobby/Countdown/Active/etc. | authoritative room snapshot | Current shared room phase |
| local_player_owned | bool | true/false | client player binding | Whether this client owns the local controllable avatar |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: During `Countdown`, the system may still update visuals locally but must not emit gameplay frames.

### Fire Edge Latch

```text
outgoing_fire_pressed = fire_pressed_since_last_sent_frame ? true : false
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| fire_pressed_since_last_sent_frame | bool | true/false | local edge latch | Whether a fresh fire press occurred before the next send tick |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: Held input without a new press must become false after the first emitted fire edge.

### Aim Fallback Rule

```text
if length(current_aim_vector) == 0:
    outgoing_aim = last_valid_non_zero_aim
else:
    outgoing_aim = normalize(current_aim_vector)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| current_aim_vector | Vector2 | any finite vector | mouse position vs player anchor | Current derived aim direction |
| last_valid_non_zero_aim | Vector2 | normalized vector | previous controller state | Last meaningful aim direction |

**Expected output range**: normalized aim vector
**Edge case**: On first frame before any valid aim exists, default to a safe forward/right-facing fallback chosen by implementation.

## Edge Cases

| Scenario | Expected Behavior | Rationale |
|----------|------------------|-----------|
| Player presses movement diagonally | Vector is normalized before send | Prevent faster diagonal movement |
| Mouse sits exactly on player anchor | Use last valid aim direction | Avoid zero-direction fire ambiguity |
| Fire button is held down | Only the initial edge becomes `FirePressed` until released and re-pressed | Prevent accidental auto-fire drift |
| Room leaves `Active` during a held movement | Gameplay input emission stops immediately | Room state controls authority on input flow |
| Client receives strong correction after local prediction | Smooth small differences, snap large ones per tuning threshold | Preserve responsiveness without hiding truth |
| Debuff arrives while player is moving locally | Prediction reuses latest authoritative movement modifier on next update | Local feel must remain aligned with server truth |
| Local player loses ownership or disconnects | Controller enters `Disabled`/`InputIdle` and stops emitting gameplay frames | Prevent stale ghost control |
| UI menu is open | UI map takes priority; gameplay actions do not fire | Avoid menu/gameplay input overlap |

## Dependencies

| System | Direction | Nature of Dependency |
|--------|-----------|---------------------|
| Match Lifecycle & Room State | This depends on room state | Determines whether gameplay or UI control mode is active |
| Network Session & Transport | Other system depends on this | Consumes `InputFrame` payloads and UI intents from this controller layer |
| Slow Shot & Trap Interaction | This depends on effect state | Needs latest authoritative movement modifiers for prediction feel |
| HUD, Results, and Ranking UI | Mutual interaction | UI map drives confirm/back/click while room state controls control-mode switching |
| `docs/architecture/adr-0007-player-controller-and-input-runtime-contract.md` | Design dependency | Governs the full runtime/input contract |
| `docs/architecture/adr-0001-network-authority-and-transport-strategy.md` | Design dependency | Governs cadence, authority, and transport message shape |
| `docs/architecture/adr-0004-runtime-ui-stack-and-screen-flow.md` | Design dependency | Governs UI/gameplay control split |
| `docs/architecture/adr-0006-slow-shot-and-trap-fairness-rules.md` | Design dependency | Governs fire input meaning and debuff consumption constraints |

## Tuning Knobs

| Parameter | Current Value | Safe Range | Effect of Increase | Effect of Decrease |
|-----------|--------------|------------|-------------------|-------------------|
| Prediction smoothing strength | Medium | Low-High | Smoother corrections, more client-side softness | Tighter authority feel, more visible snaps |
| Snap correction threshold | Implementation-defined | small-medium distance | Fewer visible snaps, more drift | More authoritative feel, harsher corrections |
| Camera follow smoothing | Medium | Low-High | Smoother camera, less jitter | Tighter camera, possibly harsher feel |
| Aim reticle sensitivity | 1.0x | 0.8x-1.2x equivalent | Faster aim feel | Slower/more controlled aim feel |
| Fire edge buffer window | 1 transport tick | 1-2 ticks | More forgiving fire capture | More exact timing, higher miss risk |

## Acceptance Criteria

- [ ] Gameplay control uses the Unity Input System rather than legacy `Input.*` APIs.
- [ ] Gameplay and UI action maps do not fight each other during room-state transitions.
- [ ] The local player emits exactly one tick-aligned `InputFrame` per active gameplay tick.
- [ ] Diagonal movement does not exceed the speed of cardinal movement.
- [ ] Fire input is represented as a latched edge, not an implicit auto-fire hold.
- [ ] The system can preserve a valid aim direction when the mouse is directly over the player anchor.
- [ ] Prediction improves local responsiveness without inventing score, pickup, or hit outcomes.
- [ ] Authoritative corrections and debuff state can override local presentation cleanly.
- [ ] All rules remain consistent with ADR-0007, ADR-0001, ADR-0004, and ADR-0006.
