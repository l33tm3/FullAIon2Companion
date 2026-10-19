import fs from 'node:fs/promises';
const read=async p=>JSON.parse(await fs.readFile(p,'utf8'));
const raw=await read('research/dbaion/skills.json'),names=new Map();
const classes=[...new Set(raw.map(s=>s.classType))],lists=new Map();
const visit=(s,fn)=>{fn(s);for(const c of s.chain||[])visit(c,fn);};
for(const cls of classes){const list=await read(`public/data/game/skills-${cls}.json`);lists.set(cls,list);for(const s of list){const r=raw.find(x=>x.id===s.id);names.set(r.name.toLowerCase(),s.name);}}
const normalize=text=>String(text||'').replace(/\[([^\]]+)\]/g,(all,name)=>names.has(name.toLowerCase())?'['+names.get(name.toLowerCase())+']':all)
  .replace(/\bAoE\b/g,'de área').replace(/\bNPC\b/g,'PNJ').replace(/\bMP\b/g,'PM').replace(/\bHP\b/g,'PV')
  .replace(/PvP/g,'JcJ').replace(/PvE/g,'JcE').replace(/tiempo de reutilización/g,'recarga').replace(/enfriamiento/g,'recarga')
  .replace(/Daño del calibre escalonado/g,'Daño a la barra de desequilibrio').replace(/Daño del indicador de escalonamiento/g,'Daño a la barra de desequilibrio');
for(const [cls,list]of lists){for(const s of list)visit(s,node=>{if(node.description)node.description=normalize(node.description);for(const l of node.levels||[])l.description=normalize(l.description);if(node.desc)node.desc=node.desc.map(normalize);for(const p of node.parts||[])p.desc=normalize(p.desc);});await fs.writeFile(`public/data/game/skills-${cls}.json`,JSON.stringify(list));}
console.log('Unidades y referencias de habilidades normalizadas en español:',classes.length,'clases');
