"""Builds desktop/FullAIon2Companion/dungeon-names.json for the Clasificación tab (ui/app.js): the name of each dungeon
difficulty id of the website's catalog (public/data/game/dungeons/*.json), «Santuario del odio · Extremo» for 910034,
as the awakening ranking (0x578D, ranking 21) names its dungeon by that id.
Usage: python scripts/build-companion-dungeon-names.py
"""
import glob, json, os
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
names = {}
for path in sorted(glob.glob(os.path.join(root, 'public', 'data', 'game', 'dungeons', '*.json'))):
    d = json.load(open(path, encoding='utf-8'))
    for level in d.get('difficulties') or []:
        if level.get('id'):
            names[str(level['id'])] = d['name'] + (' · ' + level['name'] if level.get('name') and len(d.get('difficulties')) > 1 else '')
out = os.path.join(root, 'desktop', 'FullAIon2Companion', 'dungeon-names.json')
with open(out, 'w', encoding='utf-8') as f:
    json.dump(names, f, ensure_ascii=False, separators=(',', ':'), sort_keys=True)
print(len(names), 'difficulties', os.path.getsize(out), 'bytes;', '910034 =', names.get('910034'))
