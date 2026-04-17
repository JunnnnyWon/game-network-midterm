# Story 006 — Authored UI Toolkit Assets

Status: Complete
Type: Gameplay/UI slice
Epic: production/epics/epic-first-playable-network-core/EPIC.md

## Goal
Replace the spike's runtime-built UI Toolkit scaffolding with authored UXML/USS assets so the player-facing flow rests on a real UI asset structure rather than code-only construction.

## Acceptance Criteria
- The spike loads its core UI Toolkit layout from authored UXML/USS assets.
- Pre-match, Active HUD, and results presentation still reflect authoritative snapshot data correctly.
- Existing scene-backed world presentation remains intact.
- Existing gameplay behavior remains verified.
- Findings are appended to `prototypes/network-session-spike.md`.

## Verification Evidence
- `dotnet build src/NetworkSpikeServer/NetworkSpikeServer.csproj`
- `dotnet build "My project/Assembly-CSharp.csproj"`
- Authored runtime assets added under `My project/Assets/Resources/NetworkSpikeUI/`
- `NetworkSpikeApp` now loads the overlay from the authored resource path `NetworkSpikeUI/NetworkSpikeOverlay`
- `NetworkSpikeBatchSmoke` now asserts that pre-match, active HUD, and results overlays report `ToolkitUsesAuthoredAssetsForTesting == true`
