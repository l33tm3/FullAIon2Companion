// The pet families as the user's badges (Cognis, Feras, Naturas, Varians); Especial keeps its name.
const PET_BADGES={intelecto:'cognis',cognis:'cognis',salvaje:'feras',feras:'feras',naturaleza:'naturas',naturas:'naturas','transformación':'varians',transformacion:'varians',varians:'varians'};
function petBadge(cat,cls){const k=PET_BADGES[String(cat||'').toLowerCase()];return k?`<img class="${cls}" src="/assets/pet-badges/${k}.png" alt="${esc(cat)}" title="${esc(cat)}">`:`<span class="${cls} pet-badge-text">${esc(cat||'')}</span>`;}
'use strict';
let data=null,initialized=false;
const $=id=>document.getElementById(id),esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
// Numbers, dates and times in the panel's language (uiLang is set further down; before that, Spanish).
const uiLoc=()=>{try{return uiLang==='pt'?'pt-BR':uiLang==='en'?'en-GB':uiLang==='fr'?'fr-FR':'es-CL';}catch{return 'es-CL';}};
const number=v=>Number(v||0).toLocaleString(uiLoc(),{maximumFractionDigits:0}),time=v=>{v=Math.max(0,Math.floor(v));return v>=86400?`${Math.floor(v/86400)}d ${Math.floor(v%86400/3600)}h`:v>=3600?`${Math.floor(v/3600)}:${String(Math.floor(v%3600/60)).padStart(2,'0')}:${String(v%60).padStart(2,'0')}`:`${Math.floor(v/60)}:${String(v%60).padStart(2,'0')}`;};
const pendingActions=new Map();let actionSequence=0;
const actionLabels={'donate':'Abriendo PayPal…','license-check':'Comprobando licencia…','title-start':'Pidiendo la verificación…','title-check':'Comprobando en la web oficial…','title-cancel':'Cancelando la verificación…','pets-upload':'Subiendo tus mascotas…',pull:'Descargando preferencias…',pair:'Vinculando dispositivo…','pair-web':'Abriendo Daeva Companion…','update-check':'Buscando actualizaciones…','update-download':'Descargando actualización…','own-profile':'Abriendo tu perfil…'};
function actionNotice(text,kind='info'){let el=$('action-feedback');if(!el){el=document.createElement('div');el.id='action-feedback';el.setAttribute('role','status');el.setAttribute('aria-live','polite');document.body.append(el);}el.textContent=text;el.dataset.kind=kind;el.hidden=false;}
const isPending=command=>[...pendingActions.values()].some(x=>x.command===command);
const send=(command,props={})=>{if(isPending(command)){actionNotice('Esta acción sigue en curso. Espera su respuesta.');return;}if(!window.chrome?.webview?.postMessage){actionNotice('El programa no está conectado a este panel. Vuelve a abrirlo.','error');return;}const requestId='action-'+(++actionSequence);pendingActions.set(requestId,{command});document.querySelectorAll('[data-command]').forEach(el=>{if(el.dataset.command===command)el.disabled=true;});actionNotice(actionLabels[command]||'Aplicando cambio…');window.chrome.webview.postMessage({command,...props,requestId});};
window.chrome?.webview?.addEventListener('message',e=>{const r=e.data;if(r?.type!=='action-result')return;const pending=pendingActions.get(r.requestId);if(!pending)return;pendingActions.delete(r.requestId);document.querySelectorAll('[data-command]').forEach(el=>{if(el.dataset.command===pending.command)el.disabled=false;});actionNotice(r.message||'Acción completada.',r.ok?'success':'error');if(data)renderCloud(data.cloud);});
const icon=(path,name='',className='')=>path&&/^\/assets\/[\w./-]+$/.test(path)?`<img src="${esc(path)}" alt="${esc(name)}" class="${className}" loading="lazy">`:'<span class="icon-empty">◇</span>';
const skillIcon=path=>path?`<img src="/assets/aion2/skills/icons/${esc(path.split(/[\\/]/).pop())}" alt="">`:'<span>◇</span>';
window.openTab=(name,section)=>{if(!$(name))return;document.querySelectorAll('.tab,.tabs button').forEach(el=>el.classList.remove('active'));$(name).classList.add('active');document.querySelector(`.tabs [data-tab="${name}"]`)?.classList.add('active');if(section)openSection(section);};
function openSection(name){document.querySelectorAll('.config-nav button,.config-section').forEach(el=>el.classList.remove('active'));document.querySelector(`.config-nav [data-section="${name}"]`)?.classList.add('active');$('section-'+name)?.classList.add('active');}
document.addEventListener('click',e=>{const tab=e.target.closest('[data-tab]');if(tab)openTab(tab.dataset.tab,tab.dataset.section);const section=e.target.closest('.config-nav [data-section]');if(section)openSection(section.dataset.section);const toggle=e.target.closest('[data-toggle]');if(toggle){const on=toggle.getAttribute('aria-checked')!=='true';toggle.setAttribute('aria-checked',on);send('set',{key:toggle.dataset.toggle,value:on});}const action=e.target.closest('[data-set-action]');if(action)send('set',{key:action.dataset.setAction,value:true});const key=e.target.closest('[data-hotkey]');if(key)captureHotkey(key);const command=e.target.closest('[data-command]');if(command)send(command.dataset.command);const npc=e.target.closest('[data-npc]');if(npc)send('npc',{id:Number(npc.dataset.npc)});});
// The overlay's style (Configuración → Pantalla): applied by the app at once.
document.addEventListener('click',e=>{const card=e.target.closest('[data-style]');if(!card)return;document.querySelectorAll('[data-style]').forEach(b=>b.setAttribute('aria-pressed',b===card));send('set',{key:'overlayStyle',value:card.dataset.style});});
document.addEventListener('change',e=>{const el=e.target;if(el.dataset.event)send('event',{id:el.dataset.event});if(el.dataset.set)send('set',{key:el.dataset.set,value:'number' in el.dataset?Number(el.value):'bool' in el.dataset?el.value==='true':el.value});});
document.addEventListener('input',e=>{const out=e.target.id&&$(e.target.id+'-out');if(out)out.textContent=Math.round(e.target.value*100)+'%';});
if(location.hash.length>1)openTab(location.hash.slice(1));
const hotkeyNames={breakdown:'Desglose del combate',overlay:'Mostrar u ocultar el overlay',move:'Modo mover del overlay',drops:'Panel del objetivo (botín / jugador)',panel:'Abrir o cerrar este panel',reset:'Nuevo combate',minimap:'Mostrar u ocultar el minimapa'};let capturing=null;
function captureHotkey(row){capturing?.classList.remove('capturing');capturing=row;row.classList.add('capturing');row.querySelector('strong').textContent='Pulsa la combinación…';}
document.addEventListener('keydown',e=>{if(!capturing)return;e.preventDefault();const row=capturing;const done=combo=>{row.classList.remove('capturing');capturing=null;if(combo!==null)send('hotkey',{action:row.dataset.hotkey,combo});renderHotkeys(true);};if(e.key==='Escape')return done(null);if(e.key==='Delete'||e.key==='Backspace')return done('');if(['Control','Alt','Shift','Meta'].includes(e.key))return;const k=/^Key[A-Z]$/.test(e.code)?e.code.slice(3):/^Digit\d$/.test(e.code)?e.code.slice(5):/^F\d{1,2}$/.test(e.code)?e.code:null;if(!k||!(e.ctrlKey||e.altKey||e.metaKey)){row.querySelector('strong').textContent='Usa Ctrl, Alt o Win + letra, número o F1-F24';return;}done([e.ctrlKey&&'Ctrl',e.altKey&&'Alt',e.shiftKey&&'Shift',e.metaKey&&'Win',k].filter(Boolean).join('+'));});
function renderHotkeys(force){if(!data||capturing&&!force)return;const keys=data.settings.Hotkeys||{};const html=Object.entries(hotkeyNames).map(([a,label])=>`<button class="hotkey" data-hotkey="${a}"><span>${label}</span><strong>${esc(keys[a]||'Sin definir')}</strong></button>`).join('');if($('hotkeys').dataset.html!==html){$('hotkeys').innerHTML=html;$('hotkeys').dataset.html=html;}$('overlay-keys').textContent=['overlay','move','drops','panel'].filter(a=>keys[a]).map(a=>`${keys[a]} ${i18nText(({overlay:'mostrar/ocultar',move:'mover/fijar',drops:'botín',panel:'panel'})[a])}`).join(' · ');}
const toggles={shareDiagnostics:'ShareDiagnostics',minimapFiltersOpen:'MinimapFiltersOpen',eventToasts:'EventToasts',bossPaceBars:'BossPaceBars',troopAll:'OverlayTroopAll',syncPets:'SyncPets',showOd:'OverlayShowOd',showPet:'OverlayShowPet',showFarm:'OverlayShowFarm',showLootPrices:'OverlayShowLootPrices',showRanking:'OverlayShowRanking',showHeaderTimers:'OverlayShowHeaderTimers',assistant:'AssistantVisible',cubeAlerts:'CubeAlerts',cubeSound:'CubeSound',assistantBuffs:'AssistantShowBuffs',assistantCooldowns:'AssistantShowCooldowns',assistantSound:'AssistantSound',fieldBossAlerts:'FieldBossAlerts',partyOnly:'PartyOnly',focusOnly:'OverlayAttachToGame',showParty:'OverlayShowParty',showCasts:'OverlayShowCasts',showEvent:'OverlayShowEvent',showDrops:'OverlayDrops',showMinimap:'MinimapVisible',bossSounds:'BossSounds',eventSounds:'Sounds'};
const fields={'cfg-channel':'UpdateChannel','cfg-mmopacity':'MinimapOpacity','cfg-mmicons':'MinimapIconScale','cfg-toastcorner':'EventToastCorner','cfg-bosspace':'BossPacePlace','cfg-bossvoice':'BossAlertVoice','cfg-odmax':'OdMax','cfg-region':'RegionOffset','cfg-idle':'CombatIdleSeconds','cfg-bossonly':'BossOnlyDamage','cfg-opacity':'OverlayOpacity','cfg-scale':'OverlayScale','cfg-assistscale':'AssistantScale','cfg-castplace':'CastAlertPlace','cfg-lead':'LeadMinutes','cfg-volume':'Volume'};
let petOptions=false;
function renderPets(d){if(!petOptions&&d.petList){$('pet-options').innerHTML=d.petList.map(p=>`<option value="${esc(p.name)}"></option>`).join('');petOptions=true;}renderPetDay(d.snapshot.Pet?.Today||[]);renderCollection(d.petCollection,d.petsSync);const p=d.snapshot.Pet?.Tracked;if(!p){$('pet-status').className='empty';$('pet-status').textContent='Todavía no sigues ninguna mascota.';return;}
 const pct=p.Threshold?Math.min(100,p.Progress/p.Threshold*100):100;$('pet-status').className='';
 $('pet-status').innerHTML=`<div class="pet-head"><b>${esc(p.Name)}</b><span>${p.Threshold?`Nv. ${p.Level} · ${p.Progress}/${p.Threshold}`:`Nv. ${p.Level} · MÁX.`}</span></div><div class="pet-bar"><i style="width:${pct}%"></i></div>
 <dl class="pet-stats"><dt>Almas que faltan</dt><dd>${p.Threshold?p.Remaining:'—'}</dd><dt>Muertes estimadas</dt><dd>${p.KillsLeft??(p.Threshold?'tras 10 muertes':'—')}</dd><dt>Muertes en esta sesión</dt><dd>${p.Kills}</dd><dt>Almas en esta sesión</dt><dd>${p.Souls}</dd><dt>Tasa observada</dt><dd>${p.SoulsPerKill!=null?(p.SoulsPerKill*100).toFixed(1)+'% por muerte':'—'}</dd><dt>Fuente</dt><dd>${p.Source==='juego'?'Datos del juego':'Ingresado a mano'}</dd></dl>
 <p class="footnote">Criaturas que dan sus almas: ${p.Monsters.map(esc).join(' · ')||'sin datos en el catálogo'}</p>`;}
