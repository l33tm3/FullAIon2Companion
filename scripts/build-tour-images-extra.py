"""The tour's second batch (19 oct 2026, the user: «faltan más: el minimapa, la caza de bichos, consultar el PvP de otro
personaje, las alertas de los jefes con sonidos…»): the pictures of `FullAIon2Companion --tour-shots <recording> output/tour-shots`
(the app's own windows with real data, no other player's name) as public/assets/tour/*.webp. Kept apart from
build-tour-images.py so the first batch's files stay as published."""
import os
from PIL import Image
root = r'D:\Proyectos\Aion2'
shots = os.path.join(root, 'output', 'tour-shots'); out = os.path.join(root, 'public', 'assets', 'tour')
for name, width in [('minimap', 760), ('pvp-card', 620), ('boss-alert', 1300), ('assistant', 760), ('hunting', 1300)]:
    im = Image.open(os.path.join(shots, name + '.png')).convert('RGB')
    if im.width > width: im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    path = os.path.join(out, name + '.webp'); im.save(path, 'WEBP', quality=84, method=6)
    print(name, im.size, os.path.getsize(path))
