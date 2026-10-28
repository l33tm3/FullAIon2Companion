"""Daevanion boards the official profile API does not give yet, for the app's «Mi perfil» (2026-10-24, the user: «me falta
Ariel !!!»). NCSOFT's global API lists five boards and answers 404 for Ariel (boardId 15), but the Korean one has it:
research/daevanion/ariel-kr-gladiator-15.json (a Korean Gladiator, region=kr). The Korean Azphel matches the global one node
for node (ids, rows, columns, grades, stats) and Ariel is the same for every class (Gladiator 15 = Ranger 35), so its layout
holds here. The values do not: Korea's Azphel gives +5 attack where the global one gives +3, so the effects keep their names
only. Names follow the API's own Spanish for the PvP board («Ataque JcJ» -> «Ataque JcE») and the encyclopedia's for bosses.
Writes desktop/FullAIon2Companion/daevanion-extra.json, served by the app as /data/daevanion-extra.json."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
STATS = {
    'PVE 공격력': 'Ataque JcE', 'PVE 방어력': 'Defensa JcE', 'PVE 명중': 'Precisión JcE', 'PVE 회피': 'Evasión JcE',
    'PVE 피해 내성': 'Tolerancia a Daño JcE', 'PVE 피해 증폭': 'Amplificación de Daño JcE',
    '보스 공격력': 'Ataque contra Jefes', '보스 방어력': 'Defensa contra Jefes',
    '보스 피해 내성': 'Tolerancia a Daño de Jefes', '보스 피해 증폭': 'Amplificación de Daño contra Jefes',
    '최대 생명력': 'Puntos de Vida Máximos', '최대 정신력': 'Puntos de Maná Máximos', '생명력': 'Puntos de Vida', '정신력': 'Puntos de Maná',
    '아리엘 - 시작': 'Ariel - Inicio',
}

def name(text):
    parts = [p.strip() for p in text.split(',')]
    missing = [p for p in parts if p not in STATS]
    if missing: raise SystemExit(f'Sin traducción: {missing}')
    return ', '.join(STATS[p] for p in parts)

def effect(desc):
    # «PVE 공격력 +5» -> «Ataque JcE»: the Korean value is not the global one.
    stat = desc.rsplit(' +', 1)[0].strip()
    return name(stat) if stat else ''

boards = []
for index, label, source in [(5, 'Ariel', 'ariel-kr-gladiator-15.json')]:
    data = json.loads((root / 'research' / 'daevanion' / source).read_text(encoding='utf-8'))
    data = data.get('data', data)
    nodes = []
    for n in data['nodeList']:
        if n['type'] == 'None': continue
        nodes.append([n['nodeId'] % 10000, n['row'], n['col'], n['type'], n['grade'], name(n['name']), [e for e in (effect(x['desc']) for x in n['effectList']) if e]])
    boards.append({'index': index, 'name': label, 'nodes': nodes})
out = root / 'desktop' / 'FullAIon2Companion' / 'daevanion-extra.json'
out.write_text(json.dumps({'source': 'NCSOFT Corea (aion2.plaync.com, region=kr), 24 oct 2026', 'boards': boards}, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
print(f'{out}: ' + ', '.join(f"{b['name']} {len(b['nodes'])} nodos" for b in boards) + f', {out.stat().st_size} bytes')
