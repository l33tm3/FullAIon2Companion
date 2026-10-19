import fs from 'node:fs/promises';
const read=async p=>JSON.parse(await fs.readFile(p,'utf8'));
const catalog=await read('public/data/game/catalog.json'),rows=catalog.entries.filter(x=>x.detail==='npc'),npcs=new Map(rows.map(x=>[Number(x.id),x])),families=new Map(),index=new Map();
const norm=x=>(x||'').trim().toLowerCase();
for(let i=0;i<rows.length;i+=32)await Promise.all(rows.slice(i,i+32).map(async n=>{n.record=await read('public/data/game/npc/'+n.id+'.json');const key=norm(n.originalName||n.name);if(!families.has(key))families.set(key,[]);families.get(key).push(n);}));
for(const group of families.values())group.sort((a,b)=>a.level-b.level||a.id-b.id);
const maps=(await read('public/data/atlas-index.json')).groups.flatMap(x=>x.maps),mapNames=new Map(maps.map(x=>[String(x.id),x.name])),slugs={'1':'verteron','2':'altgard','3':'reshanta','5':'ishalgen','6':'poeta','7':'reshanta-mid','8':'morheim','9':'eltnen','10':'reshanta-top'};
const files=await fs.readdir('public/data/atlas');let cursor=0,linked=0,portraits=0,variants=0;
const real=x=>x&&!/Marker|unavailable|map-icons/.test(x);
await Promise.all(Array.from({length:16},async()=>{while(cursor<files.length){const file=files[cursor++],d=await read('public/data/atlas/'+file);for(const c of d.categories)for(const m of c.markers){let n=npcs.get(Number(m.npcId)),group=n?[n]:families.get(norm(m.originalName||m.name));if(!group?.length)continue;if(!n&&!['Monsters','NPCs','Pets','City Services'].includes(c.group))continue;
 const local=group.filter(x=>[...(x.record.maps||[]),...Object.keys(x.record.spawns||{})].includes(slugs[d.id]));if(local.length)group=local;
 const portrait=group.find(x=>real(x.icon))?.icon;n??=group[0];if(portrait){m.npcIcon=portrait;portraits++;}m.name=n.name;m.originalName=n.originalName||m.originalName||n.name;m.creatureName=n.record.creatureName;
 if(group.length===1)m.npcId=n.id;else if(!m.npcId){m.npcVariants=group.map(x=>({id:x.id,level:x.level}));variants++;}
 linked++;const familyKey=norm(n.originalName||n.name);if(!index.has(familyKey))index.set(familyKey,{id:n.id,name:n.name,originalName:n.originalName,icon:portrait||n.icon,level:n.level,kind:n.kind,locations:{}});const x=index.get(familyKey);if(portrait&&!real(x.icon))x.icon=portrait;x.locations[d.id]??={mapId:d.id,name:mapNames.get(d.id)||d.name,count:0};x.locations[d.id].count++;
 }await fs.writeFile('public/data/atlas/'+file,JSON.stringify(d));}}));
await fs.writeFile('public/data/game/npc-index.json',JSON.stringify({fetchedAt:new Date().toISOString(),entries:[...index.values()].map(x=>({...x,locations:Object.values(x.locations)}))}));console.log(JSON.stringify({creatureFamilies:index.size,linkedSpawns:linked,portraitSpawns:portraits,variantSpawns:variants}));
