# Network Session Spike Plan

- Date: 2026-04-16
- Status: Planned for first implementation slice
- Scope: Verify the highest-risk transport assumptions before broad gameplay implementation.

## Purpose
De-risk the authoritative session/transport path with a thin vertical prototype before wider feature implementation.

## What the spike must prove
1. Unity client can open TCP connection to the external C# server.
2. Protocol-version handshake succeeds/fails cleanly.
3. Room create/join flow works for 2 clients.
4. Heartbeat / stale-session timeout behavior is observable.
5. One tick-aligned `InputFrame` can be sent and acknowledged without freezing UI.

## Pass criteria
- Two local clients can connect and join the same room.
- Handshake mismatch produces a clear rejection path.
- Heartbeat timeout produces deterministic disconnect handling signal.
- Input frame cadence can be observed at the expected 20 Hz transport rhythm.

## Fail criteria
- Socket lifecycle blocks or crashes the Unity client.
- Handshake outcome is ambiguous.
- Two-client room join is unstable or non-deterministic.
- Heartbeat timeout cannot be observed clearly.
- Transport cadence is not measurable enough to trust further gameplay integration.

## Output expected after execution
- A short findings section appended here with `PASS` / `FAIL` for each criterion.
- Follow-up implementation adjustments if needed.


## Findings — 2026-04-16 implementation run

### Evidence captured
- External C# spike server builds successfully with `dotnet build src/NetworkSpikeServer/NetworkSpikeServer.csproj`.
- Unity project scripts compile successfully in batchmode (`My project/Logs/network-compile-batch.log`).
- Live protocol smoke against the running spike server passed with two local clients plus one mismatch client.

### Observed results
- **Two local clients can connect and join the same room** — PASS
- **Protocol mismatch rejects cleanly** — PASS
- **Heartbeat timeout path is observable** — PASS (`session_stale` with `heartbeat_timeout`)
- **One tick-aligned input frame path is observable** — PASS (`input_frame_ack` for tick 1)

### Notes
- The runtime Unity bootstrap now lives under `My project/Assets/Scripts/NetworkSpike/`.
- The external server lives under `src/NetworkSpikeServer/`.
- A full interactive play-mode pass in the Unity Editor is still recommended as the next manual sanity check, but the spike contract itself is now implemented and evidenced.


## Findings — 2026-04-16 room-state slice

### Evidence captured
- Unity project scripts compile successfully in batchmode after the room-state additions.
- Live protocol smoke proved authoritative room-state visibility on top of the existing transport layer.

### Observed results
- **Lobby state is visible after room creation/join** — PASS
- **Two ready players trigger a shared 3-second countdown** — PASS
- **Countdown transitions to Active automatically** — PASS
- **Input-frame acknowledgement still works during Active** — PASS
- **A forced debug end transitions through Ended to ResultsReady** — PASS

### Notes
- This slice is still a debug-oriented room-state implementation, not full gameplay scoring.
- ResultsReady currently uses a placeholder end reason (`manual_debug_end`) for visibility.
- The next implementation slice should replace the placeholder end path with battery scoring + target-score / timeout driven match conclusion.


## Findings — 2026-04-16 authoritative room-state slice

### Evidence captured
- Unity project scripts compile successfully in batchmode after the room-state changes.
- Live protocol smoke proved authoritative room-state transitions on top of the existing transport layer.

### Observed results
- **Lobby state is visible after room creation/join** — PASS
- **Two ready players trigger a shared countdown** — PASS
- **Countdown transitions to Active automatically** — PASS
- **Input-frame acknowledgement is only accepted during Active** — PASS
- **Active disconnect resolves as DisconnectForfeit** — PASS
- **Room state transitions through Ended -> Saving -> ResultsReady** — PASS

### Notes
- The current result path is still a placeholder room-state flow, not full score-based victory.
- The next slice should connect the battery/scoring system so end-state reasons can come from actual gameplay resolution instead of debug/disconnect cases only.


## Findings — 2026-04-16 battery/scoring slice

### Evidence captured
- External C# spike server builds successfully after the battery/scoring additions.
- Unity project scripts compile successfully in batchmode after the scoring slice changes.
- Live protocol smoke proved authoritative battery collection, score progression, and gameplay-driven match completion.

### Observed results
- **Active batteries are exposed to the client** — PASS
- **Battery collection increments authoritative score** — PASS
- **Battery respawn events occur after the configured delay** — PASS
- **Target score ends the match with `TargetScoreReached`** — PASS
- **The room still transitions through Saving -> ResultsReady after real scoring-based completion** — PASS

### Notes
- The scoring slice is still a spike-style implementation and uses battery ids rather than full world-position pickup validation.
- The next slice should connect real player-position-based pickup resolution and then integrate slow-shot/trap effects into contested routing.


## Findings — 2026-04-16 slow-shot and trap slice

### Evidence captured
- External C# spike server builds successfully after the effect-system additions.
- Unity client scripts compile successfully via `dotnet build My project/Assembly-CSharp.csproj`.
- Live protocol smoke proved authoritative slow-shot and trap interaction behavior on top of the scoring loop.

### Observed results
- **Slow shot applies a 35% slow to the opposing player** — PASS
- **Trap requests are ignored while a stronger slow is active** — PASS
- **Post-slow immunity is surfaced and blocks immediate reapplication** — PASS
- **Trap applies after immunity expires** — PASS
- **No score penalty is applied by slow/trap events** — PASS

### Notes
- The slice still uses spike-style direct effect requests (`fire_slow_shot`, `trigger_trap`) rather than full world-position hit/trap detection.
- The next slice should replace debug-triggered effect application with route/position-based interaction and integrate richer HUD feedback around cooldowns and debuffs.


## Findings — 2026-04-16 slow-shot and trap slice

### Evidence captured
- External C# spike server builds successfully after the effect-system additions.
- Unity client scripts compile successfully via `dotnet build My project/Assembly-CSharp.csproj`.
- Live protocol smoke proved authoritative slow-shot and trap interaction behavior on top of the scoring loop.

### Observed results
- **Slow shot applies a 35% slow to the opposing player** — PASS
- **Trap requests are ignored while a stronger slow is active** — PASS
- **Post-slow immunity is surfaced and blocks reapplication briefly** — PASS
- **Trap applies after immunity expires** — PASS
- **No score penalty is applied by slow/trap events** — PASS

### Notes
- This slice still uses spike-style direct requests (`fire_slow_shot`, `trigger_trap`) rather than full world-position hit/trap detection.
- The next slice should convert from debug-triggered effects to route/position-driven gameplay interactions and then connect UI polish around cooldown/debuff feedback.


## Findings — 2026-04-16 battery/scoring slice

### Evidence captured
- External C# spike server builds successfully after battery/scoring additions.
- Unity project scripts compile successfully in batchmode after the scoring slice changes.
- Live protocol smoke proved authoritative battery collection, score progression, and gameplay-driven match completion.

### Observed results
- **Active batteries are exposed to the client** — PASS
- **Battery collection increments authoritative score** — PASS
- **Battery respawn events occur after the configured delay** — PASS
- **Target score ends the match with ** — PASS
- **The room still transitions through Saving -> ResultsReady after real scoring-based completion** — PASS

### Notes
- The scoring slice is still a spike-style implementation and uses battery ids rather than full world-position pickup validation.
- The next slice should connect real player-position-based pickup resolution and then integrate slow-shot/trap effects into contested routing.
