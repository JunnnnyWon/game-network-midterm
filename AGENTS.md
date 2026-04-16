# Codex Game Studios — Codex/OMX Runtime Contract

This repository originated as a Claude Code-first studio template. In Codex, the
**active runtime surfaces are `AGENTS.md`, `.codex/skills/`, and `.codex/agents/`**.
Treat `.claude/` as upstream/source material and migration reference unless a file
explicitly says otherwise.

## Primary goals
- Preserve **workflow parity** with the original studio system.
- Preserve substantial **command / structure parity** where practical.
- Prefer **Codex-native correctness** whenever literal Claude compatibility would break usability.

## Active Codex surfaces
- `AGENTS.md` (this file) — root runtime contract for Codex work
- `.codex/skills/` — Codex-native or compatibility-draft workflow skills
- `.codex/agents/` — Codex-native project agents derived from the original studio roles
- `.codex/docs/runtime-contract.md` — canonical mapping from Claude-only primitives to Codex/OMX equivalents
- `.codex/docs/source-surface-mapping.md` — source-to-target conversion inventory

## Runtime mapping rules
- `CLAUDE.md` guidance becomes `AGENTS.md` guidance.
- `.claude/skills/*` map to `.codex/skills/*`.
- `.claude/agents/*` map to `.codex/agents/*`.
- `AskUserQuestion` maps to:
  1. `request_user_input` when available, otherwise
  2. one concise plain-text question at a time.
- Claude `Task` subagent orchestration maps to Codex native subagents and/or OMX workflow routing.
- Claude hook lifecycle expectations map to AGENTS rules, OMX state/memory, and explicit verification scripts/docs.

## Working rules
- Keep `.claude/` intact as source reference during the rebuild unless the task explicitly targets Claude surfaces.
- Prefer updating the Codex-native layer first: `AGENTS.md`, `.codex/skills`, `.codex/agents`, Codex docs, README.
- When a Claude instruction conflicts with Codex-native operation, preserve the **user-visible workflow** rather than the literal primitive.
- For interactive flows, ask only one question at a time.
- For broad work, maintain artifact-first progress: write plans/docs/mappings so the repo stays resumable.

## Release-gate skills
The current Codex release gate focuses on these five skills:
- `start`
- `brainstorm`
- `setup-engine`
- `design-system`
- `create-architecture`

## Documentation gate
A release is not ready unless a new user can read the README and reach the five release-gate skills without hidden setup knowledge.
