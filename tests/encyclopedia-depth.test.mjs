import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const json=p=>JSON.parse(fs.readFileSync(p,'utf8'));
const numbers=s=>String(s||'').match(/\d+(?:\.\d+)?/g)||[];

test('every refreshed quest retains its objectives, rewards and chain IDs',()=>{
  const rows=json('research/dbaion/quests.json');
  for(const row of rows){
    const raw=json(`research/dbaion/quest-details/${row.id}.json`),q=json(`public/data/game/quests/${row.id}.json`);
    for(const key of ['id','type','race','levelMin','levelMax','prevQuestId','nextQuestId'])assert.deepEqual(q[key],raw[key]);
    assert.equal(q.steps.length,raw.steps.length);
    assert.deepEqual(numbers(q.description),numbers(raw.description));
    q.steps.forEach((s,i)=>{assert.equal(s.order,raw.steps[i].order);assert.deepEqual(numbers(s.text),numbers(raw.steps[i].text));assert.equal(s.goals.length,raw.steps[i].goals.length);s.goals.forEach((g,j)=>assert.deepEqual(numbers(g),numbers(raw.steps[i].goals[j])));});
    assert.equal(q.rewards.exp,raw.rewards.exp);assert.equal(q.rewards.gold,raw.rewards.gold);
    const rewards=r=>({items:r.items.map(i=>[i.id,i.count]),classItems:r.classItems.map(c=>[c.classType,c.items.map(i=>[i.id,i.count])])});
    assert.deepEqual(rewards(q.rewards),rewards(raw.rewards));
  }
  assert.equal(rows.length,1429);
});

test('all NPC aliases resolve directly to a published canonical fiche',()=>{
  const aliases=json('public/data/game/encyclopedia/npc-aliases.json');
  assert.equal(Object.keys(aliases).length,4478);
  for(const [id,target]of Object.entries(aliases)){assert.notEqual(String(target),id);assert.equal(aliases[target],undefined);assert.ok(fs.existsSync(`public/data/game/npc/${target}.json`));}
});

test('all translated chain skills retain numeric descriptions recursively',()=>{
  const rows=json('research/dbaion/skills.json');let count=0;
  const walk=(translated,original)=>{assert.equal(translated.length,original.length);translated.forEach((x,i)=>{const r=original[i];count++;for(const k of ['id','rate','time'])assert.deepEqual(x[k],r[k]);assert.equal(x.desc.length,r.desc.length);x.desc.forEach((s,j)=>assert.deepEqual(numbers(s),numbers(r.desc[j])));assert.ok(x.icon.startsWith('/assets/'));walk(x.chain||[],r.chain||[]);});};
  for(const cls of [...new Set(rows.map(s=>s.classType))])for(const skill of json(`public/data/game/skills-${cls}.json`)){const source=rows.find(s=>s.id===skill.id);walk(skill.chain||[],source.chain||[]);}
  assert.equal(count,31);
});
