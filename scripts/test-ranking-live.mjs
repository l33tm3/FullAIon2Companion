import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const origin=process.env.RANKING_TEST_ORIGIN||'https://governanceai.me';
const cases=[
  {query:'region=la',region:'la'},
  {query:'region=la&server=2401&class=gladiator',region:'la',server:'2401',className:'Gladiator'},
  {query:'region=eu&class=cleric',region:'eu',className:'Cleric'},
  {query:'region=la&server=INVALID',status:400},
  {query:'region=la&class=INVALID',status:400},
];
const results=await Promise.all(cases.map(async c=>{
  const response=await fetch(origin+'/api/rankings.php?'+c.query);
  const body=await response.json();
  assert.equal(response.status,c.status||200,c.query);
  if(!c.status){
    assert.ok(body.entries.length>0,'Expected a populated ranking: '+c.query);
    assert.ok(body.entries.every(x=>x.region===c.region),'Region leaked: '+c.query);
    if(c.className)assert.ok(body.entries.every(x=>x.className===c.className),'Class leaked: '+c.query);
    if(c.server)assert.ok(body.entries.every(x=>new URL(x.profileImage,origin).searchParams.get('server')===c.server),'Server leaked: '+c.query);
    assert.equal(body.filters.region,c.region);
  }
  return {query:c.query,status:response.status,count:body.entries?.length,regions:[...new Set(body.entries?.map(x=>x.region))],classes:[...new Set(body.entries?.map(x=>x.className))],servers:[...new Set(body.entries?.map(x=>x.serverName))]};
}));
await fs.mkdir('output',{recursive:true});
await fs.writeFile('output/ranking-filters-check.json',JSON.stringify({origin,checkedAt:new Date().toISOString(),results},null,2));
console.log(JSON.stringify(results,null,2));
