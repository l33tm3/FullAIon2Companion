import fs from 'node:fs/promises';
import crypto from 'node:crypto';
const root = new URL('../', import.meta.url);
const write = async (p,d) => { await fs.mkdir(new URL(p.substring(0,p.lastIndexOf('/'))+'/',root),{recursive:true}); await fs.writeFile(new URL(p,root),typeof d==='string'||Buffer.isBuffer(d)?d:JSON.stringify(d)); };
const refs={mine:{serverId:2401,characterId:'H8WHJE_su6dZM2K913s8qowlE5DugO6YQwS_XG7yRJM=',region:'la'},target:{serverId:2401,characterId:'H8WHJE_su6dZM2K913s8qmTK7G_euqgtprPWR1ZWqLU=',region:'la'}};
const manifest={generatedAt:new Date().toISOString(),files:[],failed:[]};
const assets=new Map();
async function asset(url){if(!url||!/^https:\/\/(assets.playnccdn.com|profileimg.plaync.com)\//.test(url))return url;if(assets.has(url))return assets.get(url);const ext=url.includes('woff2')?'.woff2':url.includes('.png')?'.png':'.jpg';const path='/assets/'+crypto.createHash('sha256').update(url).digest('hex').slice(0,20)+ext;const r=await fetch(url);if(!r.ok){manifest.failed.push({source:url,status:r.status});assets.set(url,'/favicon.svg');return '/favicon.svg';}const buf=Buffer.from(await r.arrayBuffer());await write('public'+path,buf);assets.set(url,path);manifest.files.push({source:url,path,bytes:buf.length,sha256:crypto.createHash('sha256').update(buf).digest('hex')});return path;}
async function localize(v){if(Array.isArray(v)){const result=[];for(const item of v)result.push(await localize(item));return result;}if(v&&typeof v==='object'){for(const k of Object.keys(v))v[k]=await localize(v[k]);return v;}if(typeof v==='string'&&/^https:\/\/(assets.playnccdn.com|profileimg.plaync.com)\//.test(v))return asset(v);return v;}
async function api(path,params={}){const u='https://aion2.plaync.com'+path+'?'+new URLSearchParams({lang:'es-ES',...params});const r=await fetch(u);if(!r.ok)throw Error(path+' '+r.status);return r.json();}
for(const [key,ref] of Object.entries(refs)){
 const info=await api('/api/character/info',ref),equipment=await api('/api/character/equipment',ref),boards={},items={};
 for(const b of info.daevanion.boardList){boards[b.id]=await api('/api/character/daevanion/detail',{...ref,boardId:b.id});}
 for(const item of equipment.equipment.equipmentList){try{items[item.slotPos]=await api('/api/character/equipment/item',{...ref,id:item.id,enchantLevel:item.enchantLevel,exceedLevel:item.exceedLevel,slotPos:item.slotPos});}catch(e){console.warn('Item detail:',item.name,e.message);}}
 await write('public/data/'+key+'.json',await localize({ref,info,equipment,boards,items,fetchedAt:manifest.generatedAt}));console.log('Sincronizado',info.profile.characterName);
}
await write('public/data/servers.json',await api('/es-es/api/gameinfo/servers',{region:'la'}));
for(const [name,url] of [['light','https://assets.playnccdn.com/uikit/preorder/client/fonts/aion2_Light.woff2'],['medium','https://assets.playnccdn.com/uikit/preorder/client/fonts/aion2_Medium.woff2']]){const path=await asset(url);if(path!=='/favicon.svg')await fs.copyFile(new URL('public'+path,root),new URL('public/assets/font-'+name+'.woff2',root));}
const options=JSON.parse(await fs.readFile(new URL('research/map-options.json',root),'utf8'));
const categories=options.groups.flatMap(g=>g.categories.map(c=>({id:c.id,name:c.name,group:g.name,markers:c.data.filter(m=>m.type==='Point').map(m=>({id:m.properties.id,lat:m.coordinates[0],lng:m.coordinates[1],name:m.properties.nt||m.properties.icon_text||m.properties.note_title||c.name,description:m.properties.tooltipText||'',icon:m.properties.icon_image||c.icon_image}))})));
await write('public/data/altgard.json',{name:'Altgard',source:'https://interactivemap.app/aion2/maps/es/altgard',fetchedAt:manifest.generatedAt,categories});
// Only the 64 tiles displayed at zoom 3; no supporter-only offline package.
for(let x=0;x<8;x++)for(let y=0;y<8;y++){const url='https://interactivemap.app/aion2/maps/imapp/'+options.tiles_url.replace('{z}','3').replace('{x}',x).replace('{y}',y);const r=await fetch(url);if(r.ok){const b=Buffer.from(await r.arrayBuffer());const path='/assets/map/3_'+x+'_'+y+'.webp';await write('public'+path,b);manifest.files.push({source:url,path,bytes:b.length,sha256:crypto.createHash('sha256').update(b).digest('hex')});}}
await write('public/data/maps.json',JSON.parse(await fs.readFile(new URL('research/map-getmaps.json',root),'utf8')).map(g=>({name:g.group.name,maps:g.maps.map(m=>({id:m.id,name:m.name,slug:m.slug}))})));
await write('public/data/assets-manifest.json',manifest);
console.log('Assets',manifest.files.length,'map points',categories.reduce((n,c)=>n+c.markers.length,0));
