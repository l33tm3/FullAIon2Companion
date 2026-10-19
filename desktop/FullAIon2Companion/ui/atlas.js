'use strict';
// The Atlas inside the app (tab «Mapa de Atreia»), no website needed: the 9 open-world atlases the app ships (tiles
// assets/atlas/<id>/4_x_y.webp, markers data/minimap-markers/<id>.json as [x, y, name], icons assets/map-icons), with
// filters, search and «Tú estás aquí» from MainWindow's AtlasLocator (renderCompanion data.atlas: the atlas id, atlas
// pixels, exact or approximate with a ring, the last place known, or a note saying why not). The first position shows the
// player's map; «Centrar en mí» follows him until the map is dragged or another map is chosen. Leaflet 1.9.4 (ui/vendor).
(()=>{
const MAPS=[['2','Altgard'],['1','Verteron'],['8','Morheim'],['9','Eltnen'],['5','Ishalgen'],['6','Poeta'],['3','Reshanta Inferior del Caos'],['7','Reshanta Medio del Caos'],['10','Reshanta Caótica Superior']];
const GROUPS={zonas:'Regiones',lugares:'Lugares',misiones:'Misiones',jefes:'Jefes',mascotas:'Mascotas',criaturas:'Criaturas',cubos:'Cubos ocultos',rastros:'Rastros empíreos',servicios:'Servicios',pnj:'PNJ',minerales:'Minerales',plantas:'Plantas',od:'Od'};
const SIZE={zonas:22,lugares:22,jefes:24,misiones:18,mascotas:20,criaturas:16,servicios:18,pnj:14,cubos:14,rastros:12,minerales:12,plantas:12,od:12};
// Gathering and the dense layers start hidden, as in the website's Atlas.
const HIDDEN_FIRST=['minerales','plantas','od','cubos','rastros'];
// Creatures and pets wear their race crest, as in the website's Atlas; each marker's creature comes from
// data/atlas-npcs/<map>.json (scripts/build-companion-atlas-npcs.py) and its card from data/npc/<id>.json.
const CRESTS={C:'cc416897be8a43e9eb6e.png',F:'ddb1f29d5d0e5a9b2786.png',N:'8ed4cb5ffa18466b5825.png',V:'74b78b71948ea757eff1.png'},RACES={C:'Cogni',F:'Fera',N:'Natura',V:'Varian'};
const GRADES={NormalMonster:'Normal',EliteMonster:'Élite',HeroMonster:'Héroe',LegendMonster:'Leyenda'},RANK={Epic:0,Unique:1,Legend:2,Rare:3,Special:4,Common:5};
const NAMES={Merchant:'Mercader',Armorsmithing:'Armería',Alchemy:'Alquimia',Teleport:'Teletransporte','Otros PNC':'Otros PNJ','NPC Shugo':'PNJ Shugo'};
const $=id=>document.getElementById(id),esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const plain=s=>String(s||'').normalize('NFD').replace(/[̀-ͯ]/g,'').toLowerCase();
const store={get(k,f){try{const v=localStorage.getItem('atlas:'+k);return v===null?f:JSON.parse(v);}catch{return f;}},set(k,v){try{localStorage.setItem('atlas:'+k,JSON.stringify(v));}catch{}}};
const key=c=>c.filter+':'+c.name,label=c=>NAMES[c.name]||c.name;
let npcs={},portraits={},tip=null,tipFor=null,initialLoad=Promise.resolve(),map=null,tiles=null,layer=null,meLayer=null,marksLayer=null,renderer=null,data=null,id=String(store.get('map','2')),hidden=store.get('hidden',null),follow=false,me=null,meShown=false,loading=0,redraw=0;
// A pet's soul creatures (Mascotas → its name): the NPC ids ringed on the map, whatever the filters.
let focus=null;
const sprites=new Map(),cards=new Map();
const open=()=>$('atlas')?.classList.contains('active');
// Smaller icons when zoomed out: thousands of creatures would otherwise cover the map in blots.
const zoomScale=()=>{const z=map?.getZoom()??4;return z<=2?.45:z===3?.6:z===4?.8:1;};

function init(){
 if(map){map.invalidateSize();return;}
 if(!window.L||!$('atlas-map'))return;
 const crs=L.extend({},L.CRS.Simple,{transformation:new L.Transformation(1/16,0,1/16,0)});
 map=L.map('atlas-map',{crs,minZoom:1,maxZoom:7,preferCanvas:true,attributionControl:false,maxBounds:[[-400,-400],[4496,4496]],maxBoundsViscosity:.8}).setView([2048,2048],3);
 // Icons drawn on the canvas, like the website's Atlas: thousands of markers stay fast.
 renderer=L.canvas({padding:.3,tolerance:4});const basic=renderer._updateCircle.bind(renderer);
 renderer._updateCircle=l=>{const path=l.options.sprite;if(!path)return basic(l);let img=sprites.get(path);
  if(!img){img=new Image();img.src=path;sprites.set(path,img);img.onload=()=>{clearTimeout(redraw);redraw=setTimeout(()=>renderer._redraw(),80);};}
  if(!renderer._drawing||!img.complete||!img.naturalWidth||l._empty())return;const ctx=renderer._ctx,p=l._point,s=(l.options.spriteSize||16)*zoomScale();
  if(l.options.portrait){ctx.save();ctx.beginPath();ctx.arc(p.x,p.y,s/2,0,Math.PI*2);ctx.clip();ctx.drawImage(img,p.x-s/2,p.y-s/2,s,s);ctx.restore();ctx.save();ctx.beginPath();ctx.arc(p.x,p.y,s/2,0,Math.PI*2);ctx.strokeStyle='#0b1117';ctx.lineWidth=1.5;ctx.stroke();ctx.restore();}
  else ctx.drawImage(img,p.x-s/2,p.y-s/2,s,s);
  if(l.options.selected){ctx.save();ctx.beginPath();ctx.arc(p.x,p.y,s/2+3,0,Math.PI*2);ctx.strokeStyle='#ff4968';ctx.lineWidth=3;ctx.stroke();ctx.restore();}};
 layer=L.featureGroup().addTo(map);marksLayer=L.featureGroup().addTo(map);meLayer=L.layerGroup().addTo(map);
 // The panel's window can be resized at will: Leaflet must learn the new size, or its canvas (where the markers are drawn
 // and hit) keeps the old one, and the markers outside it neither show their card on hover nor answer a click.
 let resizing=0;new ResizeObserver(()=>{clearTimeout(resizing);resizing=setTimeout(()=>{if(map&&open())map.invalidateSize({pan:false});},60);}).observe($('atlas-map'));
 // One shared hover card and one popup for all the markers (thousands of bindings would weigh on the panel).
 tip=L.tooltip({direction:'top',offset:[0,-8],className:'atlas-tip',opacity:1});
 layer.on('mouseover',e=>{const o=e.layer.options;tipFor=o;tip.setLatLng(e.layer.getLatLng()).setContent(card(o,true));map.openTooltip(tip);
  if(o.npc&&!cards.has(o.npc))void npc(o.npc).then(()=>{if(tipFor===o)tip.setContent(card(o,true));});
  else if(!o.npc&&o.group==='Mascotas'&&!petCards.has(o.title))void petOf(o.title).then(info=>{petCards.set(o.title,info);if(tipFor===o)tip.setContent(card(o,true));});});
 layer.on('mouseout',()=>{tipFor=null;map.closeTooltip(tip);});
 // While placing a pin or a route a click on a marker is a click on the map (the map's own click adds the point).
 layer.on('click',e=>{if(!mode)show(e.layer);});
 map.on('click',e=>markClick(e.latlng));map.on('dblclick',()=>{if(mode==='route')finishRoute();});
 $('atlas-pin').onclick=()=>setMode(mode==='pin'?'':'pin');$('atlas-route').onclick=()=>mode==='route'?finishRoute():setMode('route');
 map.on('dragstart',unfollow);
 const select=$('atlas-select');select.innerHTML=MAPS.map(([v,n])=>`<option value="${v}">${esc(n)}</option>`).join('');
 select.onchange=()=>{unfollow();focus=null;setMode('');void load(select.value);};
 $('atlas-search').oninput=()=>{focus=null;draw();frame();};
 $('atlas-all').onclick=()=>{hidden=[];store.set('hidden',hidden);filters();draw();};
 $('atlas-none').onclick=()=>{hidden=data.categories.map(key);store.set('hidden',hidden);filters();draw();};
 $('atlas-me').onclick=()=>{if(!me?.map)return;follow=true;$('atlas-me').classList.add('active');void showMe(true);};
 // The round buttons of the website's Atlas: events, the whole map, a wider map (both side panels away).
 const layout=$('atlas-layout'),relayout=()=>setTimeout(()=>map.invalidateSize(),60);
 layout.classList.toggle('no-events',!store.get('events',true));layout.classList.toggle('wide',store.get('wide',false));
 const showEvents=on=>{layout.classList.toggle('no-events',!on);store.set('events',on);relayout();};
 $('atlas-events-toggle').onclick=()=>{if(layout.classList.contains('wide')){layout.classList.remove('wide');store.set('wide',false);showEvents(true);return;}showEvents(layout.classList.contains('no-events'));};
 $('atlas-events-close').onclick=()=>showEvents(false);
 $('atlas-reset').onclick=()=>{unfollow();map.setView([2048,2048],3);};
 $('atlas-wide').onclick=()=>{const wide=!layout.classList.contains('wide');layout.classList.toggle('wide',wide);store.set('wide',wide);relayout();};
 initialLoad=load(id);
}
function unfollow(){if(!follow)return;follow=false;$('atlas-me').classList.remove('active');}
async function load(next){
 const token=++loading;$('atlas-count').textContent='Cargando mapa…';
 try{
  const d=await fetch('/data/minimap-markers/'+encodeURIComponent(next)+'.json').then(r=>{if(!r.ok)throw Error();return r.json();});
  const who=await fetch('/data/atlas-npcs/'+encodeURIComponent(next)+'.json').then(r=>r.ok?r.json():{}).catch(()=>({}));
  if(token!==loading)return;
  id=String(next);data=d;npcs=who.categories||{};portraits=who.icons||{};store.set('map',id);$('atlas-select').value=id;
  if(hidden===null)hidden=d.categories.filter(c=>HIDDEN_FIRST.includes(c.filter)).map(key);
  tiles?.remove();tiles=L.tileLayer('/assets/atlas/'+id+'/4_{x}_{y}.webp',{minNativeZoom:4,maxNativeZoom:4,minZoom:1,maxZoom:7,noWrap:true,bounds:[[0,0],[4096,4096]]}).addTo(map);tiles.bringToBack();
  filters();draw();map.setView([2048,2048],3);drawMe(false);drawMarks();summary();
 }catch{$('atlas-count').textContent='Este mapa no se pudo cargar.';}
}
function filters(){
 const groups=Object.keys(GROUPS).filter(g=>data.categories.some(c=>c.filter===g));
 $('atlas-filters').innerHTML=groups.map(g=>{const cats=data.categories.filter(c=>c.filter===g);return `<div class="atlas-group"><b>${esc(GROUPS[g])}</b><div>${cats.map(c=>`<button type="button" class="atlas-cat${hidden.includes(key(c))?' off':''}" data-atlas-cat="${esc(key(c))}" aria-pressed="${!hidden.includes(key(c))}"><img src="/assets/map-icons/${esc(c.icon)}" alt="">${esc(label(c))}<small>${c.markers.length}</small></button>`).join('')}</div></div>`;}).join('');
 $('atlas-filters').querySelectorAll('[data-atlas-cat]').forEach(b=>b.onclick=()=>{const k=b.dataset.atlasCat;hidden=hidden.includes(k)?hidden.filter(x=>x!==k):[...hidden,k];store.set('hidden',hidden);b.classList.toggle('off');b.setAttribute('aria-pressed',String(!hidden.includes(k)));draw();});
}
function draw(named){
 if(!map||!data)return;layer.clearLayers();
 const q=plain($('atlas-search').value.trim()),hits=[],f=q?null:focus;let count=0;
 for(const c of data.categories){
  // A search looks in every layer, shown or not: the result must not hide behind a filter. So do a pet's soul creatures.
  const off=hidden.includes(key(c));if(!q&&!f&&off)continue;
  const icon='/assets/map-icons/'+c.icon,name=label(c),who=npcs[c.id],crested=c.filter==='criaturas'||c.filter==='mascotas',landmark=/Bastion/i.test(c.name);
  c.markers.forEach(([x,y,title],i)=>{
   if(q&&!plain(title+' '+name).includes(q))return;
   // A creature or an NPC shows its portrait, as in the website; a creature without one, its race crest. A search rings what it found.
   const id_=who?.npcs?.[i]||0,race=who?.races?.[i],face=portraits[id_],crest=crested&&CRESTS[race],focused=!!f&&f.ids.has(id_);
   if(off&&!q&&!focused)return;
   const selected=!!q||focused||named!==undefined&&plain(title)===named,size=face?22:crest?20:SIZE[c.filter]||16;
   const m=L.circleMarker([y,x],{renderer,radius:size/2,sprite:face?'/assets/'+face:crest?'/assets/map-icons/'+CRESTS[race]:icon,portrait:!!face,spriteSize:selected?size+8:size,selected,stroke:false,fillOpacity:0,
    title,category:name,group:GROUPS[c.filter]||'',npc:id_,race}).addTo(layer);
   if(landmark)m.bindTooltip(esc(title),{permanent:true,direction:'bottom',className:'atlas-landmark',offset:[0,8]});
   count++;if((q||focused)&&hits.length<300)hits.push({title,name,at:[y,x],marker:m});
  });
 }
 $('atlas-count').textContent=`${count.toLocaleString('es-CL')} puntos visibles`;
 const results=$('atlas-results');results.hidden=!q&&!f;
 const list=hits.slice(0,40).map((h,i)=>`<button type="button" data-atlas-hit="${i}"><b>${esc(h.title)}</b><small>${esc(h.name)}</small></button>`).join('');
 results.innerHTML=q?(hits.length?list:'<p class="muted">Nada con ese nombre en este mapa.</p>')
  :f?`<p class="atlas-focus"><span>Alma de <b>${esc(f.label)}</b> · ${hits.length} ${hits.length===1?'lugar':'lugares'}</span><button type="button" data-atlas-unfocus title="Quitar" aria-label="Quitar">×</button></p>`+(hits.length?list:'<p class="muted">Las criaturas de su alma no están en los mapas abiertos: viven en mazmorras o en el Abismo.</p>'):'';
 results.querySelector('[data-atlas-unfocus]')?.addEventListener('click',()=>{focus=null;draw();});
 layer.hits=hits;
 results.querySelectorAll('[data-atlas-hit]').forEach(b=>b.onclick=()=>{const h=hits[Number(b.dataset.atlasHit)];unfollow();map.setView(h.at,5,{animate:false});show(h.marker);});
}
// A search frames what it found (up to 300 places), as the website's Atlas does.
function frame(){const q=$('atlas-search').value.trim();if(!q||!map)return;const pts=[];layer.eachLayer(l=>{if(pts.length<=300)pts.push(l.getLatLng());});
 if(pts.length&&pts.length<=300){unfollow();map.fitBounds(L.latLngBounds(pts),{maxZoom:5,padding:[60,60],animate:false});}}
// A marker's card: for a creature, its catalog record (level, grade, hunted or not, its pet soul, the best of its loot);
// for anything else, its name and category. The hover card is short; the click one adds the button to the full card.
const pct=c=>typeof c==='number'?(c>=1?`${Math.round(c*10)/10}%`:`${Math.round(c*100)/100}%`):'';
async function npc(id){if(cards.has(id))return cards.get(id);let r=null;try{const res=await fetch('/data/npc/'+id+'.json');if(res.ok)r=await res.json();}catch{}cards.set(id,r);return r;}
// The loot as the overlay's card groups it: objects (souls, chests, materials) first, then the armour and the weapons
// of one series and grade in one line («Equipo de tormenta ×7»), each by grade and then chance.
function lootLines(drops){
 const lines=[],sets=new Map(),qty=d=>d.min&&d.max&&d.min!==d.max?`${d.min}–${d.max}`:String(d.max||d.min||1);
 for(const d of drops){const kind=Math.floor((d.id||0)/1e7);
  if(kind!==11&&kind!==21){lines.push({gear:false,name:d.name,grade:d.grade,icon:d.icon,sub:`${d.category||''} · ${qty(d)}`,top:d.chance??0,count:1,odds:pct(d.chance)});continue;}
  const cat=d.category||'',tail=d.name.toLowerCase().startsWith(cat.toLowerCase())?d.name.slice(cat.length).trim():d.name.replace(/^\S+\s/,'');
  const k=kind+':'+d.grade+':'+(d.id%100000);let g=sets.get(k);
  if(!g){g={gear:true,name:(kind===21?'Equipo ':'Armas ')+tail,grade:d.grade,icon:d.icon,top:0,low:Infinity,count:0};sets.set(k,g);lines.push(g);}
  g.count++;g.top=Math.max(g.top,d.chance??0);g.low=Math.min(g.low,d.chance??0);}
 for(const g of sets.values()){g.odds=g.count>1&&pct(g.low)!==pct(g.top)?`${pct(g.low).slice(0,-1)}–${pct(g.top)}`:pct(g.top);g.sub=g.count>1?`${g.count} piezas`:'1 pieza';}
 return lines.sort((a,b)=>(a.gear-b.gear)||(RANK[a.grade]??9)-(RANK[b.grade]??9)||b.top-a.top);
}
// A pet's marker (no creature of its own): whose soul it is, from assets/aion2/pets/pets.json.
let pets=null;
async function petOf(title){
 pets??=fetch('/assets/aion2/pets/pets.json').then(r=>r.json()).catch(()=>null);const book=await pets;if(!book)return null;
 const want=plain(title),id=Object.keys(book.pets).find(k=>plain(book.pets[k].names?.es)===want);if(!id)return null;
 const sources=Object.entries(book.monsters).filter(([,p])=>String(p)===id).map(([n])=>Number(n)).slice(0,30);
 const records=(await Promise.all(sources.map(npc))).filter(r=>r&&r.isMonster);
 return {name:book.pets[id].names.es,sources:[...new Map(records.map(r=>[r.name,r])).values()].slice(0,5)};
}
function petCard(o,info){
 let h=`<div class="atlas-card"><b>${esc(o.title)}</b><small>Mascota · ${esc(o.category)}</small>`;
 if(info===undefined)return h+'<small class="muted">Buscando de quién sale su alma…</small></div>';
 if(!info?.sources.length)return h+'<small class="muted">Sin criaturas conocidas para su alma.</small></div>';
 return h+`<div class="atlas-loot"><small>Su alma sale de</small>${info.sources.map(r=>`<span>${esc(r.name)}<em>Nv. ${esc(r.level)}</em></span>`).join('')}</div></div>`;
}
const petCards=new Map();
const APP_MAPS={altgard:1,verteron:1,morheim:1,eltnen:1,ishalgen:1,poeta:1};
function card(o,hover){
 if(!o.npc&&o.group==='Mascotas')return petCard(o,petCards.get(o.title));
 if(!o.npc)return `<div class="atlas-card"><b>${esc(o.title)}</b><small>${esc(o.category)}${o.group&&o.group!==o.category?' · '+esc(o.group):''}</small></div>`;
 const n=cards.get(o.npc),crest=CRESTS[o.race],face=portraits[o.npc];
 const sub=[n?.level?'Nv. '+n.level:'',GRADES[n?.grade]||'',n?.race==='dark'?'Asmodianos':n?.race==='light'?'Elios':''].filter(Boolean).join(' · ');
 const race=RACES[o.race]||n?.creatureName||'';
 let h=`<div class="atlas-card${hover?'':' full'}"><div class="atlas-card-head">${face?`<img class="face" src="/assets/${esc(face)}" alt="">`:crest?`<img src="/assets/map-icons/${crest}" alt="">`:''}<div><b>${esc(n?.name||o.title)}</b><small>${esc(sub||o.category)}</small>${race?`<span class="atlas-race">${crest?`<img src="/assets/map-icons/${crest}" alt="">`:''}${esc(race)}</span>`:''}</div></div>`;
 if(n===undefined)return h+'<small class="muted">Cargando ficha…</small></div>';
 if(!n)return h+'<small class="muted">Sin ficha en el catálogo.</small></div>';
 const drops=Array.isArray(n.drops)?n.drops:[],soul=drops.find(d=>d.category==='Mascota');
 h+=`<div class="atlas-tags">${n.isMonster?'<span class="atlas-tag hunt">Cazable</span>':'<span class="atlas-tag">No cazable</span>'}${soul?`<span class="atlas-tag soul">${esc(soul.name.replace(/\s*\((Encuadernado|Vinculado)\)/,''))} · ${pct(soul.chance)}</span>`:''}</div>`;
 if(hover){
  const best=lootLines(drops.filter(d=>d!==soul)).slice(0,3);
  if(drops.length)h+=`<div class="atlas-loot"><small>Botín · ${drops.length} objetos</small>${best.map(l=>`<span class="g-${esc(l.grade)}">${esc(l.name)}${l.count>1?` ×${l.count}`:''}<em>${l.odds}</em></span>`).join('')}</div>`;
  else h+='<small class="muted">Sin botín en el catálogo.</small>';
  return h+'<small class="muted atlas-hint">Clic para más</small></div>';
 }
 // The full card, as the website's: where it lives (each map of the app takes you to its places), its loot with icons.
 const spots=Object.entries(n.spawns||{});
 if(spots.length)h+=`<h4>Dónde encontrarlo</h4><div class="atlas-spots">${spots.map(([slug,v])=>APP_MAPS[slug]?`<button type="button" data-atlas-find="${esc(n.name)}" data-atlas-map="${esc(slug)}">${esc(v.name)} · ${v.points?.length||0} ubicaciones</button>`:`<span>${esc(v.name)} · ${v.points?.length||0} ubicaciones</span>`).join('')}</div>`;
 if(drops.length)h+=`<h4>Botín <small>${drops.length}</small></h4><div class="atlas-loot-list">${lootLines(drops).map(l=>`<div class="atlas-loot-row">${l.icon&&/^\/assets\/game\/[\w.-]+$/.test(l.icon)?`<img src="${esc(l.icon)}" alt="">`:'<i></i>'}<div><b class="g-${esc(l.grade)}">${esc(l.name)}</b><small>${esc(l.sub)}</small></div><em>${l.odds}</em></div>`).join('')}</div>`;
 else h+='<small class="muted">Sin botín en el catálogo.</small>';
 return h+`<div class="atlas-card-foot"><small class="muted">ID ${o.npc} · Datos del cliente</small><button type="button" class="atlas-full" data-npc="${o.npc}">Ver ficha completa</button></div></div>`;
}
async function show(m){const o=m.options;tipFor=null;map.closeTooltip(tip);
 const pop=L.popup({maxWidth:380,minWidth:300,autoPanPaddingTopLeft:[10,60]}).setLatLng(m.getLatLng()).setContent(card(o,false)).openOn(map);
 if(o.npc&&!cards.has(o.npc)){await npc(o.npc);if(pop.isOpen())pop.setContent(card(o,false));}
 else if(!o.npc&&o.group==='Mascotas'&&!petCards.has(o.title)){petCards.set(o.title,await petOf(o.title));if(pop.isOpen())pop.setContent(card(o,false));}}
// «Ver en el mapa» of a creature card: its name, on the map where it is (the catalog's map, else the current one).
async function find(name,slug){
 openTabSafe();await initialLoad;const target={altgard:'2',verteron:'1',morheim:'8',eltnen:'9',ishalgen:'5',poeta:'6'}[slug]||id;
 unfollow();focus=null;if(target!==id)await load(target);$('atlas-search').value=name;draw(plain(name));
 const first=$('atlas-results').querySelector('[data-atlas-hit]');if(first)first.click();
}
// Mascotas → a pet's name: the creatures whose soul it is (NPC ids), on the app's map where most of them live (the open one
// if it has any), ringed and framed, with the first one's card open, as a search does.
async function findNpcs(ids,label){
 openTabSafe();await initialLoad;unfollow();setMode('');
 const want=new Set(ids.map(Number).filter(Boolean)),tally=who=>{let n=0;for(const c of Object.values(who?.categories||{}))for(const x of c.npcs||[])if(want.has(x))n++;return n;};
 let target=id,best=tally({categories:npcs});
 if(!best)for(const [m] of MAPS){if(m===id)continue;const who=await fetch('/data/atlas-npcs/'+m+'.json').then(r=>r.ok?r.json():null).catch(()=>null),n=tally(who);if(n>best){best=n;target=m;}}
 focus={ids:want,label:label||''};$('atlas-search').value='';
 if(target!==id)await load(target);else draw();
 const hits=layer.hits||[];if(!hits.length)return;
 map.fitBounds(L.latLngBounds(hits.map(h=>h.at)),{maxZoom:5,padding:[70,70],animate:false});show(hits[0].marker);
}
function openTabSafe(){if(!open())window.openTab?.('atlas');init();}
async function showMe(center){if(!me?.map||!map)return;if(me.map!==id)await load(me.map);drawMe(center);}
function drawMe(center){
 if(!map)return;meLayer.clearLayers();if(!me?.map||me.map!==id)return;
 const at=L.latLng(me.y,me.x),approximate=!me.exact||me.stale;
 if(approximate&&me.ring>0)L.circle(at,{radius:me.ring,interactive:false,color:'#5fd3b4',weight:1,opacity:.7,fillOpacity:.12}).addTo(meLayer);
 L.marker(at,{interactive:false,keyboard:false,zIndexOffset:1000,icon:L.divIcon({className:'atlas-me'+(approximate?' approximate':''),html:'<span></span><b>Tú</b>',iconSize:[20,20],iconAnchor:[10,10]})}).addTo(meLayer);
 if(center)map.setView(at,Math.max(map.getZoom(),5),{animate:false});
}
function position(fix){
 me=fix&&fix.type==='daeva-position'?fix:null;const status=$('atlas-me-status'),button=$('atlas-me');if(!status)return;
 status.hidden=!me;status.classList.toggle('waiting',!me?.map);button.classList.toggle('unavailable',!me?.map);
 status.textContent=!me?'':me.map?`Tú · ${me.mapName}${me.stale?' · última posición':me.exact?'':' · posición aproximada'}`:me.note||'Esperando tu posición…';
 button.title=me?.map?'Centrar en mí':'Centrar en mí · '+(me?.note||'esperando tu posición');
 if(!map||!data||!open())return;
 if(me?.map&&(follow||!meShown)){meShown=true;void showMe(true);return;}
 drawMe(false);
}
const openTab=window.openTab;
window.openTab=(name,section)=>{openTab?.(name,section);if(name!=='atlas')return;init();if(me?.map&&!meShown&&map&&data){meShown=true;void showMe(true);}};
// «Próximos eventos» beside the map, as in the website's Atlas: the clock and the times of the region chosen for the
// alerts (settings.RegionOffset, minutes from UTC), sound, and each event with its countdown and its alert switch.
let eventKeys='';
const pad=n=>String(n).padStart(2,'0'),countdown=v=>{v=Math.max(0,Math.floor(v));const h=Math.floor(v/3600),m=Math.floor(v%3600/60),sec=v%60;return h?`${h}:${pad(m)}:${pad(sec)}`:`${m}:${pad(sec)}`;};
function events(d){
 const list=$('atlas-event-list');if(!list||!open())return;const offset=Number(d.settings?.RegionOffset??-180)*60000,local=t=>new Date(t+offset);
 const now=local(Date.now());$('atlas-clock').textContent=`${pad(now.getUTCHours())}:${pad(now.getUTCMinutes())}:${pad(now.getUTCSeconds())}`;
 const region=$('atlas-region');if(document.activeElement!==region)region.value=String(d.settings?.RegionOffset??-180);
 const sound=$('atlas-sound');sound.textContent=d.settings?.Sounds?'Sonido activado':'Activar sonido';sound.classList.toggle('active',!!d.settings?.Sounds);
 const rows=(d.events||[]).slice(0,30),keys=rows.map(e=>e.Id+':'+e.At+':'+e.Alert).join('|');
 if(keys!==eventKeys){eventKeys=keys;list.innerHTML=rows.length?rows.map(e=>{const at=local(new Date(e.At).getTime());
  return `<div class="atlas-event" data-color="${esc(e.Color)}"><div><b>${esc(e.Name)}</b><small>${pad(at.getUTCHours())}:${pad(at.getUTCMinutes())} · ${at.getUTCDate()}-${at.toLocaleDateString('es-CL',{month:'short',timeZone:'UTC'}).replace('.','')}</small></div><strong data-remaining>${countdown(e.Remaining)}</strong><label class="atlas-switch" title="Aviso para ${esc(e.Name)}"><input type="checkbox" ${e.Alert?'checked':''} data-event="${esc(e.Id)}" aria-label="Aviso para ${esc(e.Name)}"><i></i></label></div>`;}).join(''):'<p class="muted">Sin eventos próximos.</p>';
  // The panel's policy blocks style attributes: the colour goes through the style object.
  list.querySelectorAll('[data-color]').forEach(el=>el.style.setProperty('--event',el.dataset.color));}
 else list.querySelectorAll('[data-remaining]').forEach((el,i)=>{const v=countdown(rows[i].Remaining);if(el.textContent!==v)el.textContent=v;});
}
const render=window.renderCompanion;
window.renderCompanion=d=>{render?.(d);if(d){position(d.atlas||null);events(d);if(d.atlasMarks)marksFromApp(d.atlasMarks.maps,d.atlasMarks.version);}};
document.addEventListener('click',e=>{const b=e.target.closest('[data-atlas-find]');if(b){e.preventDefault();void find(b.dataset.atlasFind,b.dataset.atlasMap);}
 const n=e.target.closest('[data-atlas-npcs]');if(n){e.preventDefault();void findNpcs(n.dataset.atlasNpcs.split(','),n.dataset.atlasLabel);}});
// ---- Tus marcas: the player's own pins and routes, per map, kept by the app (atlas-marks.json) and drawn on the overlay's
// minimap too. A pin: the pin button, then a click on the map. A route: the route button, a click per point, then a double
// click, Enter or the button again; Esc drops it. A click on a pin or a route names or deletes it.
let marks={},marksFrom=-1,savedAt=0,mode='',draft=null;
if(typeof actionLabels==='object')actionLabels['atlas-marks']='Guardando tus marcas…';
const mapMarks=()=>marks[id]||(marks[id]={pins:[],routes:[]});
const point=at=>[Math.round(at.lng*10)/10,Math.round(at.lat*10)/10];
// Each map's marks go to the app whole; a change made while the last one is still on its way waits its turn (the panel
// sends one action of a kind at a time), so none is lost.
const marksDirty=new Set();
function saveMarks(){savedAt=Date.now();marksDirty.add(id);flushMarks();drawMarks();summary();}
function flushMarks(){if(!marksDirty.size)return;if(typeof isPending==='function'&&isPending('atlas-marks')){setTimeout(flushMarks,250);return;}
 const map_=marksDirty.values().next().value;marksDirty.delete(map_);const m=marks[map_]||{pins:[],routes:[]};savedAt=Date.now();
 send('atlas-marks',{map:map_,pins:m.pins,routes:m.routes});if(marksDirty.size)setTimeout(flushMarks,250);}
function setMode(next){
 mode=next;draft=next==='route'?[]:null;if(!map)return;
 $('atlas-pin').classList.toggle('active',mode==='pin');$('atlas-route').classList.toggle('active',mode==='route');
 map.getContainer().classList.toggle('atlas-drawing',!!mode);if(mode)map.doubleClickZoom.disable();else map.doubleClickZoom.enable();
 hint();drawMarks();
}
function hint(){const el=$('atlas-mode');if(!el)return;el.hidden=!mode;
 el.textContent=mode==='pin'?'Haz clic en el mapa para poner un pin · Esc para cancelar':mode==='route'?`Clic en cada punto de la ruta (${draft?.length||0}) · doble clic o Enter para terminar · Esc para cancelar`:'';}
function markClick(at){
 if(mode==='pin'){const m=mapMarks();if(m.pins.length>=200)return;const [x,y]=point(at);m.pins.push({x,y,name:'Pin '+(m.pins.length+1)});setMode('');saveMarks();return;}
 if(mode==='route'){const p=point(at),last=draft[draft.length-1];if(last&&Math.abs(last[0]-p[0])+Math.abs(last[1]-p[1])<1)return;if(draft.length<400)draft.push(p);hint();drawMarks();}
}
function finishRoute(){const pts=draft||[];if(pts.length>=2){const m=mapMarks();if(m.routes.length<50)m.routes.push({name:'Ruta '+(m.routes.length+1),points:pts});setMode('');saveMarks();}else setMode('');}
document.addEventListener('keydown',e=>{if(!mode||!open())return;if(e.key==='Escape'){e.preventDefault();setMode('');}else if(e.key==='Enter'&&mode==='route'){e.preventDefault();finishRoute();}});
function drawMarks(){
 if(!map)return;marksLayer.clearLayers();const m=marks[id]||{pins:[],routes:[]};
 const routes=[...m.routes.map((r,i)=>({r,i})),...(draft?.length?[{r:{points:draft},i:-1}]:[])];
 for(const {r,i} of routes){const at=r.points.map(([x,y])=>[y,x]);
  const line=L.polyline(at,{color:'#71ded6',weight:4,opacity:i<0?.6:.95,dashArray:'9 6',lineCap:'round'}).addTo(marksLayer);
  at.forEach((p,k)=>L.circleMarker(p,{radius:k===0?5:3,color:'#0b1117',weight:1,fillColor:k===0?'#e9c77e':'#71ded6',fillOpacity:1,interactive:false}).addTo(marksLayer));
  if(i>=0)line.on('click',e=>{if(mode)return;L.DomEvent.stop(e);markCard(e.latlng,'route',i);});}
 m.pins.forEach((p,i)=>{const pin=L.marker([p.y,p.x],{keyboard:false,zIndexOffset:900,icon:L.divIcon({className:'atlas-pin',html:`<span></span><b>${esc(p.name)}</b>`,iconSize:[22,30],iconAnchor:[11,28]})}).addTo(marksLayer);
  pin.on('click',e=>{if(mode)return;L.DomEvent.stop(e);markCard(pin.getLatLng(),'pin',i);});});
}
function markCard(at,kind,i){
 const m=mapMarks(),list=kind==='pin'?m.pins:m.routes,item=list[i];if(!item)return;
 const pop=L.popup({minWidth:230,maxWidth:280}).setLatLng(at).setContent(`<div class="atlas-card atlas-mark-card"><b>${kind==='pin'?'Tu pin':'Tu ruta'}</b><input type="text" maxlength="40" value="${esc(item.name)}" data-mark-name aria-label="Nombre"><small class="muted">${kind==='route'?item.points.length+' puntos · ':''}También se ve en el minimapa del overlay.</small><div class="atlas-mark-actions"><button type="button" data-mark-save>Guardar</button><button type="button" class="danger" data-mark-delete>Borrar</button></div></div>`).openOn(map);
 const el=pop.getElement();
 el.querySelector('[data-mark-save]').onclick=()=>{item.name=(el.querySelector('[data-mark-name]').value.trim()||item.name).slice(0,40);map.closePopup(pop);saveMarks();};
 el.querySelector('[data-mark-delete]').onclick=()=>{list.splice(i,1);map.closePopup(pop);saveMarks();};
}
function summary(){const el=$('atlas-marks');if(!el)return;const m=marks[id]||{pins:[],routes:[]},any=m.pins.length||m.routes.length;
 el.innerHTML=`<b>Tus marcas</b><small>${any?`${m.pins.length} ${m.pins.length===1?'pin':'pines'} · ${m.routes.length} ${m.routes.length===1?'ruta':'rutas'} en este mapa · también en el minimapa`:'Pon pines y rutas con los botones del mapa: se ven también en el minimapa del overlay.'}</small>${any?'<button type="button" data-marks-clear>Borrar las de este mapa</button>':''}`;
 el.querySelector('[data-marks-clear]')?.addEventListener('click',()=>{if(!confirm('¿Borrar tus pines y rutas de este mapa?'))return;marks[id]={pins:[],routes:[]};saveMarks();});}
// The app's copy (data.atlasMarks): taken when it comes (start, panel reload), except right after an edit of the panel's own.
function marksFromApp(list,version){if(!list||version===marksFrom||Date.now()-savedAt<4000)return;marksFrom=version;marks=list;drawMarks();summary();}
window.atlasFindNpcs=findNpcs;
window.atlasFind=find;window.atlasPosition=position;window.atlasEvents=events;window.atlasMap=()=>map; // also for a quick check from the console
// Opened straight on the Atlas (index.html#atlas): app.js switched the tab before this script ran.
setTimeout(()=>{if(open())window.openTab('atlas');},0);
})();
