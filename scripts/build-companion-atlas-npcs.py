"""Builds desktop/FullAIon2Companion/atlas-npcs/<map>.json for the Atlas inside the app (ui/atlas.js): for each creature
category of the website's atlas (public/data/atlas/<map>.json), the creature (NPC id, or the first variant's) and its race
of every marker, in the same order as the app's markers (minimap-markers/<map>.json, checked marker by marker: 46,437 of
46,437 on 2026-10-17), and each creature's portrait (the icon of its catalog record, public/data/game/npc/<id>.json, when the
app ships it in assets/game or assets/pets; else the icon of the pet whose soul it drops, the one the Pets page shows; else
aion2.th.gl's; never a generic map marker). Races: C Cogni, F Fera, N Natura, V Varian, - unknown.
Output: {"categories": {id: {"npcs": [...], "races": "CF-..."}}, "icons": {npc: path under /assets/}}.
aion2.th.gl's portraits are used with its authors' permission (given to the owner on 2026-10-17), on condition that our
website credits them (footer and /endpoints/; the app's Atlas credits them too). --fetch downloads its creature list
(research/thgl/bestiary-es.json) and only the portraits the map lacks (desktop/FullAIon2Companion/thgl-icons), one at a
time; without it, only what is already here is used.
Usage: python scripts/build-companion-atlas-npcs.py [--fetch]
"""
import json, os, re, sys, time, urllib.request
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
out = os.path.join(root, 'desktop', 'FullAIon2Companion', 'atlas-npcs')
os.makedirs(out, exist_ok=True)
race = {'Cogni': 'C', 'Fera': 'F', 'Natura': 'N', 'Varian': 'V'}
app_dir = os.path.join(root, 'desktop', 'FullAIon2Companion')
pet_of = json.load(open(os.path.join(root, 'desktop', 'vendor', 'Aion-DPS-Meter-084e7e94c5c5c4d280c4547ce9c39168e86d6437', 'Client', 'assets', 'aion2', 'pets', 'pets.json'), encoding='utf-8'))['monsters']
pet_icon = json.load(open(os.path.join(app_dir, 'pet-icons.json'), encoding='utf-8'))
siblings = json.load(open(os.path.join(app_dir, 'npc-siblings.json'), encoding='utf-8'))
fetch = '--fetch' in sys.argv
thgl_list = os.path.join(root, 'research', 'thgl', 'bestiary-es.json')
thgl_dir = os.path.join(app_dir, 'thgl-icons')
# aion2.th.gl's dot and two skulls stand for "no portrait" (641, 83 and 14 of its creatures): not portraits.
thgl_generic = {'5ee309ad.webp', '454ff65d.webp', '1acc87a1.webp'}


def get(url, path):
    request = urllib.request.Request(url, headers={'User-Agent': 'DaevaCompanion/1.0 (+https://governanceai.me)'})
    with urllib.request.urlopen(request, timeout=30) as answer:
        data = answer.read()
    with open(path, 'wb') as f:
        f.write(data)
    time.sleep(0.3)


if fetch:
    os.makedirs(os.path.dirname(thgl_list), exist_ok=True)
    get('https://aion2.th.gl/api/db/sidebar?section=bestiary&locale=es', thgl_list)
thgl_id, thgl_name = {}, {}
if os.path.exists(thgl_list):
    for group in json.load(open(thgl_list, encoding='utf-8'))['groups']:
        for item in group['items']:
            if item['id'].startswith('m_') and item['id'][2:].isdigit() and (item.get('icon') or {}).get('url'):
                thgl_id[int(item['id'][2:])] = item['icon']['url']
                thgl_name.setdefault(re.sub(r'\s*\(Lv \d+\)$', '', item['name']).strip().lower(), set()).add(item['icon']['url'])


def record(n):
    path = os.path.join(root, 'public', 'data', 'game', 'npc', f'{n}.json')
    return json.load(open(path, encoding='utf-8')) if os.path.exists(path) else {}


