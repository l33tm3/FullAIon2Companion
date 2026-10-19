import type {ProgressCloud} from '../lib/cloud';
import {sameCharacter} from '../lib/character-context';
import {profileConnection,profileProgress} from '../lib/profile-progress.mjs';
import {apiJson,PageInsteadOfData,showApiError} from '../lib/api-response.mjs';

export function profileHub(personal:HTMLElement,connection:HTMLElement,character:any,cloud:ProgressCloud,go:(view:string)=>void,esc:(x:any)=>string,isProfile:()=>boolean){
 let version=0,signature='',progressSignature='',summary:any=null,billing:any=null,controller:AbortController|null=null;
 const own=()=>isProfile()&&!!cloud.user&&sameCharacter(cloud.records['ref:mine'],character.ref);
 const current=(v:number,uid:string)=>version===v&&cloud.user?.id===uid&&own()&&personal.isConnected&&isProfile();
 const time=(s:any)=>s&&Number.isFinite(Date.parse(s))?new Date(s).toLocaleString('es-CL',{timeZone:'America/Santiago',day:'2-digit',month:'short',hour:'2-digit',minute:'2-digit'}):'Sin lectura';
 const num=(n:number)=>n.toLocaleString('es-CL');
 const account=(tab:string)=>document.dispatchEvent(new CustomEvent('daeva-account-tab',{detail:tab}));
 const tools=[['equipment','Equipo y habilidades','Revisa atributos y mejoras.','swords'],['roadmap','Hoja de ruta','Organiza tu próxima mejora.','route'],['planner','Planificar habilidades','Prepara tu distribución.','sparkles'],['history','Mi progreso','Compara tus lecturas guardadas.','chart-no-axes-combined'],['pets','Mis mascotas','Consulta la colección de este personaje.','paw-print'],['daevanion','Daevanion','Explora tus nodos y efectos.','compass']];
 function progress(){
  if(!own()){progressSignature='';personal.hidden=true;personal.replaceChildren();return;}
  const p=profileProgress(cloud.records,character),key=JSON.stringify([cloud.user!.id,character.ref,p,cloud.status]);personal.hidden=false;if(key===progressSignature)return;progressSignature=key;
  personal.innerHTML=`<div class="profile-hub-heading"><div><h2>Tu aventura</h2><p>Tus herramientas siguen a este personaje. Elegirlo no confirma su propiedad.</p></div><div class="profile-hub-actions"><button class="text-button" data-hub-connection>Aplicación y licencia</button><button class="button secondary" data-hub-sync>Sincronizar progreso</button></div></div><nav class="profile-tools" aria-label="Herramientas de mi personaje">${tools.map(([view,title,description,icon])=>`<button data-hub-view="${view}"><i data-lucide="${icon}"></i><span><strong>${title}</strong><small>${description}</small></span><i data-lucide="chevron-right"></i></button>`).join('')}</nav><section class="profile-recent"><h3>Tu progreso guardado</h3><dl><div><dt>Lecturas de este personaje</dt><dd>${num(p.readings)}</dd><small>${p.last?'Última: '+esc(time(p.last.at)):'Guarda una lectura desde Mi progreso.'}</small></div><div><dt>Cambio de poder guardado</dt><dd>${p.growth===null?'—':(p.growth>=0?'+':'')+num(p.growth)}</dd><small>${p.growth===null?'Se necesitan dos lecturas.':'Desde la primera lectura guardada.'}</small></div><div><dt>Mascotas recibidas de la app</dt><dd>${p.pets===null?'—':num(p.pets)}</dd><small>${p.pets===null?'Todavía no hay una colección sincronizada.':'Actualización: '+esc(time(p.petUpdatedAt))}</small></div></dl><p class="profile-sync-message" role="status">${esc(cloud.status)}</p></section>`;
  personal.querySelector<HTMLButtonElement>('[data-hub-connection]')!.onclick=()=>connection.scrollIntoView({block:'start',behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});
  personal.querySelectorAll<HTMLButtonElement>('[data-hub-view]').forEach(b=>b.onclick=()=>go(b.dataset.hubView!));
  personal.querySelector<HTMLButtonElement>('[data-hub-sync]')!.onclick=async()=>{const uid=cloud.user?.id,v=version,b=personal.querySelector<HTMLButtonElement>('[data-hub-sync]')!;b.disabled=true;try{await cloud.sync();}finally{if(b.isConnected)b.disabled=false;if(uid&&current(v,uid)){progress();redraw();}}};
 }
 const redraw=()=>document.dispatchEvent(new Event('daeva-redraw-icons'));
 async function api(path:string,uid:string,v:number,body?:any){
  const response=await fetch(path,{credentials:'same-origin',method:body?'POST':'GET',headers:body?{'Content-Type':'application/json','X-CSRF-Token':cloud.csrf}:{},body:body?JSON.stringify(body):undefined,signal:AbortSignal.any([controller!.signal,AbortSignal.timeout(15000)])});
  const data=await apiJson(response);if(!current(v,uid))throw Error('La sesión o el personaje cambió.');if(!response.ok)throw Error(data.error||'No se pudo consultar el servidor.');return data;
 }
 function controls(){connection.querySelectorAll<HTMLButtonElement>('[data-hub-account]').forEach(b=>b.onclick=()=>account(b.dataset.hubAccount!));}
 function connections(error='',billingError='',page=false){
  if(!own())return;
  const s=profileConnection(summary,billing,character.ref,cloud.user?.emailVerified===true);
  connection.innerHTML=`<section class="panel profile-connection"><h3>Tu aplicación y tu personaje</h3>${error?`<p role="status">${esc(error)}</p><button class="button secondary" data-hub-retry>Reintentar conexión</button>${page?'<button class="button secondary" data-reload-page>Recargar página</button>':''}`:`<dl class="profile-connection-status"><div><dt>Aplicación</dt><dd>${s.online?'Activa ahora':s.devices.length?'Emparejada · sin actividad reciente':'Sin dispositivo conectado'}</dd></div><div><dt>Correo de la cuenta</dt><dd>${cloud.user?.emailVerified?'Confirmado':'Pendiente de confirmar'}</dd></div><div><dt>Propiedad del personaje</dt><dd>${s.verified?'Verificada por el servidor':s.titleChallenge?'Verificación en curso':'Sin verificar'}</dd></div><div><dt>Funciones</dt><dd>Todas gratis · PvP y minimapa incluidos</dd></div></dl>${ownership(s)}${billingError?`<p role="status">${esc(billingError)}</p>${page?'<button class="button secondary" data-reload-page>Recargar página</button>':''}`:''}<div class="profile-connection-actions"><button class="button secondary" data-hub-account="desktop">${s.devices.length?'Gestionar aplicación':'Conectar aplicación'}</button><button class="text-button" data-hub-account="subscription">Ver mi suscripción</button>${!cloud.user?.emailVerified?'<button class="text-button" data-hub-security>Confirmar mi correo</button>':''}<button class="text-button" data-hub-refresh>Actualizar estado</button></div>${owned(s)}`}<p data-hub-message role="status"></p><small>El emparejamiento conecta la cuenta y la app. La licencia se comprueba para este personaje; este resumen no activa permisos en el ejecutable.</small></section>`;
  controls();connection.querySelector<HTMLButtonElement>('[data-hub-security]')?.addEventListener('click',()=>document.dispatchEvent(new Event('daeva-sign-in')));
  connection.querySelector<HTMLButtonElement>('[data-hub-refresh]')?.addEventListener('click',()=>void load());connection.querySelector<HTMLButtonElement>('[data-hub-retry]')?.addEventListener('click',()=>void load());
  connection.querySelector<HTMLButtonElement>('[data-hub-title]')?.addEventListener('click',()=>void action('title-challenge',{character:character.ref}));
  connection.querySelector<HTMLButtonElement>('[data-hub-title-check]')?.addEventListener('click',()=>void action('title-check',{challengeId:s.titleChallenge?.challengeId}));
  connection.querySelector<HTMLButtonElement>('[data-hub-title-cancel]')?.addEventListener('click',()=>void action('title-cancel',{challengeId:s.titleChallenge?.challengeId}));
  connection.querySelectorAll<HTMLButtonElement>('[data-hub-unlink]').forEach(b=>b.addEventListener('click',()=>{const l=s.linked[Number(b.dataset.hubUnlink)];if(l&&confirm(`¿Quitar ${linkName(l)} de tus personajes verificados? Puedes volver a verificarlo cuando quieras.`))void action('character-unlink',{character:l.character});}));redraw();
 }
 // Ownership is proven by swapping an equipped title in the game; the server reads the change on the official profile.
 const characterName=()=>character.info?.profile?.characterName||'este personaje';
 const linkName=(l:any)=>l.characterName||(sameCharacter(l.character,character.ref)?characterName():'Personaje');
 function ownership(s:any){
  if(s.verified)return '';const t=s.titleChallenge;
  if(t)return `<div class="profile-ownership"><h4>Verifica que ${esc(characterName())} es tuyo</h4><ol><li>En el juego, cambia tu título <strong>«${esc(t.currentTitle)}»</strong> (categoría ${esc(t.categoryLabel)}) por cualquier otro.</li><li>Pulsa «Comprobar». La web oficial de AION 2 puede tardar unos minutos en mostrar el cambio.</li><li>Cuando quede verificado, vuelve a ponerte el título que prefieras.</li></ol><small>Caduca: ${esc(time(t.expiresAt))}</small><div class="profile-connection-actions"><button class="button" data-hub-title-check>Comprobar</button><button class="text-button" data-hub-title-cancel>Cancelar</button></div></div>`;
  const why=!cloud.user?.emailVerified?'Primero confirma tu correo.':s.linked.length>=s.limit?`Ya tienes ${s.limit} personajes verificados, el máximo por cuenta. Quita uno de la lista para añadir este.`:'';
  return `<div class="profile-ownership"><h4>¿Es tu personaje?</h4><p>Verifícalo para vincularlo a tu cuenta. Te pediremos cambiar un título en el juego y lo comprobaremos en la web oficial de AION 2. Puedes verificar hasta ${s.limit} personajes por cuenta.</p>${why?`<p role="status">${esc(why)}</p>`:''}<div class="profile-connection-actions"><button class="button" data-hub-title ${s.canVerify?'':'disabled'}>Verificar que es mío</button></div></div>`;
 }
 function owned(s:any){
  if(!s.linked.length)return '';
  return `<div class="profile-owned"><h4>Tus personajes verificados (${s.linked.length}/${s.limit})</h4><ul>${s.linked.map((l:any,i:number)=>`<li><span>${esc(linkName(l))}<small>${esc(String(l.character?.region||'').toUpperCase())} · servidor ${esc(l.character?.serverId)}</small></span><button class="text-button" data-hub-unlink="${i}">Quitar</button></li>`).join('')}</ul></div>`;
 }
 const done:Record<string,string>={'title-challenge':'Cambia el título en el juego y pulsa «Comprobar».','title-cancel':'Verificación cancelada.','character-unlink':'Personaje quitado de tu cuenta.'};
 async function action(name:string,body:any){
  const uid=cloud.user?.id,v=version;if(!uid||!current(v,uid))return;
  connection.querySelectorAll<HTMLButtonElement>('[data-hub-title],[data-hub-title-check],[data-hub-title-cancel],[data-hub-unlink]').forEach(b=>b.disabled=true);connection.querySelector('[data-hub-message]')!.textContent='Consultando el servidor…';
  try{const data=await api('/api/companion.php?action='+name,uid,v,body);await load();const m=connection.querySelector('[data-hub-message]');if(cloud.user?.id===uid&&own()&&isProfile()&&m)m.textContent=data.message||done[name]||'';}catch(e){if(current(v,uid)){connections();showApiError(connection.querySelector('[data-hub-message]')!,e);}}
 }
 async function load(){
  const uid=cloud.user?.id;if(!uid||!own())return;const v=++version;controller?.abort();controller=new AbortController();connection.innerHTML='<section class="panel profile-connection" role="status">Consultando aplicación, verificación y licencia…</section>';
  const [a]=await Promise.allSettled([api('/api/companion.php?action=account-overview',uid,v)]);
  if(!current(v,uid))return;summary=a.status==='fulfilled'?a.value:null;billing=null;connections(a.status==='rejected'?'No pudimos consultar la conexión. '+a.reason.message:'','',a.status==='rejected'&&a.reason instanceof PageInsteadOfData);
 }
 function refresh(){
  if(isProfile()){document.getElementById('page-title')!.textContent=(own()?'Mi perfil · ':'Perfil de ')+character.info.profile.characterName;document.getElementById('crumb')!.textContent=own()?'Mi perfil':'Perfil del personaje';document.title=character.info.profile.characterName+' · Daeva Companion';}
  const next=own()?cloud.user!.id+':'+JSON.stringify(character.ref):'';progress();
  if(!next){signature='';version++;controller?.abort();summary=billing=null;connection.innerHTML='<section class="panel companion-panel"><h3>Perfil público</h3><p>Inicia sesión y elige tu personaje para ver tus herramientas, dispositivos y progreso. La selección no verifica la propiedad.</p></section>';return;}
  if(next!==signature){signature=next;void load();}redraw();
 }
 refresh();return {refresh,destroy(){version++;controller?.abort();personal.replaceChildren();connection.replaceChildren();}};
}
