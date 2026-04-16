# Codex Runtime Contract

This document defines how the original Claude Code Game Studios runtime maps onto Codex/OMX.

## Canonical replacements

| Claude surface | Codex/OMX replacement | Notes |
|---|---|---|
| `CLAUDE.md` | `AGENTS.md` | Root + scoped guidance preserved via new AGENTS files |
| `.claude/skills/*` | `.codex/skills/*` | Core 5 are manually ported; remaining skills are compatibility drafts during migration |
| `.claude/agents/*` | `.codex/agents/*` | Converted to project-local TOML agents |
| `AskUserQuestion` | `request_user_input` or one-question plain-text turns | One question at a time remains mandatory |
| `Task` | Codex native subagents / OMX orchestration | Use `spawn_agent`, role routing, or team/ralph workflows |
| `.claude/settings.json` hooks | AGENTS rules + OMX state/memory + explicit verification scripts/docs | Preserve behavior, not literal event wiring |
| Claude model tiers (`opus`/`sonnet`/`haiku`) | `gpt-5.4` / `gpt-5.4-mini` / `gpt-5.3-codex-spark` | Mapped by task complexity |
| Claude status line | optional OMX HUD / trace/status docs | No direct 1:1 requirement |

## Operational rules
- Preserve workflow parity first.
- Preserve command/structure parity second.
- Allow Codex-required deviations when needed for correct use.
- Treat `.claude/` as source reference, `.codex/` as the active Codex surface.

## Release-gate validation
The minimum validated Codex layer must support:
- `start`
- `brainstorm`
- `setup-engine`
- `design-system`
- `create-architecture`

## Interaction contract
- One question per round.
- Prefer structured user input when the runtime supports it.
- Fall back to concise plain-text questions when it does not.
- Do not ask the user for codebase facts that can be discovered directly.

## Orchestration contract
- Preserve the studio hierarchy conceptually.
- Use Codex native subagents or OMX role workflows instead of Claude-only task semantics.
- Keep domain boundaries explicit in prompts and docs.
