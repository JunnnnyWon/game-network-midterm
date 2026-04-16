# CCGS Skill Testing Framework — Codex Rules

This folder is the QA layer for the studio skill/agent system.

## Rules
- Read `catalog.yaml` first when validating a skill or agent spec.
- Treat `.claude/` specs as source behavior and `.codex/` as the active Codex target during the rebuild.
- When testing Codex-native surfaces, verify both parity intent and Codex-native replacements.
- Record mismatches as investigation items, not instant failures, until the migration target is finalized.
