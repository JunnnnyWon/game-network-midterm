# Codex CLI Quickstart

This is the fastest safe way to use the Codex-native port of this repository.

## 1. Enter the repo

```bash
cd /tmp/Claude-Code-Game-Studios
```

## 2. Run smoke verification first

```bash
python3 tools/codex/verify_port.py
python3 tools/codex/qa_smoke.py
```

Both commands should exit successfully before you rely on the active Codex layer.

## 3. Launch Codex CLI

```bash
codex
```

In Codex, the active runtime surfaces are:
- `AGENTS.md`
- `.codex/skills/`
- `.codex/agents/`
- `.codex/docs/`

The original `.claude/` tree remains in the repo as legacy/source reference.

## 4. Recommended first workflow

Start with the validated release-gate skills:

```text
$start
$brainstorm open
$setup-engine <engine version>
$design-system <system-name>
$create-architecture
```

Example:

```text
$start
$brainstorm roguelike deckbuilder
$setup-engine godot 4.6
$design-system combat-system
$create-architecture
```

## 5. What is validated right now

The strongest validated Codex surface is:
- `start`
- `brainstorm`
- `setup-engine`
- `design-system`
- `create-architecture`

The broader `.codex/skills/` catalog exists, but the repository's release gate currently treats the above five as the minimum runtime-proven set.

## 6. Team / orchestration note

The Codex port includes orchestration contracts and hygiene checks for team-oriented flows. However, full end-to-end runtime proof for every migrated workflow is still broader than the current smoke gate.

For current orchestration expectations, see:
- `.codex/docs/orchestration-contract.md`
- `docs/CODEX-RELEASE-CHECKLIST.md`
- `production/qa/evidence/codex-port-smoke-2026-04-16.md`

## 7. If something feels Claude-first

That usually means you are reading a legacy reference surface rather than the active Codex layer.

Use these in this order:
1. `AGENTS.md`
2. `.codex/docs/runtime-contract.md`
3. `.codex/skills/<skill>/SKILL.md`
4. `docs/CODEX-MIGRATION.md`

## 8. Fresh evidence locations

- `production/qa/evidence/verify-port-2026-04-16.json`
- `production/qa/evidence/qa-smoke-2026-04-16.json`
- `production/qa/evidence/codex-port-smoke-2026-04-16.md`
