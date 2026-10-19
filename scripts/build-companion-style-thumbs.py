"""Miniaturas de Configuración → Estilos del panel, por idioma.

Uso: python scripts/build-companion-style-thumbs.py <carpeta de --overlay-styles> <es|en|pt>

Toma <clave>-calma.png de la carpeta (FullAIon2Companion.exe --overlay-styles <carpeta> [en|pt]) y escribe
ui/styles/<clave>.webp (es) o ui/styles/<idioma>/<clave>.webp: 300 px de ancho, WebP calidad 82, como las que ya había.
El panel elige la carpeta de su idioma (ui/app.js, styleThumbs).
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STYLES = os.path.join(ROOT, "desktop", "FullAIon2Companion", "ui", "styles")


def main():
    if len(sys.argv) != 3 or sys.argv[2] not in ("es", "en", "pt"):
        sys.exit(__doc__)
    source, lang = sys.argv[1], sys.argv[2]
    keys = sorted(f[:-5] for f in os.listdir(STYLES) if f.endswith(".webp"))
    target = STYLES if lang == "es" else os.path.join(STYLES, lang)
    os.makedirs(target, exist_ok=True)
    missing = [k for k in keys if not os.path.exists(os.path.join(source, f"{k}-calma.png"))]
    if missing:
        sys.exit("Faltan imágenes en la carpeta: " + ", ".join(missing))
    total = 0
    for key in keys:
        image = Image.open(os.path.join(source, f"{key}-calma.png")).convert("RGB")
        width = 300
        image = image.resize((width, round(image.height * width / image.width)), Image.LANCZOS)
        path = os.path.join(target, f"{key}.webp")
        image.save(path, "WEBP", quality=82, method=6)
        total += os.path.getsize(path)
    print(f"{len(keys)} miniaturas en {target}: {total:,} bytes")


if __name__ == "__main__":
    main()
