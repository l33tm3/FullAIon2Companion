import fs from 'node:fs/promises';
const read=async p=>JSON.parse(await fs.readFile(p,'utf8')),root='public/data/game/encyclopedia',index=await read(root+'/index.json');
const names=new Map(index.entries.map(x=>[x.detail+':'+x.id,x.name])),cache=await read('output/encyclopedia-reviewed-names.json');
const changes=await read('output/encyclopedia-spanish-files.json');
const reviewed=new Map(changes.changed.map(x=>[x.before,x.after]));
const fields=new Set(['name','title']);
const replace=(x,k='')=>{if(Array.isArray(x))return x.map(v=>replace(v,k));if(x&&typeof x==='object')return Object.fromEntries(Object.entries(x).map(([key,v])=>[key,replace(v,key)]));return typeof x==='string'&&fields.has(k)?reviewed.get(x)||cache[x]||x:x;};
let tasks=[];for(const section of ['items','npc','quests','dungeons'])for(const file of await fs.readdir('public/data/game/'+section))if(file.endsWith('.json'))tasks.push([section,'public/data/game/'+section+'/'+file]);
let cursor=0;await Promise.all(Array.from({length:24},async()=>{while(cursor<tasks.length){const [section,p]=tasks[cursor++],d=await read(p),n=replace(d);if(names.has(section+':'+d.id))n.name=names.get(section+':'+d.id);if(JSON.stringify(n)!==JSON.stringify(d)){await fs.writeFile(p,JSON.stringify(n));changes.files.push(p);}}}));
const raw=await read('research/dbaion/pet-index-full.json'),pets=await read(root+'/pets.json');for(const pet of pets)pet.soulItemIds=Object.entries(raw.bySoul).filter(([,v])=>Number(v)===pet.id).map(([id])=>Number(id));await fs.writeFile(root+'/pets.json',JSON.stringify(pets));
const sources={source:'https://dbaion2.ru/en/',importedAt:new Date().toISOString(),importedNpcDetails:0,entries:{}};
const entry=id=>sources.entries[id]??={creatures:[],quests:[]};
for(const [section,p]of tasks){if(!['npc','quests'].includes(section))continue;const d=await read(p);if(section==='npc'){sources.importedNpcDetails++;for(const item of d.drops||[])entry(item.id).creatures.push({id:d.id,name:d.name,level:d.level,chance:item.chance??null});}else for(const item of [...d.rewards?.items||[],...d.rewards?.classItems||[]])if(item.id)entry(item.id).quests.push({id:d.id,name:d.name,count:item.count});}
await fs.writeFile(root+'/item-sources.json',JSON.stringify(sources));changes.files=[...new Set(changes.files)];await fs.writeFile('output/encyclopedia-spanish-files.json',JSON.stringify(changes));console.log('Archivos localizados',changes.files.length,'objetos con obtención',Object.keys(sources.entries).length,'fichas NPC',sources.importedNpcDetails);
