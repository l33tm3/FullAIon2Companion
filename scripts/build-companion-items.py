"""Builds desktop/FullAIon2Companion/items.json for the app's loot log: item id -> [Spanish name, English name, icon, grade, NPC sell price,
Portuguese name, French name], from the website catalog (public/data/game/items/*.json). The Portuguese and French names come from
the vendor's table (Aion-DPS-Meter, Backend/src/data/aion2/item_names_i18n.json, keyed by the English name); it has the gear, not the
souls, chests or materials, which keep five columns and show in English."""
import json, glob, os
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
names = {}
for f in glob.glob(os.path.join(root, 'desktop', 'vendor', '*', 'Backend', 'src', 'data', 'aion2', 'item_names_i18n.json')):
    names = json.load(open(f, encoding='utf-8'))
out = {}
for f in glob.glob(os.path.join(root, 'public', 'data', 'game', 'items', '*.json')):
    r = json.load(open(f, encoding='utf-8'))
    row = [r.get('name') or '', r.get('originalName') or '', r.get('icon') or '', r.get('grade') or '', r.get('sellPrice') or 0]
    local = names.get(row[1]) or {}
    if local.get('pt') or local.get('fr'): row += [local.get('pt') or '', local.get('fr') or '']
    out[str(r['id'])] = row
json.dump(out, open(os.path.join(root, 'desktop', 'FullAIon2Companion', 'items.json'), 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
print(len(out), 'items,', sum(len(r) > 5 for r in out.values()), 'with Portuguese and French names')
