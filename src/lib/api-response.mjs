// Answers of the site's own API (/api/*.php), which always replies JSON. A web page instead means the request never reached
// it: the hosting's anti-bot challenge (Imunify360 shows «One moment, please…» to IPs on its grey list, a client on
// 2026-10-14), a captive portal or a proxy. r.json() then failed with «Unexpected token '<'»; apiJson names the real cause and
// the message comes with «Recargar página», since a normal page load can pass the challenge and fetch() cannot.
export const PAGE_MESSAGE='El servidor de seguridad del hosting o tu red devolvió una página en lugar de datos';

export class PageInsteadOfData extends Error{
 /** @param {number} httpStatus @param {string} [title] */
 constructor(httpStatus,title=''){super(`${PAGE_MESSAGE} (HTTP ${httpStatus}${title?`, «${title}»`:''})`);this.name='PageInsteadOfData';this.httpStatus=httpStatus;this.pageTitle=title;}
}

const ENTITIES={amp:'&',lt:'<',gt:'>',quot:'"',apos:"'",nbsp:' ',hellip:'…',ndash:'–',mdash:'—',laquo:'«',raquo:'»'};
/** The page's <title>, decoded, on one line and at most 80 characters ('' without one). @param {string} html */
export function pageTitle(html){
 const m=/<title\b[^>]*>([\s\S]*?)<\/title\s*>/i.exec(String(html||''));if(!m)return '';
 const text=m[1].replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi,(all,e)=>{const l=e.toLowerCase();if(l[0]!=='#')return ENTITIES[l]??all;const n=l[1]==='x'?parseInt(l.slice(2),16):parseInt(l.slice(1),10);return n>0&&n<0x110000?String.fromCodePoint(n):'';})
  .replace(/[\s\p{C}]+/gu,' ').trim();
 return text.length>80?text.slice(0,79)+'…':text;
}

/**
 * The JSON of an API answer, whatever its status (callers still read `error` from a failed one). A web page throws
 * PageInsteadOfData; anything else that is not JSON throws a plain Error. Both carry httpStatus.
 * @param {Response} response @returns {Promise<any>}
 */
export async function apiJson(response){
 const type=(response.headers.get('content-type')||'').toLowerCase(),text=await response.text();
 // JSON never starts with '<'; an HTML content type with any other body is a page too.
 if(/^\s*</.test(text)||/^\s*(text\/html|application\/xhtml\+xml)/.test(type))throw new PageInsteadOfData(response.status,pageTitle(text));
 try{return JSON.parse(text);}catch{throw Object.assign(new Error(`El servidor respondió sin datos válidos (HTTP ${response.status}). Vuelve a intentarlo.`),{httpStatus:response.status});}
}

/** A «Recargar página» button after a page error, '' for any other error. @param {unknown} error */
export function reloadButton(error,className='button secondary'){return error instanceof PageInsteadOfData?`<button type="button" class="${className}" data-reload-page>Recargar página</button>`:'';}
/** The error's message in el, followed by «Recargar página» when a page came instead of data. @param {Element} el @param {unknown} error */
export function showApiError(el,error){el.textContent=error instanceof Error?error.message:String(error);if(error instanceof PageInsteadOfData){el.append(' ');el.insertAdjacentHTML('beforeend',reloadButton(error,'text-button'));}}

// One listener serves every «Recargar página» button, however its HTML was rendered.
if(typeof document!=='undefined'&&!(/** @type {any} */(globalThis)).daevaReloadButtons){(/** @type {any} */(globalThis)).daevaReloadButtons=true;document.addEventListener('click',e=>{if(e.target instanceof Element&&e.target.closest('[data-reload-page]'))location.reload();});}
