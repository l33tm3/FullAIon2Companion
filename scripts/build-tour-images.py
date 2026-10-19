"""Images of the website's tour (public/assets/tour/*.webp): the app's own captures, invented names for other players."""
import os
from PIL import Image, ImageDraw, ImageFont
root = r'D:\Proyectos\Aion2'
shots = os.path.join(root, 'output', 'showcase'); styles = os.path.join(root, 'output', 'overlay-styles')
out = os.path.join(root, 'public', 'assets', 'tour'); os.makedirs(out, exist_ok=True)
sizes = {}

def save(im, name, width=None, quality=82):
    im = im.convert('RGB')
    if width and im.width > width: im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    path = os.path.join(out, name + '.webp'); im.save(path, 'WEBP', quality=quality, method=6)
    sizes[name] = (im.size, os.path.getsize(path))

# Panel tabs (1920x1200 captures of a 1280x800 panel).
for tab in ['combat', 'pets', 'market', 'ranking', 'atlas', 'fieldbosses', 'cubes', 'farm', 'goals', 'styles', 'events', 'resources']:
    p = os.path.join(shots, f'panel-{tab}.png')
    if os.path.exists(p): save(Image.open(p), f'panel-{tab}', 1600)

# The overlay over the game, as a set: boss card, overlay, strip and loot card (Pulido and Aion dorado).
for key in ['pulido', 'aion', 'cyberpunk']:
    save(Image.open(os.path.join(styles, f'{key}-conjunto.png')), f'overlay-{key}', 1400)
# Out of a fight (Según el momento) and the boss card in its states.
save(Image.open(os.path.join(styles, 'momento-calma.png')), 'overlay-momento', 700)
boss = Image.open(os.path.join(root, 'output', 'boss-card-states.png'))
save(boss.crop((0, 0, boss.width, min(boss.height, int(boss.height * .64)))), 'boss-card', 760)

# Twelve styles side by side, with their names.
keys = ['clasico', 'aion', 'momento', 'minimo', 'cyberpunk', 'gamergirl', 'synthwave', 'terminal', 'steampunk', 'holograma', 'radiactivo', 'sakura']
names = dict(l.split('\t')[:2] for l in open(os.path.join(styles, 'styles.txt'), encoding='utf-8').read().splitlines() if l.strip())
cw, cols = 320, 4
font = ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf', 22)
tiles = []
for k in keys:
    im = Image.open(os.path.join(styles, f'{k}-calma.png')).convert('RGB')
    im = im.resize((cw, round(im.height * cw / im.width)), Image.LANCZOS)
    im = im.crop((0, 0, cw, min(im.height, 430)))
    tiles.append((names[k], im))
rows = (len(tiles) + cols - 1) // cols; ch = 430 + 40
sheet = Image.new('RGB', (cols * (cw + 16) + 16, rows * (ch + 16) + 16), (16, 19, 22))
draw = ImageDraw.Draw(sheet)
for i, (name, im) in enumerate(tiles):
    x = 16 + (i % cols) * (cw + 16); y = 16 + (i // cols) * (ch + 16)
    draw.text((x + 4, y), name, fill=(237, 202, 135), font=font)
    sheet.paste(im, (x, y + 36))
save(sheet, 'estilos', 1400)
for k, (size, b) in sizes.items(): print(f'{k:22} {size[0]}x{size[1]} {b // 1024} KB')
print('total', sum(b for _, b in sizes.values()) // 1024, 'KB')
