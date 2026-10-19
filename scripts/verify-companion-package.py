from pathlib import Path
import hashlib
import json
import re
import sys
from PIL import Image

root=Path(__file__).resolve().parents[1]
app=Path(sys.argv[1]).resolve() if len(sys.argv)>1 else root/'desktop/release/1.0.2-final/app'
source=root/'public'
icons=list((app/'assets/game').glob('*.webp'))
for file in icons:
    with Image.open(file) as image:
        image.verify()
tiles=list((app/'assets/atlas').rglob('*.webp'))
for file in tiles:
    original=source/file.relative_to(app)
    if hashlib.sha256(file.read_bytes()).digest()!=hashlib.sha256(original.read_bytes()).digest():
        raise RuntimeError('Map tile changed: '+str(file))
npc_paths=list((app/'data/npc').glob('*.json'))
missing=set()
for file in npc_paths:
    text=file.read_text(encoding='utf-8-sig')
    json.loads(text)
    for path in re.findall(r'/assets/game/([A-Za-z0-9_.-]+)',text):
        if not (app/'assets/game'/path).is_file():
            missing.add(path)
if missing:
    raise RuntimeError('Missing NPC assets: '+str(sorted(missing)))
if (app/'THIRD-PARTY-NOTICES.txt').read_text().find('MIT')<0:
    raise RuntimeError('Third party notice missing')
secrets=[str(p.relative_to(app)) for p in app.rglob('*') if p.is_file() and p.suffix.lower() in ['.pfx','.p12','.key','.env']]
if secrets:
    raise RuntimeError('Unexpected private files: '+str(secrets))
report=dict(ok=True,iconsVerified=len(icons),npcRecordsVerified=len(npc_paths),mapTilesUnchanged=len(tiles),missingNpcAssets=0,privateKeyFiles=0,selfContained=(app/'coreclr.dll').is_file(),runtimeLibrary=(app/'PresentationFramework.dll').is_file())
(root/'output/desktop-package-check.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report))
