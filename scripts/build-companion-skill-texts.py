"""Skill texts for the combat assistant's hover card: base skill id -> its description at each level (index 0 = level 1).
Only active skills: the assistant shows their buffs and cooldowns. A level whose text is the one before is stored as "",
so skills whose text does not change (most buffs) cost one line. Source: public/data/game/skills-*.json (the site's
class skill tables, in Spanish). Output: desktop data/skill-texts.json."""
import glob, json, pathlib
root = pathlib.Path(__file__).resolve().parent.parent
out = {}
for f in sorted(glob.glob(str(root / 'public/data/game/skills-*.json'))):
    for s in json.load(open(f, encoding='utf-8')):
        if s.get('type') != 'active': continue
        texts, last = [], None
        for level in sorted(s.get('levels') or [], key=lambda x: x['level']):
            text = (level.get('description') or '').strip()
            texts.append('' if text == last else text)
            last = text
        if not texts and s.get('description'): texts = [s['description'].strip()]
        if any(texts): out[str(s['id'])] = texts
dest = root / 'desktop/FullAIon2Companion/skill-texts.json'
dest.write_text(json.dumps(out, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
print(len(out), 'skills,', dest.stat().st_size, 'bytes ->', dest)
