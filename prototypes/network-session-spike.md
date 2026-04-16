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
