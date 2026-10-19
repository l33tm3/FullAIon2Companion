import fs from 'node:fs/promises';
import crypto from 'node:crypto';
const read=async p=>JSON.parse(await fs.readFile(p,'utf8'));
const save=async(p,d)=>{await fs.mkdir(p.slice(0,p.lastIndexOf('/')),{recursive:true});await fs.writeFile(p,JSON.stringify(d));};
const root='public/data/game/encyclopedia',catalog=await read('public/data/game/catalog.json'),extras=await read('public/data/catalog.json');
const rows=new Map(catalog.entries.map(x=>[x.detail+':'+x.id,x]));
const groups=await read('research/dbaion/item-groups-full.json'),rawItems=await read('research/dbaion/items.json'),rawNpcs=await read('research/dbaion/npcs.json');
const itemNames=new Map(rawItems.map(x=>[x.id,rows.get('items:'+x.id)?.name||x.name]));
const npcNames=new Map(catalog.entries.filter(x=>x.detail==='npc').map(x=>[Number(x.id),x.name]));
const aliases=new Map(rawNpcs.filter(x=>x.dup).map(x=>[x.id,x.dup]));
const translate=(await read('public/data/es-localization.json')).dictionary;
const tr=s=>translate[s]||s;
const assets=new Set();const icon=s=>{if(!s)return '/assets/unavailable.svg';if(s.startsWith('/assets/'))return s;assets.add(s);return '/assets/game/'+s+'.webp';};
const enumGroups=new Map(groups.flatMap(g=>g.cats.map(([c])=>[c,g.id])));
const entries=catalog.entries.map(x=>({...x}));const byKey=new Map(entries.map(x=>[x.detail+':'+x.id,x]));
for(const x of rawItems){const n=byKey.get('items:'+x.id);if(n)Object.assign(n,{categoryCode:x.category,group:enumGroups.get(x.category)||'other',itemLevel:x.itemLevel,levelReq:x.levelReq,race:x.race,itemType:x.itemType});}
for(const x of rawNpcs){const n=byKey.get('npc:'+x.id);if(n)Object.assign(n,{race:x.race,balaur:x.balaur,role:x.role,creature:x.creature,maps:x.maps||[],dungeons:x.dungeons||[]});}
for(const x of await read('research/dbaion/quests.json')){const n=byKey.get('quests:'+x.id);if(n)Object.assign(n,{race:x.race,questType:x.type,level:x.levelMin,region:tr(x.region)});}
for(const x of await read('research/dbaion/dungeons.json')){const n=byKey.get('dungeons:'+x.slug);if(n)Object.assign(n,{dungeonType:x.type,level:x.recommendLevel,members:x.members});}
const localItem=x=>({...x,name:itemNames.get(Number(x.id))||tr(x.name),icon:icon(x.icon)});
const recipes=(await read('research/dbaion/recipes-full.json')).map(x=>({...x,originalName:x.name,name:itemNames.get(x.result.id)||tr(x.name),categoryName:tr(x.categoryName),tab:tr(x.tab),result:localItem(x.result),materials:x.materials.map(localItem),chances:(x.chances||[]).map(localItem)}));
const titleRaw=await read('research/dbaion/titles-full.json'),extraTitles=new Map(extras.entries.filter(x=>x.kind==='Título').map(x=>[Number(x.id),x]));
const titles=titleRaw.list.map(x=>({...x,name:extraTitles.get(x.id)?.name||tr(x.name),originalName:x.name,description:x.desc,icon:extraTitles.get(x.id)?.icon||'/assets/ui/stat_lords_justice.png'}));
const petRaw=await read('research/dbaion/pet-index-full.json'),extraPets=new Map(extras.entries.filter(x=>x.kind==='Mascota').map(x=>[Number(x.id),x]));
const pets=Object.entries(petRaw.pets).map(([id,p])=>({id:Number(id),name:extraPets.get(Number(id))?.name||tr(p.n),originalName:p.n,creature:p.t,creatureName:tr(p.tn),soulsRequired:p.need,icon:extraPets.get(Number(id))?.icon||icon(p.ic),sources:(p.src||[]).map(n=>({id:aliases.get(n.id)||n.id,name:npcNames.get(aliases.get(n.id)||n.id)||tr(n.n),level:n.lv,location:n.w?{kind:n.w.k,slug:n.w.s,name:tr(n.w.n)}:null})),otherSources:(p.oth||[]).map(tr),bonuses:(petRaw.types[p.t]?.perPet||[]).map(tr),attributes:extraPets.get(Number(id))?.attributes,levels:extraPets.get(Number(id))?.levels}));
const daevanion=await read('research/dbaion/daevanion-full.json');
for(const cls of Object.values(daevanion.classes))for(const b of cls.boards)b.nodes=b.nodes.map(([row,col,type,grade,name,stat,value,cost,level])=>({row,col,type,grade,name:tr(name),stat,value,cost,level}));
for(const x of extras.entries.filter(x=>x.kind==='Alas'))entries.push({...x,detail:'wings'});
for(const x of titles)entries.push({id:x.id,name:x.name,originalName:x.originalName,icon:x.icon,kind:'Título',detail:'titles',grade:x.grade,race:x.race,titleCategory:x.cat,slot:x.slot});
for(const x of pets)entries.push({id:x.id,name:x.name,originalName:x.originalName,icon:x.icon,kind:'Mascota',detail:'pets',creature:x.creature});
for(const x of recipes)entries.push({id:x.id,name:x.name,originalName:x.originalName,icon:x.result.icon,kind:'Receta',detail:'recipes',grade:x.result.grade,profession:x.category,level:x.masteryLevel});
await save(root+'/recipes.json',recipes);await save(root+'/titles.json',titles);await save(root+'/pets.json',pets);await save(root+'/daevanion.json',daevanion);
await save(root+'/index.json',{source:'https://dbaion2.ru/en/',importedAt:new Date().toISOString(),groups,entries});
// Data explicitly links each tameable creature to its pet soul. Recover only generic markers.
const repairs=[];const donors=new Map();for(const p of pets)for(const s of p.sources){if(!donors.has(s.id))donors.set(s.id,new Map());donors.get(s.id).set(p.icon,p.id);}
for(const n of catalog.entries.filter(x=>x.detail==='npc'&&/Marker|unavailable/.test(x.icon))){const choices=donors.get(Number(n.id));if(choices?.size!==1)continue;const [portrait,petId]=[...choices][0];const p='public/data/game/npc/'+n.id+'.json',d=await read(p);d.icon=n.icon=portrait;d.portraitSource=n.portraitSource='pet-soul-source:'+petId;await save(p,d);byKey.get('npc:'+n.id).icon=portrait;repairs.push({id:n.id,petId,icon:portrait});}
if(repairs.length){await save('public/data/game/catalog.json',catalog);await save(root+'/index.json',{source:'https://dbaion2.ru/en/',importedAt:new Date().toISOString(),groups,entries});}
const manifest={source:'https://dbaion2.ru/en/',fetchedAt:new Date().toISOString(),files:[],failed:[]};
for(const name of assets){const p='/assets/game/'+name+'.webp';try{let b;try{b=await fs.readFile('public'+p);}catch{const r=await fetch('https://dbaion2.ru/icons/'+name+'.webp',{signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error('HTTP '+r.status);b=Buffer.from(await r.arrayBuffer());if(b.length<12||b.toString('ascii',0,4)!=='RIFF'||b.toString('ascii',8,12)!=='WEBP')throw Error('Not WebP');await fs.writeFile('public'+p,b);await new Promise(r=>setTimeout(r,300));}manifest.files.push({path:p,source:'https://dbaion2.ru/icons/'+name+'.webp',bytes:b.length,sha256:crypto.createHash('sha256').update(b).digest('hex')});}catch(e){manifest.failed.push({name,error:e.message});}}
await save(root+'/assets-manifest.json',manifest);await save('output/encyclopedia-import.json',{items:rawItems.length,recipes:recipes.length,titles:titles.length,pets:pets.length,entries:entries.length,portraitRepairs:repairs,assets:manifest.files.length,failed:manifest.failed});console.log(JSON.stringify({recipes:recipes.length,titles:titles.length,pets:pets.length,entries:entries.length,portraitRepairs:repairs.length,failed:manifest.failed.length}));
