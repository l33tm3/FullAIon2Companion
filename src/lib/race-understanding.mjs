export const understandingGrades=['Common','Rare','Legend','Unique','Epic','Mythic'];
export function understandingLevel(type,level){return type.levels.find(r=>r[0]===Math.max(0,Math.min(10,Math.floor(Number(level)||0))));}
export function understandingOption(type,slotIndex,level,selection){
  const slot=type.slots[slotIndex],row=understandingLevel(type,level),index=Number(selection?.option);
  if(!slot||!row||slot.open>row[0]||!Number.isInteger(index)||index<0)return null;
  const option=slot.options[index],quality=option&&row[2][understandingGrades.indexOf(option[0])];
  if(!option||!quality)return null;
  return {option,index,value:Math.max(option[2],Math.min(option[3],Number(selection.value)||option[2])),probability:quality*option[4]/100};
}
export function understandingTotals(types,records,preset){
  const totals={};for(const type of types){const r=records[type.id];if(!r)continue;for(let i=0;i<type.slots.length;i++){const choice=understandingOption(type,i,r.level,r.presets?.[preset]?.[i]);if(choice)totals[choice.option[1]]=(totals[choice.option[1]]||0)+choice.value;}}return totals;
}
export function cumulativeChance(percent,attempts){const n=Math.max(0,Math.floor(Number(attempts)||0)),p=Math.max(0,Math.min(100,percent));if(!n||!p)return 0;if(p===100)return 100;return 100*(-Math.expm1(n*Math.log1p(-p/100)));}
function weighted(items,weight,random){const total=items.reduce((sum,item)=>sum+Math.max(0,weight(item)),0);if(!total)return null;let cursor=Math.max(0,Math.min(1-Number.EPSILON,random()))*total;for(const item of items){cursor-=Math.max(0,weight(item));if(cursor<0)return item;}return items.at(-1);}
export function analyzeUnderstanding(type,level,selections,random=Math.random){
 const row=understandingLevel(type,level);let changed=0;
 const results=type.slots.map((slot,i)=>{
  const previous=selections?.[i],valid=understandingOption(type,i,level,previous);
  if(!row||slot.open>row[0])return previous||null;
  if(previous?.locked&&valid)return {...previous,value:valid.value};
  const available=understandingGrades.map((grade,g)=>({grade,weight:row[2][g]})).filter(g=>g.weight>0&&slot.options.some(o=>o[0]===g.grade&&o[4]>0));
  const grade=weighted(available,g=>g.weight,random);if(!grade)return previous||null;
  const candidates=slot.options.map((option,index)=>({option,index})).filter(c=>c.option[0]===grade.grade&&c.option[4]>0);
  const picked=weighted(candidates,c=>c.option[4],random);if(!picked)return previous||null;
  const [,,min,max]=picked.option,r=Math.max(0,Math.min(1-Number.EPSILON,random()));
  changed++;return {option:picked.index,value:min+Math.floor(r*(max-min+1)),locked:false};
 });return {results,changed};
}
export function understandingTargetReached(type,level,selections,grade){
 const minimum=understandingGrades.indexOf(grade);if(minimum<0)return false;
 return selections.some((s,i)=>{const choice=understandingOption(type,i,level,s);return choice&&!s.locked&&understandingGrades.indexOf(choice.option[0])>=minimum;});
}
export function sanitizeUnderstanding(data,types,edition){
  const out={...(edition?{edition}:{}),active:Math.max(0,Math.min(2,Math.floor(Number(data?.active)||0))),family:types.some(t=>t.id===data?.family)?data.family:types[0].id,records:{}};
  for(const type of types){const source=data?.records?.[type.id]||{},level=Math.max(0,Math.min(10,Math.floor(Number(source.level)||0))),row=understandingLevel(type,level);
    const record={level,xp:Math.max(0,Math.min(row?.[1]||0,Math.floor(Number(source.xp)||0))),presets:[]};
    for(let p=0;p<3;p++)record.presets.push(type.slots.map((slot,i)=>{let s=source.presets?.[p]?.[i];if(s&&edition&&data?.edition!==edition&&slot.legacyOptionKeys){const previous=slot.legacyOptionKeys[s.option],index=previous?slot.options.findIndex(o=>o[0]===previous[0]&&o[1]===previous[1]):-1;s=index<0?null:{...s,option:index};}const choice=understandingOption(type,i,level,s);return choice?{option:choice.index,value:choice.value,locked:s.locked===true}:null;}));out.records[type.id]=record;
  }return out;
}
