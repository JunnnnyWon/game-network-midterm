# Audio Feedback

> **Status**: Draft
> **Author**: Codex
> **Last Updated**: 2026-04-16
> **Last Verified**: 2026-04-16
> **Implements Pillar**: Pillar 1 — Instantly Readable Competition

## Summary

`Audio Feedback` defines the approved sound cue vocabulary and playback boundaries for Battery Rush Arena’s vertical-slice polish layer. It exists so sound supports clarity and impact without becoming a hidden authority source or a blocker for MVP gameplay.

> **Quick reference** — Layer: `Presentation` · Priority: `Vertical Slice` · Key deps: `Match Lifecycle & Room State`, `Slow Shot & Trap Interaction`

## Overview

This system handles one-shot cues and lightweight audio routing for gameplay-confirmed events and local UI-only interactions. It is intentionally a polish layer rather than a gameplay-critical dependency. Sound should reinforce what the player can already see, not replace or contradict visual state.

## Player Fantasy

The player should feel that the arena is alive and reactive: countdown ticks build anticipation, pickups feel rewarding, hits feel impactful, and results feel official. Audio should make the match feel sharper and more polished without ever becoming mandatory for understanding the rules.

## Detailed Rules

### Core Rules

1. Audio is **Vertical Slice**, not MVP-critical gameplay logic.
2. Audio may come from only two source categories:
   - authoritative presentation events
   - local UI-only confirm/back actions
3. Audio never owns game state.
4. Missing or muted audio must never break gameplay or flow.

### Approved Cue Set

The approved cue vocabulary is:
- `CountdownTick`
- `MatchStart`
- `BatteryPickupConfirmed`
- `SlowShotFireLocal`
- `SlowShotHitConfirmed`
- `TrapTriggered`
- `DebuffApplied`
- `DebuffExpired`
- `MatchPointWarning`
- `MatchEndedWin`
- `MatchEndedLoss`
- `MatchEndedDraw`
- `PersistenceFailed`
- `UiConfirm`
- `UiBack`

### Source Rules

1. `SlowShotFireLocal` may be played from the local input action itself.
2. `UiConfirm` and `UiBack` may be played from local UI actions.
3. All other gameplay/result cues must come from authoritative transitions/events only.
4. Audio must not infer victory, hit success, pickup success, or save success from speculative local state.

### Routing Rules

1. One centralized audio cue router owns one-shot playback behavior.
2. Cue translation happens from authoritative room/gameplay/persistence transitions into approved cue events.
3. Audio should route through at minimum these mixer groups:
   - Master
   - SFX
   - UI
   - Ambience
4. MVP/Vertical Slice default playback is 2D one-shot clarity, not spatial audio.

### Deduplication Rules

1. The same authoritative event should not replay the same one-shot repeatedly from duplicate snapshots.
2. One stable cue key should be enough to deduplicate playback (for example `matchId + serverTick + cueType + subjectId`).
3. Duplicate snapshot delivery must not create sound spam.

### Scope Rules

1. Audio may be implemented after the MVP loop works.
2. If time is short, the event contract remains valid even if only a subset of clips is actually shipped.
3. Reducing clip count is acceptable; breaking authority boundaries is not.

## Formulas

### Cue Deduplication Key

```text
cue_key = match_id + ":" + server_tick + ":" + cue_type + ":" + subject_id
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| match_id | string | non-empty | authoritative match context | Match identity |
| server_tick | int | 0+ | authoritative server timeline | Tick at which the cue became valid |
| cue_type | enum | approved cue set | audio cue translator | Type of cue to play |
| subject_id | string | optional/non-empty | gameplay/UI context | Specific player/object if needed |

**Expected output range**: stable unique key per cue event
**Edge case**: UI-only local cues may use a local synthetic subject key rather than a server object id.

### Vertical Slice Availability

```text
audio_required_for_mvp = false
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| audio_required_for_mvp | bool | true/false | production scope | Whether gameplay correctness depends on audio |

**Expected output range**: false for MVP
**Edge case**: Even when audio is absent, all corresponding visual gameplay feedback must remain intact.

### Cue Playback Rule

```text
play_cue = (cue_is_approved == true) AND (cue_not_recently_deduplicated == true)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| cue_is_approved | bool | true/false | audio router | Whether the cue type is in the approved set |
| cue_not_recently_deduplicated | bool | true/false | cue history tracker | Whether this exact event has already played |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: Local UI cues are still subject to duplicate suppression if the same action fires repeatedly too fast.

## Edge Cases

| Scenario | Expected Behavior | Rationale |
|----------|------------------|-----------|
| Duplicate snapshot arrives | Same one-shot cue does not replay | Prevent sound spam |
| Audio clip is missing | Gameplay continues; development may log warning | Audio must be non-blocking |
| Player mutes audio | UI/gameplay flow remains unchanged | Audio is presentation-only |
| Slow shot is fired but misses | Local fire cue may still play; hit cue does not | Separate local action from authoritative hit confirmation |
| Persistence fails | Failure cue may play only when failure is actually surfaced | Preserve trustworthiness of feedback |
| No audio is implemented before MVP demo | Game remains playable and readable | Vertical Slice scope, not MVP-critical |

## Dependencies

| System | Direction | Nature of Dependency |
|--------|-----------|---------------------|
| Match Lifecycle & Room State | This depends on room transitions | Countdown, match start/end, draw, and failure cues follow state changes |
| Slow Shot & Trap Interaction | This depends on effect events | Hit, trap, and debuff cues consume authoritative effect transitions |
| HUD, Results, and Ranking UI | Mutual interaction | UI confirm/back actions may trigger local cues; visible failure state should match audio cue |
| `docs/architecture/adr-0008-audio-feedback-event-contract.md` | Design dependency | Governs cue scope, routing, and authority boundaries |

## Tuning Knobs

| Parameter | Current Value | Safe Range | Effect of Increase | Effect of Decrease |
|-----------|--------------|------------|-------------------|-------------------|
| SFX volume | Standard | mute-loud | Stronger impact | Softer cues |
| UI volume | Standard | mute-loud | Stronger menu feedback | Softer UI feel |
| Cue set size | Minimal approved set | minimal-moderate | Richer polish | Simpler scope |
| Deduplication memory window | Event-key based | short-medium | Fewer accidental repeats | More risk of replay spam |

## Acceptance Criteria

- [ ] Gameplay-confirmation cues play only from approved authoritative event sources.
- [ ] UI confirm/back sounds remain local UI-only cues.
- [ ] Duplicate snapshots do not replay the same one-shot sound repeatedly.
- [ ] Missing or muted audio never blocks gameplay, persistence flow, or results visibility.
- [ ] Audio remains a Vertical Slice polish system rather than an MVP-critical dependency.
- [ ] The audio design remains consistent with ADR-0008.
