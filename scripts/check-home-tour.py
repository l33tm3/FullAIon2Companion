"""Full-page captures of the home page's tour (local dist on :4370, or a given base URL), desktop and phone, plus errors."""
import sys
from playwright.sync_api import sync_playwright
base = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:4370/'
prefix = sys.argv[2] if len(sys.argv) > 2 else 'local'
out = r'D:\Proyectos\Aion2\output\showcase'
with sync_playwright() as p:
    browser = p.chromium.launch()
    for name, size, scale in [('desktop', {'width': 1440, 'height': 900}, 1), ('phone', {'width': 390, 'height': 844}, 2)]:
        page = browser.new_page(viewport=size, device_scale_factor=scale)
        errors = []; failed = []
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.on('console', lambda m: errors.append(m.text) if m.type == 'error' else None)
        page.on('requestfailed', lambda r: failed.append(r.url))
        page.on('response', lambda r: failed.append(f'{r.status} {r.url}') if r.status >= 400 else None)
        page.goto(base, wait_until='networkidle')
        tour = page.locator('.tour')
        # Every lazy image in: scroll through the tour.
        height = page.evaluate("document.querySelector('.tour').getBoundingClientRect().height")
        top = page.evaluate("document.querySelector('.tour').getBoundingClientRect().top + scrollY")
        for y in range(int(top), int(top + height), 600):
            page.evaluate(f'scrollTo(0,{y})'); page.wait_for_timeout(120)
        page.wait_for_timeout(800)
        broken = page.evaluate("[...document.querySelectorAll('.tour img')].filter(i => !i.complete || i.naturalWidth === 0).map(i => i.src)")
        overflow = page.evaluate("document.documentElement.scrollWidth - innerWidth")
        tour.screenshot(path=f'{out}\\{prefix}-tour-{name}.png')
        print(name, 'height', int(height), 'broken', broken, 'overflow', overflow, 'errors', errors[:4], 'failed', [f for f in failed if 'favicon' not in f][:6])
        page.close()
    browser.close()
