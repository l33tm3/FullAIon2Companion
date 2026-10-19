"""Optimize packaged UI thumbnails, preserving source assets, filenames, transparency and map tiles."""
from pathlib import Path
import json
import sys
from io import BytesIO
from PIL import Image
from concurrent.futures import ThreadPoolExecutor, as_completed

root = Path(sys.argv[1]).resolve()
icons = root / 'assets' / 'game'
if not (root / 'FullAIon2Companion.exe').is_file() or not icons.is_dir():
    raise SystemExit('Expected a published companion application folder')
def optimize(file):
    old = file.read_bytes()
    changed = False
    with Image.open(BytesIO(old)) as image:
        # UI icons render at 24-64 px; bundled background illustrations never need 2048 px.
        # The atlas is in a separate folder and is never processed here.
        edge = max(image.size)
        target = 128 if 128 < edge <= 256 else 1024 if edge > 1024 and (file.name.startswith('UT_BG_') or '_BG_' in file.name) else None
        if target:
            image.thumbnail((target, target), Image.Resampling.LANCZOS)
            encoded = BytesIO()
            image.save(encoded, format='WEBP', quality=90 if target == 128 else 85, method=4)
            if len(encoded.getvalue()) < len(old):
                temporary = file.with_suffix('.optimized.tmp')
                temporary.write_bytes(encoded.getvalue())
                temporary.replace(file)
                changed = True
    return len(old), file.stat().st_size, changed

before = after = changed = 0
files = list(icons.glob('*.webp')) + list((root / 'assets' / 'pets').glob('*.webp'))  # pet portraits too
with ThreadPoolExecutor(max_workers=4) as pool:
    for count, task in enumerate(as_completed([pool.submit(optimize, file) for file in files]), 1):
        old, new, edited = task.result()
        before += old
        after += new
        changed += edited
        if count % 500 == 0:
            print(json.dumps(dict(processed=count, total=len(files))), flush=True)
print(json.dumps(dict(changed=changed, beforeBytes=before, afterBytes=after, savedBytes=before-after)))
