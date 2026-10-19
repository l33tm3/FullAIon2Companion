"""Builds i18n/panel.json (used by the panel and the WPF windows) from panel.tsv and overlay.tsv: Spanish<TAB>English<TAB>Portuguese (Brazil)."""
import json, os
here = os.path.dirname(os.path.abspath(__file__))
en, pt = {}, {}
for name in ('panel.tsv', 'overlay.tsv'):
    for line in open(os.path.join(here, name), encoding='utf-8'):
        line = line.rstrip('\n')
        if not line.strip(): continue
        p = line.split('\t'); assert len(p) == 3, (name, line[:60])
        en[p[0]] = p[1]; pt[p[0]] = p[2]
json.dump({'en': en, 'pt': pt}, open(os.path.join(here, 'panel.json'), 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
print(len(en), 'phrases')
