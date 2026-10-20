"""Captures every tab of the companion panel with the user's own panel data (panel-dump.json from the running dev app),
other players' names replaced by invented ones, for the website's tour. Output: output/showcase/panel-<tab>.png"""
import json, pathlib, subprocess, sys, time
from playwright.sync_api import sync_playwright

root = pathlib.Path(r'D:\Proyectos\Aion2')
# argv[1]: es (default), en, pt or fr - the panel in that language (the GitHub README uses English), to output/showcase[-<lang>].
lang = sys.argv[1] if len(sys.argv) > 1 else 'es'
out = root / 'output' / ('showcase' if lang == 'es' else f'showcase-{lang}'); out.mkdir(parents=True, exist_ok=True)
data = json.load(open(r'C:/Users/nanos/AppData/Local/FullAIon2Companion/panel-dump.json', encoding='utf-8'))

# Other players' names: invented ones (the character Darkni stays).
s = data['snapshot']
players = set()
classes = data.get('classes') or {}
for a in s.get('Actors') or []:
    if str(a.get('Id')) in classes and a.get('Name') != 'Darkni': players.add(a['Name'])
for m in ((s.get('Troop') or {}).get('Members') or []) + ((s.get('Room') or {}).get('Members') or []):
    if m.get('Name') != 'Darkni': players.add(m['Name'])
for n in s.get('Party') or []:
    if n != 'Darkni': players.add(n)
fake = ['Lyra', 'Kael', 'Mirae', 'Torvin', 'Selene', 'Aurik', 'Nyxa', 'Brann', 'Isolde', 'Zephyr', 'Rowan', 'Tamsin', 'Elowen', 'Darius', 'Ysolde', 'Corvin']
names = {n: fake[i % len(fake)] for i, n in enumerate(sorted(players))}
text = json.dumps(data, ensure_ascii=False)
for real, other in names.items():
    text = text.replace(json.dumps(real, ensure_ascii=False), json.dumps(other, ensure_ascii=False))
data = json.loads(text)
data['settings']['Language'] = lang
print('renamed', len(names))

server = subprocess.Popen([sys.executable, '-m', 'http.server', '4361', '--bind', '127.0.0.1'], cwd=str(root / 'desktop' / 'build-dev-next'), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
time.sleep(1.2)
tabs = ['combat', 'history', 'pets', 'farm', 'goals', 'cubes', 'resources', 'market', 'ranking', 'fieldbosses', 'events', 'atlas']
try:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(viewport={'width': 1280, 'height': 800}, device_scale_factor=1.5, bypass_csp=True)
        # The panel talks to the app through WebView2: a silent stand-in, so no «el programa no responde» notice shows.
        page.add_init_script("window.chrome={webview:{postMessage(){},addEventListener(){},removeEventListener(){}}};")
        errors = []
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.goto('http://127.0.0.1:4361/ui/index.html')
        page.wait_for_load_state('networkidle')
        for _ in range(3):
            page.evaluate('d => window.renderCompanion(d)', data); time.sleep(.4)
        for tab in tabs:
            page.evaluate("t => document.querySelector(`.tabs [data-tab=\"${t}\"]`)?.click()", tab)
            time.sleep(2.5 if tab == 'atlas' else .8)
            page.evaluate('d => window.renderCompanion(d)', data); time.sleep(.5)
            page.add_style_tag(content='#action-feedback{display:none!important}')
            page.screenshot(path=str(out / f'panel-{tab}.png'))
            print('saved', tab)
        page.evaluate("() => { document.querySelector('.tabs [data-tab=\"settings\"]')?.click(); }"); time.sleep(.4)
        page.evaluate("() => document.querySelector('.config-nav [data-section=\"styles\"]')?.click()"); time.sleep(1.2)
        page.add_style_tag(content='#action-feedback{display:none!important}')
        page.screenshot(path=str(out / 'panel-styles.png'))
        print('saved styles; page errors:', errors[:5])
        browser.close()
finally:
    server.terminate()
