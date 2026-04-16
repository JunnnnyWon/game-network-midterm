#!/usr/bin/env python3
from pathlib import Path
import json, sys

ROOT = Path(__file__).resolve().parents[2]
CORE = {
    'start': {
        'must_contain': ['one question at a time', 'request_user_input', 'brainstorm', 'setup-engine', 'design-system', 'create-architecture'],
    },
    'brainstorm': {
        'must_contain': ['one question at a time', 'Codex-native subagents', 'design/gdd/game-concept.md'],
    },
    'setup-engine': {
        'must_contain': ['technical-preferences.md', 'official engine docs', 'one question at a time'],
    },
    'design-system': {
        'must_contain': ['Overview', 'Player Fantasy', 'Detailed Rules', 'Formulas', 'Edge Cases', 'Dependencies', 'Tuning Knobs', 'Acceptance Criteria'],
    },
    'create-architecture': {
        'must_contain': ['technical requirements baseline', 'API boundaries', 'Codex-native one-question interactions'],
    },
}
LEGACY_TOKENS = ['AskUserQuestion', 'Task']
report = {'core5': {}, 'ok': True}
for name, cfg in CORE.items():
    path = ROOT / '.codex' / 'skills' / name / 'SKILL.md'
    text = path.read_text() if path.exists() else ''
    missing = [s for s in cfg['must_contain'] if s not in text]
    legacy = [tok for tok in LEGACY_TOKENS if tok in text]
    report['core5'][name] = {
        'path': str(path.relative_to(ROOT)),
        'exists': path.exists(),
        'missing_required_strings': missing,
        'legacy_tokens': legacy,
    }
    if (not path.exists()) or missing or legacy:
        report['ok'] = False
print(json.dumps(report, indent=2))
sys.exit(0 if report['ok'] else 1)
