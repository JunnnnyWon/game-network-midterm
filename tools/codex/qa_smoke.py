#!/usr/bin/env python3
from pathlib import Path
import json, subprocess, sys
try:
    import tomllib
except Exception:
    tomllib = None
ROOT = Path(__file__).resolve().parents[2]

# verify_port baseline
vp = subprocess.run([sys.executable, str(ROOT/'tools/codex/verify_port.py')], capture_output=True, text=True)
report = json.loads(vp.stdout)

# parse agents TOML
agent_errors = []
if tomllib is not None:
    for p in (ROOT/'.codex/agents').glob('*.toml'):
        try:
            tomllib.loads(p.read_text())
        except Exception as e:
            agent_errors.append({'file': str(p.relative_to(ROOT)), 'error': str(e)})

# basic skill frontmatter parse
skill_errors = []
for p in (ROOT/'.codex/skills').glob('*/SKILL.md'):
    txt = p.read_text()
    if not txt.startswith('---\n'):
        skill_errors.append({'file': str(p.relative_to(ROOT)), 'error': 'missing opening frontmatter'})
        continue
    end = txt.find('\n---\n', 4)
    if end == -1:
        skill_errors.append({'file': str(p.relative_to(ROOT)), 'error': 'missing closing frontmatter'})
        continue
    fm = txt[4:end].splitlines()
    keys = {line.split(':',1)[0].strip() for line in fm if ':' in line}
    if 'name' not in keys or 'description' not in keys:
        skill_errors.append({'file': str(p.relative_to(ROOT)), 'error': 'frontmatter missing name/description'})

summary = {
    'verify_port_ok': vp.returncode == 0,
    'verify_port': report,
    'agent_toml_errors': agent_errors,
    'skill_frontmatter_errors': skill_errors,
}
print(json.dumps(summary, indent=2))
sys.exit(1 if (not summary['verify_port_ok'] or agent_errors or skill_errors) else 0)
