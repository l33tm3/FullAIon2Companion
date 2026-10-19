import fs from 'node:fs/promises';
const read=async p=>JSON.parse(await fs.readFile(p,'utf8'));
const root='public/data/game/encyclopedia',index=await read(root+'/index.json');
const official=await read('research/current/item_names_i18n.json'),npc=await read('research/current/npc_names.json'),skills=await read('research/current/skill_names_i18n.json');
const cachePath='output/encyclopedia-reviewed-names.json';let cache={};try{cache=await read(cachePath);}catch{}
const exact={'Power Shard':'Fragmento de poder','Abyssal Power Shard':'Fragmento de poder abisal','Superior Power Shard':'Fragmento de poder superior','Enhance Stone':'Piedra de mejora','Tester Reward Chest':'Cofre de recompensa de pruebas','Tester Styling Chest':'Cofre de apariencias de pruebas','Wind Serum':'Suero del viento','Superior Life Serum':'Suero de vida superior','Daevanion':'Daevanion'};
const pending=new Set();
const english=/\b(Shard|Stone|Chest|Wings|Wing|Bound|Craft|Refining|Ore|Wood|Powder|Paper|Pet|Skin|Upgrade|Expert|Artisan|Coin|Token|Magic|Serum|Juice|Tea|Scroll|Power|Forest|Soul|Abyssal|Superior|Pure|Radiant|Premium|Battle Pass|Test Subject|Worker|Guardian|Chief|Scout|Officer|Slime|Mutant|Spider|Warrior|Soulstone|Supply|Storage|Cave|Den|Reward|Quest)\b/;
for(const x of index.entries){let es=x.detail==='items'?official[x.originalName]?.es:x.detail==='npc'?npc[x.id]?.es:x.detail==='skills'?skills[x.id]?.es:null;if(es&&es!==x.originalName)cache[x.name]=es;else if((x.name===x.originalName&&x.name.includes(' '))||english.test(x.name))pending.add(x.name);}
Object.assign(cache,exact);
const names=[...pending].filter(n=>!cache[n]);
for(let i=0;i<names.length;i+=20){const batch=names.slice(i,i+20),u=new URL('https://translate.googleapis.com/translate_a/single');u.search=new URLSearchParams({client:'gtx',sl:'en',tl:'es',dt:'t',q:batch.join('\n')});const r=await fetch(u,{signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error('Translation HTTP '+r.status);const result=(await r.json())[0].map(x=>x[0]).join('').trim().split('\n');if(result.length!==batch.length)throw Error('Translation row mismatch: '+i);batch.forEach((n,j)=>cache[n]=result[j].replace(/\(Bound\)/g,'(Vinculado)').replace(/\(Atado\)/g,'(Vinculado)'));await fs.writeFile(cachePath,JSON.stringify(cache));console.log('Nombres',Math.min(i+20,names.length),'/',names.length);await new Promise(r=>setTimeout(r,350));}
const nameMap=new Map(),changed=[];
for(const x of index.entries){const next=cache[x.name]||x.name;if(next!==x.name){nameMap.set(x.name,next);changed.push({detail:x.detail,id:x.id,before:x.name,after:next});x.name=next;}}
const itemNames=new Map(index.entries.filter(x=>x.detail==='items').map(x=>[Number(x.id),x.name])),npcNames=new Map(index.entries.filter(x=>x.detail==='npc').map(x=>[Number(x.id),x.name]));
const replace=(x,k='')=>{if(Array.isArray(x))return x.map(v=>replace(v,k));if(x&&typeof x==='object')return Object.fromEntries(Object.entries(x).map(([key,v])=>[key,replace(v,key)]));return typeof x==='string'&&k==='name'?(nameMap.get(x)||cache[x]||x):x;};
const files=[];const save=async(p,d)=>{await fs.writeFile(p,JSON.stringify(d));files.push(p);};
for(const n of await fs.readdir(root)){if(!n.endsWith('.json')||n==='assets-manifest.json')continue;const p=root+'/'+n,d=n==='index.json'?index:await read(p);let result=replace(d);if(n==='recipes.json')for(const recipe of result){recipe.name=itemNames.get(recipe.result.id)||recipe.name;for(const item of [recipe.result,...recipe.materials,...recipe.chances])item.name=itemNames.get(item.id)||item.name;}if(n==='pets.json')for(const pet of result)for(const source of pet.sources)source.name=npcNames.get(source.id)||source.name;await save(p,result);}
for(const path of ['public/data/game/catalog.json','public/data/catalog.json'])await save(path,replace(await read(path)));
// Change local ficha names only; retain numeric mechanics, IDs and provenance.
for(const x of changed.filter(x=>['items','npc','quests','dungeons'].includes(x.detail))){const p='public/data/game/'+x.detail+'/'+x.id+'.json';try{const d=replace(await read(p));d.name=x.after;await save(p,d);}catch(e){if(e.code!=='ENOENT')throw e;}}
await fs.writeFile('output/encyclopedia-spanish-files.json',JSON.stringify({changed,files:[...new Set(files)]}));console.log('Nombres revisados',changed.length,'archivos',files.length);
