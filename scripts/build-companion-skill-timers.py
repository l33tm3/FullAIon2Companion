"""Skill timers for the combat assistant: base skill id -> [Spanish name, cooldown s, icon path, type].
Source: public/data/game/skills-*.json (the site's class skill tables). Output: desktop data/skill-timers.json."""
import glob, json, pathlib
root = pathlib.Path(__file__).resolve().parent.parent
out = {}
for f in sorted(glob.glob(str(root / 'public/data/game/skills-*.json'))):
    for s in json.load(open(f, encoding='utf-8')):
        out[str(s['id'])] = [s['name'], s.get('cooldownSec') or 0, s.get('icon') or '', s.get('type') or '']
dest = root / 'desktop/FullAIon2Companion/skill-timers.json'
dest.write_text(json.dumps(out, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
print(len(out), 'skills ->', dest)
