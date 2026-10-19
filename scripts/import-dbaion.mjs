import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
const origin='https://dbaion2.ru', root='public/data/game', iconDir='public/assets/game';
await fs.mkdir(root,{recursive:true});await fs.mkdir(iconDir,{recursive:true});
const read=async p=>JSON.parse(await fs.readFile(p,'utf8'));
const save=async(p,v)=>{await fs.mkdir(path.dirname(p),{recursive:true});await fs.writeFile(p,JSON.stringify(v));};
const lists={};for(const name of ['npcs','items','quests','dungeons','skills','classes','skill-costs','skill-points'])lists[name]=await read('research/dbaion/'+name+'.json');
const icons=new Set();const icon=name=>{if(!name||!/^[\w-]+$/.test(name))return '';icons.add(name);return '/assets/game/'+name+'.webp';};
function localize(v){if(Array.isArray(v))return v.map(localize);if(v&&typeof v==='object'){const r={};for(const [k,val]of Object.entries(v))r[k]=k==='icon'||k==='image'?icon(val):localize(val);return r;}return v;}
const named=lists.npcs.filter(x=>x.name&&!x.dup), byName=new Map();for(const n of named){const k=n.name.toLowerCase().trim();if(!byName.has(k))byName.set(k,[]);byName.get(k).push(n);}
const details=new Set([2406181]);let linked=0;
for(const file of await fs.readdir('public/data/atlas')){const atlas=await read('public/data/atlas/'+file);for(const c of atlas.categories)for(const m of c.markers){const hits=byName.get(m.name.toLowerCase().trim());if(hits?.length===1){m.npcId=hits[0].id;m.creatureName=hits[0].creatureName;const race=atlas.categories.find(c=>c.name===m.creatureName&&c.icon);if(race)m.raceIcon=race.icon;details.add(m.npcId);linked++;}}await save('public/data/atlas/'+file,atlas);}
const entries=[];for(const i of lists.items){const d=localize(i);await save(root+'/items/'+i.id+'.json',d);entries.push({id:i.id,name:i.name,kind:'Objeto',grade:i.grade,icon:d.icon,category:i.categoryName,detail:'items',source:origin+'/en/items/'+i.id+'/'});}
for(const n of named){const d=localize(n);try{await fs.access(root+'/npc/'+n.id+'.json');}catch{await save(root+'/npc/'+n.id+'.json',d);}entries.push({id:n.id,name:n.name,kind:n.isMonster?'Monstruo':'PNJ',grade:n.grade,icon:d.icon,category:[n.creatureName,n.title].filter(Boolean).join(' · '),level:n.level,detail:'npc',source:origin+'/en/npcs/'+n.id+'/'});}
for(const q of lists.quests){try{await fs.access(root+'/quests/'+q.id+'.json');}catch{await save(root+'/quests/'+q.id+'.json',q);}entries.push({...q,kind:'Misión',detail:'quests',icon:'/assets/ui/stat_lords_justice.png',source:origin+'/en/quests/'+q.id+'/'});}
for(const d of lists.dungeons){const x=localize(d);await save(root+'/dungeons/'+d.slug+'.json',x);entries.push({id:d.slug,name:d.name,kind:'Mazmorra',detail:'dungeons',icon:x.image,category:d.typeName,source:origin+'/en/dungeons/'+d.slug+'/'});}
const classes=lists.classes.filter(x=>x.classType!=='All');for(const cls of classes){const skills=localize(lists.skills.filter(s=>s.classType===cls.classType));await save(root+'/skills-'+cls.classType+'.json',skills);icon('UT_Class_'+cls.classType+'_Large');}
for(const s of lists.skills){entries.push({id:s.id,name:s.name,kind:'Habilidad',detail:'skills',classType:s.classType,icon:icon(s.icon),source:origin+'/en/skills/#c='+s.classType});}
for(const name of ['classes','skill-costs','skill-points'])await save(root+'/'+name+'.json',lists[name]);
async function pool(jobs,fn,concurrency=6){let cursor=0,done=0;await Promise.all(Array.from({length:concurrency},async()=>{while(cursor<jobs.length){const job=jobs[cursor++];await fn(job);done++;if(done%200===0)console.log('progress',done,'/',jobs.length);await new Promise(r=>setTimeout(r,120));}}));}
let errors=[];async function download(url,binary=false){for(let attempt=0;attempt<3;attempt++){try{const r=await fetch(url,{signal:AbortSignal.timeout(20000)});if(r.status===429||r.status>=500){await new Promise(r=>setTimeout(r,2000*(attempt+1)));continue;}if(!r.ok)throw Error('HTTP '+r.status);return binary?Buffer.from(await r.arrayBuffer()):await r.json();}catch(e){if(attempt===2)throw e;}}throw Error('Retries exhausted');}
console.log('NPC detail import',details.size,'linked markers',linked);
await pool([...details],async id=>{const file=root+'/npc/'+id+'.json';const prior=await read(file);if(prior.spawns)return;try{await save(file,localize(await download(origin+'/data-en/npc/'+id+'.json')));}catch(e){errors.push({type:'npc',id,error:e.message});}});
console.log('Quest detail import',lists.quests.length);
await pool(lists.quests,async q=>{const file=root+'/quests/'+q.id+'.json';const prior=await read(file);if(prior.steps)return;try{await save(file,localize(await download(origin+'/data-en/quest/'+q.id+'.json')));}catch(e){errors.push({type:'quest',id:q.id,error:e.message});}});
// Include assets discovered in cached detail files when resuming this import.
for(const section of ['npc','quests'])for(const f of await fs.readdir(root+'/'+section)){const text=await fs.readFile(root+'/'+section+'/'+f,'utf8');for(const match of text.matchAll(/\/assets\/game\/([\w-]+)\.webp/g))icons.add(match[1]);}
console.log('Assets',icons.size);const assets=[];
await pool([...icons],async name=>{const file=iconDir+'/'+name+'.webp';try{let b;try{b=await fs.readFile(file);}catch{b=await download(origin+'/icons/'+name+'.webp',true);}if(!(b.toString('ascii',0,4)==='RIFF'&&b.toString('ascii',8,12)==='WEBP')&&b.readUInt32BE(0)!==0x89504e47&&b.readUInt16BE(0)!==0xffd8)throw Error('Invalid image');await fs.writeFile(file,b);assets.push({path:file.replace('public',''),source:origin+'/icons/'+name+'.webp',bytes:b.length,sha256:crypto.createHash('sha256').update(b).digest('hex')});}catch(e){errors.push({type:'icon',name,error:e.message});}});
const available=new Set(assets.map(x=>x.path));for(const x of entries)if(x.icon.startsWith('/assets/game/')&&!available.has(x.icon))x.icon='/assets/unavailable.svg';
const meta={source:origin+'/en/',fetchedAt:new Date().toISOString(),counts:Object.fromEntries(Object.entries(lists).filter(([,a])=>Array.isArray(a)).map(([k,a])=>[k,a.length])),linkedMarkers:linked,npcDetails:details.size,assets:assets.length,errors};
await save(root+'/catalog.json',{...meta,entries});await save(root+'/manifest.json',{...meta,assets});console.log('Complete',JSON.stringify({...meta,errors:errors.length}));
