# Codex Release Gate Checklist

This checklist defines the minimum bar for calling the Codex-native port usable.

## 1. Active runtime surfaces exist
- [ ] `AGENTS.md`
- [ ] scoped `AGENTS.md` files in `src/`, `design/`, `docs/`, and `CCGS Skill Testing Framework/`
- [ ] `.codex/docs/runtime-contract.md`
- [ ] `.codex/docs/source-surface-mapping.md`
- [ ] `.codex/docs/orchestration-contract.md`

## 2. Core 5 skill gate
The following skills must exist in `.codex/skills/` and be treated as the minimum validated set:
- [ ] `start`
- [ ] `brainstorm`
- [ ] `setup-engine`
- [ ] `design-system`
- [ ] `create-architecture`

## 3. Core skill hygiene
For each of the five release-gate skills:
- [ ] no direct `AskUserQuestion` token remains in the active Codex skill file
- [ ] no direct Claude `Task` tool expectation remains in the active Codex skill file
- [ ] the file describes one-question-at-a-time Codex interaction behavior

## 4. Orchestration hygiene
For orchestration-heavy Codex skills:
- [ ] team skills use `spawn_agent` / `request_user_input` wording in active frontmatter
- [ ] team skills reference `.codex/docs/orchestration-contract.md`
- [ ] active team skill files do not retain raw `AskUserQuestion` tokens
- [ ] active team skill files do not retain raw Claude `Task` tokens in frontmatter/body as active instructions

## 5. Onboarding gate
- [ ] `README.md` explicitly points Codex users to `AGENTS.md` and `.codex/`
- [ ] README exposes the five release-gate skills
- [ ] `docs/CODEX-MIGRATION.md` exists and explains the source-to-target mapping

## 6. Compatibility stance
- [ ] workflow parity stated as first priority
- [ ] structure/command parity stated as second priority
- [ ] Codex-required changes explicitly allowed

## 7. Legacy reference policy
- [ ] `.claude/` preserved as upstream/source reference during migration
- [ ] `CLAUDE.md` marked as legacy/source reference for Codex users

## 8. Verification command
Primary automated verification:

```bash
python3 tools/codex/verify_port.py
```
