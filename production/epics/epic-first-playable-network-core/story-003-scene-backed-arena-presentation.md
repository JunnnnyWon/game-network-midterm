# Story 003 — Scene-Backed Arena Presentation

Status: Complete
Type: Gameplay presentation slice
Epic: production/epics/epic-first-playable-network-core/EPIC.md

## Goal
Replace the spike's abstract arena-preview dependency with simple scene-backed authoritative player/battery/trap presentation so the first playable network core feels closer to a game than a debug dashboard.

## Acceptance Criteria
- The Unity spike renders players, active batteries, and trap zones as scene/world presentation rather than only as an IMGUI mini-preview.
- Local camera or viewport framing keeps the authoritative play space readable during Active play.
- Existing authoritative HUD/results/cooldown feedback remains visible and correct.
- Existing authoritative gameplay behavior remains verified.
- Findings are appended to `prototypes/network-session-spike.md`.

## Verification Evidence
- `dotnet build src/NetworkSpikeServer/NetworkSpikeServer.csproj`
- `dotnet build "My project/Assembly-CSharp.csproj"`
- `NetworkSpikeBatchSmoke` now includes a scene-presentation assertion seam through `ApplyAuthoritativeSnapshotForTesting(...)`
- Live protocol smoke still proves create/join/ready, authoritative Active positions, trap/effect payloads, and cooldown-after-fire behavior after the scene-backed presentation changes
