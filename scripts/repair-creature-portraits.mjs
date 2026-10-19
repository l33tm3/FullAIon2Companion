import fs from 'node:fs/promises';
const read=async p=>JSON.parse(await fs.readFile(p,'utf8'));
const catalog=await read('public/data/game/catalog.json'),extras=await read('public/data/catalog.json'),locale=(await read('public/data/es-localization.json')).dictionary;
const npcs=catalog.entries.filter(x=>x.detail==='npc'),byId=new Map(npcs.map(x=>[Number(x.id),x])),items=new Map(catalog.entries.filter(x=>x.detail==='items').map(x=>[Number(x.id),x]));
const records=new Map(),generic=x=>!x||/Marker|unavailable|map-icons/.test(x),donors=new Map();
function donate(name,icon,source){if(!name||generic(icon))return;const key=name.trim().toLowerCase();if(!donors.has(key))donors.set(key,new Map());donors.get(key).set(icon,source);}
// Import every variant of the reported creature. Keep each variant's actual loot and level.
for(const n of npcs.filter(x=>x.originalName==='Withered Branch Spider')){
 const r=await fetch('https://dbaion2.ru/data-en/npc/'+n.id+'.json',{signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error('NPC '+n.id+': '+r.status);const raw=await r.json();
 await fs.writeFile('research/dbaion/npc-'+n.id+'.json',JSON.stringify(raw));
 function local(x,key=''){if(Array.isArray(x))return x.map(v=>local(v,key));if(x&&typeof x==='object'){const obj=Object.fromEntries(Object.entries(x).map(([k,v])=>[k,local(v,k)]));if(obj.name&&items.has(Number(obj.id)))obj.name=items.get(Number(obj.id)).name;return obj;}if(typeof x==='string'){if(key==='icon'||key==='image')return x?'/assets/game/'+x+'.webp':'';if(['name','title','description','category','gradeName','typeName'].includes(key))return (locale[x]||x).replaceAll('(Bound)','(Vinculado)');}return x;}
 const d=local(raw);d.name=n.name;d.originalName=n.originalName;await fs.writeFile('public/data/game/npc/'+n.id+'.json',JSON.stringify(d));await new Promise(r=>setTimeout(r,200));
}
for(let i=0;i<npcs.length;i+=32)await Promise.all(npcs.slice(i,i+32).map(async n=>{const d=await read('public/data/game/npc/'+n.id+'.json');records.set(Number(n.id),d);donate(n.originalName||n.name,d.icon,'npc:'+n.id);const soul=Array.isArray(d.drops)?d.drops.find(x=>/^UT_Vehicle_Portrait_/.test(x.icon?.split('/').at(-1)||'')&&x.chance===100):null;if(soul)donate(n.originalName||n.name,soul.icon,'soul:'+soul.id);}));
for(const p of extras.entries.filter(x=>x.kind==='Mascota'))donate(p.originalName||p.name,p.icon,'pet:'+p.id);
let repaired=0;const repairs=[];
for(const n of npcs){const d=records.get(Number(n.id)),choices=donors.get((n.originalName||n.name).trim().toLowerCase());if(!generic(d.icon)){n.icon=d.icon;continue;}if(!choices?.size)continue;
 // Prefer the creature's own soul portrait. Exact family names only; never fuzzy-name matches.
 const own=[...choices].find(([,source])=>source==='npc:'+n.id)||[...choices].find(([,source])=>source.startsWith('soul:'))||[...choices].find(([,source])=>source.startsWith('pet:'))||(choices.size===1?[...choices][0]:null);if(!own)continue;
 const [icon,source]=own;d.icon=icon;d.portraitSource=source;n.icon=icon;n.portraitSource=source;repaired++;repairs.push({id:n.id,icon,source});
}
// Ensure imported spider drops have local assets, including portraits, before publication.
const assets=new Set();for(const d of records.values())for(const match of JSON.stringify(d).matchAll(/\/assets\/game\/([\w-]+)\.webp/g))assets.add(match[1]);let downloaded=0;
for(const name of assets){const p='public/assets/game/'+name+'.webp';try{await fs.access(p);}catch{const r=await fetch('https://dbaion2.ru/icons/'+name+'.webp',{signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error('Image '+name+': '+r.status);const b=Buffer.from(await r.arrayBuffer());if(b.toString('ascii',0,4)!=='RIFF'||b.toString('ascii',8,12)!=='WEBP')throw Error('Invalid portrait '+name);await fs.writeFile(p,b);downloaded++;await new Promise(r=>setTimeout(r,200));}}
for(let i=0;i<npcs.length;i+=32)await Promise.all(npcs.slice(i,i+32).map(n=>fs.writeFile('public/data/game/npc/'+n.id+'.json',JSON.stringify(records.get(Number(n.id))))));
await fs.writeFile('public/data/game/catalog.json',JSON.stringify(catalog));await fs.writeFile('output/creature-portrait-repairs.json',JSON.stringify({repaired,downloaded,repairs},null,2));console.log(JSON.stringify({repaired,downloaded,spiderVariants:npcs.filter(x=>x.originalName==='Withered Branch Spider').length}));