let petFilter='',petLevel='all',petRace='',petPage=0;const petPageSize=12;
function renderCollection(c,sync){if($('cfg-pet-sync')&&sync&&sync!=='Sin subir')$('cfg-pet-sync').textContent='Mascotas: '+sync;$('pet-sync-status').textContent=sync&&sync!=='Sin subir'?sync:'Sube la colección y su historial diario a tu cuenta (Mi cuenta → Mascotas) cuando el dispositivo está vinculado.';if(!c){$('pet-col-meta').textContent='Esperando la lista del juego';return;}
const ago=c.ListAt?new Date(c.ListAt):null;$('pet-col-meta').textContent=`${c.Character} · ${c.Live?'al día':'guardada'}${ago?' · lista del juego '+ago.toLocaleString(uiLoc(),{day:'2-digit',month:'short',hour:'2-digit',minute:'2-digit'}):''}`;
const pct=Math.round(c.Owned/c.Total*100);const cats=Object.entries(c.ByCategory).map(([k,v])=>`<div class="pet-cat${petRace===k?' on':petRace?' off':''}" data-pet-race="${esc(k)}" role="button" tabindex="0" title="${petRace===k?'Quitar el filtro':'Ver solo '+esc(k)}">${petBadge(k,'pet-cat-badge')}<b>${v[0]}/${v[1]}</b><div class="bar"><i style="width:${v[1]?v[0]/v[1]*100:0}%"></i></div></div>`).join('');
const hist=c.History.slice(-14);const maxO=Math.max(1,...hist.map(h=>h.Owned));const spark=hist.length>1?`<div class="pet-hist" title="Mascotas obtenidas por día (últimos ${hist.length} días)">${hist.map(h=>`<i style="height:${Math.max(6,h.Owned/maxO*100)}%" title="${h.Date}: ${h.Owned} mascotas · Nv3 ${h.L3} · ${h.Souls} almas"></i>`).join('')}</div>`:'';
const pinned=new Set((data?.settings?.PinnedPets)||[]);const soon=p=>p.Threshold>0&&p.Threshold-p.Progress<=5;
const list=c.Pets.filter(p=>(petLevel==='all'||petLevel==='soon'&&soon(p)||petLevel==='pinned'&&pinned.has(p.Id)||String(p.Level)===petLevel)&&(!petRace||p.Category===petRace)&&(!petFilter||p.Name.toLowerCase().includes(petFilter)));
petPage=Math.min(petPage,Math.max(0,Math.ceil(list.length/petPageSize)-1));const rows=list.slice(petPage*petPageSize,(petPage+1)*petPageSize).map(p=>`<div class="pet-own lv${p.Level}">${petPic(p.Id)}<button class="pet-pin${pinned.has(p.Id)?' on':''}" data-pet-pin="${p.Id}" title="${pinned.has(p.Id)?'Quitar del overlay':'Fijar en el overlay'}">📌</button><span><button type="button" class="pet-name" data-pet-map="${p.Id}" data-pet-name="${esc(p.Name)}" title="Ver en el mapa dónde sale su alma">${esc(p.Name)}</button><small>${petBadge(p.Category,'pet-row-badge')} <a href="#" data-pet-src="${p.Id}">fuentes de almas</a> · <a href="#" class="pet-map-open" data-pet-map="${p.Id}" data-pet-name="${esc(p.Name)}">📍 ver en el mapa</a></small></span><b>${p.Level?'Nv. '+p.Level:'Bloqueada'}</b>${p.Threshold?`<div class="bar"><i style="width:${Math.min(100,p.Progress/p.Threshold*100)}%"></i></div><em>${p.Progress}/${p.Threshold}</em>`:'<div class="bar max"><i style="width:100%"></i></div><em>Máx.</em>'}</div><div class="pet-sources" id="pet-src-${p.Id}" hidden></div>`).join('')||'<div class="empty">Ninguna mascota con ese filtro.</div>';
const html=`<div class="pet-summary"><div class="pet-total"><strong>${c.Owned}</strong><span>de ${c.Total} · ${pct}%</span></div><div class="pet-levels"><span>Nv. 1 <b>${c.ByLevel[1]}</b></span><span>Nv. 2 <b>${c.ByLevel[2]}</b></span><span>Nv. 3 <b>${c.ByLevel[3]}</b></span>${c.ByLevel[0]?`<span>Empezadas <b>${c.ByLevel[0]}</b></span>`:''}</div>${spark}</div><div class="pet-cats">${cats}</div><div class="pet-filter"><input id="pet-col-search" placeholder="Buscar mascota" value="${esc(petFilter)}"><select id="pet-col-level"><option value="all">Todos los niveles</option><option value="3">Nv. 3</option><option value="2">Nv. 2</option><option value="1">Nv. 1</option><option value="0">Empezadas</option><option value="soon">Terminan pronto</option><option value="pinned">Fijadas</option></select>${petRace?`<button class="pet-race-clear" data-pet-race="${esc(petRace)}">${petBadge(petRace,'pet-row-badge')} ✕</button>`:''}</div><div class="pet-own-list">${rows}</div>`;
const pager=`<div class="pet-pagination"><span>${list.length?petPage*petPageSize+1:0}–${Math.min((petPage+1)*petPageSize,list.length)} de ${list.length} mascotas</span><button data-pet-page="-1" ${petPage===0?'disabled':''}>Anterior</button><button data-pet-page="1" ${(petPage+1)*petPageSize>=list.length?'disabled':''}>Siguiente</button></div>`;
const host=$('pet-collection');if(host.dataset.html===html+pager)return;const focus=document.activeElement?.id;host.innerHTML=html+pager;host.dataset.html=html+pager;$('pet-col-level').value=petLevel;$('pet-col-search').oninput=e=>{petFilter=e.target.value.trim().toLowerCase();petPage=0;renderCollection(data?.petCollection,data?.petsSync);};$('pet-col-level').onchange=e=>{petLevel=e.target.value;petPage=0;renderCollection(data?.petCollection,data?.petsSync);};if(focus==='pet-col-search'){const i=$('pet-col-search');i.focus();i.setSelectionRange(i.value.length,i.value.length);}}
document.addEventListener('click',e=>{const r=e.target.closest('[data-pet-race]');if(r){petRace=petRace===r.dataset.petRace?'':r.dataset.petRace;petPage=0;renderCollection(data?.petCollection,data?.petsSync);return;}const b=e.target.closest('[data-pet-page]');if(b&&!b.disabled){petPage+=Number(b.dataset.petPage);renderCollection(data?.petCollection,data?.petsSync);}});
function renderPetDay(rows){const html=rows.length?'<div class="pet-row pet-row-head"><span>Mascota</span><span>Almas</span><span>Muertes</span><span>Tasa</span><span></span></div>'+rows.map(r=>`<div class="pet-row"><span>${esc(r.Name)}</span><span>${r.Souls}</span><span>${r.Kills}</span><span>${r.SoulsPerKill!=null?(r.SoulsPerKill*100).toFixed(1)+'%':'—'}</span><button data-pet-follow="${esc(r.Name)}">Seguir</button></div>`).join(''):'<div class="empty">Todavía no hay almas hoy. Aparecerán aquí en cuanto caiga la primera.</div>';if($('pet-day').dataset.html!==html){$('pet-day').innerHTML=html;$('pet-day').dataset.html=html;}}
document.addEventListener('click',e=>{const f=e.target.closest('[data-pet-follow]');if(f){$('pet-name').value=f.dataset.petFollow;$('pet-progress').focus();}});
$('pet-track').onclick=()=>{const name=$('pet-name').value.trim().toLowerCase();const pet=(data?.petList||[]).find(x=>x.name.toLowerCase()===name);if(!pet){$('pet-name').setCustomValidity(i18nText('Elige una mascota de la lista'));$('pet-name').reportValidity();return;}$('pet-name').setCustomValidity('');send('pet-track',{id:pet.id,level:Number($('pet-level').value),progress:Number($('pet-progress').value)});};
function renderSettings(d){renderPets(d);const s=d.settings;document.querySelectorAll('[data-style]').forEach(b=>b.setAttribute('aria-pressed',b.dataset.style===(s.OverlayStyle||'clasico')));document.querySelectorAll('[data-toggle]').forEach(el=>el.setAttribute('aria-checked',!!s[toggles[el.dataset.toggle]]));for(const[id,key]of Object.entries(fields)){const el=$(id);if(el&&document.activeElement!==el){el.value=String(s[key]);const out=$(id+'-out');if(out)out.textContent=Math.round(s[key]*100)+'%';}}$('cfg-version').textContent=d.version;$('event-summary').textContent=`${s.Sounds?'Avisos con sonido activados':'Avisos con sonido desactivados'} · ${s.LeadMinutes} min antes y al inicio · marca los eventos que quieres escuchar.`;renderHotkeys(false);}
$('farm-goal').onchange=e=>send('goal',{value:Number(e.target.value)});$('pair-button').onclick=()=>{send('pair',{code:$('pair-code').value});$('pair-code').value='';};$('mob-search').oninput=renderMobs;$('close-dialog').onclick=()=>$('npc-dialog').close();
function eventRow(e){return `<div class="event-row" style="--event:${esc(e.Color)}"><div><b>${esc(e.Name)}</b><small>${new Date(e.At).toLocaleTimeString(uiLoc(),{hour:'2-digit',minute:'2-digit',timeZone:'UTC'})} UTC · ${new Date(e.At).toLocaleDateString(uiLoc(),{day:'numeric',month:'short'})}</small></div><strong>${time(e.Remaining)}</strong><input type="checkbox" ${e.Alert?'checked':''} data-event="${esc(e.Id)}" aria-label="Aviso para ${esc(e.Name)}"></div>`;}
function renderMobs(){if(!data)return;const q=$('mob-search').value.toLocaleLowerCase('es');const mobs=data.mobs.filter(m=>m.name.toLocaleLowerCase('es').includes(q));$('mobs').innerHTML=mobs.map(m=>`<button class="mob-card" data-npc="${m.id}">${icon(m.record?.icon,m.name)}<div><b>${esc(m.name)}</b><small>${m.count} cerca${m.record?.level?' · Nv. '+m.record.level:''}${m.record?.creatureName?' · '+esc(m.record.creatureName):''}</small></div></button>`).join('')||'<div class="empty">No hay criaturas identificadas con este filtro. Entra en otra zona para recibir sus apariciones.</div>';}
window.renderCompanion=d=>{
 data=d;renderOwnProfile(d.profile);$('build-version').textContent='VERSIÓN '+d.version;const s=d.snapshot,known=s.LocalId>0;
 $('capture-status').textContent=s.Status;$('packets').textContent=`${s.PingMs!=null?s.PingMs+' ms · ':''}${number(s.Packets)} segmentos · ${number(s.Frames)} tramas · ${number(s.DamageEvents)} eventos`;
 $('encounter').textContent=s.Target?.Name||'Combate observado';$('dps').textContent=known?number(s.Dps):'—';$('damage').textContent=known?number(s.Damage):'—';$('healing').textContent=known?number(s.Healing):'—';$('identity').textContent=known?(s.IdentityInferred?`${s.Character} · identidad deducida de la captura`:`${s.Character} · identidad recibida en el juego`):'Esperando la ficha de tu personaje';$('duration').textContent=`${time(s.Duration)} de combate observado`;
 $('pause').textContent=s.Paused?'Reanudar':'Pausar';$('overlay-toggle').textContent=d.overlay?'Ocultar overlay':'Mostrar overlay';$('lock-toggle').textContent=d.locked?'Mover overlay':'Fijar overlay';$('boss-sound-toggle').textContent=d.settings.BossSounds?'Alerta de habilidades: activada':'Alerta de habilidades: silenciada';
 $('skills').className=s.Skills.length?'':'empty';$('skill-count').textContent=s.Skills.length?`${s.Skills.length} habilidades`:'Sin golpes atribuidos';
 $('skills').innerHTML=s.Skills.length?'<div class="skill skill-head"><span></span><span>Habilidad</span><span class="right">Daño</span><span class="right">Golpes</span><span class="right">%</span></div>'+s.Skills.map(x=>`<div class="skill">${skillIcon(x.Icon)}<div><b>${esc(x.Name)}</b><div class="damage-bar"><i style="width:${Math.max(2,x.Share)}%"></i></div></div><span class="right">${number(x.Damage)}</span><span class="right">${x.Hits}</span><span class="right">${x.Share.toFixed(1)}</span></div>`).join(''):'Cuando el juego envíe la ficha de tu personaje, aparecerán las habilidades con su icono y daño.';
 $('actors').className=s.Actors.length?'':'empty';$('actors').innerHTML=actorRows(s);
 $('target-kind').textContent=s.Target?.NpcId?'Criatura':s.Target?'Identidad recibida':'Pendiente';
 const t=s.Target,n=d.npc;$('target').className=t?'':'empty';$('target').innerHTML=t?`<div class="target-identity">${icon(n?.icon,t.Name)}<div><h4>${esc(t.Name)}</h4><small>${n?`Nv. ${n.level} · ${esc(n.creatureName||'Criatura')}`:t.ServerId?'Servidor '+t.ServerId:'Servidor pendiente'}</small></div></div>${t.Hp!==null?`<div class="hp"><small><span>Vida observada</span><span>${number(t.Hp)}</span></small><div class="track"><div class="fill" style="width:${t.HighestObservedHp?Math.min(100,t.Hp/t.HighestObservedHp*100):0}%"></div></div><small>Referencia: mayor vida recibida; no confirma vida máxima.</small></div>`:''}${n?`<button data-npc="${n.id}">Ficha, botín y apariciones →</button>`:'<p class="footnote">El perfil oficial se consulta solo con nombre y servidor confirmados.</p>'}`:'Vuelve a seleccionar una criatura para recibir su identidad.';
 $('next-events').innerHTML=d.events.slice(0,3).map(eventRow).join('');$('all-events').innerHTML=d.events.map(eventRow).join('');$('notice').textContent=d.message;
 $('kills').textContent=number(s.ConfirmedKills);$('mobs-count').textContent=number(s.NearbyMobs);$('remaining').textContent=`${Math.max(0,d.settings.FarmGoal-s.ConfirmedKills)} por completar`;
 renderSettings(d);$('share-toggle').textContent=d.settings.ShareTelemetry?'Desactivar envío de telemetría':'Activar envío de telemetría';$('cloud-status').textContent=d.companion;renderOps(d);renderCastKinds(d.settings);renderCloud(d.cloud);$('alert').textContent=d.alert||'Elige los eventos que quieres escuchar.';
 $('diagnostics').innerHTML=[['Tramas',s.Frames],['Eventos de daño',s.DamageEvents],['Errores',s.DecodeErrors],['Retransmisiones',s.Retransmissions],['Saltos TCP',s.Gaps],['Zona',s.Zone||'Pendiente'],['ID local',s.LocalId||'Pendiente']].map(([k,v])=>`<dt>${esc(k)}</dt><dd>${esc(v)}</dd>`).join('');
 if(!initialized){$('farm-goal').value=d.settings.FarmGoal;initialized=true;}
 renderMobs();$('clock').textContent=new Date().toLocaleTimeString(uiLoc());
};
window.showNpc=async(n,sib)=>{await itemTable();if(n&&sib&&!(n.drops||[]).length){n={...n,drops:sib.drops,dropsFrom:sib.level};}if(!n){$('npc-detail').innerHTML='<h2>Ficha pendiente</h2><p class="muted">Esta criatura todavía no está en el catálogo local.</p>';}else{$('npc-detail').innerHTML=`<div class="target-identity">${icon(n.icon,n.name)}<div><span class="eyebrow">CRIATURA · ${n.id}</span><h2>${esc(n.name)}</h2><small>Nv. ${n.level} · ${esc(n.gradeName||'')} · ${esc(n.creatureName||'')}</small></div></div><h3 style="margin-top:25px">Dónde aparece</h3><p class="muted">${(n.where||[]).map(x=>esc(x.name)).join(' · ')||'Sin apariciones en el catálogo'}</p><button data-atlas-find="${esc(n.name)}" data-atlas-map="${esc((n.maps||[])[0]||'')}" style="margin:12px 0">Ver en el mapa</button><h3 style="margin:15px 0">Botín registrado ${n.drops?.length?'· '+n.drops.length:''}</h3>${n.dropsFrom?`<p class="notice">Esta variante no trae tabla propia en el catálogo: se muestra la de la misma criatura de Nv. ${n.dropsFrom}.</p>`:''}${(n.drops||[]).slice(0,100).map(x=>`<div class="drop">${icon(x.icon,itemName(x))}<b class="grade-${esc(x.grade)}">${esc(itemName(x))}</b><strong>${Number(x.chance).toLocaleString(uiLoc(),{maximumFractionDigits:2})}%</strong></div>`).join('')||'<p class="muted">La ficha importada no contiene una tabla de botín.</p>'}<p class="footnote">Datos del catálogo local de Daeva Companion. Las probabilidades corresponden a la fuente del catálogo, no a esta captura.</p>`;}$('npc-dialog').showModal();};

// Cuenta: device state chips and the ownership challenge.
function renderCloud(c){if(!c)return;const t=c.title,pull=c.pull;const chip=(id,on,txt)=>{const el=$(id);el.className=on?'on':'';el.textContent=txt;};
// With a device token the code box goes away: linking again is only needed after disconnecting or a revocation.
const hasToken=['Connected','Offline','TokenStored'].includes(c.state);$('pair-box').hidden=hasToken;$('disconnect-button').hidden=!hasToken;$('pull-button').disabled=c.state!=='Connected';
chip('chip-token',c.state!=='NotPaired'&&c.state!=='Revoked',c.state==='Revoked'?'Token revocado':c.state==='NotPaired'?'Sin token':'Token guardado');
chip('chip-device',c.state==='Connected',c.state==='Connected'?'Dispositivo conectado':c.state==='Offline'?'Sin conexión':'Dispositivo no conectado');
chip('chip-owner',t?.State==='verified',t?.State==='verified'?'Personaje verificado':t?.State==='pending'?'Verificación en curso':'Personaje sin verificar');
if(pull)$('cloud-plan').textContent='La cuenta sirve para guardar tu progreso: todo es gratis. Desconectar aquí no revoca el token: eso se hace desde Mi cuenta en la web.';
if(!t)return;
// Verificar personaje: the same title-change flow as Mi perfil on the web (TitleVerification.cs).
$('verify-state').textContent=t.Badge;$('verify-state').dataset.state=t.State;
const ch=t.Character;$('verify-character').innerHTML=ch?`<span>Personaje</span><b>${esc(t.CharacterName||'Personaje identificado')}</b><small>Región ${esc(ch.region.toUpperCase())} · servidor ${ch.serverId} · ID ${esc(ch.characterId.slice(0,4))}…${esc(ch.characterId.slice(-10))}</small>`:'';
$('verify-message').textContent=t.Text;
const pend=t.State==='pending';$('verify-title').hidden=!pend;
if(pend){$('verify-title-name').textContent=t.CurrentTitle?`«${t.CurrentTitle}»`:'—';$('verify-title-category').textContent=t.CategoryLabel||'—';$('verify-title-expiry').textContent=t.ExpiresAtUtc?'Caduca a las '+new Date(t.ExpiresAtUtc).toLocaleTimeString(uiLoc(),{hour:'2-digit',minute:'2-digit'}):'';}
const res=$('verify-result');res.hidden=!t.Message;res.textContent=t.Message||'';
$('title-start').hidden=!['unverified','full'].includes(t.State);$('title-start').disabled=!t.CanStart||isPending('title-start');
$('title-check').hidden=!pend;$('title-check').disabled=!t.CanCheck||isPending('title-check');
$('title-cancel').hidden=!pend;$('title-cancel').disabled=!t.CanCheck||isPending('title-cancel');
$('verify-linked').innerHTML=t.Linked&&t.Linked.length?`<h5>Tus personajes verificados (${t.Linked.length}/${t.Limit})</h5><ul>${t.Linked.map(n=>`<li>${esc(n)}</li>`).join('')}</ul>`:'';}

