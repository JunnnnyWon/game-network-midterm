---
name: create-architecture
description: Produce the master architecture document for the Codex-native studio workflow
argument-hint: "[no arguments]"
---

# Create Architecture

Build the master architecture document from engine constraints, GDD requirements, and existing ADRs.

## Required reads
- engine reference docs for the configured engine/version
- `design/gdd/game-concept.md`
- `design/gdd/systems-index.md`
- every GDD in `design/gdd/`
- `.claude/docs/technical-preferences.md`
- existing files in `docs/architecture/`

## Phases
1. Load context + build a technical requirements baseline.
2. Map systems into architecture layers.
3. Define module ownership.
4. Define data flow.
5. Define API boundaries.
6. Audit ADR coverage and identify missing ADRs.
7. Write the master architecture document.
8. Suggest next architecture steps.

## Codex-native behavior
- preserve the original architecture rigor
- replace legacy structured-question widgets with Codex-native one-question interactions
- replace Claude-only review-step orchestration with Codex native subagents / OMX roles
