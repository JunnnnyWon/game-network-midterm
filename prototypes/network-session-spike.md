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


## Findings — 2026-04-17 position-driven interaction slice

### Evidence captured
- External C# spike server builds successfully after authoritative player-position tracking was added.
- Unity client scripts compile successfully via `dotnet build My project/Assembly-CSharp.csproj`.
- Live protocol smoke proved that movement/input now drives position snapshots, battery pickup, trap trigger, and slow-shot targeting without relying on direct collect/trigger requests.

### Observed results
- **Active-room snapshots expose authoritative player positions** — PASS
- **Moving into a battery pickup radius increments score without a `collect_battery` request** — PASS
- **Moving into a trap region applies the trap slow automatically** — PASS
- **Input-frame fire with aim data applies slow shot to an in-range target** — PASS

### Notes
- The authoritative path now depends on movement/input frames rather than the old direct collect/trap/fire requests, and the server rejects those legacy gameplay messages.
- The next slice should convert these position snapshots into proper gameplay visuals so the client is no longer a pure debug HUD.


## Findings — 2026-04-17 visual presentation slice

### Evidence captured
- Unity 6.3 batch compile completed successfully after the Unity-side arena preview/HUD changes (`My project/Logs/network-compile-batch.log`).
- The Unity network spike runtime now derives player markers, active battery markers, trap zones, score/status summaries, countdown, and results overlays directly from authoritative room snapshots in `NetworkSpikeApp`.
- The batch smoke assertions now validate the presentation-driving payloads as well: player positions, scoreboard rows, debuff/effect feed, saving status, and final results payloads.

### Observed results
- **Authoritative room snapshots now drive a readable arena preview** — PASS
- **Players, active batteries, trap zones, and result states are rendered without giving the client gameplay authority** — PASS
- **Saving/results payloads expose persistence status and final score state for the client presentation** — PASS
- **The spike remains runnable without extra scene wiring** — PASS

### Notes
- This slice intentionally keeps the presentation lightweight inside the existing runtime debug shell rather than introducing a full UI Toolkit screen stack yet.
- A manual in-Editor sanity pass is still recommended to tune placement/readability, but the compile-verified presentation seam is now in place for the spike demo.


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


## Findings — 2026-04-17 cooldown/debuff HUD slice

### Evidence captured
- External C# spike server builds successfully after the localized cooldown snapshot payload was added.
- Unity client scripts compile successfully via `dotnet build My project/Assembly-CSharp.csproj`.
- Unity batch smoke now asserts both readiness and spent-cooldown payloads alongside the existing effect/immunity checks.
- The IMGUI spike HUD now surfaces a dedicated local ability panel for slow-shot readiness plus debuff/immunity state without introducing client authority.

### Observed results
- **Room snapshots expose authoritative local slow-shot readiness/cooldown state** — PASS
- **The spike HUD shows readable slow-shot readiness/cooldown feedback during Active play** — PASS
- **Cooldown snapshots keep refreshing while the room stays Active** — PASS
- **Debuff and immunity timing are surfaced in a dedicated HUD treatment rather than only the raw log/effect string** — PASS
- **Existing scoring/effect/persistence smoke coverage still passes with the HUD additions** — PASS

### Notes
- The cooldown payload is still intentionally local-only per snapshot (`SessionId`-scoped) so opponents do not gain extra remote-state authority beyond the existing effect feed.
- The current HUD remains IMGUI-based for spike speed; a later production UI pass can replace the pills/cards with the project’s real screen framework without changing the authoritative payload contract.
