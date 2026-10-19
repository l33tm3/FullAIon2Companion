// A reminder and a start alert are separate occurrences. Persisted keys prevent repeat chimes.
export function dueAlerts(events, now, leadMinutes, seen){
 const result=[];
 for(const e of events){const at=e.at,remaining=at-now;if(!Number.isFinite(at))continue;
  const phase=remaining>0&&remaining<=leadMinutes*60000?'soon':remaining<=0&&remaining>=-15000?'start':null;
  if(!phase)continue;const key=`${e.id}:${at}:${phase}`;if(!seen.has(key))result.push({...e,key,phase});
 }return result;
}
