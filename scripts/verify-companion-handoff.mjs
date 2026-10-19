import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import {createHash} from 'node:crypto';

const origin='https://governanceai.me';
const res=await fetch(origin+'/?handoff=20261011',{cache:'no-store'});
assert.equal(res.status,200);
const html=await res.text(),local=await fs.readFile('dist/index.html','utf8');
const hash=s=>createHash('sha256').update(s).digest('hex');
assert.equal(hash(html),hash(local),'El HTML servido debe coincidir con el build publicado');
assert.ok(html.includes('id="account-settings"'));
assert.ok(html.includes('id="account-dropdown"'));
const scripts=[...html.matchAll(/<script\b[^>]*\bsrc="([^"]+)"/g)].map(x=>x[1]);
const responses=await Promise.all(scripts.map(async path=>{
 const r=await fetch(new URL(path,origin));assert.equal(r.status,200);return r.text();
}));
const js=responses.join('\n');
assert.ok(js.includes('Verificaciones recientes'));
assert.ok(/\.pattern\.length\+1/.test(js),'Ocho ataques, no siete');
for(const action of ['pull','account-overview','challenge-status']){
 const r=await fetch(origin+'/api/companion.php?action='+action);
 assert.equal(r.status,401,'No exponer '+action+' sin autenticación');
}
const report={checkedAt:new Date().toISOString(),productionHtmlMatchesBuild:true,accountMenuPublished:true,recentVerificationStatusPublished:true,eightAttacksCopyPublished:true,privateEndpointsRejectAnonymous:true,backendIntegrationReport:'companion-device-verification-check.json',accountCenterReport:'account-center-verification.json',ownershipVerification:'pending-trusted-capture-verifier',desktopCodeModifiedByThisHandoff:false};
await fs.writeFile('output/companion-handoff-check.json',JSON.stringify(report,null,2));
console.log(JSON.stringify(report,null,2));