function renderOwnProfile(p){const host=$('own-profile-content');if(!host)return;const signature=JSON.stringify(p);if(host.dataset.profile===signature)return;host.dataset.profile=signature;const card=p?.card;if(!card){host.innerHTML=`<div class="empty">${esc(p?.error||(p?'Cargando tu perfil oficial…':'Pulsa «Mi perfil» en la barra superior para consultar tu personaje.'))}</div>`;return;}
const SLOT_ES={MainHand:'Arma principal',SubHand:'Arma secundaria',Torso:'Torso',Pants:'Piernas',Helmet:'Casco',Shoulder:'Hombros',Gloves:'Guantes',Boots:'Botas',Cape:'Capa',Belt:'Cinturón',Necklace:'Collar',Earring1:'Pendiente 1',Earring2:'Pendiente 2',Ring1:'Anillo 1',Ring2:'Anillo 2',Bracelet1:'Brazalete 1',Bracelet2:'Brazalete 2',Rune1:'Runa 1',Rune2:'Runa 2',Amulet:'Amuleto',Arcana1:'Arcana 1',Arcana2:'Arcana 2',Arcana3:'Arcana 3',Arcana4:'Arcana 4',Arcana5:'Arcana 5',Wing:'Alas'};const slotName=s=>SLOT_ES[s]||s||'';
const pic=url=>url&&/^https:\/\//.test(url)?`<img src="${esc(url)}" alt="" loading="lazy">`:'';
host.innerHTML=`<div class="own-profile-head">${pic(card.Portrait)}<div><h2>${esc(card.Name)}</h2><p>${esc(card.ClassName)} · Nv. ${card.Level} · ${esc(card.Server)}</p><p>${esc(card.Legion||'Sin legión')}${card.Title?' · '+esc(card.Title):''}</p></div></div><div class="metrics"><div class="metric main"><span>Poder de combate</span><strong>${number(card.CombatPower)}</strong></div><div class="metric"><span>Nivel de objeto</span><strong>${card.ItemLevel?number(card.ItemLevel):'—'}</strong></div><div class="metric"><span>Última lectura</span><strong class="profile-date">${esc(new Date(card.FetchedAt).toLocaleTimeString(uiLoc()))}</strong></div></div><h3>Equipo</h3><div class="own-profile-gear">${(card.Gear||[]).map(g=>`<div class="own-profile-item">${pic(g.Icon)}<div><b class="grade-${esc(g.Grade)}">${esc(g.Name)}${g.Enchant?' +'+g.Enchant:''}</b><small>${esc(slotName(g.Slot))}</small></div></div>`).join('')||'<p>Equipo pendiente de la fuente oficial.</p>'}</div><h3>Clasificación</h3><p class="profile-rankings">${(card.Rankings||[]).map(esc).join(' · ')||'La fuente oficial no devolvió posiciones para este personaje.'}</p><p class="footnote">Perfil público de NCSOFT. Consultar esta ficha no verifica que el personaje sea tuyo.</p>`;}

// Versions, notices, diagnostics and the minimap section.
function renderOps(d){const u=d.update;if(u){$('update-status').textContent=u.status;const card=$('update-card');card.hidden=!(u.newer&&u.latest);if(u.latest){$('update-title').textContent=`Versión ${u.latest.Version} (${u.channel==='stable'?'estable':'beta'})`;$('update-meta').textContent=`Tienes la ${u.current} · ${(u.latest.Bytes/1048576).toFixed(1)} MB`+(u.latest.created?` · publicada ${new Date(u.latest.created).toLocaleDateString(uiLoc())}`:'');$('update-notes').textContent=u.latest.Notes||'Sin notas de versión.';}$('update-open').hidden=!u.downloaded;$('update-download').hidden=!!u.downloaded;}
const ann=(d.announcements||[]);const html=ann.map(a=>`<div class="announcement ${['warning','maintenance','critical'].includes(a.Level)?'warn':''}"><b>${esc(a.Title)}</b><span>${esc(a.Body)}</span></div>`).join('');if($('announcements').dataset.html!==html){$('announcements').innerHTML=html;$('announcements').dataset.html=html;}
if(d.diagnostics)$('diag-status').textContent=d.diagnostics;
for(const id of ['cfg-mmopacity','cfg-mmicons']){const o=$(id+'-out');const el=$(id);if(o&&el)o.textContent=id==='cfg-mmicons'?`${Math.round(el.value*100)}%`:`${Math.round(el.value*100)}%`;}
const on=new Set(d.settings.MinimapFilters||[]);const layers=(d.minimapLayers||[]).map(l=>`<label class="layer"><input type="checkbox" data-layer="${esc(l.key)}" ${on.has(l.key)?'checked':''}><i style="background:${esc(l.color)}"></i>${esc(l.label)}</label>`).join('');if($('minimap-layers').dataset.html!==layers){$('minimap-layers').innerHTML=layers;$('minimap-layers').dataset.html=layers;}}
document.addEventListener('change',e=>{const l=e.target.closest('[data-layer]');if(l)send('set',{key:'minimapLayer',value:{layer:l.dataset.layer,on:l.checked}});});

function renderCastKinds(s){document.querySelectorAll('[data-cast-kind]').forEach(el=>{const set=new Set((el.dataset.what==='sound'?s.CastSoundKinds:s.CastBannerKinds)||[]);if(document.activeElement!==el)el.checked=set.has(el.dataset.castKind);});}
document.addEventListener('change',e=>{const k=e.target.closest('[data-cast-kind]');if(k)send('set',{key:'castKind',value:{kind:k.dataset.castKind,what:k.dataset.what,on:k.checked}});});

// ---- Historial de combates (guardado en este equipo) ----
let historyVersion=-1,historyOpen=null,historyTimer=null;
const CLASS_ES={Gladiator:'Gladiador',Templar:'Templario',Ranger:'Cazador',Assassin:'Asesino',Sorcerer:'Hechicero',Spiritmaster:'Espiritista',Elementalist:'Espiritista',Cleric:'Clérigo',Chanter:'Cantor'};
const big=n=>{n=Number(n)||0;return n>=1e9?(n/1e9).toFixed(2)+'B':n>=1e6?(n/1e6).toFixed(2)+'M':n>=1e3?(n/1e3).toFixed(1)+'k':String(Math.round(n));};
const clock=s=>{s=Math.max(0,Math.round(Number(s)||0));return `${Math.floor(s/60)}:${String(s%60).padStart(2,'0')}`;};
const when=d=>{const t=new Date(d);return t.toLocaleDateString(uiLoc(),{day:'2-digit',month:'2-digit'})+' '+t.toLocaleTimeString(uiLoc(),{hour:'2-digit',minute:'2-digit'});};
function askHistory(){send('history-list',{query:$('history-query')?.value||'',bosses:!!$('history-bosses')?.checked});}
window.renderHistory=list=>{const host=$('history-list');if(!host)return;
 host.innerHTML=list.length?list.map(f=>`<button class="history-row${f.Id===historyOpen?' active':''}" data-fight="${esc(f.Id)}"><span class="h-target">${f.Boss?'<i class="h-boss">JEFE</i>':''}<b>${esc(f.Target)}</b><small>${esc(f.Place)} · ${when(f.Start)}</small></span><span class="h-num"><b>${big(f.GroupDps)}</b><small>DPS grupo</small></span><span class="h-num"><b>${clock(f.Seconds)}</b><small>${f.Killed?'derrotado':'duración'}</small></span></button>`).join(''):'<p class="empty">No hay combates con ese filtro.</p>';};
window.showFight=f=>{const s=f.Summary;historyOpen=s.Id;document.querySelectorAll('.history-row').forEach(r=>r.classList.toggle('active',r.dataset.fight===s.Id));
 const max=Math.max(1,...f.Players.map(p=>p.Damage));
 const players=f.Players.map((p,i)=>`<div class="h-player${p.Self?' self':''}" data-player="${i}"><span class="h-name"><b>${esc(p.Name)}</b><small>${esc(CLASS_ES[p.Class]||p.Class||'')}</small></span><span class="h-bar"><i style="width:${(p.Damage/max*100).toFixed(1)}%"></i></span><span>${big(p.Damage)}</span><span>${big(p.Dps)}/s</span><span>${p.Share.toFixed(1)}%</span></div>`).join('');
 $('history-detail').innerHTML=`<div class="panel-head"><div><h3>${s.Boss?'<i class="h-boss">JEFE</i> ':''}${esc(s.Target)}</h3><small class="muted">${esc(s.Place)} · ${when(s.Start)}${s.Character?' · '+esc(s.Character):''}</small></div><button class="secondary" data-history-delete="${esc(s.Id)}">Borrar</button></div>
 <div class="h-metrics"><div><span>DPS del grupo</span><strong>${big(s.GroupDps)}</strong></div><div><span>Duración</span><strong>${clock(s.Seconds)}</strong></div><div><span>Daño total</span><strong>${big(s.Damage)}</strong></div><div><span>Tu DPS</span><strong>${s.MyDps?big(s.MyDps):'—'}</strong></div></div>
 <p class="h-result">${s.Killed?'✔ Derrotado':'Sin muerte confirmada'}${f.TargetMaxHp?' · vida máxima '+big(f.TargetMaxHp):''} · ${s.Players} ${s.Players===1?'jugador':'jugadores'}</p>
 <div class="h-player h-head"><span>Jugador</span><span></span><span>Daño</span><span>DPS</span><span>%</span></div>${players}
 <div id="history-skills" class="h-skills"><p class="muted">Pulsa un jugador para ver sus habilidades.</p></div>`;
 const showSkills=i=>{const p=f.Players[i];if(!p)return;document.querySelectorAll('.h-player[data-player]').forEach(r=>r.classList.toggle('open',r.dataset.player===String(i)));
  $('history-skills').innerHTML=`<h4>${esc(p.Name)} · habilidades</h4>`+(p.Skills.length?p.Skills.map(k=>`<div class="h-skill">${skillIcon(k.Icon)}<span><b>${esc(k.Name)}</b><small>${k.Hits} golpes · ${k.CritPct}% crítico</small></span><span>${big(k.Damage)}</span><span>${k.Share}%</span></div>`).join(''):'<p class="muted">Sin desglose guardado.</p>');};
 $('history-detail').onclick=e=>{const r=e.target.closest('[data-player]');if(r)showSkills(Number(r.dataset.player));};
 showSkills(Math.max(0,f.Players.findIndex(p=>p.Self)));};
document.addEventListener('click',e=>{const row=e.target.closest('[data-fight]');if(row)send('history-open',{id:row.dataset.fight});
 const del=e.target.closest('[data-history-delete]');if(del&&confirm(i18nText('¿Borrar este combate del historial?'))){send('history-delete',{id:del.dataset.historyDelete});historyOpen=null;$('history-detail').innerHTML='<p class="empty">Combate borrado.</p>';setTimeout(askHistory,300);}
 if(e.target.closest('[data-tab="history"]'))askHistory();});
document.addEventListener('input',e=>{if(e.target.id==='history-query'){clearTimeout(historyTimer);historyTimer=setTimeout(askHistory,350);}});
document.addEventListener('change',e=>{if(e.target.id==='history-bosses')askHistory();});
// New fights saved while the tab is open: refresh the list.
const _renderCompanion=window.renderCompanion;window.renderCompanion=d=>{_renderCompanion?.(d);if(d&&d.historyVersion!==historyVersion){const first=historyVersion<0;historyVersion=d.historyVersion;if(!first&&$('history')?.classList.contains('active'))askHistory();}};

// ---- Mascotas: fijar en el overlay y fuentes de almas ----
// A pet's name opens the Mapa de Atreia on the creatures whose soul it is (the app knows them: pet-sources).
let petMapWanted=null;
window.showPetSources=(id,list)=>{if(petMapWanted?.id===id){const name=petMapWanted.name;petMapWanted=null;window.atlasFindNpcs?.(list.map(s=>s.Npc),name);return;}
 const box=$('pet-src-'+id);if(!box)return;box.hidden=false;
 box.innerHTML=list.length?list.map(s=>`<div class="pet-source"><span><b>${esc(s.Name)}</b><small>Nv. ${s.Level??'?'} · ${esc(s.Grade||'')}</small></span><span class="pet-maps">${(s.Maps||[]).map(m=>`<i>${esc(m)}</i>`).join('')||'<i>Mazmorra o sin mapa</i>'}</span><button type="button" class="pet-map-link" data-atlas-npcs="${Number(s.Npc)}" data-atlas-label="${esc(s.Name)}">Ver en el mapa</button></div>`).join(''):'<p class="muted">Sin fuentes conocidas en el catálogo.</p>';};
document.addEventListener('click',e=>{const named=e.target.closest('[data-pet-map]');if(named){e.preventDefault();petMapWanted={id:Number(named.dataset.petMap),name:named.dataset.petName};send('pet-sources',{id:petMapWanted.id});return;}
 const pin=e.target.closest('[data-pet-pin]');if(pin){e.preventDefault();send('pet-pin',{id:Number(pin.dataset.petPin)});pin.classList.toggle('on');}
 const src=e.target.closest('[data-pet-src]');if(src){e.preventDefault();const box=$('pet-src-'+src.dataset.petSrc);if(box&&!box.hidden){box.hidden=true;return;}send('pet-sources',{id:Number(src.dataset.petSrc)});}});

// ---- Rediseño: filas de jugador al estilo TeamMate, en verde ----
const CLASS_KEY={Gladiator:'gladiator',Templar:'templar',Ranger:'ranger',Assassin:'assassin',Sorcerer:'sorcerer',Spiritmaster:'elementalist',Elementalist:'elementalist',Cleric:'cleric',Chanter:'chanter'};
const CLASS_COLOR={gladiator:'#e8893a',templar:'#4c8df6',ranger:'#6cc24a',assassin:'#e2555c',sorcerer:'#a274f2',elementalist:'#3fc8d8',cleric:'#e9cc55',chanter:'#e36cb8'};
function actorRows(s){if(!s.Actors.length)return 'Todavía no se han recibido eventos de combate.';
 const list=[...s.Actors].filter(a=>(a.Damage>0||a.Healing>0)&&(a.Id===s.LocalId||!/^Entidad \d+$/.test(a.Name))).sort((a,b)=>b.Damage-a.Damage);const total=list.reduce((t,a)=>t+a.Damage,0)||1,top=Math.max(1,list[0]?.Damage||1);
 return list.map((a,i)=>{const cls=(data?.classes||{})[a.Id],key=CLASS_KEY[cls]||'',color=CLASS_COLOR[key]||'#3fb8af',pct=a.Damage/total*100;
  return `<div class="meter-row${a.Id===s.LocalId?' self':''}" style="--c:${color};--w:${(a.Damage/top*100).toFixed(1)}%"><span class="rank">${i+1}</span>${key?`<img src="/assets/ui/class_icon_${key}.png" alt="">`:'<i class="noclass"></i>'}<b>${esc(a.Name)}</b><span class="dmg">${number(a.Damage)}</span><strong>${number(a.Dps)}/s</strong><span class="pct">${pct.toFixed(1)}%</span></div>`;}).join('');}
const _rcPills=window.renderCompanion;window.renderCompanion=d=>{_rcPills?.(d);if(!d)return;
 const set=(id,on,txt,warn)=>{const el=$(id);if(!el)return;el.classList.toggle('on',!!on);el.classList.toggle('warn',!!warn);const b=el.querySelector('b');if(b&&txt)b.textContent=txt;};
 const s=d.snapshot||{};const cap=!/Abre AION|detenida|no tiene conexi/i.test(s.Status||'');set('pill-capture',cap,null,!cap);
 set('pill-character',!!s.Character,'Personaje: '+(s.Character||'esperando'),!s.Character);
 const st=d.cloud?.state;set('pill-cloud',st==='Connected',st==='Connected'?'Cuenta: conectada':st==='Offline'?'Cuenta: sin conexión':st==='Revoked'?'Cuenta: revocada':'Cuenta: sin vincular',st&&st!=='Connected');};

// Pet portraits (data/pet-icons.json, loaded once).
let petIconMap={};fetch('/data/pet-icons.json').then(r=>r.json()).then(m=>{petIconMap=m;const host=$('pet-collection');if(host){host.dataset.html='';renderCollection(data?.petCollection,data?.petsSync);}}).catch(()=>{});
const petPic=id=>petIconMap[id]?`<img class="pet-pic" src="${esc(petIconMap[id])}" alt="">`:'<i class="pet-pic"></i>';
// The side menu highlights the button pressed (Mi cuenta and Configuración open the same page).
document.addEventListener('click',e=>{const b=e.target.closest('.sidenav button[data-tab]');if(!b)return;document.querySelectorAll('.sidenav button').forEach(x=>x.classList.toggle('active',x===b));});

