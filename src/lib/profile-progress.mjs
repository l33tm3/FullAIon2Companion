const same=(a,b)=>!!a&&!!b&&a.region===b.region&&String(a.serverId)===String(b.serverId)&&a.characterId===b.characterId;
export function profileProgress(records,character){
 const ref=character.ref,name=String(character.info?.profile?.characterName||'').toLocaleLowerCase();
 const readings=Object.entries(records).filter(([k,v])=>k.startsWith('reading:')&&same(v?.ref,ref)&&Number.isFinite(Number(v.power))&&Number.isFinite(Date.parse(v.at))).map(([,v])=>v).sort((a,b)=>Date.parse(a.at)-Date.parse(b.at));
 const pets=Object.entries(records).filter(([k,v])=>k.startsWith('pets:')&&v?.region===ref.region&&String(v.serverId)===String(ref.serverId)&&String(v.name).toLocaleLowerCase()===name).map(([,v])=>v).sort((a,b)=>String(b.receivedAt||b.capturedAt).localeCompare(String(a.receivedAt||a.capturedAt)))[0];
 const owned=Array.isArray(pets?.pets)?pets.pets.filter(p=>Array.isArray(p)&&p[1]>=1&&p[1]<=3).length:null;
 return {readings:readings.length,last:readings.at(-1)||null,growth:readings.length>1?Number(readings.at(-1).power)-Number(readings[0].power):null,pets:owned,petUpdatedAt:pets?.receivedAt||pets?.capturedAt||null};
}
export function profileConnection(summary,billing,ref,emailVerified){
 const verified=!!summary?.linkedCharacters?.some(x=>same(x.character,ref));
 const challenge=summary?.challenges?.find(x=>same(x.character,ref));
 const license=billing?.licenses?.find(x=>same(x.character,ref));
 const active=verified&&emailVerified===true&&license?.status==='active'&&license?.plan==='plus';
 const linked=Array.isArray(summary?.linkedCharacters)?summary.linkedCharacters:[],limit=Number(summary?.characterLimit)||3;
 const titleChallenge=summary?.titleChallenges?.find(x=>same(x.character,ref)&&x.status==='pending')||null;
 return {verified,linked,limit,titleChallenge,canVerify:!verified&&!titleChallenge&&emailVerified===true&&linked.length<limit,challenge:challenge?.status||null,ready:summary?.verificationReady===true,devices:summary?.devices?.filter(d=>d.active)||[],online:summary?.devices?.some(d=>d.active&&d.online)===true,active:!!active,expiresAt:license?.expiresAt||null,features:{pvp:!!active&&license?.features?.companionPvp===true,minimap:!!active&&license?.features?.companionMinimap===true}};
}
