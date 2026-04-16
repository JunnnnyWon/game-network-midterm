---
name: start
description: First-time Codex onboarding for the Game Studios workflow
argument-hint: "[no arguments]"
---

# Start

Codex-native onboarding for this repository.

## Purpose
Orient a new user without assuming they already know the workflow, engine, or project stage.

## Codex interaction rules
- Ask **one question at a time**.
- Prefer `request_user_input` when the runtime supports it.
- Otherwise ask a concise plain-text question with clear options.
- Gather repo facts before asking the user about them.

## Phase 1 — detect state silently
Check:
- engine configured in `.claude/docs/technical-preferences.md`
- concept doc in `design/gdd/game-concept.md`
- source files in `src/`
- prototypes in `prototypes/`
- design docs in `design/gdd/`
- production artifacts in `production/`

## Phase 2 — ask starting point
Ask where the user is starting from:
- no idea yet
- vague idea
- clear concept
- existing work

## Phase 3 — route
Recommend the next path using the studio workflow and the five release-gate skills where relevant.

Preferred early route:
- `brainstorm`
- `setup-engine`
- `design-system`
- `create-architecture`

If the repo already has work, surface what was found before recommending the next step.

## Phase 4 — review mode
If `production/review-mode.txt` is missing, ask once for:
- full
- lean
- solo
Then write the selected value.

## Output contract
End with a short handoff to the next recommended skill. Preserve collaborative onboarding; do not auto-run the next skill unless the user explicitly asks.
