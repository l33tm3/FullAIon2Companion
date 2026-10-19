import fs from 'node:fs/promises';
import {statNames} from '../src/lib/encyclopedia.mjs';
import {reviewStatNames} from '../src/lib/stat-spanish.mjs';
const read=async p=>JSON.parse(await fs.readFile(p,'utf8'));
const write=(p,d)=>fs.writeFile(p,JSON.stringify(d));
const root='public/data/game/encyclopedia/',raw='research/dbaion/';
const [stats,enchant,progress,pets,index]=await Promise.all(['stats-full','enchant-full','progress-full','pets-full'].map(n=>read(raw+n+'.json')).concat(read(root+'index.json')));
const cachePath='output/encyclopedia-reviewed-names.json',cache=await read(cachePath);
const missing=[...new Set(Object.values(stats.names).filter(n=>!cache[n]))];
for(let i=0;i<missing.length;i+=18){const batch=missing.slice(i,i+18),u=new URL('https://translate.googleapis.com/translate_a/single');u.search=new URLSearchParams({client:'gtx',sl:'en',tl:'es',dt:'t',q:batch.join('\n')});const r=await fetch(u,{signal:AbortSignal.timeout(25000)});if(!r.ok)throw Error('Translation '+r.status);const lines=(await r.json())[0].map(x=>x[0]).join('').trim().split('\n');if(lines.length!==batch.length)throw Error('Translation rows mismatch');batch.forEach((n,j)=>cache[n]=lines[j]);await write(cachePath,cache);console.log('Atributos traducidos',Math.min(i+18,missing.length),missing.length);await new Promise(r=>setTimeout(r,400));}
stats.names=Object.fromEntries(Object.entries(stats.names).map(([k,n])=>[k,statNames[k]||cache[n]]));
Object.assign(stats.names,{STR:'Fuerza',DEX:'Destreza',AGI:'Precisión',INT:'Inteligencia',WIS:'Voluntad',CON:'Físico',ArmorDefense:'Defensa de armadura',DamageRatio:'Aumento de ataque',AmplifyWeaponDamage:'Amplificación de daño de arma',AbnormalAccuracy:'Probabilidad de efectos de estado',WeaponFixingDamage:'Ataque de arma',FPMax:'Resistencia de vuelo',ShieldBlock:'Bloqueo con escudo'});
reviewStatNames(stats.names);
// Keep client tables numeric and only publish the names used by our own renderer.
delete stats.desc;delete stats.summaryLabels;delete stats.groups;delete stats.classes;delete stats.main;delete stats.conqueror;
delete enchant.stats;enchant.wardrobe=progress.wardrobe;
const materialIds=[...new Set([...Object.values(enchant.enchant),...Object.values(enchant.exceed)].flatMap(t=>Object.values(t).flatMap(v=>v[2].map(m=>m[0]))))];enchant.materials=Object.fromEntries(materialIds.map(id=>{const x=index.entries.find(x=>x.detail==='items'&&Number(x.id)===id);return [id,x?{name:x.name,icon:x.icon}:null];}));
for(const stone of enchant.stones){const item=index.entries.find(x=>x.detail==='items'&&Number(x.id)===stone.id);stone.name=item?.name||cache[stone.name]||stone.name;stone.icon=item?.icon||'/assets/unavailable.svg';}
const wingRows=index.entries.filter(x=>x.detail==='wings');
for(const w of progress.wings){const existing=wingRows.find(x=>Number(x.id)===w.id);w.originalName=w.name;w.name=existing?.name||cache[w.name]||w.name;w.icon=existing?.icon||'/assets/unavailable.svg';}
for(const type of pets.types){type.name=({Intellect:'Intelecto · Cogni',Feral:'Salvaje · Fera',Nature:'Naturaleza · Natura',Trans:'Transformación · Varian',Special:'Especial'})[type.id]||type.id;}
delete pets.pets;
await Promise.all([write(root+'stats.json',stats),write(root+'enchant.json',enchant),write(root+'progression.json',progress),write(root+'pet-progression.json',pets)]);
// Reverse dungeon and chest rewards so an item's acquisition is usable here.
const sources=await read(root+'item-sources.json');for(const entry of Object.values(sources.entries))delete entry.dungeons;let chests=0;
for(const f of await fs.readdir('public/data/game/dungeons')){const d=await read('public/data/game/dungeons/'+f);for(const diff of d.difficulties||[])for(const it of diff.rewards||[]){const entry=sources.entries[it.id]??={creatures:[],quests:[]};entry.dungeons??=[];if(!entry.dungeons.some(x=>x.id===d.slug&&x.difficulty===diff.name&&x.chest===null))entry.dungeons.push({id:d.slug,name:d.name,difficulty:diff.name,chest:null,count:it.count??null});}for(const chest of d.chests||[]){chests++;for(const it of chest.items||[]){const entry=sources.entries[it.id]??={creatures:[],quests:[]};entry.dungeons??=[];entry.dungeons.push({id:d.slug,name:d.name,difficulty:null,chest:chest.label,count:it.count??null});}}}
await write(root+'item-sources.json',sources);
await write('output/encyclopedia-depth-import.json',{stats:Object.keys(stats.names).length,enchantEffects:Object.keys(enchant.effects).length,enchantCosts:Object.keys(enchant.enchant).length,breakthroughGroups:Object.keys(enchant.exceed).length,stones:enchant.stones.length,wings:progress.wings.length,petTypes:pets.types.length,chests,source:'https://dbaion2.ru/data-en/',updated:new Date().toISOString()});
console.log('Tablas ampliadas',await read('output/encyclopedia-depth-import.json'));

const rawNpcs=await read(raw+'npcs.json'),aliases=Object.fromEntries(rawNpcs.filter(n=>n.dup).map(n=>[n.id,n.dup]));for(const id of Object.keys(aliases)){let resolved=aliases[id];const seen=new Set([Number(id)]);while(aliases[resolved]){if(seen.has(resolved))throw Error('NPC alias cycle');seen.add(resolved);resolved=aliases[resolved];}aliases[id]=resolved;}await write(root+'npc-aliases.json',aliases);
