// The website in English and Portuguese (19 oct 2026, «prepara el sitio para multi idioma: inglés, español, portugués»).
// The pages are written in Spanish; this swaps every text and label that has a translation in src/i18n/{en,pt}.json
// (scripts/build-site-i18n.py), including what the scripts add later, and keeps the Spanish to switch back. The language
// comes from ?lang=, then the last choice, then the browser. In English and Portuguese the tour shows the app in English
// (/assets/tour-en/). The encyclopedia's game data stays in Spanish.
import en from '../i18n/en.json';
import pt from '../i18n/pt.json';

type Lang = 'es' | 'en' | 'pt';
const dictionaries: Record<'en' | 'pt', Record<string, string>> = { en, pt };
const KEY = 'daeva-lang';
const ATTRS = ['placeholder', 'aria-label', 'title', 'alt'];
const months: Record<string, [string, string]> = { ene: ['Jan', 'jan'], feb: ['Feb', 'fev'], mar: ['Mar', 'mar'], abr: ['Apr', 'abr'], may: ['May', 'mai'], jun: ['Jun', 'jun'], jul: ['Jul', 'jul'], ago: ['Aug', 'ago'], sep: ['Sep', 'set'], sept: ['Sep', 'set'], oct: ['Oct', 'out'], nov: ['Nov', 'nov'], dic: ['Dec', 'dez'] };
const descriptions: Record<Lang, string> = {
 es: '',
 en: 'Your AION 2 companion: compare characters, find what to upgrade and plan your way through Atreia. Free overlay with DPS meter, boss timers, minimap and more.',
 pt: 'Seu companheiro de AION 2: compare personagens, descubra o que melhorar e organize seu caminho por Atreia. Overlay grátis com medidor de DPS, chefes, minimapa e mais.',
};

function stored(): Lang | null { try { const s = localStorage.getItem(KEY); return s === 'es' || s === 'en' || s === 'pt' ? s : null; } catch { return null; } }
function pick(): Lang {
 const asked = new URLSearchParams(location.search).get('lang');
 if (asked === 'es' || asked === 'en' || asked === 'pt') { try { localStorage.setItem(KEY, asked); } catch {} return asked; }
 const saved = stored(); if (saved) return saved;
 const browser = (navigator.languages?.[0] || navigator.language || 'es').toLowerCase();
 return browser.startsWith('es') ? 'es' : browser.startsWith('pt') ? 'pt' : 'en';
}
let lang: Lang = pick();
export const siteLanguage = () => lang;

// Spanish → current language, or null when there is nothing to change.
function translate(text: string): string | null {
 if (lang === 'es') return null;
 const d = dictionaries[lang], key = text.replace(/\s+/g, ' ').trim();
 if (!key) return null;
 if (d[key]) return d[key];
 const en = lang === 'en';
 let m: RegExpMatchArray | null;
 if ((m = key.match(/^(\d{2}) · (.+)$/)) && d[m[2]]) return `${m[1]} · ${d[m[2]]}`;
 if ((m = key.match(/^Ampliar: (.+)$/))) return (en ? 'Enlarge: ' : 'Ampliar: ') + (d[m[1]] ?? m[1]);
 if ((m = key.match(/^Versión (.+)$/))) return (en ? 'Version ' : 'Versão ') + m[1];
 if ((m = key.match(/^Instalador \.exe · (.+)$/))) return (en ? 'Installer .exe · ' : 'Instalador .exe · ') + m[1];
 if ((m = key.match(/^Descargar FullAIon2Companion (.+) para Windows de 64 bits$/))) return en ? `Download FullAIon2Companion ${m[1]} for Windows 64-bit` : `Baixar FullAIon2Companion ${m[1]} para Windows 64 bits`;
 if ((m = key.match(/^Windows de 64 bits · ([\d.,]+) MB(?: · Publicada (\d+) (\S+?)\.? (\d{4}))?$/))) {
  const size = en ? m[1].replace(',', '.') : m[1];
  const month = m[3] ? months[m[3].toLowerCase()]?.[en ? 0 : 1] ?? m[3] : '';
  return (en ? 'Windows 64-bit · ' : 'Windows 64 bits · ') + size + ' MB' + (m[2] ? (en ? ` · Released ${month} ${m[2]}, ${m[4]}` : ` · Publicada em ${m[2]} ${month} ${m[4]}`) : '');
 }
 if ((m = key.match(/^([\d.]+) · ([\d.,]+) MB · (Estable|Beta)$/))) return `${m[1]} · ${en ? m[2].replace(',', '.') : m[2]} MB · ${m[3] === 'Estable' ? (en ? 'Stable' : 'Estável') : 'Beta'}`;
 return null;
}

