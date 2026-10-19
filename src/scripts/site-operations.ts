import {apiJson} from '../lib/api-response.mjs';
import {renderDownloads} from './download-ui';
import '../styles/site-operations.css';
const esc=(s:unknown)=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
let session:any=null,lastSession=0,visits=new Map<string,number>();
async function usage(){
 const feature=location.hash.slice(1).split('/')[0]||'home';if(!['home','overview','equipment','daevanion','roadmap','map','skills','progress','database','damage','events','ranking','profile'].includes(feature)||Date.now()-(visits.get(feature)||0)<600000)return;
 try{if(Date.now()-lastSession>60000){session=await fetch('/api/account.php?action=session',{credentials:'same-origin'}).then(apiJson);lastSession=Date.now();}if(!session?.user)return;visits.set(feature,Date.now());await fetch('/api/operations.php?action=usage',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json','X-CSRF-Token':session.csrfToken},body:JSON.stringify({feature})});}catch{}
}
async function refresh(){try{const r=await fetch('/api/operations.php?action=public',{signal:AbortSignal.timeout(15000)});if(!r.ok)throw Error('No se pudo consultar la descarga.');const x=await apiJson(r);let banner=document.getElementById('site-notices');if(!banner){banner=document.createElement('div');banner.id='site-notices';banner.setAttribute('role','status');document.querySelector('main')?.prepend(banner);}banner.innerHTML=x.notices.map((n:any)=>`<article class="site-notice ${esc(n.level)}"><strong>${esc(n.title)}</strong><p>${esc(n.body)}</p></article>`).join('');banner.hidden=!x.notices.length;
 renderDownloads(x.releases);
 }catch{renderDownloads(null,true);}}
document.addEventListener('daeva-releases-retry',()=>void refresh());
window.addEventListener('hashchange',()=>void usage());window.addEventListener('focus',()=>{void refresh();void usage();});void refresh();void usage();
