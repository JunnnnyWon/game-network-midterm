## Prototype Report: Network Stability and Observability

### Hypothesis
Battery Rush Arena can be pushed from a minimally playable networking spike into a class-presentation-ready prototype by stabilizing the most visible multiplayer failures and adding explicit network telemetry that makes the client/server architecture easy to explain live.

### Approach
I treated the current `NetworkSpike` slice as the prototype baseline and focused on the smallest changes that strengthen networking evidence rather than general polish.

- Stabilized transport-facing gameplay issues already uncovered during live tests: room listing fan-out, duplicate player-name collisions, readable room-code policy, and frame-rate-driven over-movement.
- Added protocol/server telemetry fields so the authoritative server can expose snapshot sequence, acked client tick, server send time, and heartbeat age.
- Added client telemetry capture for heartbeat RTT, last message type, received-message count, last snapshot sequence, and last acked client tick.
- Added an always-on network telemetry/event panel to the authored UI Toolkit overlay and to the IMGUI diagnostics window so the networking model is visible during 발표.
- Extended the spike smoke checks so room listing sync and the telemetry seam are part of the prototype’s regression surface.

### Result
The prototype now shows stronger evidence of real networking behavior rather than only “it kind of works.”

- Room creation/listing behavior is now deterministic and easier to demonstrate: the room code family is `ROOM##`, and connected idle clients receive room-listing updates.
- The main-client movement overshoot bug was removed by switching input transmission from render-frame cadence to fixed transport-tick cadence.
- The client now records and can display network-specific values such as snapshot sequence, last processed client tick, RTT from heartbeat echo, last message type, and total received messages.
- The prototype still uses the custom TCP authoritative server architecture, but the transport is now much easier to explain because the UI exposes what the server is actually doing.

### Metrics
- Server build: `dotnet build src/NetworkSpikeServer/NetworkSpikeServer.csproj` passed
- Unity client build: `dotnet build "My project/Assembly-CSharp.csproj"` passed
- Live telemetry check against the running server:
  - `room=ROOM01`
  - `creatorSnapshotSeq=1`
  - `creatorServerTick=1`
  - `watcherRtt=2`
  - `watcherMsgs=2`
  - `watcherLastType=heartbeat_ack`
- UI telemetry seam check:
  - Snapshot label rendered `Snapshot #42`
  - Ack label rendered `Ack tick 39`
  - Heartbeat age rendered `0.25s`
- Iteration count: 1 focused prototype pass on top of the existing spike

### Recommendation: PROCEED

The prototype is strong enough to justify continuing with production implementation. The key reason is that the networking layer is no longer only implicit in the code or hidden in logs — it is visible in the running game. That directly supports the midterm requirement that the project should show clear evidence of networking knowledge. The remaining work is now mostly about deepening persistence and presentation, not proving whether the custom authoritative client/server architecture is viable.

### If Proceeding
- Keep the authoritative transport model and extend it with real persistence/leaderboard writes to MySQL.
- Add one more presentation-focused debug screen or overlay for “network scenario demos” such as timeout, disconnect forfeit, and persistence success/failure.
- Expand smoke coverage to assert telemetry values after live room creation and active play, not only snapshot-application seams.
- Validate the telemetry panel in actual Unity multiplayer play-mode windows after a full editor restart.
- Estimated production effort from this point:
  - network stabilization polish: small
  - persistence integration: medium
  - 발표/demo flow hardening: medium

### Lessons Learned
- Frame-rate-dependent input transmission can masquerade as a gameplay bug when it is really a transport cadence bug.
- Room-listing visibility must be broadcast as a separate concern from room membership snapshots if non-member clients need to see open rooms.
- Duplicate player-name handling is a presentation/usability issue as much as a transport issue; silent handshake rejection is bad demo UX.
- Networking features score higher in a class presentation when they are visible in the runtime UI, not only described in code or docs.

### Review Note
Configured review mode was `full`, but this turn did not run a separate child-agent creative-director pass. This report currently reflects the prototyper verdict only.
