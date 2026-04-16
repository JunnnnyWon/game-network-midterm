---
name: design-system
description: Author or retrofit a system GDD in the Codex-native studio workflow
argument-hint: "[system name or path]"
---

# Design System

Create or retrofit a system GDD while preserving the original studio discipline.

## Required sections
1. Overview
2. Player Fantasy
3. Detailed Rules
4. Formulas
5. Edge Cases
6. Dependencies
7. Tuning Knobs
8. Acceptance Criteria

## Flow
1. Parse target system or infer from `design/gdd/systems-index.md`.
2. Read context: game concept, systems index, technical preferences, related GDDs.
3. Present a concise context/feasibility brief.
4. Build or retrofit the GDD section by section.
5. For specialized input, use Codex-native subagents instead of Claude-only child-agent calls.
6. Update the systems index and suggest follow-up review.

## Interaction rules
- one question at a time
- analysis before decision capture
- preserve the original GDD rigor even if Codex-native implementation differs
