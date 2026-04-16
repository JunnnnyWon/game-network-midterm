# Codex Migration Guide

This repository started as a Claude-first template. The Codex port keeps the
original `.claude/` material as source reference while introducing active Codex
surfaces.

## Active Codex surfaces
- `AGENTS.md`
- scoped `AGENTS.md` files in `src/`, `design/`, `docs/`, and `CCGS Skill Testing Framework/`
- `.codex/skills/`
- `.codex/agents/`
- `.codex/docs/runtime-contract.md`
- `.codex/docs/source-surface-mapping.md`

## Mapping summary
| Legacy source | Codex target |
|---|---|
| `CLAUDE.md` | `AGENTS.md` |
| `.claude/skills/*` | `.codex/skills/*` |
| `.claude/agents/*` | `.codex/agents/*` |
| `AskUserQuestion` | structured input when available, otherwise one concise plain-text question |
| `Task` | Codex native subagents / OMX workflows |
| Claude hooks in `.claude/settings.json` | AGENTS/runtime-contract docs + explicit verification flows |

## Current release gate
The minimum validated Codex surface is:
- `start`
- `brainstorm`
- `setup-engine`
- `design-system`
- `create-architecture`

## Compatibility stance
- Preserve workflow parity first.
- Preserve command / structure parity second.
- Allow Codex-required deviations when necessary.

## Legacy note
The `.claude/` tree remains in the repo to preserve the upstream template and to
support future incremental conversion of the remaining skills and agents.
