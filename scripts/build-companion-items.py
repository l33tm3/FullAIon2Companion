"""Builds desktop/FullAIon2Companion/items.json for the app's loot log: item id -> [Spanish name, English name, icon, grade, NPC sell price],
from the website catalog (public/data/game/items/*.json)."""
import json, glob, os
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
out = {}
for f in glob.glob(os.path.join(root, 'public', 'data', 'game', 'items', '*.json')):
    r = json.load(open(f, encoding='utf-8'))
    out[str(r['id'])] = [r.get('name') or '', r.get('originalName') or '', r.get('icon') or '', r.get('grade') or '', r.get('sellPrice') or 0]
json.dump(out, open(os.path.join(root, 'desktop', 'FullAIon2Companion', 'items.json'), 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
print(len(out), 'items')
