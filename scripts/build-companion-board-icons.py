"""The Daevanion node icons for the app's «Mi perfil» (2026-10-24, the user: «me faltan las imágenes originales»): the website's
board_icon_*.png (140 px, 594 KB in all) as 64 px WebP in desktop/FullAIon2Companion/board-icons (about 40 KB), served by
the app as /assets/board/. Run again if the website's icons change."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
source = root / 'public' / 'assets' / 'ui'
target = root / 'desktop' / 'FullAIon2Companion' / 'board-icons'
target.mkdir(exist_ok=True)
total = 0
for png in sorted(source.glob('board_icon_*.png')):
    out = target / (png.stem + '.webp')
    Image.open(png).convert('RGBA').resize((64, 64), Image.LANCZOS).save(out, 'WEBP', quality=86, method=6)
    total += out.stat().st_size
print(f'{len(list(target.glob("*.webp")))} icons, {total} bytes in {target}')
