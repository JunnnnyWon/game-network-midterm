# Story 007 — Canonical UI Toolkit Flow

Status: Complete
Type: Gameplay/UI slice
Epic: production/epics/epic-first-playable-network-core/EPIC.md

## Goal
Make UI Toolkit the canonical player-facing runtime flow for the spike and reduce the remaining IMGUI shell to minimal debug diagnostics.

## Acceptance Criteria
- The normal pre-match, active, and results flow can be used through UI Toolkit without relying on the IMGUI shell.
- The IMGUI layer is reduced to a bounded debug/diagnostic surface only.
- Existing authoritative scene/HUD/results behavior remains verified.
- Findings are appended to `prototypes/network-session-spike.md`.

## Verification Evidence
- `dotnet build src/NetworkSpikeServer/NetworkSpikeServer.csproj`
- `dotnet build "My project/Assembly-CSharp.csproj"`
- `NetworkSpikeApp` now keeps IMGUI as a diagnostics-only surface while the canonical player-facing flow remains in UI Toolkit
- Existing authored UI Toolkit, scene-backed arena, and results flow stay intact after the IMGUI reduction
