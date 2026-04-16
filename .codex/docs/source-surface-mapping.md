# Source Surface Mapping

## Scope summary
- Source repo: `.claude/` + scoped `CLAUDE.md`
- Codex target: `AGENTS.md` + `.codex/skills` + `.codex/agents`

## Highest-risk source surfaces

| Source | Risk | Codex target |
|---|---|---|
| `CLAUDE.md` + scoped `CLAUDE.md` | High | root/scoped `AGENTS.md` |
| `.claude/settings.json` | High | runtime-contract docs + AGENTS behavioral rules |
| `.claude/hooks/*` | High | explicit Codex/OMX verification patterns and future script hooks |
| `.claude/skills/*` | High | `.codex/skills/*` |
| `.claude/agents/*` | High | `.codex/agents/*.toml` |
| `.claude/rules/*` | Medium | referenced by AGENTS/docs until finer Codex enforcement evolves |
| README / onboarding docs | High | Codex-native onboarding + migration notes |

## Port status
- Root runtime contract: **created**
- Scoped AGENTS files: **created**
- Codex docs/runtime mapping: **created**
- Project-local agents: **bulk converted**
- Skill catalog: **bulk copied + compatibility-noted**
- Core 5 skills: **manually ported**
- README Codex onboarding: **updated**

## Core 5 skill set
- `start`
- `brainstorm`
- `setup-engine`
- `design-system`
- `create-architecture`

## Notes
- `.claude/` is preserved as upstream reference during the transition.
- Remaining non-core skills are currently portability drafts unless manually overridden later.
