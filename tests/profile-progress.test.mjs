import test from 'node:test';
import assert from 'node:assert/strict';
import {profileProgress,profileConnection} from '../src/lib/profile-progress.mjs';
const ref={region:'la',serverId:2401,characterId:'own-character'};
const character={ref,info:{profile:{characterName:'Darkni'}}};
test('profile progress scopes readings and pet snapshots to the selected character',()=>{
 const other={...ref,region:'eu'};
 const p=profileProgress({'reading:a':{ref,at:'2026-10-01',power:100},'reading:b':{ref,at:'2026-10-02',power:160},'reading:other':{ref:other,at:'2026-10-03',power:900},'reading:bad':{ref,at:'invalid',power:5},'pets:own':{...ref,name:'DARKNI',pets:[[1001,0,0],[1002,3,0]],capturedAt:'2026-10-02'},'pets:other':{...other,name:'Darkni',pets:[[1003,1,0]],capturedAt:'2026-10-03'}},character);
 assert.equal(p.readings,2);assert.equal(p.growth,60);assert.equal(p.pets,1);
});
test('missing progress stays unknown rather than claiming a collection of zero',()=>{
 assert.deepEqual(profileProgress({},character),{readings:0,last:null,growth:null,pets:null,petUpdatedAt:null});
});
test('observed evidence and global Plus never imply character ownership',()=>{
 const p=profileConnection({verificationReady:false,linkedCharacters:[],challenges:[{character:ref,status:'observed'}],subscription:{plan:'plus'},devices:[]},{licenses:[{character:ref,status:'active',plan:'plus',features:{companionPvp:true}}]},ref,true);
 assert.equal(p.verified,false);assert.equal(p.challenge,'observed');assert.equal(p.active,false);assert.equal(p.features.pvp,false);
});
test('character-specific license keeps independent feature flags and email requirement',()=>{
 const summary={linkedCharacters:[{character:ref}],verificationReady:false,devices:[{active:true,online:true}]};
 const billing={licenses:[{character:ref,status:'active',plan:'plus',features:{companionPvp:true,companionMinimap:false}}]};
 const p=profileConnection(summary,billing,ref,true);assert.equal(p.verified,true);assert.equal(p.ready,false);assert.equal(p.active,true);assert.equal(p.features.pvp,true);assert.equal(p.features.minimap,false);
 assert.equal(profileConnection(summary,billing,ref,false).active,false);assert.equal(profileConnection(summary,billing,{...ref,serverId:2402},true).active,false);
});
test('title verification is offered only with a confirmed e-mail and a free slot out of three',()=>{
 const other=(n)=>({character:{...ref,characterId:'other-'+n}});
 assert.equal(profileConnection({linkedCharacters:[],characterLimit:3},null,ref,true).canVerify,true);
 assert.equal(profileConnection({linkedCharacters:[],characterLimit:3},null,ref,false).canVerify,false);
 const full=profileConnection({linkedCharacters:[other(1),other(2),other(3)],characterLimit:3},null,ref,true);
 assert.equal(full.canVerify,false);assert.equal(full.linked.length,3);assert.equal(full.limit,3);
 assert.equal(profileConnection({linkedCharacters:[{character:ref}],characterLimit:3},null,ref,true).canVerify,false);
 assert.equal(profileConnection({linkedCharacters:[]},null,ref,true).limit,3);
});
test('a pending title challenge belongs to its own character only',()=>{
 const pending={character:ref,status:'pending',challengeId:'a'.repeat(32),currentTitle:'Socorrista',categoryLabel:'Ataque'};
 const p=profileConnection({linkedCharacters:[],titleChallenges:[pending]},null,ref,true);
 assert.equal(p.titleChallenge?.challengeId,pending.challengeId);assert.equal(p.canVerify,false);
 assert.equal(profileConnection({linkedCharacters:[],titleChallenges:[pending]},null,{...ref,characterId:'other'},true).titleChallenge,null);
 assert.equal(profileConnection({linkedCharacters:[],titleChallenges:[{...pending,status:'expired'}]},null,ref,true).titleChallenge,null);
});
