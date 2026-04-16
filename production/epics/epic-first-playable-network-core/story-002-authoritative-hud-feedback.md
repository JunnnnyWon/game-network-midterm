# Story 002 — Authoritative HUD Feedback

Status: Complete
Type: Gameplay/UI spike slice
Epic: production/epics/epic-first-playable-network-core/EPIC.md

## Goal
Add authoritative cooldown and debuff/immunity HUD feedback so the network spike is easier to play without relying on debug-text interpretation alone.

## Acceptance Criteria
- Server snapshots expose slow-shot cooldown state for the local player.
- The existing IMGUI spike HUD shows cooldown readiness/remaining time clearly during Active play.
- Debuff and immunity duration are surfaced in a clearer HUD treatment than text-only logs.
- Existing authoritative scoring/effect behavior remains verified.
- Findings are appended to `prototypes/network-session-spike.md`.

## Verification Evidence
- `dotnet build src/NetworkSpikeServer/NetworkSpikeServer.csproj`
- `dotnet build "My project/Assembly-CSharp.csproj"`
- Unity batch smoke (`NetworkSpikeBatchSmoke`) covering cooldown-ready, cooldown-refresh, cooldown-spent payload assertions plus the prior effect/scoring/results checks
