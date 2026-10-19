"""Pictures of the GitHub README (docs/images/*.webp in the public repository): the same set as the website's tour, but with
the app in English. Sources, made first:
  FullAIon2Companion --overlay-styles output/overlay-styles-en en
  FullAIon2Companion --pace-test output/boss-card-states-en.png en
  FullAIon2Companion --tour-shots <recording> output/tour-shots-en en
  python scripts/capture-companion-tour.py en          (output/showcase-en/panel-*.png)
Usage: python scripts/build-readme-images.py <out-folder>"""
import json, os, sys
from PIL import Image, ImageDraw, ImageFont
root = r'D:\Proyectos\Aion2'
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
shots = os.path.join(root, 'output', 'showcase-en'); styles = os.path.join(root, 'output', 'overlay-styles-en')
extra = os.path.join(root, 'output', 'tour-shots-en')
english = json.load(open(os.path.join(root, 'desktop', 'FullAIon2Companion', 'i18n', 'panel.json'), encoding='utf-8'))['en']
sizes = {}

def save(im, name, width=None, quality=82):
    im = im.convert('RGB')
    if width and im.width > width: im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    path = os.path.join(out, name + '.webp'); im.save(path, 'WEBP', quality=quality, method=6)
    sizes[name] = (im.size, os.path.getsize(path))

for tab in ['combat', 'pets', 'market', 'ranking', 'atlas', 'fieldbosses', 'cubes', 'farm', 'goals', 'styles', 'events', 'resources']:
    p = os.path.join(shots, f'panel-{tab}.png')
    if os.path.exists(p): save(Image.open(p), f'panel-{tab}', 1600)
for key in ['pulido', 'aion', 'cyberpunk']:
    save(Image.open(os.path.join(styles, f'{key}-conjunto.png')), f'overlay-{key}', 1400)
save(Image.open(os.path.join(styles, 'momento-calma.png')), 'overlay-momento', 700)
boss = Image.open(os.path.join(root, 'output', 'boss-card-states-en.png'))
save(boss.crop((0, 0, boss.width, min(boss.height, int(boss.height * .64)))), 'boss-card', 760)
for name, width in [('minimap', 760), ('pvp-card', 620), ('boss-alert', 1300), ('assistant', 760), ('hunting', 1300)]:
    save(Image.open(os.path.join(extra, name + '.png')), name, width, 84)

# Twelve styles side by side, with their English names.
keys = ['clasico', 'aion', 'momento', 'minimo', 'cyberpunk', 'gamergirl', 'synthwave', 'terminal', 'steampunk', 'holograma', 'radiactivo', 'sakura']
names = {l.split('\t')[0]: english.get(l.split('\t')[1], l.split('\t')[1]) for l in open(os.path.join(styles, 'styles.txt'), encoding='utf-8').read().splitlines() if l.strip()}
cw, cols = 320, 4
font = ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf', 22)
tiles = []
for k in keys:
    im = Image.open(os.path.join(styles, f'{k}-calma.png')).convert('RGB')
    im = im.resize((cw, round(im.height * cw / im.width)), Image.LANCZOS)
    tiles.append((names[k], im.crop((0, 0, cw, min(im.height, 430)))))
rows = (len(tiles) + cols - 1) // cols; ch = 430 + 40
sheet = Image.new('RGB', (cols * (cw + 16) + 16, rows * (ch + 16) + 16), (16, 19, 22))
draw = ImageDraw.Draw(sheet)
for i, (label, im) in enumerate(tiles):
    x = 16 + (i % cols) * (cw + 16); y = 16 + (i // cols) * (ch + 16)
    draw.text((x + 4, y + 4), label, font=font, fill=(233, 199, 126))
    sheet.paste(im, (x, y + 40))
save(sheet, 'estilos', 1300)
for k, (size, b) in sorted(sizes.items()): print(k, size, b)
print(len(sizes), 'images,', sum(b for _, b in sizes.values()), 'bytes')
