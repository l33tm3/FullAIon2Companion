"""Builds i18n/panel.json (used by the panel and the WPF windows) from panel.tsv and overlay.tsv: Spanish<TAB>English<TAB>Portuguese (Brazil)<TAB>French.
A row without its French column uses the English text."""
import json, os
here = os.path.dirname(os.path.abspath(__file__))
en, pt, fr = {}, {}, {}
for name in ('panel.tsv', 'overlay.tsv'):
    for line in open(os.path.join(here, name), encoding='utf-8'):
        line = line.rstrip('\r\n')
        if not line.strip(): continue
        p = line.split('\t'); assert len(p) in (3, 4), (name, line[:60])
        en[p[0]] = p[1]; pt[p[0]] = p[2]; fr[p[0]] = p[3] if len(p) == 4 and p[3] else p[1]
json.dump({'en': en, 'pt': pt, 'fr': fr}, open(os.path.join(here, 'panel.json'), 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
print(len(en), 'phrases')
