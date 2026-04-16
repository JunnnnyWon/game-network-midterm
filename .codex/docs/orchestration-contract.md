# Codex Orchestration Contract

This document defines how orchestration-heavy studio workflows should behave in the Codex-native port.

## Primitive replacements
- `Task` -> Codex native subagents (`spawn_agent`) or OMX orchestration lanes
- `AskUserQuestion` -> structured user input when available, otherwise one concise plain-text question
- Claude slash-command phrasing -> skill/workflow names, with slash forms treated as compatibility aliases

## Rules
- Ask one question at a time.
- Preserve workflow intent, not Claude-only syntax.
- Prefer parallel child agents only when their inputs are independent.
- Surface partial progress and blockers; do not silently skip a blocked lane.
- Keep domain ownership explicit when delegating to child agents.

## Team-skill guidance
- Team skills may reference original workflow names for parity, but Codex-facing wording should describe them as skills/workflows.
- When the source says to "spawn via Task", interpret that as spawning Codex-native child agents with the same domain ownership.
- When the source says to "use AskUserQuestion", interpret that as: show the analysis, then capture the decision with structured input if available, otherwise a concise single question.