// ---- Idiomas: español (original), inglés y portugués de Brasil ----
// The page is written in Spanish; every visible text (and placeholder / title / aria-label) is looked up in
// data/i18n.json and swapped, also for whatever is rendered later (MutationObserver). Spanish restores the originals.
let i18nDict={en:{},pt:{},fr:{}},uiLang='es',i18nBusy=false;
const i18nOrig=new WeakMap();
// Dynamic phrases with numbers: [regex over the Spanish text, English, Portuguese].
const I18N_PATTERNS=[
 // Combate, objetivo y cabecera
 [/^(.+) · identidad recibida en el juego$/,'$1 · identity received in the game','$1 · identidade recebida no jogo',"$1 · identité reçue dans le jeu"],
 [/^(.+) · identidad deducida de la captura$/,'$1 · identity deduced from the capture','$1 · identidade deduzida da captura',"$1 · identité déduite de la capture"],
 [/^(\d+(?::\d\d){1,2}|\d+d \d+h) de combate observado$/,'$1 of combat observed','$1 de combate observado',"$1 de combat observé"],
 [/^(\d+) habilidades$/,'$1 skills','$1 habilidades',"$1 compétences"],
 [/^(\d+) ms · ([\d.,]+) segmentos · ([\d.,]+) tramas · ([\d.,]+) eventos$/,'$1 ms · $2 segments · $3 frames · $4 events','$1 ms · $2 segmentos · $3 quadros · $4 eventos',"$1 ms · $2 segments · $3 trames · $4 événements"],
 [/^([\d.,]+) segmentos · ([\d.,]+) tramas · ([\d.,]+) eventos$/,'$1 segments · $2 frames · $3 events','$1 segmentos · $2 quadros · $3 eventos',"$1 segments · $2 trames · $3 événements"],
 [/^VERSIÓN (.+)$/,'VERSION $1','VERSÃO $1',"VERSION $1"],
 [/^Captura activa · (.+)$/,'Capture active · $T1','Captura ativa · $T1',"Capture active · $T1"],
 [/^CRIATURA · (\d+)$/,'CREATURE · $1','CRIATURA · $1',"CRÉATURE · $1"],
 [/^Botín registrado · (\d+)$/,'Recorded loot · $1','Saque registrado · $1',"Butin enregistré · $1"],
 [/^Esta variante no trae tabla propia en el catálogo: se muestra la de la misma criatura de Nv\. (\d+)\.$/,'This variant has no table of its own in the catalog: the one of the same creature at Lv. $1 is shown.','Esta variante não traz tabela própria no catálogo: é mostrada a da mesma criatura de Nv. $1.',"Cette variante n'a pas de table propre dans le catalogue : celle de la même créature au Niv. $1 est affichée."],
 [/^(\d+) cerca · Nv\. (\d+) · (.+)$/,'$1 nearby · Lv. $2 · $T3','$1 por perto · Nv. $2 · $T3',"$1 à proximité · Niv. $2 · $T3"],
 [/^(\d+) cerca · Nv\. (\d+)$/,'$1 nearby · Lv. $2','$1 por perto · Nv. $2',"$1 à proximité · Niv. $2"],
 [/^(\d+) cerca · (.+)$/,'$1 nearby · $T2','$1 por perto · $T2',"$1 à proximité · $T2"],
 [/^(\d+) cerca$/,'$1 nearby','$1 por perto',"$1 à proximité"],
 [/^Nv\. (\d+) · (\d+)\/(\d+)$/,'Lv. $1 · $2/$3','Nv. $1 · $2/$3',"Niv. $1 · $2/$3"],
 [/^Nv\. (\d+) · MÁX\.$/,'Lv. $1 · MAX','Nv. $1 · MÁX.',"Niv. $1 · MAX"],
 [/^Nv\. ([\d?]+) · (.+) · (.+)$/,'Lv. $1 · $T2 · $T3','Nv. $1 · $T2 · $T3',"Niv. $1 · $T2 · $T3"],
 [/^Nv\. ([\d?]+) · (.+)$/,'Lv. $1 · $T2','Nv. $1 · $T2',"Niv. $1 · $T2"],
 [/^(.+) · Nv\. (\d+) · (.+)$/,'$T1 · Lv. $2 · $3','$T1 · Nv. $2 · $3',"$T1 · Niv. $2 · $3"],
 [/^(\d+) por completar$/,'$1 to go','$1 restantes',"$1 restantes"],
 [/^Avisos con sonido activados · (\d+) min antes y al inicio · marca los eventos que quieres escuchar\.$/,'Sound alerts on · $1 min before and at the start · tick the events you want to hear.','Avisos com som ativados · $1 min antes e no início · marque os eventos que você quer ouvir.',"Alertes sonores activées · $1 min avant et au début · coche les événements que tu veux entendre."],
 [/^Avisos con sonido desactivados · (\d+) min antes y al inicio · marca los eventos que quieres escuchar\.$/,'Sound alerts off · $1 min before and at the start · tick the events you want to hear.','Avisos com som desativados · $1 min antes e no início · marque os eventos que você quer ouvir.',"Alertes sonores désactivées · $1 min avant et au début · coche les événements que tu veux entendre."],
 [/^Otra aplicación ya usa (.+)\. Elige otra combinación en Configuración > Atajos\.$/,'Another app already uses $1. Choose another combination in Settings > Shortcuts.','Outro aplicativo já usa $1. Escolha outra combinação em Configurações > Atalhos.',"Une autre app utilise déjà $1. Choisis une autre combinaison dans Réglages > Raccourcis."],
 [/^Novedades \((\d+)\)$/,'News ($1)','Novidades ($1)',"Nouveautés ($1)"],
 // Mascotas
 [/^Mascotas obtenidas por día \(últimos (\d+) días\)$/,'Pets obtained per day (last $1 days)','Mascotes obtidos por dia (últimos $1 dias)',"Familiers obtenus par jour ($1 derniers jours)"],
 [/^(\d{4}-\d\d-\d\d): (\d+) mascotas · Nv3 (\d+) · (\d+) almas$/,'$1: $2 pets · Lv3 $3 · $4 souls','$1: $2 mascotes · Nv3 $3 · $4 almas',"$1 : $2 familiers · Niv3 $3 · $4 âmes"],
 [/^Ver solo (.+)$/,'Show only $T1','Ver só $T1',"Afficher seulement $T1"],
 [/^(.+) · (al día|guardada) · lista del juego (.+)$/,"$1 · $T2 · game's list $3",'$1 · $T2 · lista do jogo $3',"$1 · $T2 · liste du jeu $3"],
 [/^(.+) · (al día|guardada)$/,'$1 · $T2','$1 · $T2',"$1 · $T2"],
 [/^([\d.,]+)% por muerte$/,'$1% per kill','$1% por abate',"$1% par élimination"],
 [/^Criaturas que dan sus almas: (.+)$/,'Creatures that drop its souls: $1','Criaturas que dão suas almas: $1',"Créatures qui donnent ses âmes : $1"],
 [/^Mascotas: (.+)$/,'Pets: $T1','Mascotes: $T1',"Familiers : $T1"],
 [/^(.+) fijada en el overlay\.$/,'$1 pinned to the overlay.','$1 fixado no overlay.',"$1 épinglé dans l'overlay."],
 [/^(.+) ya no está fijada\.$/,'$1 is no longer pinned.','$1 não está mais fixado.',"$1 n'est plus épinglé."],
 [/^(\d+) criaturas dan almas de (.+)\.$/,'$1 creatures drop souls of $2.','$1 criaturas dão almas de $2.',"$1 créatures donnent des âmes de $2."],
 // Historial
 [/^(✔ Derrotado|Sin muerte confirmada) · vida máxima (\S+) · 1 jugador$/,'$T1 · max HP $2 · 1 player','$T1 · vida máxima $2 · 1 jogador',"$T1 · PV max $2 · 1 joueur"],
 [/^(✔ Derrotado|Sin muerte confirmada) · vida máxima (\S+) · (\d+) jugadores$/,'$T1 · max HP $2 · $3 players','$T1 · vida máxima $2 · $3 jogadores',"$T1 · PV max $2 · $3 joueurs"],
 [/^(✔ Derrotado|Sin muerte confirmada) · 1 jugador$/,'$T1 · 1 player','$T1 · 1 jogador',"$T1 · 1 joueur"],
 [/^(✔ Derrotado|Sin muerte confirmada) · (\d+) jugadores$/,'$T1 · $2 players','$T1 · $2 jogadores',"$T1 · $2 joueurs"],
 [/^Combate exportado en (.+)$/,'Fight exported to $1','Combate exportado em $1',"Combat exporté vers $1"],
 // Jefes de campo
 [/^Murió a las (.+) · abre la lista del mapa para saber su reaparición$/,'Died at $1 · open the map list to learn its respawn','Morreu às $1 · abra a lista do mapa para saber o renascimento',"Mort à $1 · ouvre la liste de la carte pour connaître sa réapparition"],
 [/^Aparece hacia las (.+) · estimado por su última muerte$/,'Spawns around $1 · estimated from its last death','Aparece por volta das $1 · estimado pela última morte',"Apparaît vers $1 · estimé depuis sa dernière mort"],
 // Objetivos
 [/^Reto de (\d+) min$/,'$1 min challenge','Desafio de $1 min',"Défi de $1 min"],
 [/^¡Objetivo cumplido en (.+)!$/,'Goal reached in $1!','Objetivo cumprido em $1!',"Objectif atteint en $1 !"],
 [/^Quedan (.+) · vas a tiempo: terminas en (.+)$/,'$1 left · on pace: you finish in $2','Faltam $1 · no ritmo: você termina em $2',"Reste $1 · dans les temps : tu finis en $2"],
 [/^Quedan (.+) · a este ritmo te faltan (.+)$/,'$1 left · at this pace you need $2 more','Faltam $1 · neste ritmo faltam $2',"Reste $1 · à ce rythme il te faut encore $2"],
 [/^Quedan (.+) · sin ritmo aún$/,'$1 left · no pace yet','Faltam $1 · sem ritmo ainda',"Reste $1 · pas encore de rythme"],
 [/^A este ritmo, (.+) restantes$/,'At this pace, $1 left','Neste ritmo, faltam $1',"À ce rythme, il reste $1"],
 [/^([\d.,]+) \/ ([\d.,]+) (muertes|kinah en botín|kinah)$/,'$1 / $2 $T3','$1 / $2 $T3',"$1 / $2 $T3"],
 // Cubos
 [/^(\d+) m (arriba|abajo)$/,'$1 m $T2','$1 m $T2',"$1 m $T2"],
 [/^desde las (.+)$/,'since $1','desde as $1',"depuis $1"],
 [/^(.+) · desde las (.+)$/,'$T1 · since $2','$T1 · desde as $2',"$T1 · depuis $2"],
 [/^(.+) · (-?\d+), (-?\d+) · altura (-?\d+) · detectado a (\d+) m$/,'$1 · $2, $3 · height $4 · detected at $5 m','$1 · $2, $3 · altura $4 · detectado a $5 m',"$1 · $2, $3 · hauteur $4 · détecté à $5 m"],
 [/^(.+) · (-?\d+), (-?\d+) · altura (-?\d+)$/,'$1 · $2, $3 · height $4','$1 · $2, $3 · altura $4',"$1 · $2, $3 · hauteur $4"],
 // Recursos y Mercado
 [/^(.+) · en juego$/,'$1 · in game','$1 · em jogo',"$1 · en jeu"],
 [/^Actualizado a las (.+)$/,'Updated at $1','Atualizado às $1',"Mis à jour à $1"],
 [/^Actualizado (.+)$/,'Updated $1','Atualizado $1',"Mis à jour $1"],
 [/^(Sudamérica|Norteamérica Este|Norteamérica Oeste|Europa|Asia) · (Asmodianos|Elios)$/,'$T1 · $T2','$T1 · $T2',"$T1 · $T2"],
 // Versiones
 [/^Versión (.+) \((estable|beta)\)$/,'Version $1 ($T2)','Versão $1 ($T2)',"Version $1 ($T2)"],
 [/^Tienes la (.+) · ([\d.,]+) MB · publicada (?:el )?(.+)$/,'You have $1 · $2 MB · published $3','Você tem a $1 · $2 MB · publicada em $3',"Tu as la $1 · $2 Mo · publiée le $3"],
 [/^Tienes la (.+) · ([\d.,]+) MB$/,'You have $1 · $2 MB','Você tem a $1 · $2 MB',"Tu as la $1 · $2 Mo"],
 [/^Hay una versión nueva: (.+)$/,'A new version is available: $1','Há uma versão nova: $1',"Une nouvelle version est disponible : $1"],
 [/^Versión (.+) disponible$/,'Version $1 available','Versão $1 disponível',"Version $1 disponible"],
 [/^Versión (.+) descargada y verificada \(tamaño y SHA-256\)$/,'Version $1 downloaded and verified (size and SHA-256)','Versão $1 baixada e verificada (tamanho e SHA-256)',"Version $1 téléchargée et vérifiée (taille et SHA-256)"],
 [/^Instalando (.+): la app se cerrará y volverá a abrirse sola en unos segundos\.$/,'Installing $1: the app will close and open again by itself in a few seconds.','Instalando $1: o app vai fechar e reabrir sozinho em alguns segundos.',"Installation de la $1 : l'app va se fermer et se rouvrir toute seule dans quelques secondes."],
 [/^No se pudo descargar \(HTTP (\d+)\)\.$/,'Could not download (HTTP $1).','Não foi possível baixar (HTTP $1).',"Téléchargement impossible (HTTP $1)."],
 // Mensajes del programa (MainWindow, CompanionClient)
 [/^Buscando el perfil público de (.+)…$/,'Looking up the public profile of $1…','Buscando o perfil público de $1…',"Recherche du profil public de $1…"],
 [/^Perfil de (.+) abierto\. Los datos oficiales se completan al recibirlos\.$/,"$1's profile opened. The official data fills in as it arrives.",'Perfil de $1 aberto. Os dados oficiais são completados ao recebê-los.',"Profil de $1 ouvert. Les données officielles se complètent à leur arrivée."],
 [/^Buscando la ficha oficial de (.+)…$/,'Looking up the official card of $1…','Buscando a ficha oficial de $1…',"Recherche de la fiche officielle de $1…"],
 [/^Preferencias y estado descargados\. (.+)$/,'Preferences and status downloaded. $T1','Preferências e estado baixados. $T1',"Préférences et état téléchargés. $T1"],
 [/^(.+) ya está asignado a otra acción$/,'$1 is already assigned to another action','$1 já está atribuído a outra ação',"$1 est déjà attribué à une autre action"],
 [/^Atajo guardado: (.+)$/,'Shortcut saved: $1','Atalho salvo: $1',"Raccourci enregistré : $1"],
 [/^Esta acción no está disponible: (.+)$/,'This action is not available: $1','Esta ação não está disponível: $1',"Cette action n'est pas disponible : $1"],
 [/^Diagnóstico enviado · (.+)$/,'Diagnostics sent · $1','Diagnóstico enviado · $1',"Diagnostic envoyé · $1"],
 [/^Diagnóstico rechazado: (.+)$/,'Diagnostics rejected: $T1','Diagnóstico rejeitado: $T1',"Diagnostic refusé : $T1"],
 [/^Subida a tu cuenta · (.+)$/,'Uploaded to your account · $1','Enviado para sua conta · $1',"Envoyé à ton compte · $1"],
 [/^No se pudo subir · (.+) · reintento en 5 min$/,'Could not upload · $T1 · retry in 5 min','Não foi possível enviar · $T1 · nova tentativa em 5 min',"Envoi impossible · $T1 · nouvel essai dans 5 min"],
 [/^No se pudo subir · (.+)$/,'Could not upload · $T1','Não foi possível enviar · $T1',"Envoi impossible · $T1"],
 [/^Lote de telemetría rechazado: (.+)$/,'Telemetry batch rejected: $T1','Lote de telemetria rejeitado: $T1',"Lot de télémétrie refusé : $T1"],
 [/^Nv\. ([\d?]+) ·$/,'Lv. $1 ·','Nv. $1 ·',"Niv. $1 ·"],
 // Soporte: tema o autor y fecha
 [/^(Cuenta|Aplicación|Mapa|Datos del juego|Suscripción|Otro) · (.+)$/,'$T1 · $2','$T1 · $2',"$T1 · $2"],
 [/^Equipo de soporte · (.+)$/,'Support team · $1','Equipe de suporte · $1',"Équipe d'assistance · $1"],
 // Botín agrupado del Atlas, como en el overlay (i18n/patterns.tsv)
 [/^Equipo de ([^()]+)$/,'Armor · $1','Equipamento de $1',"Armure · $1"],
 [/^Equipo del ([^()]+)$/,'Armor · $1','Equipamento do $1',"Armure · $1"],
 [/^Armas de ([^()]+)$/,'Weapons · $1','Armas de $1',"Armes · $1"],
 [/^Armas del ([^()]+)$/,'Weapons · $1','Armas do $1',"Armes · $1"],
 [/^Equipo (olvidado|terrestre|sediento de sangre)$/,'Armor · $1','Equipamento · $1',"Armure · $1"],
 [/^Armas (olvidadas|terrestres|sedientas de sangre)$/,'Weapons · $1','Armas · $1',"Armes · $1"],
 [/^ID (\d+) · Datos del cliente$/,'ID $1 · Client data','ID $1 · Dados do cliente',"ID $1 · Données du client"],
 [/^(.+) · (\d+) ubicaciones$/,'$1 · $2 places','$1 · $2 locais',"$1 · $2 lieux"],
 [/^(\d+) piezas$/,'$1 pieces','$1 peças',"$1 pièces"],
 [/^Botín · (\d+) objetos$/,'Loot · $1 items','Saque · $1 itens',"Butin · $1 objets"],
 [/^(\d+) con precio · (\d+) sin precio$/,'$1 priced · $2 without a price','$1 com preço · $2 sem preço',"$1 avec prix · $2 sans prix"],
 [/^PNJ ([\d.,]+) c\/u$/,'NPC $1 each','PNJ $1 cada',"PNJ $1 chacun"],
 [/^hace (\d+) (min|h|d)$/,'$1 $2 ago','há $1 $2',"il y a $1 $2"],
 [/^Servidor (\d+)$/,'Server $1','Servidor $1',"Serveur $1"],
 [/^Vista (.+) · se renueva a las (.+)$/,'Seen $1 · renews at $2','Vista $1 · renova às $2',"Vu $1 · renouvelé à $2"],
 [/^Vista (.+) · hay datos nuevos: vuelve a abrirla en el juego$/,'Seen $1 · newer data: open it again in the game','Vista $1 · há dados novos: abra de novo no jogo',"Vu $1 · données plus récentes : rouvre-le dans le jeu"],
 [/^de ([\d.,]+) · top ([\d.,]+) %$/,'of $1 · top $2 %','de $1 · top $2 %',"sur $1 · top $2 %"],
 [/^de ([\d.,]+)$/,'of $1','de $1',"sur $1"],
 [/^🏆 ([\d.,]+) puntos$/,'🏆 $1 points','🏆 $1 pontos',"🏆 $1 points"],
 [/^([\d.,]+) bajas · ([\d.,]+) muertes · ([\d.,]+) asistencias$/,'$1 kills · $2 deaths · $3 assists','$1 abates · $2 mortes · $3 assistências',"$1 éliminations · $2 morts · $3 assistances"],
 [/^([\d.,]+) partidas · ([\d.,]+) victorias · ([\d.,]+) derrotas$/,'$1 matches · $2 wins · $3 losses','$1 partidas · $2 vitórias · $3 derrotas',"$1 matchs · $2 victoires · $3 défaites"],
 [/^Mejor marca: nivel ([\d.,]+) · (\d+:\d\d) · ([\d.,]+) puntos$/,'Best: level $1 · $2 · $3 points','Melhor marca: nível $1 · $2 · $3 pontos',"Meilleur : niveau $1 · $2 · $3 points"],
 [/^(.+) · mejor marca (\d+:\d\d) · ([\d.,]+) puntos$/,'$1 · best $2 · $3 points','$1 · melhor marca $2 · $3 pontos',"$1 · meilleur $2 · $3 points"],
 [/^Clase: (.+)$/,'Class: $1','Classe: $1',"Classe : $1"],
 [/^General: (.+)$/,'Overall: $1','Geral: $1',"Général : $1"],
 [/^desde (.+)$/,'since $1','desde $1',"depuis $1"],
 [/^Clasificación (\d+)$/,'Ranking $1','Classificação $1',"Classement $1"],
 [/^Grabando… (\d+:\d\d)$/,'Recording… $1','Gravando… $1',"Enregistrement… $1"],
 [/^Grabación guardada: (.+) \(([\d.,]+) MB\), en la carpeta de datos\.$/,'Recording saved: $1 ($2 MB), in the data folder.','Gravação salva: $1 ($2 MB), na pasta de dados.',"Enregistrement sauvegardé : $1 ($2 Mo), dans le dossier de données."],
 [/^No se grabó: (.+)$/,'Not recorded: $T1','Não gravado: $T1',"Non enregistré : $T1"],
 [/^No se encontró la conexión del juego: entra al juego con el personaje y vuelve a grabar\. \((.+)\)$/,'The game connection was not found: enter the game with the character and record again. ($T1)','A conexão do jogo não foi encontrada: entre no jogo com o personagem e grave de novo. ($T1)',"La connexion du jeu est introuvable : entre dans le jeu avec le personnage et enregistre à nouveau. ($T1)"],
 [/^· 1 lugar$/,'· 1 place','· 1 lugar',"· 1 lieu"],
 [/^· (\d+) lugares$/,'· $1 places','· $1 lugares',"· $1 lieux"],
 [/^(\d+) pin(es)? · (\d+) ruta(s)? en este mapa · también en el minimapa$/,'$1 pin(s) · $3 route(s) on this map · also on the minimap','$1 pin(s) · $3 rota(s) neste mapa · também no minimapa',"$1 épingle(s) · $3 itinéraire(s) sur cette carte · aussi sur le minimap"],
 [/^Clic en cada punto de la ruta \((\d+)\) · doble clic o Enter para terminar · Esc para cancelar$/,'Click each point of the route ($1) · double click or Enter to finish · Esc to cancel','Clique em cada ponto da rota ($1) · duplo clique ou Enter para terminar · Esc para cancelar',"Clique sur chaque point de l'itinéraire ($1) · double-clic ou Entrée pour terminer · Échap pour annuler"],
 [/^(\d+) puntos · También se ve en el minimapa del overlay\.$/,'$1 points · Also shown on the overlay minimap.','$1 pontos · Também aparece no minimapa do overlay.',"$1 points · Affiché aussi sur le minimap de l'overlay."],
 [/^Tus marcas de este mapa: (\d+) pin(es)? · (\d+) ruta(s)?\. También se ven en el minimapa\.$/,'Your marks on this map: $1 pin(s) · $3 route(s). Also shown on the minimap.','Suas marcas neste mapa: $1 pin(s) · $3 rota(s). Também aparecem no minimapa.',"Tes repères sur cette carte : $1 épingle(s) · $3 itinéraire(s). Affichés aussi sur le minimap."],
 [/^de ([\d.,]+) objetos seguidos$/,'of $1 items followed','de $1 itens acompanhados',"sur $1 objets suivis"],
 [/^hace (\d+) (min|h|d) · se actualiza cada hora$/,'$1 $2 ago · updated every hour','há $1 $2 · atualiza a cada hora',"il y a $1 $2 · mis à jour toutes les heures"],
 [/^Datos: aion2\.exchange · foto de las (.+)$/,'Data: aion2.exchange · snapshot at $1','Dados: aion2.exchange · foto das $1',"Données : aion2.exchange · instantané de $1"],
 [/^· tienes ([\d.,]+)$/,'· you have $1','· você tem $1',"· tu en as $1"],
 [/^([\d.,]+) en venta$/,'$1 on sale','$1 à venda',"$1 en vente"],
 [/^Se muestran 300 de ([\d.,]+): afina la búsqueda o la categoría\.$/,'Showing 300 of $1: narrow the search or the category.','Mostrando 300 de $1: refine a busca ou a categoria.',"300 affichés sur $1 : affine la recherche ou la catégorie."],
 [/^No se pudo actualizar el Mercado \((.+)\)\. Se muestran los datos de las (.+)\.$/,'Could not update the Market ($T1). Showing the data of $2.','Não foi possível atualizar o Mercado ($T1). Mostrando os dados das $2.',"Impossible de mettre à jour le Marché ($T1). Données du $2 affichées."],
 [/^No se pudo actualizar el Mercado \((.+)\)\.$/,'Could not update the Market ($T1).','Não foi possível atualizar o Mercado ($T1).',"Impossible de mettre à jour le Marché ($T1)."],
 [/^(.+): no respondió con datos \(HTTP (\d+)\)\.?$/,'$1: did not answer with data (HTTP $2).','$1: não respondeu com dados (HTTP $2).',"$1 : n'a pas répondu avec des données (HTTP $2)."],
 [/^(.+) · no es tu servidor$/,'$1 · not your server','$1 · não é seu servidor',"$1 · pas ton serveur"],
 [/^(.+) · tu servidor$/,'$1 · your server','$1 · seu servidor',"$1 · ton serveur"],
 [/^Precio guardado: (.+) a ([\d.,]+) kinah en este servidor\.$/,'Price saved: $1 at $2 kinah on this server.','Preço salvo: $1 a $2 kinah neste servidor.',"Prix enregistré : $1 à $2 kinah sur ce serveur."],
 [/^Precio de (.+) borrado\.$/,'Price of $1 cleared.','Preço de $1 apagado.',"Prix de $1 effacé."],
 [/^Mascota · (.+)$/,'Pet · $1','Mascote · $1',"Familier · $1"],
 [/^Nv\. (\d+)$/,'Lv. $1','Nv. $1',"Niv. $1"],
 [/^Aviso para (.+)$/,'Alert for $T1','Aviso para $T1',"Alerte pour $T1"],
 [/^Tú · (.+) · posición aproximada$/,'You · $T1 · approximate position','Você · $T1 · posição aproximada',"Toi · $T1 · position approximative"],
 [/^Tú · (.+) · última posición$/,'You · $T1 · last known position','Você · $T1 · última posição',"Toi · $T1 · dernière position connue"],
 [/^Tú · ([^·]+)$/,'You · $T1','Você · $T1',"Toi · $T1"],
 [/^([\d.,]+) puntos visibles$/,'$1 visible points','$1 pontos visíveis',"$1 points visibles"],
 [/^Centrar en mí · (.+)$/,'Center on me · $T1','Centralizar em mim · $T1',"Centrer sur moi · $T1"],
 [/^Caduca a las (.+)$/,'Expires at $1','Expira às $1',"Expire à $1"],
 [/^Tus personajes verificados \((\d+)\/(\d+)\)$/,'Your verified characters ($1/$2)','Seus personagens verificados ($1/$2)',"Tes personnages vérifiés ($1/$2)"],
 [/^Ya tienes (\d+) personajes verificados, el máximo por cuenta\. Quita uno en Mi perfil de la web para añadir este\.$/,'You already have $1 verified characters, the maximum per account. Remove one in My profile on the website to add this one.','Você já tem $1 personagens verificados, o máximo por conta. Remova um em Meu perfil no site para adicionar este.',"Tu as déjà $1 personnages vérifiés, le maximum par compte. Retires-en un dans Mon profil sur le site pour ajouter celui-ci."],
 [/^La web oficial todavía muestra «(.+)»\. Puede tardar unos minutos en actualizarse; si no cambia, cierra la sesión del personaje en el juego y vuelve a comprobar\.$/,"The official site still shows «$1». It can take a few minutes to update; if it doesn't change, log out of the character in the game and check again.",'O site oficial ainda mostra «$1». Pode levar alguns minutos para atualizar; se não mudar, saia do personagem no jogo e verifique de novo.',"Le site officiel affiche encore « $1 ». La mise à jour peut prendre quelques minutes ; si rien ne change, déconnecte le personnage dans le jeu et vérifie à nouveau."],
 [/^Región ([A-Z]+) · servidor (\d+) · ID (.+)$/,'Region $1 · server $2 · ID $3','Região $1 · servidor $2 · ID $3',"Région $1 · serveur $2 · ID $3"],
 [/^(\d+) combates en el historial\.$/,'$1 fights in the history.','$1 combates no histórico.',"$1 combats dans l'historique."],
 [/^1 combate en el historial\.$/,'1 fight in the history.','1 combate no histórico.',"1 combat dans l'historique."],
 [/^(\d+)–(\d+) de (\d+) mascotas$/,'$1–$2 of $3 pets','$1–$2 de $3 mascotes',"$1–$2 sur $3 familiers"],
 [/^de (\d+) · (\d+)%$/,'of $1 · $2%','de $1 · $2%',"sur $1 · $2%"],
 [/^Personaje: (.+)$/,'Character: $1','Personagem: $1',"Personnage : $1"],
 [/^(\d+) golpes · (.+)% crítico$/,'$1 hits · $2% crit','$1 golpes · $2% crítico',"$1 coups · $2% crit."],
 [/^(.+) · habilidades$/,'$1 · skills','$1 · habilidades',"$1 · compétences"],
 [/^Nv\. (\d+)$/,'Lv. $1','Nv. $1',"Niv. $1"],
 [/^(\d+) jugadores$/,'$1 players','$1 jogadores',"$1 joueurs"],
 [/^hoy (\d+) almas?(.*)$/,'today $1 souls$2','hoje $1 almas$2',"aujourd'hui $1 âmes$2"],
 [/^Aparece a las (.+)$/,'Comes back at $1','Volta às $1',"Revient à $1"],
 [/^Horario fijo · (.+)$/,'Fixed time · $1','Horário fixo · $1',"Heure fixe · $1"],
 [/^vivo (\d.+)$/,'up $1','vivo $1',"en vie $1"],
 [/^El servidor de seguridad del hosting o tu red devolvió una página en lugar de datos \(HTTP (\d+), «(.+)»\)$/,"The hosting's security server or your network returned a page instead of data (HTTP $1, «$2»)",'O servidor de segurança da hospedagem ou a sua rede devolveu uma página em vez de dados (HTTP $1, «$2»)',"Le serveur de sécurité de l'hébergeur ou ton réseau a renvoyé une page au lieu de données (HTTP $1, « $2 »)"],
 [/^El servidor de seguridad del hosting o tu red devolvió una página en lugar de datos \(HTTP (\d+)\)$/,"The hosting's security server or your network returned a page instead of data (HTTP $1)",'O servidor de segurança da hospedagem ou a sua rede devolveu uma página em vez de dados (HTTP $1)',"Le serveur de sécurité de l'hébergeur ou ton réseau a renvoyé une page au lieu de données (HTTP $1)"],
 [/^No se pudo vincular \(HTTP (\d+)\)$/,'Could not link (HTTP $1)','Não foi possível vincular (HTTP $1)',"Liaison impossible (HTTP $1)"],
 // Last: a category and a quantity («Mascota · 1», the Atlas loot lines).
 [/^(.+) · (\d+(?:–\d+)?)$/,'$T1 · $2','$T1 · $2',"$T1 · $2"],
];
// In a pattern, $T1…$T9 is that group translated as well (an event, a status or a category inside a longer phrase).
function i18nText(s,depth=0){if(uiLang==='es'||!s)return s;const d=i18nDict[uiLang]||{};const t=s.trim();if(!t)return s;
 if(d[t]!==undefined)return s.replace(t,()=>d[t]);
 for(const [re,en,pt,fr] of I18N_PATTERNS){const m=t.match(re);if(!m)continue;
  const out=(uiLang==='en'?en:uiLang==='fr'?(fr??en):pt).replace(/\$(T?)(\d)/g,(_,tr,i)=>{const v=m[+i]??'';return tr&&depth<3?i18nText(v,depth+1):v;});return s.replace(t,()=>out);}
 return s;}
