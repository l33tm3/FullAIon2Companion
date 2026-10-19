import fs from 'node:fs/promises';
const path='output/encyclopedia-spanish-files.json',manifest=JSON.parse(await fs.readFile(path,'utf8'));
const normal=x=>x.replace(/\((?:atad[oa]s?|ligad[oa]s?|vinculad[oa]s?)\)/gi,'(Vinculado)');
const textKeys=new Set(['name','description','title']);
const walk=(x,k='')=>{if(Array.isArray(x))return x.map(v=>walk(v,k));if(x&&typeof x==='object')return Object.fromEntries(Object.entries(x).map(([key,v])=>[key,walk(v,key)]));return typeof x==='string'&&textKeys.has(k)?normal(x):x;};
let cursor=0,count=0;await Promise.all(Array.from({length:24},async()=>{while(cursor<manifest.files.length){const p=manifest.files[cursor++];if(!p.endsWith('.json'))continue;const d=JSON.parse(await fs.readFile(p,'utf8')),next=walk(d);if(JSON.stringify(next)!==JSON.stringify(d)){await fs.writeFile(p,JSON.stringify(next));count++;}}}));console.log('Terminología de vinculación normalizada',count);
