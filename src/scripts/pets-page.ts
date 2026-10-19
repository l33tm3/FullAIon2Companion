import type {ProgressCloud} from '../lib/cloud';
export function petsPage(cloud:ProgressCloud,getCharacter:()=>any,host:HTMLElement){
 const esc=(s:unknown)=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
 const time=(v:string|null)=>v?new Date(v).toLocaleString('es-CL',{timeZone:'America/Santiago',day:'2-digit',month:'short',hour:'2-digit',minute:'2-digit'}):'Sin actividad recibida';
 const empty=(icon:string,title:string,text:string)=>`<div class="settings-empty"><i data-lucide="${icon}"></i><strong>${esc(title)}</strong><p>${esc(text)}</p></div>`;
 const redraw=()=>document.dispatchEvent(new Event('daeva-redraw-icons'));
 let version=0,signature='';
 function refresh(){
  const c=getCharacter();if(!cloud.user||!c){version++;signature='';host.innerHTML='';return;}
  if(!document.getElementById('view-pets')!.classList.contains('active'))return;
  const list=Object.entries(cloud.records).filter(([k])=>k.startsWith('pets:')).map(([,v])=>v as any).sort((a,b)=>String(b.capturedAt).localeCompare(String(a.capturedAt)));
  const next=JSON.stringify([cloud.user.id,c.ref,list]);if(next===signature)return;signature=next;const v=++version;
  host.innerHTML='<p role="status">Cargando tu colección…</p>';void petsBox(host,list,v);
 }
 let petCatalog:Promise<any>|null=null;
 async function petsBox(host:HTMLElement,list:any[],v:number){
  try{petCatalog??=fetch('/data/pets.json').then(r=>{if(!r.ok)throw Error('Catálogo no disponible');return r.json();});const cat=await petCatalog;if(v!==version||!host.isConnected)return;
   const total=Object.keys(cat.pets).length,need=(l:number)=>l===0?5:l===1?25:l===2?75:0;
   const pages:string[][]=[];
   const active=getCharacter();if(active){list=list.filter(c=>String(c.serverId)===String(active.ref.serverId)&&String(c.name).toLocaleLowerCase()===String(active.info.profile.characterName).toLocaleLowerCase());}
   if(!list.length){host.innerHTML=empty('paw-print','Aún no hay una colección para tu personaje','Entra al juego con este personaje y sincroniza Mascotas desde el ejecutable.');redraw();return;}
   host.innerHTML=list.map((c:any,i:number)=>{const pets=(c.pets||[]).filter((p:number[])=>cat.pets[p[0]]);const owned=pets.filter((p:number[])=>p[1]>=1);const lv=[0,1,2,3].map(l=>pets.filter((p:number[])=>p[1]===l).length);
    const cats=cat.categories.map((k:string)=>{const all=Object.values(cat.pets).filter((x:any)=>x[2]===k).length,have=owned.filter((p:number[])=>cat.pets[p[0]][2]===k).length;return `<div class="pet-web-cat"><span>${esc(k)}</span><div class="pet-web-bar"><i style="width:${all?have/all*100:0}%"></i></div><b>${have}/${all}</b></div>`;}).join('');
    const days=Object.entries(c.history||{}).slice(-30) as [string,number[]][];const max=Math.max(1,...days.map(d=>d[1][0]));const first=days[0]?.[1][0]??owned.length;
    const chart=days.length?`<div class="pet-web-hist" role="img" aria-label="Mascotas obtenidas en los últimos ${days.length} días">${days.map(([d,h])=>`<i style="height:${Math.max(4,h[0]/max*100)}%" title="${esc(d)}: ${h[0]} mascotas · Nv.3 ${h[3]} · ${h[4]} almas"></i>`).join('')}</div><small>${days.length} días registrados · ${owned.length-first>=0?'+':''}${owned.length-first} mascotas en el periodo · ${days.reduce((n,d)=>n+(d[1][4]||0),0)} almas</small>`:'';
    const rows=[...pets].sort((a:number[],b:number[])=>b[1]-a[1]||String(cat.pets[a[0]][0]).localeCompare(cat.pets[b[0]][0])).map((p:number[])=>{const x=cat.pets[p[0]],n=need(p[1]);const icon=/^\/assets\/[\w/.-]+$/.test(x[1])?`<img src="${esc(x[1])}" alt="" loading="lazy">`:'<span class="pet-web-noicon" aria-label="Sin imagen disponible">◇</span>';return `<div class="pet-web-row lv${p[1]}">${icon}<span>${esc(x[0])}<small>${esc(x[2])}</small></span><b>${p[1]?'Nv. '+p[1]:'Empezada'}</b><em>${n?p[2]+'/'+n:'Máx.'}</em></div>`;});pages.push(rows);
    return `<details class="settings-card pet-web" ${i===0?'open':''}><summary><div><strong>${esc(c.name)}</strong><small>Servidor ${esc(c.serverId)} · actualizado ${esc(time(c.capturedAt))}</small></div><div class="pet-web-total"><b>${owned.length}</b><span>de ${total}</span></div></summary>
     <div class="pet-web-levels"><span>Nv. 1 <b>${lv[1]}</b></span><span>Nv. 2 <b>${lv[2]}</b></span><span>Nv. 3 <b>${lv[3]}</b></span>${lv[0]?`<span>Empezadas <b>${lv[0]}</b></span>`:''}</div>${chart}<div class="pet-web-cats">${cats}</div><div class="pet-web-list">${rows.slice(0,12).join('')}</div><div class="pet-web-pager"><button class="button secondary" data-pet-prev disabled>← Anterior</button><span data-pet-page>1–${Math.min(12,rows.length)} de ${rows.length}</span><button class="button secondary" data-pet-next ${rows.length<=12?'disabled':''}>Siguiente →</button></div></details>`;}).join('');
   host.querySelectorAll<HTMLElement>('.pet-web').forEach((card,i)=>{let page=0;const rows=pages[i],prev=card.querySelector<HTMLButtonElement>('[data-pet-prev]')!,next=card.querySelector<HTMLButtonElement>('[data-pet-next]')!;const draw=()=>{card.querySelector('.pet-web-list')!.innerHTML=rows.slice(page*12,(page+1)*12).join('');card.querySelector('[data-pet-page]')!.textContent=`${page*12+1}–${Math.min((page+1)*12,rows.length)} de ${rows.length}`;prev.disabled=page===0;next.disabled=(page+1)*12>=rows.length;};prev.onclick=()=>{page--;draw();};next.onclick=()=>{page++;draw();};});
  }catch(e){if(v===version&&host.isConnected)host.innerHTML=empty('paw-print','No pudimos mostrar tus mascotas',(e as Error).message);}
  redraw();
 }
 return {refresh};
}
