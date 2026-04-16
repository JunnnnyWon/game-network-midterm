<p align="center">
  <h1 align="center">Claude Code Game Studios — Codex Port</h1>
  <p align="center">
    A Codex-first, AGENTS.md-driven port of <strong>Claude Code Game Studios</strong>.<br/>
    Turn one Codex CLI session into a structured game-dev studio with roles, workflows, QA gates, and reusable docs.
  </p>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/runtime-Codex%20CLI-412991" alt="Codex CLI" />
  <img src="https://img.shields.io/badge/config-AGENTS.md-blue" alt="AGENTS.md" />
  <img src="https://img.shields.io/badge/skills-72-green" alt="72 skills" />
  <img src="https://img.shields.io/badge/agents-49-blueviolet" alt="49 agents" />
  <img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License" />
</p>

> [!IMPORTANT]
> This repository is the **Codex-native port** of the original upstream project:
> [`Donchitos/Claude-Code-Game-Studios`](https://github.com/Donchitos/Claude-Code-Game-Studios).
>
> - **Active runtime surface for Codex:** `AGENTS.md`, `.codex/skills/`, `.codex/agents/`
> - **Legacy/source reference:** `.claude/`, `CLAUDE.md`
>
> If you are using **Codex CLI**, start from this README and `docs/CODEX-CLI-QUICKSTART.md`.

---

## What is this?

This repo gives Codex a **structured game studio operating system** instead of a blank prompt.

Instead of one generic assistant trying to do everything, you get:
- a studio hierarchy of specialized roles
- workflow skills for concept, design, architecture, QA, release, and team coordination
- documentation conventions for GDDs, ADRs, UX, and production artifacts
- repo-local verification scripts and release evidence for the active Codex layer

The goal is simple:

> **Use Codex CLI like a disciplined game-dev studio, not a random chat tab.**

---

## Why this port exists

The upstream template was built for **Claude Code**. It had strong workflow design, but its runtime assumptions were Claude-specific:
- `CLAUDE.md`
- `.claude/skills/*`
- `.claude/agents/*`
- `AskUserQuestion`
- Claude hook/runtime expectations

This port keeps the studio design, but makes the repository **usable from Codex CLI** by introducing:
- `AGENTS.md`
- `.codex/skills/`
- `.codex/agents/`
- Codex runtime + orchestration contracts
- Codex-specific smoke/release evidence

---

## What you get

| Category | Count | Notes |
|---|---:|---|
| Codex agents | 49 | Project-local `.codex/agents/*.toml` ports |
| Codex skills | 72 | Project-local `.codex/skills/*/SKILL.md` |
| Release-gate skills | 5 | The currently validated Codex-first path |
| QA evidence artifacts | 6+ | Smoke, audit, release-gate proof |
| Scoped AGENTS files | 5 | Root + directory-scoped Codex guidance |

### Current release-gate skills
These are the five workflows currently treated as the **validated Codex path**:
- `start`
- `brainstorm`
- `setup-engine`
- `design-system`
- `create-architecture`

---

## Quickstart

### 1) Clone the repo
```bash
git clone https://github.com/JunnnnyWon/Claude-Code-Game-Studios.git
cd Claude-Code-Game-Studios
```

### 2) Run verification first
```bash
python3 tools/codex/verify_port.py
python3 tools/codex/qa_smoke.py
python3 tools/codex/core5_audit.py
```

### 3) Launch Codex CLI
```bash
codex
```

### 4) Start the workflow
Inside Codex:
```text
$start
```

### 5) Recommended first sequence
```text
$start
$brainstorm open
$setup-engine godot 4.6
$design-system combat-system
$create-architecture
```

For a dedicated usage guide, see:
- [`docs/CODEX-CLI-QUICKSTART.md`](docs/CODEX-CLI-QUICKSTART.md)

---

## How to think about the repo

### Active Codex surfaces
Use these first:
1. `AGENTS.md`
2. `.codex/docs/runtime-contract.md`
3. `.codex/skills/<skill>/SKILL.md`
4. `.codex/agents/*.toml`
5. `docs/CODEX-CLI-QUICKSTART.md`

### Legacy reference surfaces
These are preserved for lineage and incremental migration:
- `CLAUDE.md`
- `.claude/`
- Claude-specific historical notes in older docs

---

## Validation status

### What is already proven
The repo has durable evidence for the active Codex layer:
- `tools/codex/verify_port.py`
- `tools/codex/qa_smoke.py`
- `tools/codex/core5_audit.py`
- `production/qa/evidence/codex-port-smoke-2026-04-16.md`
- `production/qa/evidence/codex-pipeline-audit-2026-04-16.md`
- `production/qa/evidence/codex-core5-proof-2026-04-16.md`

### What that means in practice
This port is **usable in Codex CLI** for the intended release gate:
- active runtime surface exists
- core 5 workflows are present and checked
- active Codex skills/agents parse cleanly
- orchestration hygiene checks pass
- onboarding/usage guidance exists in-repo

### What is not yet claimed
This repo does **not** claim that all 72 migrated workflows have been fully runtime-proven end-to-end in live Codex sessions.

So the honest status is:
- **Codex-usable:** yes
- **Release-gate approved:** yes
- **Every migrated workflow fully runtime-proven:** not yet

---

## Workflow model

The studio flow still follows the upstream philosophy:

1. **Concept**
2. **Systems Design**
3. **Technical Setup**
4. **Pre-Production**
5. **Production**
6. **Polish**
7. **Release**

And the collaboration rule remains:

> **Question → Options → Decision → Draft → Approval**

This matters because the port is not just about file conversion — it is about preserving the workflow discipline in Codex.

---

## Project structure

```text
AGENTS.md                           # Codex-native root runtime contract
CLAUDE.md                           # Legacy Claude-first reference
.codex/                             # Active Codex-native skills, agents, docs
.claude/                            # Upstream/legacy reference surfaces
src/                                # Game source code
assets/                             # Art, audio, VFX, shaders, data
design/                             # GDDs, narrative docs, levels, UX
docs/                               # Architecture, migration, workflow docs
tests/                              # Test suites
tools/                              # Verification and helper scripts
prototypes/                         # Throwaway prototypes
production/                         # QA evidence, sprint/release artifacts
```

---

## Repo-specific Codex docs

- [`docs/CODEX-CLI-QUICKSTART.md`](docs/CODEX-CLI-QUICKSTART.md)
- [`docs/CODEX-MIGRATION.md`](docs/CODEX-MIGRATION.md)
- [`docs/CODEX-RELEASE-CHECKLIST.md`](docs/CODEX-RELEASE-CHECKLIST.md)
- [`.codex/docs/runtime-contract.md`](.codex/docs/runtime-contract.md)
- [`.codex/docs/orchestration-contract.md`](.codex/docs/orchestration-contract.md)
- [`.codex/docs/source-surface-mapping.md`](.codex/docs/source-surface-mapping.md)

---

## Contributing

If you want to improve this port, the highest-value areas are:
1. **runtime-proving more workflows beyond the core 5**
2. **making team/orchestration flows more deeply Codex-native**
3. **reducing Claude-first historical wording in non-critical docs**
4. **expanding repo-local QA evidence**

If you change the active Codex surface, refresh the smoke evidence:
```bash
python3 tools/codex/verify_port.py
python3 tools/codex/qa_smoke.py
python3 tools/codex/core5_audit.py
```

---

## Credits

- **Original upstream project:** [`Donchitos/Claude-Code-Game-Studios`](https://github.com/Donchitos/Claude-Code-Game-Studios)
- **This fork:** Codex-native port and release-gate adaptation by [`JunnnnyWon`](https://github.com/JunnnnyWon)

---

## License

MIT — see [`LICENSE`](LICENSE).
