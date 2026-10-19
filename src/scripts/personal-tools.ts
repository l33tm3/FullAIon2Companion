import type {ProgressCloud} from '../lib/cloud';
import {sameCharacter,personalViews} from '../lib/character-context';
import {profileUrl,profileRoute} from './character-profile';

export function personalTools(cloud:ProgressCloud,getCharacter:()=>any,go:(view:string)=>void,choose:()=>void){
 const group=document.getElementById('personal-navigation')!,context=document.getElementById('personal-context')!,empty=document.getElementById('personal-empty')!;
 const esc=(v:any)=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
 let signature='';
 function selected(){const c=getCharacter();return cloud.user&&sameCharacter(c?.ref,cloud.records['ref:mine'])?c:null;}
 function update(view:string){
  group.hidden=!cloud.user;
  const c=selected(),ref=cloud.user?cloud.records['ref:mine']:null,ownProfile=view==='profile'&&sameCharacter(profileRoute(),ref),personal=personalViews.has(view)||ownProfile;
  group.querySelector('[data-own-profile]')?.classList.toggle('active',ownProfile);
  document.getElementById('personal-nav-name')!.textContent=c?.info?.profile?.characterName||'Elige tu personaje';
  context.hidden=!cloud.user||!personal||view==='profile';empty.hidden=!cloud.user||!personalViews.has(view)||!!c;
  const active=document.getElementById('view-'+view);if(personal&&active)active.classList.toggle('active',!!c&&!!cloud.user);
  const key=JSON.stringify([cloud.user?.id,ref,c?.info?.profile]);
  if(key!==signature){signature=key;const p=c?.info?.profile;
   context.innerHTML=`<div><span class="eyebrow">TU PERSONAJE ACTIVO</span><strong>${esc(p?.characterName||'Selecciona tu Daeva')}</strong><small>${p?`${esc(p.serverName||ref.serverId)} · ${esc(p.className)}${p.legionName?' · '+esc(p.legionName):''}`:'Las herramientas se guardan en tu cuenta para el personaje que elijas.'}</small></div><div class="settings-buttons">${c?`<a class="text-button" href="${esc(profileUrl(ref))}">Ver mi perfil →</a>`:''}<button class="button secondary" data-personal-choose>${c?'Cambiar personaje':'Elegir personaje'}</button></div>`;
   context.querySelector('[data-personal-choose]')!.addEventListener('click',choose);
   empty.innerHTML=`<h2>${ref?'Cargando tu personaje…':'Primero, elige tu personaje'}</h2><p>${ref?'Estamos consultando su ficha. Si tarda, puedes volver a elegirlo para reintentar.':'Selecciona tu Daeva para consultar equipo y habilidades, organizar tu hoja de ruta y guardar tu progreso.'}</p><button class="button" data-personal-choose>Buscar personaje</button>`;
   empty.querySelector('[data-personal-choose]')!.addEventListener('click',choose);
  }
 }
 document.addEventListener('click',e=>{if((e.target as Element).closest('[data-own-profile]')){const c=selected();if(c)location.hash=profileUrl(c.ref).slice(1);else choose();}});
 document.addEventListener('daeva-personal-tool',e=>{const view=(e as CustomEvent<string>).detail;if(view==='profile'){const c=selected();if(c)location.hash=profileUrl(c.ref).slice(1);else choose();}else if(personalViews.has(view))go(view);});
 return {update,selected};
}
