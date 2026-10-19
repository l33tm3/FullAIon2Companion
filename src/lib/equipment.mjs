// Client tables contain cumulative bonuses, not bonuses to sum at every level.
export function clampLevel(value,table){const n=Math.max(0,Math.floor(Number(value)||0));return Math.max(0,...Object.keys(table||{}).map(Number).filter(k=>k<=n));}
export function formatStat(key,value,metadata){return (metadata.pct.includes(key)?value/100:value).toLocaleString('es-CL',{maximumFractionDigits:2})+(metadata.pct.includes(key)?'%':'');}
export function itemStats(item,config,enchant){
 const values={};const add=(k,v)=>{values[k]=(values[k]||0)+v;};
 const wardrobe=enchant.wardrobe?.find(w=>w.cat===item.category),ratio=wardrobe?Math.max(0,Math.min(wardrobe.max,Math.floor(Number(config.wardrobe)||0)))*wardrobe.per/10000:0;
 for(const s of item.mainStats||[]){const weapon=/^Weapon(Min|Fixing)?Damage$/.test(s.stat)&&wardrobe?.stats.some(k=>k.endsWith('DamageRatio')),armor=s.stat==='ArmorDefense'&&wardrobe?.stats.some(k=>k.endsWith('DefenseRatio'));add(s.stat,(Number(s.value)||0)*(1+(weapon||armor?ratio:0)));}
 const effects=enchant.effects[item.enchant?.effect]||{},exceed=enchant.exceed[item.enchant?.exceed]||{};
 const level=clampLevel(config.en,effects),ex=clampLevel(config.ex,exceed);
 for(const [k,v]of effects[level]||[])add(k,v);
 for(const [k,v]of exceed[ex]?.[3]||[])add(k,v);
 // Only explicitly selected random lines count; adding every possible roll is invalid.
 const rolls=(config.rolls||[]).slice(0,item.subRolls||0);const seen=new Set();
 for(const r of rolls){const s=item.subStats?.find(s=>s.stat===r.stat);if(s&&!seen.has(s.stat)){seen.add(s.stat);add(s.stat,Math.max(s.min,Math.min(s.max,Number(r.value)||s.min)));}}
 for(const stone of (config.stones||[]).slice(0,item.slots?.manastone||0)){const definition=enchant.stones.find(s=>s.id===Number(stone.id));if(!definition||definition.target!==item.slots.msGroup)continue;const option=enchant.imprint[definition.group]?.find(s=>s[0]===stone.stat)?.[1];const tier=Math.floor(Number(stone.tier));if(option?.[tier])add(stone.stat,option[tier][0]);}
 return values;
}
export function equipmentTotals(items,configs,metadata,enchant,cls,level,extras=[]){
 /** @type {Record<string,number>} */ const values={};const add=(k,v)=>values[k]=(values[k]||0)+Number(v||0);
 for(const [k,v]of metadata.base[cls]?.[level]||[])add(k,v);
 items.forEach((item,i)=>Object.entries(itemStats(item,configs[i]||{},enchant)).forEach(([k,v])=>add(k,v)));
 for(const [k,v]of extras)add(k,v);
 for(const [k,table]of Object.entries(metadata.second)){const n=Math.max(0,Math.min(1000,Math.floor(values[k]||0)));for(const [s,v]of table[n]||[])add(s,v);}
 return values;
}
export function enhancementCost(item,level,kind,enchant){const group=kind==='ex'?enchant.exceed[item.enchant?.exceed]:enchant.enchant[item.enchant?.group];const step=group?.[level];return step&&step[0]>0?{chance:step[0],kinah:step[1],materials:step[2]}:null;}
export function statDifference(current,reference){return Object.fromEntries([...new Set([...Object.keys(current),...Object.keys(reference)])].sort().map(k=>[k,(current[k]||0)-(reference[k]||0)]));}