// What the page said in Spanish, and what we wrote, so the scripts' own later changes are told apart from ours.
const spanishText = new WeakMap<Text, string>(), writtenText = new WeakMap<Text, string>();
const spanishAttr = new WeakMap<Element, Record<string, string>>(), writtenAttr = new WeakMap<Element, Record<string, string>>();
function textNode(node: Text) {
 const parent = node.parentElement;
 if (!parent || parent.closest('script,style,code,textarea,[data-no-translate]')) return;
 if (writtenText.get(node) !== node.data) spanishText.set(node, node.data);
 const original = spanishText.get(node) ?? node.data;
 const done = translate(original);
 const next = done === null ? original : original.match(/^\s*/)![0] + done + original.match(/\s*$/)![0];
 if (node.data !== next) { writtenText.set(node, next); node.data = next; } else writtenText.set(node, next);
}
function element(el: Element) {
 const originals = spanishAttr.get(el) ?? {}, written = writtenAttr.get(el) ?? {};
 for (const name of ATTRS) {
  const value = el.getAttribute(name);
  if (value === null) continue;
  if (written[name] !== value) originals[name] = value;
  const done = translate(originals[name]) ?? originals[name];
  written[name] = done;
  if (value !== done) el.setAttribute(name, done);
 }
 // The tour's pictures: the app in English for English and Portuguese.
 for (const name of ['src', 'href']) {
  const value = el.getAttribute(name);
  if (!value || !/^\/assets\/tour(-en)?\//.test(value)) continue;
  if (written[name] !== value) originals[name] = value;
  const done = lang === 'es' ? originals[name] : originals[name].replace(/^\/assets\/tour\//, '/assets/tour-en/');
  written[name] = done;
  if (value !== done) el.setAttribute(name, done);
 }
 spanishAttr.set(el, originals); writtenAttr.set(el, written);
}
function apply(root: Node) {
 if (root.nodeType === Node.TEXT_NODE) { textNode(root as Text); return; }
 if (root.nodeType !== Node.ELEMENT_NODE && root.nodeType !== Node.DOCUMENT_NODE) return;
 if (root.nodeType === Node.ELEMENT_NODE) element(root as Element);
 const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
 for (let n = walker.nextNode(); n; n = walker.nextNode()) n.nodeType === Node.TEXT_NODE ? textNode(n as Text) : element(n as Element);
}
function head() {
 document.documentElement.lang = lang === 'pt' ? 'pt-BR' : lang;
 const meta = document.querySelector<HTMLMetaElement>('meta[name="description"]');
 if (meta) { meta.dataset.es ??= meta.content; meta.content = lang === 'es' ? meta.dataset.es : descriptions[lang]; }
 const select = document.getElementById('site-lang') as HTMLSelectElement | null;
 if (select && select.value !== lang) select.value = lang;
}
export function setSiteLanguage(next: Lang) {
 lang = next;
 try { localStorage.setItem(KEY, next); } catch {}
 const url = new URL(location.href);
 if (url.searchParams.has('lang')) { url.searchParams.set('lang', next); history.replaceState(history.state, '', url); }
 head(); apply(document.body);
 document.dispatchEvent(new CustomEvent('daeva-language', { detail: next }));
}

head();
apply(document.body);
new MutationObserver(records => {
 for (const r of records) {
  if (r.type === 'characterData') textNode(r.target as Text);
  else if (r.type === 'attributes') element(r.target as Element);
  else r.addedNodes.forEach(apply);
 }
}).observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: [...ATTRS, 'src', 'href'] });
document.getElementById('site-lang')?.addEventListener('change', e => {
 const value = (e.target as HTMLSelectElement).value;
 if (value === 'es' || value === 'en' || value === 'pt') setSiteLanguage(value);
});