function i18nNode(n){if(n.nodeType===3){const p=n.parentNode;if(!p||/^(SCRIPT|STYLE)$/.test(p.nodeName))return;
  if(!i18nOrig.has(n)||(n.nodeValue!==i18nOrig.get(n).out))i18nOrig.set(n,{src:n.nodeValue,out:null});
  const o=i18nOrig.get(n),v=i18nText(o.src);o.out=v;if(n.nodeValue!==v)n.nodeValue=v;return;}
 if(n.nodeType!==1)return;
 for(const a of n.tagName==='OPTGROUP'?['label']:['placeholder','title','aria-label']){if(!n.hasAttribute(a))continue;const k='i18n'+a.replace('-','');
  if(n.dataset[k]===undefined||n.getAttribute(a)!==n.dataset[k+'Out'])n.dataset[k]=n.getAttribute(a);const v=i18nText(n.dataset[k]);n.dataset[k+'Out']=v;n.setAttribute(a,v);}
 if(n.tagName==='OPTION'||n.tagName==='SELECT'){} // options are text nodes, handled below
 for(const c of n.childNodes)i18nNode(c);}
// Configuración → Estilos: each thumbnail is the overlay drawn in the panel's language (styles/en/, styles/pt/; Spanish in styles/).
function styleThumbs(){const dir=uiLang==='pt'?'styles/pt/':uiLang==='en'||uiLang==='fr'?'styles/en/':'styles/';document.querySelectorAll('.style-card[data-style] img').forEach(img=>{const src=dir+img.closest('[data-style]').dataset.style+'.webp';if(img.getAttribute('src')!==src)img.setAttribute('src',src);});}
// Item names in the panel's language, from /data/items.json ([es, en, icon, grade, price, pt, fr], loaded on first need):
// Portuguese and French for the gear only (the vendor's table), English otherwise; Spanish as the loot tables write it.
let itemRows={},itemBook=null;
const itemTable=()=>uiLang==='es'?Promise.resolve():(itemBook??=fetch('/data/items.json').then(r=>r.json()).then(t=>{itemRows=t||{};}).catch(()=>{}));
const ownItemName=(id,lang)=>{const r=itemRows[id],c={es:0,en:1,pt:5,fr:6}[lang];return r&&c!=null&&r[c]?r[c]:null;};
const itemName=d=>uiLang==='es'?d.name:ownItemName(d.id,uiLang)||ownItemName(d.id,'en')||d.name;
// A gear series in the panel's language, as DropsWindow.LocalSeries does it for the overlay: English puts it first («Tempest
// armor»), Portuguese and French after the piece («Equipamento da Tempestade», «Armure de la tempête») or as an adjective
// taken from a piece of the head word's gender («Armure oubliée», «Armes oubliées»). Null keeps the Spanish title.
const SERIES_AGREE={pt:['Yelmo','Peto'],fr:['Peto','Capa'],weapons:['Daga','Espada larga','Maza']};
function localSeries(armour,pieces){
 if(uiLang==='es')return null;
 const lang=pieces.every(p=>ownItemName(p.id,uiLang))?uiLang:'en',names=pieces.map(p=>ownItemName(p.id,lang));
 if(names.some(x=>!x)||new Set(names).size<2)return null;
 const words=names.map(x=>x.split(' '));
 if(lang==='en'){let same=0;while(words.every(w=>same<w.length-1&&w[same]===words[0][same]))same++;return same?`${words[0].slice(0,same).join(' ')} ${armour?'armor':'weapons'}`:null;}
 const head=lang==='pt'?(armour?'Equipamento':'Armas'):(armour?'Armure':'Armes'),tails=new Map();
 for(const w of words){const i=w.findIndex((x,k)=>k>0&&(/^(de|da|do|das|dos|du|des)$/.test(x)||/^d['’]/.test(x)));if(i>0){const t=w.slice(i).join(' ');tails.set(t,(tails.get(t)||0)+1);}}
 const usual=[...tails].sort((a,b)=>b[1]-a[1])[0];if(usual&&usual[1]*2>=names.length)return `${head} ${usual[0]}`;
 const piece=pieces.find(p=>(armour?SERIES_AGREE[lang]:SERIES_AGREE.weapons).includes(p.category)),named=piece&&ownItemName(piece.id,lang).split(' ');
 if(!named||named.length<2)return null;
 const adj=named[named.length-1];return `${head} ${!armour&&!/[sx]$/.test(adj)?adj+'s':adj}`;
}
function applyLanguage(lang){uiLang=lang||'es';document.documentElement.lang=uiLang==='pt'?'pt-BR':uiLang;itemTable();i18nBusy=true;try{i18nNode(document.body);}finally{i18nBusy=false;}styleThumbs();const sel=$('cfg-language');if(sel&&document.activeElement!==sel)sel.value=uiLang;}
new MutationObserver(list=>{if(i18nBusy||uiLang==='es')return;i18nBusy=true;try{for(const m of list){if(m.type==='characterData')i18nNode(m.target);else m.addedNodes.forEach(i18nNode);}}finally{i18nBusy=false;}})
 .observe(document.documentElement,{childList:true,subtree:true,characterData:true});
fetch('/data/i18n.json').then(r=>r.json()).then(d=>{i18nDict=d;applyLanguage(data?.settings?.Language||uiLang);}).catch(()=>{});
document.addEventListener('change',e=>{if(e.target.id==='cfg-language'){send('set',{key:'language',value:e.target.value});applyLanguage(e.target.value);}});
const _rcLang=window.renderCompanion;window.renderCompanion=d=>{_rcLang?.(d);const want=d?.settings?.Language||'es';if(want!==uiLang)applyLanguage(want);};

// ---- Jefes de campo (lista del mapa del juego, 0x0191) ----
let fbData=null,fbMap=null;
const fbClock=ms=>{const s=Math.max(0,Math.floor(ms/1000)),h=Math.floor(s/3600),m=Math.floor(s%3600/60),x=s%60;return h?`${h}:${String(m).padStart(2,'0')}:${String(x).padStart(2,'0')}`:`${String(m).padStart(2,'0')}:${String(x).padStart(2,'0')}`;};
function renderFieldBosses(){const maps=fbData;if(!maps||!$('fb-list'))return;
 if(!fbMap||!maps.some(m=>m.Key===fbMap))fbMap=(maps.find(m=>m.Bosses.some(b=>b.Phase!=='Unknown'))||maps[0])?.Key;
 const tabs=maps.map(m=>`<button class="fb-tab${m.Key===fbMap?' on':''}" data-fb-map="${esc(m.Key)}">${esc(m.Name)}</button>`).join('');
 if($('fb-maps').dataset.html!==tabs){$('fb-maps').innerHTML=tabs;$('fb-maps').dataset.html=tabs;}
 const all=maps.flatMap(m=>m.Bosses),known=all.filter(b=>b.Phase!=='Unknown').length;$('fb-count').textContent=`${known}/${all.length}`;
 const lead=data?.settings?.FieldBossLeadMinutes||5;const leadHtml=[1,5,10,15,20,25,30].map(n=>`<button class="fb-min${n===lead?' on':''}" data-fb-lead="${n}">${n} min</button>`).join('');
 if($('fb-lead').dataset.html!==leadHtml){$('fb-lead').innerHTML=leadHtml;$('fb-lead').dataset.html=leadHtml;}
 const map=maps.find(m=>m.Key===fbMap);const now=Date.now();
 $('fb-list').innerHTML=(map?.Bosses||[]).map(b=>{const at=b.At?new Date(b.At).getTime():null;let right,cls='';
  if(b.Phase==='Now'){right=at?`vivo ${fbClock(now-at)}`:'vivo';cls='up';}
  else if((b.Phase==='Respawn'||b.Phase==='Fixed')&&at){right=(b.Estimated?'~':'')+fbClock(at-now);cls=at-now<5*60000?'soon':'';}
  else if(b.Phase==='Dead'){right='muerto';cls='dead';}
  else right='—';
  const when=at?new Date(at).toLocaleTimeString(uiLang==='pt'?'pt-BR':uiLang==='en'?'en-GB':uiLang==='fr'?'fr-FR':'es-CL',{hour:'2-digit',minute:'2-digit'}):null;
  const src=b.Source==='seen'?' · visto por ti':'';
  const sub=b.Phase==='Now'?'Ha aparecido'+src:b.Phase==='Dead'?`Murió a las ${when} · abre la lista del mapa para saber su reaparición`:b.Phase==='Respawn'?(b.Estimated?`Aparece hacia las ${when} · estimado por su última muerte`:`Aparece a las ${when}`):b.Phase==='Fixed'?`Horario fijo · ${when}`:'Sin datos: abre la lista en el mapa del juego';
  return `<div class="fb-row ${cls}"><button class="fb-star${b.Starred?' on':''}" data-fb-star="${b.Npc}" title="Avisarme de este jefe">★</button>${icon(b.Icon,b.Name,'fb-icon')}<span class="fb-name"><b>${esc(b.Name)}</b><small>${esc(sub)}</small></span><strong class="fb-time">${esc(right)}</strong></div>`;}).join('')||'<p class="empty">Sin jefes en este mapa.</p>';}
const _rcFb=window.renderCompanion;window.renderCompanion=d=>{_rcFb?.(d);if(d?.fieldBosses){fbData=d.fieldBosses;}if($('fieldbosses')?.classList.contains('active'))renderFieldBosses();};
setInterval(()=>{if($('fieldbosses')?.classList.contains('active'))renderFieldBosses();},1000);
document.addEventListener('click',e=>{const t=e.target.closest('[data-fb-map]');if(t){fbMap=t.dataset.fbMap;renderFieldBosses();}
 const st=e.target.closest('[data-fb-star]');if(st){send('set',{key:'fieldBossStar',value:Number(st.dataset.fbStar)});st.classList.toggle('on');}
 const ld=e.target.closest('[data-fb-lead]');if(ld){send('set',{key:'fieldBossLead',value:Number(ld.dataset.fbLead)});document.querySelectorAll('[data-fb-lead]').forEach(x=>x.classList.toggle('on',x===ld));}});

// ---- Registro de botín y kinah de la sesión (0x1B56) ----
function renderLoot(f){if(!f||!$('loot-log'))return;const n=v=>Number(v||0).toLocaleString(uiLang==='pt'?'pt-BR':uiLang==='en'?'en-US':uiLang==='fr'?'fr-FR':'es-CL');
 const hours=Math.max(1/60,(Date.now()-new Date(f.start).getTime())/3600000);
 $('loot-kinah').textContent=n(f.kinah);$('loot-kinah-h').textContent=n(Math.round(f.kinah/hours));$('loot-value').textContent=n(f.lootValue);$('loot-kinah-now').textContent=f.kinahNow==null?'—':n(f.kinahNow);
 const row=(icn,name,grade,right,sub)=>`<div class="loot-row">${icon(icn,name,'loot-icon')}<span><b class="grade-${esc(grade)}">${esc(name)}</b><small>${esc(sub)}</small></span><strong>${esc(right)}</strong></div>`;
 const log=(f.loot||[]).map(l=>row(l.Icon,l.Name,l.Grade,'×'+n(l.Quantity),`${new Date(l.At).toLocaleTimeString(uiLang==='pt'?'pt-BR':uiLang==='en'?'en-GB':uiLang==='fr'?'fr-FR':'es-CL')}${l.Value?' · '+n(l.Value)+' kinah':''}`)).join('');
 const top=(f.top||[]).map(t=>row(t.icon,t.name,t.grade,n(t.value)+' kinah','×'+n(t.qty))).join('');
 const set=(id,html,empty)=>{const el=$(id);const v=html||`<p class="empty">${empty}</p>`;if(el.dataset.html!==v){el.innerHTML=v;el.dataset.html=v;}};
 set('loot-log',log,'Lo que recojas aparecerá aquí con su cantidad y su valor de venta al mercader.');set('loot-top',top,'Sin botín todavía.');}
const _rcLoot=window.renderCompanion;window.renderCompanion=d=>{_rcLoot?.(d);if(d?.farm)renderLoot(d.farm);};

// ---- Objetivos: meta de muertes, kinah o botín con estimación de fin ----
const goalPresets={kills:[50,100,300,500],kinah:[100000,500000,1000000,5000000],loot:[50000,200000,1000000]};
let goalInit=false;
const goalLocale=()=>uiLang==='pt'?'pt-BR':uiLang==='en'?'en-US':uiLang==='fr'?'fr-FR':'es-CL';
function goalPresetButtons(){const k=$('goal-kind').value;$('goal-presets').innerHTML=goalPresets[k].map(v=>`<button class="chip" data-goal-preset="${v}">${Number(v).toLocaleString(goalLocale())}</button>`).join('');}
function goalSend(minutes){const target=Math.max(1,Math.round(Number($('goal-target').value)||0));send('goal-set',{kind:$('goal-kind').value,target,minutes:minutes??Math.max(0,Math.round(Number($('goal-minutes').value)||0))});}
if($('goal-kind')){$('goal-kind').onchange=goalPresetButtons;goalPresetButtons();$('goal-set').onclick=()=>goalSend();$('goal-challenge').onclick=()=>{$('goal-minutes').value=30;goalSend(30);};
 $('goal-presets').addEventListener('click',e=>{const b=e.target.closest('[data-goal-preset]');if(b)$('goal-target').value=b.dataset.goalPreset;});}
function spanText(sec){sec=Math.max(0,Math.round(sec));const h=Math.floor(sec/3600),m=Math.floor(sec%3600/60),s=sec%60;return h?`${h} h ${String(m).padStart(2,'0')} m`:`${m} m ${String(s).padStart(2,'0')} s`;}
function renderGoal(d){if(!$('goal-view'))return;const s=d.settings,o=d.objective;const n=v=>Number(v||0).toLocaleString(goalLocale());
 if(!goalInit&&s){if(s.GoalTarget>0){$('goal-kind').value=s.GoalKind;$('goal-target').value=s.GoalTarget;$('goal-minutes').value=s.GoalMinutes;goalPresetButtons();}goalInit=true;}
 const put=v=>{const el=$('goal-view');if(el.dataset.html!==v){el.innerHTML=v;el.dataset.html=v;el.querySelectorAll('[data-width]').forEach(x=>x.style.width=x.dataset.width+'%');}};
 if(!o){$('goal-sub').textContent='Sin objetivo definido';put('<p class="empty">Define un objetivo para ver tu progreso, tu ritmo por hora y cuánto te falta.</p>');return;}
 const unit={kills:'muertes',kinah:'kinah',loot:'kinah en botín'}[o.Kind]||'';
 $('goal-sub').textContent=o.Minutes>0?`Reto de ${o.Minutes} min`:'Sin límite de tiempo';
 const line=o.Done?`¡Objetivo cumplido en ${spanText(o.Elapsed)}!`:o.ChallengeLeft!=null?(o.ChallengeLeft<=0?'Reto terminado: se acabó el tiempo':`Quedan ${spanText(o.ChallengeLeft)} · `+(o.EtaSeconds!=null?(o.OnPace?`vas a tiempo: terminas en ${spanText(o.EtaSeconds)}`:`a este ritmo te faltan ${spanText(o.EtaSeconds)}`):'sin ritmo aún')):o.EtaSeconds!=null?`A este ritmo, ${spanText(o.EtaSeconds)} restantes`:'Esperando el primer progreso';
 const cls=o.Done?'done':o.OnPace===false?'late':'';
 const end=o.Done?'—':o.EtaSeconds!=null?new Date(Date.now()+o.EtaSeconds*1000).toLocaleTimeString(goalLocale(),{hour:'2-digit',minute:'2-digit'}):'—';
 put(`<div class="goal-big ${cls}"><strong>${o.Percent.toFixed(0)} %</strong><span>${n(o.Progress)} / ${n(o.Target)} ${unit}</span></div><div class="goal-bar ${cls}"><i data-width="${Math.min(100,o.Percent).toFixed(1)}"></i></div><p class="goal-line">${line}</p>`
  +`<div class="loot-metrics"><div><span>Por hora</span><strong>${n(Math.round(o.PerHour))}</strong></div><div><span>Faltan</span><strong>${n(Math.max(0,o.Target-o.Progress))}</strong></div><div><span>Tiempo</span><strong>${spanText(o.Elapsed)}</strong></div><div><span>Fin estimado</span><strong>${end}</strong></div></div>`);}
const _rcGoal=window.renderCompanion;window.renderCompanion=d=>{_rcGoal?.(d);renderGoal(d);};

// ---- Cubos ocultos: el cubo vivo (0x3436) y los puntos posibles alrededor ----
function cubeArrow(deg){return deg==null?'':`<span class="cube-arrow" data-rot="${(deg-90).toFixed(0)}">➤</span>`;}
function renderCubes(c){if(!c||!$('cube-now'))return;const loc=uiLang==='pt'?'pt-BR':uiLang==='en'?'en-GB':uiLang==='fr'?'fr-FR':'es-CL';
 $('cube-map').textContent=c.map||'Mapa sin identificar';
 const put=(id,v)=>{const el=$(id);if(el.dataset.html!==v){el.innerHTML=v;el.dataset.html=v;el.querySelectorAll('[data-rot]').forEach(x=>x.style.transform=`rotate(${x.dataset.rot}deg)`);}};
 const a=c.Active;const hgt=c.Height==null?'':Math.abs(c.Height)<2?'a tu altura':c.Height>0?`${c.Height.toFixed(0)} m arriba`:`${(-c.Height).toFixed(0)} m abajo`;
 const live=a?`<div class="cube-live">${cubeArrow(c.Bearing)}<div><b>Cubo oculto cerca</b><small>${[hgt,'desde las '+new Date(a.At).toLocaleTimeString(loc,{hour:'2-digit',minute:'2-digit'})].filter(Boolean).join(' · ')}</small></div><strong>${c.Distance==null?'—':c.Distance.toFixed(0)+' m'}</strong></div>`
  :`<p class="empty">Ningún cubo a la vista. Cuando el servidor muestre uno cerca, aparecerá aquí y sobre el overlay.</p>`;
 const spots=c.NearestSpot==null?'':`<div class="loot-metrics"><div><span>Puntos posibles a menos de 100 m</span><strong>${c.SpotsNear}</strong></div><div><span>Punto más cercano</span><strong>${cubeArrow(c.NearestBearing)} ${c.NearestSpot.toFixed(0)} m</strong></div></div>`;
 put('cube-now',live+spots);
 const rows=(c.history||[]).map(h=>`<div class="loot-row"><span class="cube-dot ${h.GoneAt?'gone':''}">◆</span><span><b>${esc(h.map||'?')}</b><small>${new Date(h.At).toLocaleString(loc,{day:'2-digit',month:'short',hour:'2-digit',minute:'2-digit'})} · ${h.x}, ${h.y} · altura ${h.z}${h.SeenFrom!=null?` · detectado a ${Math.round(h.SeenFrom)} m`:''}</small></span><strong>${h.Missing?'Ya no estaba':h.GoneAt?'Abierto':h.LeftRangeAt?'Fuera de alcance':'Sigue ahí'}</strong></div>`).join('');
 put('cube-history',rows||'<p class="empty">Aún no has visto ningún cubo.</p>');}
const _rcCubes=window.renderCompanion;window.renderCompanion=d=>{_rcCubes?.(d);renderCubes(d.cubes);};
// The panel's CSP (style-src 'self') ignores style="" attributes in generated HTML: bars, colours and rotations set
// that way never showed. Styles set through the CSSOM are allowed, so every added element's style attribute is re-applied.
(()=>{const fix=el=>{if(el.nodeType!==1)return;if(el.hasAttribute('style'))el.style.cssText=el.getAttribute('style');el.querySelectorAll?.('[style]').forEach(x=>x.style.cssText=x.getAttribute('style'));};
 fix(document.body);new MutationObserver(ms=>{for(const m of ms)m.addedNodes.forEach(fix);}).observe(document.body,{childList:true,subtree:true});})();

// The assistant's move button says what the next click does.
const _rcAssist=window.renderCompanion;window.renderCompanion=d=>{_rcAssist?.(d);const b=$('assistant-move');if(b){const t=d?.assistantMoving?'Fijar':'Mover';if(b.textContent!==t)b.textContent=t;b.classList.toggle('accent',!!d?.assistantMoving);}};

// Novedades: the header pill shows how many unread items there are (an update, the site's notices).
const _rcNews=window.renderCompanion;window.renderCompanion=d=>{_rcNews?.(d);const n=d?.news?.unread||0;const p=$('news-pill');if(p){p.classList.toggle('unread',n>0);const t=n>0?`Novedades (${n})`:'Novedades';if($('news-pill-text').textContent!==t)$('news-pill-text').textContent=t;}};

// Recursos: coins, Od, entries and limits of each character (ResourceBook), the one in play first.
let resChoice=null;
function renderResources(list){const t=x=>x;const view=$('res-view'),sel=$('res-character');if(!view||!sel||!list)return;
 const loc=uiLang==='pt'?'pt-BR':uiLang==='en'?'en-GB':uiLang==='fr'?'fr-FR':'es-CL';const n=v=>v==null?'—':Number(v).toLocaleString(loc);
 const opts=list.map(r=>`<option value="${esc(r.key)}">${esc(r.character)}${r.serverName?' · '+esc(r.serverName):''}${r.live?' · '+t('en juego'):''}</option>`).join('');
 if(sel.dataset.html!==opts){sel.innerHTML=opts;sel.dataset.html=opts;}
 if(!list.length){sel.hidden=true;return;}sel.hidden=false;
 if(!resChoice||!list.some(r=>r.key===resChoice))resChoice=list[0].key;if(sel.value!==resChoice)sel.value=resChoice;
 const r=list.find(x=>x.key===resChoice);const max=data?.settings?.OdMax||840;
 const when=d=>d?new Date(d).toLocaleString(loc,{day:'2-digit',month:'short',hour:'2-digit',minute:'2-digit'}):null;
 const total=(r.kinah??0)+(r.bound??0);
 const top=`<div class="res-top"><article class="metric kinah"><span>${t('Kinah')}</span><strong>${n(r.kinah)}</strong><small>${t('Comerciable')}</small></article><article class="metric"><span>${t('Kinah vinculado')}</span><strong>${n(r.bound)}</strong><small>${t('Solo para este personaje')}</small></article><article class="metric main"><span>${t('Total')}</span><strong>${r.kinah==null&&r.bound==null?'—':n(total)}</strong><small>${t('Kinah + vinculado')}</small></article></div>`;
 const card=(img,name,value,sub,cls='')=>`<div class="res-card ${cls}">${img==null?'<i class="icon-empty"></i>':icon(img,name,'')}<span><small>${esc(name)}</small><strong>${value}</strong>${sub?`<small>${esc(sub)}</small>`:''}</span></div>`;
 const od=r.od?card('/assets/game/Icon_Item_Odenergy_A_001.webp',t('Energía Od'),`${n(r.od.natural)}${r.od.extra?` (+${n(r.od.extra)})`:''}/${n(max)}`,''):'';
 const coins=r.coins.map(c=>card(c.icon,c.name,n(c.count),'')).join('');
 const hms=ms=>{const s=Math.round(ms/1000),h=Math.floor(s/3600),m=Math.floor(s%3600/60);return `${h}:${String(m).padStart(2,'0')}:${String(s%60).padStart(2,'0')}`;};
 const val=c=>c.time?hms(c.value):n(c.value)+(c.extra?` (+${n(c.extra)})`:'');
 const named=r.counters.filter(c=>c.name).map(c=>card(c.icon,c.name,val(c),'')).join('');
 const counters=named;
 const html=top+`<article class="panel"><div class="panel-head"><h3>${t('Monedas y energía')}</h3><span>${r.coinsAt?t('Actualizado')+' '+when(r.coinsAt):t('Llega al entrar al juego')}</span></div>${od||coins?`<div class="res-grid">${od}${coins}</div>`:`<p class="empty">${t('Sin datos del inventario todavía.')}</p>`}</article>`
  +(counters?`<article class="panel"><div class="panel-head"><h3>${t('Entradas')}</h3><span>${r.countersAt?t('Actualizado')+' '+when(r.countersAt):''}</span></div><div class="res-grid">${counters}</div></article>`:'');
 if(view.dataset.html!==html){view.innerHTML=html;view.dataset.html=html;}}
$('res-character')?.addEventListener('change',e=>{resChoice=e.target.value;if(data)renderResources(data.resources);});
const _rcRes=window.renderCompanion;window.renderCompanion=d=>{_rcRes?.(d);renderResources(d.resources);};
// Mercado: aion2.exchange's record of the game's Market (with its authors' permission; the app only reads it and links
// nowhere), on the server chosen in the selector. «Todo el Mercado» lists every item it follows; «Mi inventario» values the
// character's items, with the player's own price winning. The host sends each list only when it changes; a price being
// typed or a menu open is not redrawn under the cursor.
let marketData=null,marketBoard=null,marketHeld=false,marketServersLocal=null;
const marketState={mode:'all',section:'',sort:'listings',onSale:true,bound:false};
try{Object.assign(marketState,JSON.parse(localStorage.getItem('market:view')||'{}'));}catch{}
const marketSave=()=>{try{localStorage.setItem('market:view',JSON.stringify(marketState));}catch{}};
const marketFold=s=>String(s||'').normalize('NFD').replace(/[̀-ͯ]/g,'').toLowerCase();
const MARKET_SECTIONS={weapons:'Armas',armor:'Armaduras',accessories:'Accesorios',consumables:'Consumibles',skins:'Apariencias',materials:'Materiales',other:'Otros'};
const MARKET_REGIONS={LA:'Sudamérica',NAE:'Norteamérica Este',NAW:'Norteamérica Oeste',EU:'Europa',AS:'Asia'};
const MARKET_FACTIONS={Asmodian:'Asmodianos',Elyos:'Elios'};
// Until the source's list arrives (first start, offline): the region's servers the app ships (data/servers.json).
fetch('/data/servers.json').then(r=>r.json()).then(d=>{marketServersLocal=(d.serverList||[]).map(x=>({id:x.serverId,name:x.serverName,region:'LA',faction:x.raceId===2?'Asmodian':'Elyos'}));renderMarket();}).catch(()=>{marketServersLocal=[];});
const marketServers=()=>marketBoard?.servers?.length?marketBoard.servers:marketServersLocal||[];
const marketServerName=id=>marketServers().find(x=>x.id===id)?.name||`Servidor ${id}`;
function renderMarketServers(home,server){const sel=$('market-server');if(!sel||document.activeElement===sel)return;const list=marketServers();if(!list.length)return;
 const homeRegion=list.find(x=>x.id===home)?.region;const order=[homeRegion,'LA','NAE','NAW','EU','AS'].filter((r,i,a)=>r&&a.indexOf(r)===i);
 const groups=[];for(const region of [...order,...new Set(list.map(x=>x.region).filter(r=>!order.includes(r)))])for(const faction of ['Asmodian','Elyos']){const g=list.filter(x=>x.region===region&&x.faction===faction).sort((a,b)=>a.name.localeCompare(b.name));if(g.length)groups.push([`${MARKET_REGIONS[region]||region} · ${MARKET_FACTIONS[faction]||faction}`,g]);}
 const html=groups.map(([label,g])=>`<optgroup label="${esc(label)}">${g.map(x=>`<option value="${Number(x.id)}">${esc(x.name)}${x.id===home?' · tu servidor':''}</option>`).join('')}</optgroup>`).join('');
 if(sel.dataset.html!==html){sel.innerHTML=html;sel.dataset.html=html;}if(server&&sel.value!==String(server))sel.value=String(server);}
function renderMarket(m,b){const view=$('market-view');if(!view)return;if(m)marketData=m;if(b)marketBoard=b;m=marketData;b=marketBoard;if(!m&&!b)return;
 renderMarketServers(m?.home,m?.server??b?.server);
 const active=document.activeElement;if(active&&view.contains(active)&&active.matches('input,select')){marketHeld=true;return;}marketHeld=false;
 const loc=uiLang==='pt'?'pt-BR':uiLang==='en'?'en-GB':uiLang==='fr'?'fr-FR':'es-CL';const n=v=>v==null?'—':Number(v).toLocaleString(loc);
 const ago=d=>{const min=Math.max(0,Math.round((Date.now()-new Date(d))/60000));return min<60?`hace ${min} min`:min<1440?`hace ${Math.round(min/60)} h`:`hace ${Math.round(min/1440)} d`;};
 const clock=d=>new Date(d).toLocaleTimeString(loc,{hour:'2-digit',minute:'2-digit'});
 const q=marketFold(($('market-search')?.value||'').trim());const mode=marketState.mode==='mine'?'mine':'all';
 const server=m?.server??b?.server,board=b&&b.server===server?b:null,rows=board?.rows||[];
 const held=new Map((m?.items||[]).map(r=>[r.item,r.qty]));
 const modes=`<div class="market-modes" role="tablist"><button role="tab" data-market-mode="all" class="${mode==='all'?'active':''}">Todo el Mercado<small>${rows.length?n(rows.length):''}</small></button><button role="tab" data-market-mode="mine" class="${mode==='mine'?'active':''}">Mi inventario<small>${m?.items?.length?n(m.items.filter(r=>!r.bound).length):''}</small></button></div>`;
 const credit=board?`Datos: aion2.exchange · foto de las ${clock(board.snapshotAt||board.fetchedAt)}`:'Datos: aion2.exchange';
 const failed=(b?.status||m?.status)?`<p class="market-note">No se pudo actualizar el Mercado (${esc(b?.status||m?.status)}).${board?` Se muestran los datos de las ${clock(board.snapshotAt||board.fetchedAt)}.`:''}</p>`:'';
 const title=server?esc(marketServerName(server))+(m?.home&&server!==m.home?' · no es tu servidor':''):'Esperando tu personaje';
 let top,filters,list;
 if(mode==='all'){
  const onSale=rows.filter(r=>r.price!=null);
  top=`<div class="res-top"><article class="metric main"><span>En venta ahora</span><strong>${board?n(onSale.length):'—'}</strong><small>${board?`de ${n(rows.length)} objetos seguidos`:'Cargando…'}</small></article><article class="metric"><span>Anuncios</span><strong>${board?n(onSale.reduce((a,r)=>a+(r.listings||0),0)):'—'}</strong><small>Ofertas publicadas en el Mercado</small></article><article class="metric"><span>Foto del Mercado</span><strong>${board?clock(board.snapshotAt||board.fetchedAt):'—'}</strong><small>${board?ago(board.snapshotAt||board.fetchedAt)+' · se actualiza cada hora':'Datos: aion2.exchange'}</small></article></div>`;
  filters=`<div class="market-filters"><select data-market-filter="section" aria-label="Categoría"><option value="">Todas las categorías</option>${Object.entries(MARKET_SECTIONS).map(([k,v])=>`<option value="${k}"${marketState.section===k?' selected':''}>${v}</option>`).join('')}</select>`
   +`<select data-market-filter="sort" aria-label="Orden">${[['listings','Más anuncios'],['price-desc','Precio: mayor primero'],['price-asc','Precio: menor primero'],['up','Más subieron (24 h, 3+ anuncios)'],['down','Más bajaron (24 h, 3+ anuncios)'],['name','Nombre']].map(([k,v])=>`<option value="${k}"${marketState.sort===k?' selected':''}>${v}</option>`).join('')}</select>`
   +`<label class="market-check"><input type="checkbox" data-market-filter="onSale"${marketState.onSale?' checked':''}> Solo en venta ahora</label></div>`;
  const price=r=>r.price??r.last;const cmpName=(a,c)=>a.name.localeCompare(c.name,loc);
  const sorters={listings:(a,c)=>(c.listings||0)-(a.listings||0)||cmpName(a,c),'price-desc':(a,c)=>(price(c)??-1)-(price(a)??-1),'price-asc':(a,c)=>(price(a)??Infinity)-(price(c)??Infinity),up:(a,c)=>(c.change??-Infinity)-(a.change??-Infinity),down:(a,c)=>(a.change??Infinity)-(c.change??Infinity),name:cmpName};
  // Ups and downs of one odd listing are noise (+499.000 %): those two orders need 3 listings or more, as on aion2.exchange.
  const movers=marketState.sort==='up'||marketState.sort==='down';
  const found=rows.filter(r=>(!marketState.onSale||r.price!=null)&&(!movers||(r.listings||0)>=3&&r.change!=null)&&(!marketState.section||r.section===marketState.section)&&(!q||marketFold(`${r.name} ${r.en||''} ${r.item}`).includes(q))).sort(sorters[marketState.sort]||sorters.listings);
  const shown=found.slice(0,300);
  const change=c=>c==null?'<span class="num">—</span>':`<span class="num change ${c>0?'up':c<0?'down':''}">${c>0?'▲':c<0?'▼':''} ${n(Math.abs(c))} %</span>`;
  const row=r=>`<div class="market-row all">${icon(r.icon,r.name)}<span><b class="grade-${esc(r.grade)}">${esc(r.name)}</b><small>${esc(MARKET_SECTIONS[r.section]||'')}${held.has(r.item)?`<em> · tienes ${n(held.get(r.item))}</em>`:''}</small></span><span class="num value">${r.price!=null?n(r.price):`<small>último</small>${n(r.last)}`}</span><span class="num">${r.price!=null?n(r.listings):'—'}</span>${change(r.change)}</div>`;
  list=!board?`<p class="empty">${b?.status?'Sin datos del Mercado todavía.':'Cargando el Mercado…'}</p>`:shown.length?`<div class="market-row all head"><span></span><span>Objeto</span><span class="num">Precio más bajo</span><span class="num">En venta</span><span class="num">24 h</span></div>${shown.map(row).join('')}${found.length>shown.length?`<p class="market-note">Se muestran 300 de ${n(found.length)}: afina la búsqueda o la categoría.</p>`:''}`:'<p class="empty">Ningún objeto coincide con los filtros.</p>';
 }else{
  top=m?`<div class="res-top"><article class="metric main"><span>Valor en el Mercado</span><strong>${m.priced?n(m.value):'—'}</strong><small>${m.priced} con precio · ${m.unpriced} sin precio</small></article><article class="metric"><span>Venta a mercaderes</span><strong>${n(m.npcValue)}</strong><small>Lo que pagan los PNJ por todo</small></article><article class="metric kinah"><span>Kinah</span><strong>${n(m.kinah)}</strong><small>Comerciable</small></article></div>`:'';
  filters=`<div class="market-filters"><label class="market-check"><input type="checkbox" data-market-filter="bound"${marketState.bound?' checked':''}> Mostrar vinculados</label></div>`;
  const items=(m?.items||[]).filter(r=>(marketState.bound||!r.bound)&&(!q||marketFold(`${r.name} ${r.item}`).includes(q)));
  const partial=m&&!m.complete?'<p class="market-note">Solo aparecen los objetos que cambiaron desde que abriste la app. Para ver todo el inventario, sal a la selección de personaje y vuelve a entrar.</p>':'';
  const priceInput=r=>`<input type="number" min="0" step="1" inputmode="numeric" data-market-item="${Number(r.item)}" value="${r.own??''}" placeholder="${r.market?n(r.market.price):'—'}" aria-label="${esc(r.name)}">`;
  const row=r=>`<div class="market-row${r.bound?' bound':''}">${icon(r.icon,r.name)}<span><b class="grade-${esc(r.grade)}">${esc(r.name)}</b><small>${r.bound?'Vinculado · no se vende':r.npc?`PNJ ${n(r.npc)} c/u`:''}</small></span><span class="num">${n(r.qty)}</span><span>${r.bound?'':priceInput(r)}</span><span class="num">${r.market?`${r.market.listed?'':'<small>último</small>'}${n(r.market.price)}<small>${r.market.listed?n(r.market.listings)+' en venta':ago(r.market.at)}</small>`:'—'}</span><span class="num value">${r.total!=null?n(r.total):'—'}</span></div>`;
  list=partial+(items.length?`<div class="market-row head"><span></span><span>Objeto</span><span class="num">Cantidad</span><span class="num">Tu precio</span><span class="num">Mercado</span><span class="num">Valor</span></div>${items.map(row).join('')}`:`<p class="empty">${q?'Ningún objeto coincide con la búsqueda.':'Aún no hay inventario. El juego lo envía al entrar con el personaje: sal a la selección de personaje y vuelve a entrar con la app abierta.'}</p>`);
 }
 const html=modes+top+`<article class="panel"><div class="panel-head"><h3>${title}</h3><span>${credit}</span></div>${filters}${failed}<div class="market-list">${list}</div></article>`;
 if(view.dataset.html!==html){view.innerHTML=html;view.dataset.html=html;}}
document.addEventListener('change',e=>{const el=e.target;
 if(el.closest?.('input[data-market-item]'))send('market-price',{item:Number(el.dataset.marketItem),price:Math.max(0,Math.round(Number(el.value)||0))});
 const f=el.dataset?.marketFilter;if(f){marketState[f]=el.type==='checkbox'?el.checked:el.value;marketSave();el.blur();renderMarket();}});
document.addEventListener('click',e=>{const b=e.target.closest?.('[data-market-mode]');if(b){marketState.mode=b.dataset.marketMode;marketSave();renderMarket();}});
document.addEventListener('focusout',e=>{if(e.target.matches?.('#market-view input,#market-view select')&&marketHeld)setTimeout(()=>renderMarket(),0);});
$('market-search')?.addEventListener('input',()=>renderMarket());
const _rcMarket=window.renderCompanion;window.renderCompanion=d=>{_rcMarket?.(d);renderMarket(d.market,d.marketBoard);};
// Clasificación: the player's places in the game's rankings (RankingWatch, 0x578D), sent when the ranking window is opened
// in the game: each ranking with its class and all-classes places, change, players ranked, points, its detail and history.
let rankingData=null,dungeonNames={};
fetch('/data/dungeon-names.json').then(r=>r.json()).then(d=>{dungeonNames=d;renderRanking();}).catch(()=>{});
const RANK_NAMES={1:'Abismo',3:'Jefes',4:'Expedición',21:'Despertar',5:'Arena',6:'Arena 2'};
function renderRanking(r){const view=$('ranking-view');if(!view)return;if(r)rankingData=r;r=rankingData;if(!r)return;
 const loc=uiLang==='pt'?'pt-BR':uiLang==='en'?'en-GB':uiLang==='fr'?'fr-FR':'es-CL';const n=v=>v==null?'—':Number(v).toLocaleString(loc);
 const clock=d=>new Date(d).toLocaleTimeString(loc,{hour:'2-digit',minute:'2-digit'}),day=d=>new Date(d).toLocaleDateString(loc,{day:'2-digit',month:'short'});
 const when=d=>{const t=new Date(d),today=new Date();return t.toDateString()===today.toDateString()?clock(d):day(d)+' '+clock(d);};
 const mmss=s=>s==null?'':`${Math.floor(s/60)}:${String(s%60).padStart(2,'0')}`;
 const detail=(type,d)=>{if(!d)return '';
  if(d.kill_count!=null)return `${n(d.kill_count)} bajas · ${n(d.death_count)} muertes · ${n(d.assist_count)} asistencias`;
  if(d.play_count!=null)return `${n(d.play_count)} partidas · ${n(d.win_count)} victorias · ${n(d.lose_count)} derrotas`;
  if(d.boss_group_id!=null)return `Mejor marca: nivel ${n(d.best_clear_point_level)} · ${mmss(d.best_clear_point_time)} · ${n(d.best_clear_point)} puntos`;
  if(Array.isArray(d.dungeon_info)&&d.dungeon_info[0]){const x=d.dungeon_info[0];return `Mejor marca: nivel ${n(x.best_clear_level)} · ${mmss(x.best_clear_point_time)} · ${n(x.best_clear_point)} puntos`;}
  if(d.dungeon_id!=null)return `${dungeonNames[d.dungeon_id]||'Mazmorra '+d.dungeon_id} · mejor marca ${mmss(d.best_clear_point_time)} · ${n(d.best_clear_point)} puntos`;
  return '';};
 const change=c=>!c?'<em class="same">=</em>':c>0?`<em class="up">▲${n(c)}</em>`:`<em class="down">▼${n(-c)}</em>`;
 const top=(rank,total)=>total?` · top ${(rank/total*100).toLocaleString(loc,{maximumFractionDigits:rank/total<.1?1:0})} %`:'';
 const row=(label,e)=>e?`<div class="rank-row"><span>${label}</span><strong>#${n(e.rank)}</strong>${change(e.change)}<small>de ${n(e.total)}${top(e.rank,e.total)}</small></div>`:`<div class="rank-row empty"><span>${label}</span><small>Ábrela en el juego para verla</small></div>`;
 // History: the place of each hourly snapshot, best (lowest) at the top; class in teal, all classes in gold.
 const chart=h=>{const lines=[[2,'cls'],[0,'all']].map(([list,cls])=>[cls,h.filter(p=>p.list===list)]).filter(([,p])=>p.length>1);if(!lines.length)return '';
  const t0=Math.min(...h.map(p=>+new Date(p.at))),t1=Math.max(...h.map(p=>+new Date(p.at))),w=300,ht=56;
  const path=pts=>{const lo=Math.min(...pts.map(p=>p.rank)),hi=Math.max(...pts.map(p=>p.rank));return pts.map(p=>`${((+new Date(p.at)-t0)/Math.max(1,t1-t0)*(w-8)+4).toFixed(1)},${(hi===lo?ht/2:4+(p.rank-lo)/(hi-lo)*(ht-8)).toFixed(1)}`).join(' ');};
  const cap=lines.map(([cls,p])=>`<span class="${cls}">${cls==='cls'?'Clase':'General'}: #${n(p[0].rank)} → #${n(p[p.length-1].rank)}</span>`).join('');
  return `<svg class="rank-chart" viewBox="0 0 ${w} ${ht}" preserveAspectRatio="none" aria-hidden="true">${lines.map(([cls,p])=>`<polyline class="${cls}" points="${path(p)}"/>`).join('')}</svg><p class="rank-chart-cap">${cap}<span>desde ${day(t0)}</span></p>`;};
 const cards=(r.rankings||[]).map(k=>{const any=k.class||k.all,d=any?.detail;
  return `<article class="rank-card${k.type===1?' main':''}"><header><h3>${esc(RANK_NAMES[k.type]||'Clasificación '+k.type)}</h3><small>${any?`Vista ${when(any.seen)} · `+(new Date(any.snapshot)>new Date()?`se renueva a las ${clock(any.snapshot)}`:'hay datos nuevos: vuelve a abrirla en el juego'):''}</small></header>`
   +row('Tu clase',k.class)+row('Todas las clases',k.all)
   +(any?`<p class="rank-points">🏆 ${n(any.points)} puntos</p>`:'')+(detail(k.type,d)?`<p class="rank-detail">${esc(detail(k.type,d))}</p>`:'')+chart(k.history||[])+'</article>';}).join('');
 const html=cards?`<div class="rank-grid">${cards}</div>`:'<p class="empty">Abre la ventana de clasificación en el juego con la app abierta: aquí aparecerán tus puestos en tu clase y en todas las clases.</p>';
 if(view.dataset.html!==html){view.innerHTML=html;view.dataset.html=html;}}
// «Grabar 2 minutos»: recording (seconds left), saved (file and size) or why it failed.
function renderRecording(s){const b=$('record-button'),note=$('record-status');if(!b||!note)return;
 const loc=uiLang==='pt'?'pt-BR':uiLang==='en'?'en-GB':uiLang==='fr'?'fr-FR':'es-CL';
 b.disabled=s?.state==='recording';const label=s?.state==='recording'?`Grabando… ${Math.floor(s.left/60)}:${String(s.left%60).padStart(2,'0')}`:'Grabar';if(b.textContent!==label)b.textContent=label;
 const text=!s?'':s.state==='recording'?'Haz ahora en el juego lo que quieras grabar.':s.state==='done'?`Grabación guardada: ${s.file} (${(s.bytes/1048576).toLocaleString(loc,{maximumFractionDigits:1})} MB), en la carpeta de datos.`:`No se grabó: ${s.reason}`;
 note.hidden=!text;if(note.textContent!==text)note.textContent=text;note.classList.toggle('failed',s?.state==='failed');}
const _rcRanking=window.renderCompanion;window.renderCompanion=d=>{_rcRanking?.(d);renderRanking(d.ranking);renderRecording(d.recording);};


// Cabecera: overlay visible u oculto con un clic; pie: la versión instalada.
const _rcChrome=window.renderCompanion;window.renderCompanion=d=>{_rcChrome?.(d);const p=$('overlay-pill');if(p){p.classList.toggle('on',!!d.overlay);const t=d.overlay?'Overlay visible':'Overlay oculto';if($('overlay-pill-text').textContent!==t)$('overlay-pill-text').textContent=t;}
 const v=$('footer-version');if(v&&d.version){const t='FullAIon2Companion '+d.version;if(v.textContent!==t)v.textContent=t;}};

// Soporte dentro de la app: consultas de la cuenta (las mismas de la web), con el token del dispositivo.
const supStates={open:'Abierta',pending:'Abierta',answered:'Respondida',waiting:'Esperando tu respuesta',closed:'Cerrada'};
const supTopics={account:'Cuenta',desktop:'Aplicación',map:'Mapa',data:'Datos del juego',subscription:'Suscripción',other:'Otro'};
let supSentAt=0,supLastOpen=null;
function renderSupport(s){if(!s||!$('sup-list'))return;const loc=uiLang==='pt'?'pt-BR':uiLang==='en'?'en-GB':uiLang==='fr'?'fr-FR':'es-CL';
 const when=x=>x?new Date(String(x).replace(' ','T')+(String(x).endsWith('Z')?'':'Z')).toLocaleString(loc,{day:'2-digit',month:'short',hour:'2-digit',minute:'2-digit'}):'';
 $('sup-unpaired').hidden=!!s.paired;$('sup-send').disabled=!s.paired;
 const put=(id,html)=>{const el=$(id);if(el.dataset.html!==html){el.innerHTML=html;el.dataset.html=html;}};
 if(s.list)put('sup-list',s.list.length?s.list.map(t=>`<button class="sup-item" data-ticket="${esc(t.id)}"><b>${esc(t.subject)}</b><span class="sup-state ${esc(t.status)}">${esc(supStates[t.status]||t.status)}</span><small>${esc(supTopics[t.category]||t.category)} · ${esc(when(t.updated_at))}</small></button>`).join(''):'<p class="empty">Aún no tienes consultas.</p>');
 if($('sup-status').textContent!==(s.status||''))$('sup-status').textContent=s.status||'';
 const o=s.open;
 if(o&&o.id!==supLastOpen&&supSentAt&&Date.now()-supSentAt<60000){$('sup-subject').value='';$('sup-message').value='';supSentAt=0;}
 supLastOpen=o?.id||null;
 put('sup-thread',o?`<article class="panel"><div class="panel-head"><h3>${esc(o.subject)}</h3><span><span class="sup-state ${esc(o.status)}">${esc(supStates[o.status]||o.status)}</span> <button data-command="support-close">Cerrar</button></span></div>`
  +(o.messages||[]).map(m=>`<div class="sup-msg ${Number(m.mine)?'':'team'}"><small>${Number(m.mine)?'Tú':'Equipo de soporte'} · ${esc(when(m.created_at))}</small>${esc(m.body)}</div>`).join('')
  +(o.status==='closed'?'<p class="muted small">Esta consulta está cerrada. Crea una nueva si necesitas ayuda.</p>':`<div class="sup-reply"><textarea id="sup-reply" rows="3" maxlength="4500" placeholder="Escribe tu respuesta"></textarea><div class="actions"><button class="accent" data-reply="${esc(o.id)}">Responder</button></div></div>`)+'</article>':'');}
$('sup-send')?.addEventListener('click',()=>{supSentAt=Date.now();send('support-create',{subject:$('sup-subject').value,category:$('sup-category').value,message:$('sup-message').value,character:$('sup-character').checked});});
document.addEventListener('click',e=>{const t=e.target.closest?.('[data-ticket]');if(t){send('support-open',{id:t.dataset.ticket});return;}
 const r=e.target.closest?.('[data-reply]');if(r){send('support-reply',{id:r.dataset.reply,message:$('sup-reply')?.value||''});return;}
 const sec=e.target.closest?.('[data-section="support"]');if(sec&&data?.support?.paired&&!data.support.list)send('support-refresh');});
const _rcSup=window.renderCompanion;window.renderCompanion=d=>{_rcSup?.(d);renderSupport(d.support);};
