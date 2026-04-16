#!/usr/bin/env python3
from pathlib import Path
import json
import sys

ROOT = Path(__file__).resolve().parents[2]
CORE_SKILLS = [
    'start',
    'brainstorm',
    'setup-engine',
    'design-system',
    'create-architecture',
]

def main() -> int:
    source_skills = sorted(p.parent.name for p in (ROOT / '.claude' / 'skills').glob('*/SKILL.md'))
    codex_skills = sorted(p.parent.name for p in (ROOT / '.codex' / 'skills').glob('*/SKILL.md'))
    source_agents = sorted(p.stem for p in (ROOT / '.claude' / 'agents').glob('*.md'))
    codex_agents = sorted(p.stem for p in (ROOT / '.codex' / 'agents').glob('*.toml'))
    source_scopes = sorted(str(p.relative_to(ROOT)) for p in ROOT.glob('**/CLAUDE.md') if '.omx/' not in str(p.relative_to(ROOT)))
    codex_scopes = sorted(str(p.relative_to(ROOT)) for p in ROOT.glob('**/AGENTS.md') if '.omx/' not in str(p.relative_to(ROOT)))

    active_orchestration_files = list((ROOT / '.codex' / 'skills').glob('team-*/SKILL.md'))
    core_skill_files = [ROOT / '.codex' / 'skills' / s / 'SKILL.md' for s in CORE_SKILLS]
    orchestration_has_legacy_tokens = {}
    core_skill_legacy_tokens = {}
    for p in active_orchestration_files:
        body = p.read_text()
        orchestration_has_legacy_tokens[str(p.relative_to(ROOT))] = ('AskUserQuestion' in body or 'Task' in body)
    for p in core_skill_files:
        body = p.read_text()
        core_skill_legacy_tokens[str(p.relative_to(ROOT))] = ('AskUserQuestion' in body or 'Task' in body)

    report = {
        'source_skill_count': len(source_skills),
        'codex_skill_count': len(codex_skills),
        'missing_skill_ports': [s for s in source_skills if s not in codex_skills],
        'source_agent_count': len(source_agents),
        'codex_agent_count': len(codex_agents),
        'missing_agent_ports': [a for a in source_agents if a not in codex_agents],
        'source_scope_count': len(source_scopes),
        'codex_scope_count': len(codex_scopes),
        'core_skills': {s: (ROOT / '.codex' / 'skills' / s / 'SKILL.md').exists() for s in CORE_SKILLS},
        'runtime_contract_exists': (ROOT / '.codex' / 'docs' / 'runtime-contract.md').exists(),
        'mapping_doc_exists': (ROOT / '.codex' / 'docs' / 'source-surface-mapping.md').exists(),
        'orchestration_contract_exists': (ROOT / '.codex' / 'docs' / 'orchestration-contract.md').exists(),
        'migration_doc_exists': (ROOT / 'docs' / 'CODEX-MIGRATION.md').exists(),
        'readme_mentions_codex': (('Codex-native port' in (ROOT / 'README.md').read_text()) and ('AGENTS.md' in (ROOT / 'README.md').read_text())),
        'orchestration_has_legacy_tokens': orchestration_has_legacy_tokens,
        'core_skill_legacy_tokens': core_skill_legacy_tokens,
        'release_checklist_exists': (ROOT / 'docs' / 'CODEX-RELEASE-CHECKLIST.md').exists(),
    }

    print(json.dumps(report, indent=2))
    failed = (
        report['missing_skill_ports'] or
        report['missing_agent_ports'] or
        not all(report['core_skills'].values()) or
        not report['runtime_contract_exists'] or
        not report['mapping_doc_exists'] or
        not report['orchestration_contract_exists'] or
        not report['release_checklist_exists'] or
        not report['migration_doc_exists'] or
        any(report['orchestration_has_legacy_tokens'].values()) or
        any(report['core_skill_legacy_tokens'].values()) or
        not report['readme_mentions_codex']
    )
    return 1 if failed else 0

if __name__ == '__main__':
    sys.exit(main())
