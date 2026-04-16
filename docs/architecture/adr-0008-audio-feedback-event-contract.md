# ADR-0008: Audio Feedback Event Contract

## Status

Accepted

## Date

2026-04-16

## Last Verified

2026-04-16

## Decision Makers

User, Codex (architecture synthesis)

## Summary

Battery Rush Arena will keep **Audio Feedback** as a **Vertical Slice presentation system**, but the architecture contract is now fixed so implementation does not drift. Audio cues will be driven by a **small presentation-safe event contract** on the Unity client, consuming authoritative match/persistence transitions plus a few local UI-only actions, and audio will never own or infer gameplay state.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3.10f1) |
| **Domain** | Audio |
| **Knowledge Risk** | MEDIUM — Unity 6.3 audio fundamentals are stable, but mixer/runtime behavior should still be verified in the pinned build |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/current-best-practices.md`, `docs/engine-reference/unity/modules/audio.md` |
| **Post-Cutoff APIs Used** | `AudioSource`, `AudioSource.PlayOneShot`, Audio Mixer groups, exposed mixer volume parameters |
| **Verification Required** | Verify cue deduplication, mixer routing, mute/volume behavior, and playback timing in Unity 6.3 desktop builds. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0002, ADR-0004, ADR-0005, ADR-0006 |
| **Enables** | Audio implementation, demo-polish pass, consistent feedback across UI/gameplay systems |
| **Blocks** | Audio Feedback implementation and any vertical-slice polish work that needs reliable cue semantics |
| **Ordering Note** | Match-state, score pacing, trap/slow rules, and runtime UI flow must already be fixed so audio can consume them as stable presentation events. |

## Context

### Problem Statement

The architecture review flagged `Audio Feedback` as a coverage gap: the systems index listed the system, but no ADR governed what should trigger sound, who owns the event contract, or whether audio was required for MVP gameplay. Without this decision, implementation could scatter `PlayOneShot` calls across gameplay/UI code or accidentally let audio infer hidden state from local client guesses.

### Current State

The concept mentions minimal-to-moderate audio needs (pickups, hits, victory, UI sounds), and `architecture.md` already describes audio as a presentation-only layer. What remained undecided was whether audio stays in Vertical Slice scope and how the Unity client receives sound triggers without becoming a second authority source.

### Constraints

- Audio must not affect gameplay authority, score, or match flow.
- The project scope is small: one arena, one major ability, one trap type, one results flow.
- Audio should support clarity and polish, not dominate the presentation.
- Unity runtime implementation should stay simple enough for a student project.
- Missing or muted audio must never block core gameplay.

### Requirements

- Must decide whether audio is MVP-critical or Vertical Slice-only.
- Must define which events are allowed to trigger cues.
- Must prevent duplicate or spammy playback from repeated snapshots.
- Must separate authoritative cues from purely local UI cosmetic sounds.
- Must keep the contract small, testable, and implementation-friendly.

## Decision

Audio Feedback remains a **Vertical Slice** system: it is **not required for MVP gameplay correctness**, but it is now **architecturally defined** so later implementation does not invent its own rules.

### Cue-source policy

Audio cues may come from exactly two sources:

1. **Authoritative presentation events** derived from server-owned state transitions or server-confirmed gameplay outcomes.
2. **Local UI-only events** that have no competitive meaning (for example button confirm/back sounds).

Audio may **not** be triggered from speculative local guesses about score, pickups, hit confirmation, trap outcomes, victory, or persistence completion.

### Approved cue set

The vertical-slice cue surface is intentionally small:

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

Rules:

- `SlowShotFireLocal` is the one allowed speculative gameplay cue because it acknowledges the local input action itself, not a competitive outcome.
- `BatteryPickupConfirmed`, `SlowShotHitConfirmed`, `TrapTriggered`, `DebuffApplied`, `MatchPointWarning`, and all match-end cues must come from authoritative snapshot/event transitions only.
- `PersistenceFailed` may be played only when ADR-0003 failure status is actually surfaced to the UI.

### Client-side routing contract

- A centralized Unity-side `AudioCueRouter` owns `AudioSource` playback and mixer routing.
- An `AudioCueTranslator` converts authoritative room/gameplay/persistence transitions into the approved cue enum.
- UI Toolkit screens may emit `UiConfirm` and `UiBack` directly.
- Audio cues are deduplicated by a stable cue key (for example `matchId + serverTick + cueType + subjectId`) so repeated snapshots do not replay one-shot sounds.

### Playback policy

- Use **2D one-shot playback** for MVP/Vertical Slice clarity; do not require spatial audio for the class demo.
- Route cues through mixer groups at minimum: `Master`, `SFX`, `UI`, `Ambience`.
- Missing audio clips degrade silently in release behavior and log warnings only in development builds.
- Muting audio or lowering mixer volume must not affect any gameplay/UI state transitions.

### Architecture

```text
Authoritative snapshot/event diffs ----\
                                        -> AudioCueTranslator -> AudioCueRouter -> AudioSource/Mixer
Local UI confirm/back actions ---------/

No gameplay system reads audio state back.
```

### Key Interfaces

```csharp
public enum AudioCueType {
    CountdownTick,
    MatchStart,
    BatteryPickupConfirmed,
    SlowShotFireLocal,
    SlowShotHitConfirmed,
    TrapTriggered,
    DebuffApplied,
    DebuffExpired,
    MatchPointWarning,
    MatchEndedWin,
    MatchEndedLoss,
    MatchEndedDraw,
    PersistenceFailed,
    UiConfirm,
    UiBack
}

