import {downloadRelease,staticDownloadUrl} from '../lib/download-release.mjs';
const esc=(v:any)=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
const arrow='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M12 3v12m-5-5 5 5 5-5M5 17v4h14v-4"/></svg>';
let last:any=null,lastError=false,signature='';
// Browsers download the plain static copy (/descargas/FullAIon2Companion-<versión>-setup.exe) once a HEAD request finds it with
// the published size; until then, or when it is missing (a release published without it), the API address, which always works.
const confirmed:Record<string,string>={},checked=new Set<string>();
export const downloadHref=(r:any):string=>(r&&confirmed[r.version])||r?.url||'/#home?download=1';
function confirmStatic(r:any){
 const url=staticDownloadUrl(r);if(!url||checked.has(url))return;checked.add(url);
 fetch(url,{method:'HEAD',cache:'no-store'}).then(x=>{if(!x.ok||Number(x.headers.get('content-length'))!==r.bytes)return;confirmed[r.version]=url;if(last?.version===r.version)paint(last,lastError);}).catch(()=>{});
}
export function renderDownloads(releases:any,error=false){
 if(!document.getElementById('companion-releases'))return;
 const r=error?last:downloadRelease(releases);if(r)last=r;lastError=error;
 paint(r,error);if(r)confirmStatic(r);
}
function paint(r:any,error:boolean){
 const host=document.getElementById('companion-releases');if(!host)return;
 document.querySelectorAll<HTMLAnchorElement>('[data-release-download]').forEach(a=>{a.href=r?downloadHref(r):'/#home?download=1';a.dataset.releaseReady=r?'true':'false';a.setAttribute('aria-label',r?`Descargar FullAIon2Companion ${r.version} para Windows de 64 bits`:'Consultar descarga de FullAIon2Companion');});
 document.querySelectorAll<HTMLElement>('[data-release-label]').forEach(el=>el.textContent=r?'Descargar para Windows':'Consultar descarga');
 document.querySelectorAll<HTMLElement>('[data-release-summary]').forEach(el=>{el.textContent=r?`${r.version} · ${(r.bytes/1048576).toLocaleString('es-CL',{minimumFractionDigits:1,maximumFractionDigits:1})} MB · ${r.channel==='stable'?'Estable':'Beta'}`:'Windows de 64 bits · Descarga sin cuenta';});
 const next=JSON.stringify([r,error,r&&downloadHref(r)]);if(next===signature){host.querySelectorAll<HTMLButtonElement>('[data-download-retry]').forEach(b=>b.disabled=false);return;}signature=next;
 if(!r){host.innerHTML=`<div class="download-unavailable"><h3>${error?'No pudimos consultar la descarga':'No hay un instalador publicado'}</h3><p>${error?'Revisa tu conexión y vuelve a intentarlo.':'La descarga estará disponible aquí cuando se publique una versión.'}</p><button class="button secondary" data-download-retry>Reintentar</button></div>`;bind(host);return;}
 const date=Number.isFinite(Date.parse(r.createdAt))?new Date(r.createdAt).toLocaleDateString('es-CL',{timeZone:'America/Santiago',day:'2-digit',month:'short',year:'numeric'}):null;
 host.innerHTML=`<div class="download-release"><div class="download-release-heading"><h3>Versión ${esc(r.version)}</h3><span>${r.channel==='stable'?'Estable':'Beta'}</span></div><p>Windows de 64 bits · ${(r.bytes/1048576).toLocaleString('es-CL',{minimumFractionDigits:1,maximumFractionDigits:1})} MB${date?' · Publicada '+esc(date):''}</p><a class="button download-primary" href="${esc(downloadHref(r))}" data-release-download data-release-ready="true">${arrow}<span>Descargar para Windows<small>Instalador .exe · ${esc(r.version)}</small></span></a><p class="download-account-note">Descarga sin cuenta. Puedes vincularla después.</p><div class="download-release-links"><button class="text-button" data-copy-download>Copiar enlace</button><a href="https://www.virustotal.com/gui/file/${r.sha256}" target="_blank" rel="noopener noreferrer">Consultar este archivo en VirusTotal <span aria-hidden="true">↗</span></a></div><p id="download-message" role="status"></p><input id="download-link-fallback" aria-label="Enlace de descarga para copiar" readonly hidden/><details class="download-changelog"><summary>Novedades de esta versión</summary><p>${esc(r.notes)}</p></details><details class="download-integrity"><summary>Huella SHA-256 del instalador</summary><code>${r.sha256}</code></details>${error?'<p role="status">No pudimos comprobar si hay una versión nueva. Se conserva la última consultada.</p><button class="text-button" data-download-retry>Volver a consultar</button>':''}</div>`;
 bind(host);document.dispatchEvent(new Event('daeva-redraw-icons'));
}
function bind(host:HTMLElement){
 host.querySelectorAll<HTMLButtonElement>('[data-download-retry]').forEach(b=>b.onclick=()=>{b.disabled=true;document.dispatchEvent(new Event('daeva-releases-retry'));});
 host.querySelector<HTMLButtonElement>('[data-copy-download]')?.addEventListener('click',async()=>{const message=host.querySelector<HTMLElement>('#download-message')!;const url=new URL(downloadHref(last),location.origin).href;try{await navigator.clipboard.writeText(url);message.textContent='Enlace copiado. Puedes abrirlo en tu equipo con Windows.';}catch{const field=host.querySelector<HTMLInputElement>('#download-link-fallback')!;field.hidden=false;field.value=url;field.focus();field.select();message.textContent='Copia este enlace y ábrelo en tu equipo con Windows.';}});
}
document.addEventListener('click',e=>{const link=(e.target as Element).closest<HTMLAnchorElement>('[data-release-download]');if(!link||link.dataset.releaseReady!=='true')return;const message=document.getElementById('download-message');if(message)message.textContent='Tu navegador iniciará la descarga del instalador. Si no aparece, vuelve a pulsar Descargar.';});