def portrait(n, marker):
    # The creature record's icon, else the website marker's own (npcIcon), when the app ships it. The website's
    # /assets/catalog portraits are the pet icons the app ships in assets/pets (same file names). UT_Marker_* are the
    # game's generic map markers (a skull, a speech bubble), which the client gives some 4,000 creatures with no portrait
    # (Araña de ojos rojos, 2000923): not portraits.
    for icon in [record(n).get('icon') or '', marker.get('npcIcon') or '']:
        name = os.path.basename(icon)
        if icon.startswith('/assets/game/') and not name.startswith('UT_Marker') and os.path.exists(os.path.join(root, 'public', 'assets', 'game', name)):
            return 'game/' + name
        if icon.startswith('/assets/catalog/') and os.path.exists(os.path.join(app_dir, 'pet-icons', name)):
            return 'pets/' + name
    pet = pet_icon.get(str(pet_of.get(str(n), '')), '')
    if pet.startswith('/assets/pets/') and os.path.exists(os.path.join(app_dir, 'pet-icons', os.path.basename(pet))):
        return pet[len('/assets/'):]
    # Last, aion2.th.gl's family portrait: by creature id, its sibling's, or a name it gives a single portrait (Durgal de la
    # meseta, Tuka, Frugon...). The rest (lava flowers, totems, towers) keep their race crest.
    names = thgl_name.get((record(n).get('name') or '').strip().lower()) or set()
    url = thgl_id.get(n) or thgl_id.get(siblings.get(str(n))) or (next(iter(names)) if len(names) == 1 else None)
    file = os.path.basename(url or '')
    if not file or file in thgl_generic:
        return None
    if fetch and not os.path.exists(os.path.join(thgl_dir, file)):
        os.makedirs(thgl_dir, exist_ok=True)
        get(url, os.path.join(thgl_dir, file))
    return 'thgl/' + file if os.path.exists(os.path.join(thgl_dir, file)) else None


used = set()
for map_id in ['1', '2', '3', '5', '6', '7', '8', '9', '10']:
    web = json.load(open(os.path.join(root, 'public', 'data', 'atlas', map_id + '.json'), encoding='utf-8'))
    app = json.load(open(os.path.join(root, 'desktop', 'FullAIon2Companion', 'minimap-markers', map_id + '.json'), encoding='utf-8'))
    by_id = {c['id']: c for c in web['categories']}
    result, icons = {}, {}
    for c in app['categories']:
        w = by_id.get(c['id'])
        if not w or len(w['markers']) != len(c['markers']):
            continue
        for m, a in zip(w['markers'], c['markers']):
            if abs(m['lng'] - a[0]) >= 1 or abs(m['lat'] - a[1]) >= 1:
                raise SystemExit(f'map {map_id} category {c["id"]}: markers out of order')
        npcs = [m.get('npcId') or (m.get('npcVariants') or [{}])[0].get('id') or 0 for m in w['markers']]
        if not any(npcs):
            continue
        # A marker without its creature's race takes its category's, as the website's Atlas does (Cognis -> Cogni crest).
        result[c['id']] = {'npcs': npcs, 'races': ''.join(race.get(m.get('creatureName')) or race.get(w.get('originalName'), '-') for m in w['markers'])}
        for n, m in zip(npcs, w['markers']):
            if n and str(n) not in icons and (icon := portrait(n, m)):
                icons[str(n)] = icon
    used |= {i for i in icons.values() if i.startswith('thgl/')}
    with open(os.path.join(out, map_id + '.json'), 'w', encoding='utf-8') as f:
        json.dump({'categories': result, 'icons': icons}, f, separators=(',', ':'), ensure_ascii=False)
    print(map_id, len(result), 'categories', sum(len(v['npcs']) for v in result.values()), 'markers', len(icons), 'portraits', os.path.getsize(os.path.join(out, map_id + '.json')), 'bytes')
unused = sorted(set(os.listdir(thgl_dir)) - {os.path.basename(i) for i in used}) if os.path.isdir(thgl_dir) else []
print(len(used), 'aion2.th.gl portraits used', '; not used any more:', unused or 'none')