public readonly record struct AudioCueEvent(
    AudioCueType Type,
    int ServerTick,
    string SubjectId,
    string MatchId);

public interface IAudioCueRouter {
    void Play(AudioCueEvent cueEvent);
    void SetSfxVolume(float linear01);
    void SetUiVolume(float linear01);
}
```

### Implementation Guidelines

- Keep all gameplay-confirmation cues behind an authoritative diff/transition translator; do not call `PlayOneShot` directly from match logic or network handlers.
- Keep `SlowShotFireLocal` and UI click sounds in a thin local presentation layer only.
- Prefer one centralized audio bootstrap/service over many scene-local ad hoc sources for this project scope.
- Start with a small cue library and clear volume balance instead of adding ambience or layered effects too early.
- Treat audio as optional polish: if time runs short, retain the contract and ship fewer clips rather than breaking the boundary model.

## Alternatives Considered

### Alternative 1: Ad hoc `PlayOneShot` calls from each gameplay/UI script
- **Description**: let every script trigger its own sound directly.
- **Pros**: quick to prototype, minimal upfront architecture.
- **Cons**: easy duplicate playback, inconsistent routing, hard to audit authority boundaries.
- **Estimated Effort**: Lower initial effort.
- **Rejection Reason**: the review specifically called for an audio-system architecture rather than scattered cue calls.

### Alternative 2: Server-emitted dedicated sound commands
- **Description**: send explicit network messages only for audio playback.
- **Pros**: very strict authority on cue timing.
- **Cons**: extra bandwidth/message surface for something already derivable from authoritative state, unnecessary complexity for this scope.
- **Estimated Effort**: Higher.
- **Rejection Reason**: existing authoritative events already provide enough information for presentation-safe audio cues.

### Alternative 3: Defer all audio until after MVP with no ADR
- **Description**: leave audio unspecified and revisit later.
- **Pros**: smallest immediate workload.
- **Cons**: preserves the review gap and invites inconsistent implementation when polish starts.
- **Estimated Effort**: Lowest short-term effort.
- **Rejection Reason**: the system stays Vertical Slice, but the contract still needs to exist now.

## Consequences

### Positive
- Audio stays clearly non-authoritative.
- The project now has explicit coverage for the `Audio Feedback` system without promoting it to MVP-critical gameplay.
- Cue spam and duplicate playback risks are reduced by design.
- UI/gameplay teams share one approved cue vocabulary.

### Negative
- Adds one more client-side translator/router service to maintain.
- Some satisfying local speculative audio (for example hit sounds before confirmation) is intentionally forbidden.
- Audio polish still depends on available time and asset quality.

### Neutral
- This ADR fixes event semantics and scope, not the creative content of individual clips.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| Repeated snapshots retrigger the same cue | Medium | Medium | Deduplicate by stable cue key before playback |
| Vertical Slice audio scope grows into MVP-critical work | Medium | Low | Keep cue list fixed and treat missing audio as non-blocking |
| Local fire cue feels misleading when a shot later misses | Low | Low | Reserve speculative playback to the fire action only and keep it subtle |

## Performance Implications

- **CPU**: very low; cue translation and one-shot playback are lightweight.
- **Memory**: low to moderate depending on clip import settings, but still small for this scope.
- **Load Time**: minor; SFX clips should use suitable import settings and preload only what is necessary.
- **Network**: none beyond the authoritative state/events already required for gameplay/UI.

## Migration Plan

1. Define the cue enum/event DTO and the authoritative transition points that may emit each cue.
2. Implement the Unity-side `AudioCueTranslator` + `AudioCueRouter` with mixer-group routing.
3. Wire local UI confirm/back and local fire-whoosh cues through the same router.
4. Verify deduplication, volume routing, and non-blocking muted/missing-audio behavior in desktop builds.

**Rollback plan**: If the vertical-slice cue set proves too large, reduce the clip set but keep the same event contract. If the routing model itself changes, write a superseding ADR rather than scattering direct calls.

## Validation Criteria

- [ ] Match, pickup, trap, debuff, and persistence-failure cues play only from approved event sources.
- [ ] UI confirm/back sounds work without implying gameplay authority.
- [ ] The same authoritative event cannot replay its one-shot sound repeatedly from duplicate snapshots.
- [ ] Muting or missing audio assets never blocks gameplay, UI flow, or persistence feedback.
- [ ] Audio remains explicitly classified as Vertical Slice polish rather than MVP-critical logic.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/systems-index.md` | Audio Feedback | **TR-systems-008** — Audio system architecture exists | Defines the allowed cue surface, trigger rules, ownership boundaries, and Vertical Slice scope for the audio system |

## Related

- `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md`
- `docs/architecture/adr-0003-persistence-boundary-and-leaderboard-formula.md`
- `docs/architecture/adr-0004-runtime-ui-stack-and-screen-flow.md`
- `docs/architecture/adr-0005-battery-spawn-and-score-pacing-model.md`
- `docs/architecture/adr-0006-slow-shot-and-trap-fairness-rules.md`
- `docs/architecture/architecture.md`
- `design/gdd/systems-index.md`
