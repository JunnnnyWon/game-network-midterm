# HUD, Results, and Ranking UI

> **Status**: Draft
> **Author**: Codex
> **Last Updated**: 2026-04-16
> **Last Verified**: 2026-04-16
> **Implements Pillar**: Pillar 1 — Instantly Readable Competition

## Summary

`HUD, Results, and Ranking UI` defines the player-facing screen flow and on-screen information hierarchy for Battery Rush Arena, from menu and lobby through HUD, results, and leaderboard. It exists so the player always knows what phase the match is in, what the score is, and what the server decided.

> **Quick reference** — Layer: `Presentation` · Priority: `MVP` · Key deps: `Match Lifecycle & Room State`, `Arena Battery Economy & Scoring`, `Results Persistence & Leaderboard`

## Overview

This system controls the full runtime UI experience. It uses a single root UI Toolkit document with panel-based screen switching and a dedicated HUD overlay during live play. The UI is responsible for visibility and interaction, not authority: it renders room state, scores, debuffs, persistence status, and leaderboard data exactly as the server provides them.

## Player Fantasy

The player should feel that the game is readable, competitive, and official. The HUD should make the score race obvious at a glance, the countdown should create anticipation, and the result screens should make the match feel like a legitimate recorded event rather than a rough prototype.

## Detailed Rules

### Core Rules

1. UI uses **Unity UI Toolkit** as the runtime UI stack.
2. One root `UIDocument` hosts the major screen panels plus the match HUD overlay.
3. UI never computes match outcomes locally.
4. UI only renders authoritative room, score, effect, persistence, and leaderboard data.
5. Keyboard/mouse are the only supported input methods in MVP.

### Screen Flow Rules

1. `MainMenuPanel`
   - player name entry
   - create/join room choice
2. `JoinRoomPanel`
   - enter room code or confirm room creation
3. `LobbyPanel`
   - room code
   - connected players
   - ready state
   - ready button / Enter shortcut
4. `CountdownPanel`
   - visible 3-2-1 start sequence
5. `MatchHudOverlay`
   - local score
   - opponent score
   - match timer
   - slow-shot cooldown
   - current debuff status
   - transient banners when needed
6. `ResultsPanel`
   - winner/loss/draw outcome
   - final scores
   - end reason
   - persistence status
7. `LeaderboardPanel`
   - top ranking rows
   - highlight current player if present
   - refresh button
   - back/rematch follow-through controls as appropriate

### HUD Rules

1. Local score is shown top-left.
2. Opponent score is shown top-right.
3. Match timer is shown top-center.
4. Cooldown state is shown bottom-right.
5. Debuff/trap status is shown bottom-center.
6. Match-point or persistence-failure banners may appear transiently near the upper-middle area.

### Results Rules

1. Results must show final score and explicit `MatchEndReason`.
2. Draw must be a visible first-class result state.
3. Persistence status must show at least:
   - saving
   - saved
   - save failed
4. The player must be able to understand whether leaderboard data reflects a committed result.

### Leaderboard Rules

1. Show top rows using the deterministic server-provided order.
2. Columns should include:
   - Rank
   - Player
   - Wins
   - Best Score
   - Matches
3. The leaderboard is a presentation of server data, not a local calculation.

### Input Rules

1. Enter acts as confirm/primary action in menu/lobby/results flow.
2. Escape acts as back/cancel where allowed.
3. Mouse click is the primary pointer interaction.
4. Gameplay input is disabled outside `Active`; UI input takes precedence.

## Formulas

### HUD Visibility Rule

```text
show_match_hud = (room_state == Active)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| room_state | enum | Lobby/Countdown/Active/etc. | authoritative room snapshot | Current room phase |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: During countdown, the dedicated countdown panel is used instead of the full live HUD.

### Leaderboard Row Count

```text
rows_shown = min(requested_rows, available_rows, 10)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| requested_rows | int | 1-10 | UI config | Desired number of leaderboard rows |
| available_rows | int | 0+ | server response | Actual number of available leaderboard entries |

**Expected output range**: 0-10 rows
**Edge case**: Empty leaderboard should still render a readable “no data yet” state.

### Result Banner Trigger

```text
show_persistence_failure_banner = (persistence_status == Failed)
```

| Variable | Type | Range | Source | Description |
|----------|------|-------|--------|-------------|
| persistence_status | enum | Saving/Saved/Failed | server persistence status | Current save outcome |

**Expected output range**: boolean (`true` / `false`)
**Edge case**: Failure banner appears without overriding the already-determined match result.

## Edge Cases

| Scenario | Expected Behavior | Rationale |
|----------|------------------|-----------|
| Match ends in a draw | Results panel explicitly says Draw and shows final tied scores | Draw is a real end state |
| Persistence fails | Results remain visible and failure is surfaced clearly | Visibility without blocking flow |
| Leaderboard has fewer than 10 rows | Show only available rows cleanly | Avoid empty padding confusion |
| Room disconnects before match start | Return to safe UI state with clear error | Transport errors must be readable |
| Debuff expires while banner is visible | Debuff widget updates independently from transient banners | Keep HUD information layered cleanly |
| Player presses Enter during gameplay | Should not trigger menu confirm behavior while gameplay map is active | Avoid control-map conflict |

## Dependencies

| System | Direction | Nature of Dependency |
|--------|-----------|---------------------|
| Match Lifecycle & Room State | This depends on authoritative state | Determines which screen/panel should be visible |
| Arena Battery Economy & Scoring | This depends on score state | Provides HUD score values and match-point context |
| Results Persistence & Leaderboard | This depends on persistence data | Provides save state and leaderboard rows |
| Player Controller & Input | Mutual interaction | UI mode and gameplay mode must switch cleanly |
| Audio Feedback | Audio depends on UI actions partly | Confirm/back and some visible state changes may drive UI-safe cues |
| `docs/architecture/adr-0004-runtime-ui-stack-and-screen-flow.md` | Design dependency | Governs full runtime UI architecture |

## Tuning Knobs

| Parameter | Current Value | Safe Range | Effect of Increase | Effect of Decrease |
|-----------|--------------|------------|-------------------|-------------------|
| Banner display duration | Short/transient | 1.0-4.0 s | More noticeable alerts | Less clutter but easier to miss |
| Leaderboard row count | 10 | 5-10 | More ranking context | Cleaner screen |
| HUD text scale | Standard readable | small-large | Better readability | More compact HUD |
| Debuff status emphasis | Medium | low-high | More obvious status change | Cleaner HUD, less clarity |

## Acceptance Criteria

- [ ] UI shows room, countdown, active, results, and leaderboard phases clearly.
- [ ] HUD displays score, timer, cooldown, and debuff status during live play.
- [ ] Results screen displays win/loss/draw plus explicit end reason.
- [ ] Persistence success/failure is visible without changing the match result itself.
- [ ] Leaderboard renders server-provided order rather than local calculation.
- [ ] UI behavior remains consistent with ADR-0004 and the authoritative architecture baseline.
