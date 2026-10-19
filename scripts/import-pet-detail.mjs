import fs from 'node:fs/promises';
import {load} from 'cheerio';
const id=process.argv[2]||'1116';if(!/^\d+$/.test(id))throw Error('Invalid pet ID');
const file='research/catalog-pages/pet-'+id+'.html';let html;try{html=await fs.readFile(file,'utf8');}catch{const r=await fetch('https://aion2.app/db/pets/'+id,{signal:AbortSignal.timeout(20000)});if(!r.ok)throw Error(r.status);html=await r.text();await fs.writeFile(file,html);}
const $=load(html),catalog=JSON.parse(await fs.readFile('public/data/catalog.json','utf8')),pet=catalog.entries.find(x=>x.kind==='Mascota'&&String(x.id)===id);if(!pet)throw Error('Pet not in local catalogue');
const labels={Genus:'Raza','Souls to summon':'Almas para invocar','Run speed':'Velocidad al correr','Sprint speed':'Velocidad al esprintar','Ground Speed':'Velocidad terrestre',Accuracy:'Precisión',DEX:'Destreza',Souls:'Almas'};
pet.attributes=$('main section').first().find('div.flex.justify-between').map((_,e)=>{const spans=$(e).children('span'),name=spans.first().text().trim(),value=spans.last().text().trim();return labels[name]?{name:labels[name],value}:null;}).get();
const raw=JSON.parse(await fs.readFile('research/dbaion/npcs.json','utf8')),alias=new Map(raw.filter(x=>x.dup).map(x=>[Number(x.id),x.dup])),seen=new Set();
pet.tamedFrom=$('main a[href^="/db/monsters/"]').map((_,e)=>{const npc=Number($(e).attr('href').split('/').pop()),canonical=alias.get(npc)||npc,level=Number($(e).text().match(/Lv\s*(\d+)/)?.[1]);if(seen.has(canonical))return null;seen.add(canonical);return {id:canonical,level};}).get();
const headers=$('main table thead th').map((_,e)=>$(e).text().replace(/🔮/g,'').trim()).get();pet.levels=$('main table tbody tr').map((_,e)=>{const values=$(e).find('td').map((_,td)=>$(td).text().trim()).get();return {level:Number(values[0]),stats:values.slice(1).map((value,i)=>({name:labels[headers[i+1]]||headers[i+1],value}))};}).get();pet.detailImportedAt=new Date().toISOString();
await fs.writeFile('public/data/catalog.json',JSON.stringify(catalog));
const itemFile='public/data/game/items/534660023.json',item=JSON.parse(await fs.readFile(itemFile,'utf8'));item.description='Alma de Araña de rama seca.';await fs.writeFile(itemFile,JSON.stringify(item));
console.log(JSON.stringify({id,attributes:pet.attributes.length,variants:pet.tamedFrom.length,levels:pet.levels.length}));
