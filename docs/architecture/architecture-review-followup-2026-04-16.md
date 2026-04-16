# Architecture Review Follow-up Checklist

Date: 2026-04-16
Source review: `docs/architecture/architecture-review-2026-04-16.md`
Verification lane: worker-2 documentation-only consistency pass

## Purpose

This note captures the exact follow-up changes required to close the current
`CONCERNS` verdict without overlapping direct ADR authoring work. It is a
consistency checklist against the review report, `architecture-traceability.md`,
`docs/registry/architecture.yaml`, and the currently accepted ADR set.

## Baseline Constraints Confirmed

- Accepted ADRs currently stop at `ADR-0006`.
- `docs/architecture/architecture.md` still claims architecture-critical ADR coverage is complete.
- `docs/architecture/architecture-traceability.md` still shows:
  - `TR-systems-003` as `⚠️ Partial`
  - `TR-systems-008` as `❌ Gap`
- `design/gdd/systems-index.md` still classifies `Network Session & Transport` as `Core` in the enumeration table while the dependency map treats it as `Foundation`.
- `docs/registry/architecture.yaml` currently has no registered input-runtime or audio-feedback contract entries.

## Required Follow-up After the New ADRs Land

### 1. Player Controller & Input ADR

Expected outcome:
- Adds dedicated ADR coverage for `TR-systems-003`.
- Keeps existing authority boundaries intact:
  - no client-authoritative competitive state
  - new Input System only
  - UI remains presentation-only

Checklist:
- [ ] New ADR uses the full ADR template, including `Engine Compatibility`, `ADR Dependencies`, and `GDD Requirements Addressed`.
- [ ] The ADR explicitly addresses `TR-systems-003` from `design/gdd/systems-index.md`.
- [ ] The ADR does not contradict ADR-0001's client/server authority split.
- [ ] If new registry contracts are introduced, they are limited to input/runtime ownership or interfaces and do not redefine existing competitive-state ownership.

### 2. Audio Feedback ADR / Event Contract

Expected outcome:
- Adds ADR coverage for `TR-systems-008`.
- Keeps audio as a presentation-only consumer of authoritative events.

Checklist:
- [ ] New ADR explicitly addresses `TR-systems-008` from `design/gdd/systems-index.md`.
- [ ] The ADR keeps audio non-authoritative and compatible with `ui_derived_match_outcome` / `client_authoritative_competitive_state` prohibitions.
- [ ] The ADR defines the safe event sources for audio cues (match state, battery pickup confirmation, hit/debuff confirmation, persistence/result cues).
- [ ] The ADR does not create hidden-state or gameplay-critical audio dependencies without visual equivalents.

### 3. `docs/architecture/architecture.md`

The master architecture doc should only be updated once the new ADR filenames and decisions are final.

Required edits:
- [ ] Add the new ADR filenames to `ADRs Referenced`.
- [ ] Remove the claim that all architecture-critical ADRs are already written until the new ADR references are actually present.
- [ ] Update `MatchEndReason` to include `Draw`, matching ADR-0002 and ADR-0004.
- [ ] Resolve the naming drift between `Match Persistence Gateway` and the systems-index system `Results Persistence & Leaderboard`.
  - Preferred: use the systems-index name as the canonical module name and describe the gateway as an internal responsibility of that module.
- [ ] Keep the `Player Controller & Input` and `Audio Feedback` module descriptions aligned with the new ADR wording.

### 4. `design/gdd/systems-index.md`

Required edits:
- [ ] Resolve the `Network Session & Transport` layering inconsistency so the enumeration table and dependency map use the same classification.
- [ ] If `Foundation` remains the intended layer, update the categories/wording so the table no longer says `Core` for that system.
- [ ] Preserve current MVP / Vertical Slice priority boundaries while syncing any architecture wording updates.

### 5. `docs/architecture/architecture-traceability.md`

Update only after both new ADRs exist.

Required edits:
- [ ] Move `TR-systems-003` from `⚠️ Partial` to `✅ Covered` and cite the new Player Controller ADR.
- [ ] Move `TR-systems-008` from `❌ Gap` to `✅ Covered` and cite the new Audio ADR.
- [ ] Update `Coverage Summary` from `20 / 1 / 1` to `22 / 0 / 0` if no other gaps are introduced.
- [ ] Remove the items under `Known Gaps` that are resolved by the new ADRs.
- [ ] Add both new ADRs to the reverse index table with their covered requirements.

### 6. `docs/registry/architecture.yaml`

The registry only needs updates if the new ADRs establish reusable cross-system constraints.

Confirm whether the ADRs introduce any of the following:
- [ ] an input/runtime interface contract other modules must obey
- [ ] an audio event-delivery contract shared across gameplay/UI/presentation modules
- [ ] a new forbidden pattern (for example, gameplay logic triggered from local-only audio state)

If none of the above become reusable constraints, do **not** add speculative registry entries.

## Merge / Review Order

Recommended order for safe document synchronization:
1. Finalize the new ADR files.
2. Update `architecture.md` wording to reference the accepted ADRs.
3. Update `systems-index.md` only for layer/name consistency.
4. Refresh `architecture-traceability.md` counts and reverse index.
5. Update `docs/registry/architecture.yaml` only if the ADRs establish reusable cross-system rules.
6. Re-run `/architecture-review` or an equivalent consistency pass.

## Verification Targets for the Final Sync

The final synchronized docs should satisfy all of the following:
- [ ] `architecture.md`, `systems-index.md`, and `architecture-traceability.md` all agree that Player Controller & Input and Audio Feedback now have ADR coverage.
- [ ] `architecture.md` no longer omits `Draw` from `MatchEndReason`.
- [ ] `systems-index.md` no longer disagrees with the dependency map about transport layering.
- [ ] No document claims complete coverage until the traceability counts actually reflect it.
- [ ] No new contradiction is introduced against ADR-0001 through ADR-0006 or `docs/registry/architecture.yaml`.
