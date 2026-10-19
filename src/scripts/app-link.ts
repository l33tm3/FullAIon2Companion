import type {ProgressCloud} from '../lib/cloud';
import {apiJson,reloadButton} from '../lib/api-response.mjs';
import '../styles/app-link.css';

// FullAIon2Companion shows this site inside its panel (WebView2). There, once signed in, the player links the app with one
// click: the page asks the API for a pairing code (session + CSRF, as «Conectar ejecutable») and hands it to the app with
// chrome.webview.postMessage; the app redeems it as if it had been typed. The app answers only to this site's own documents
// and never gives the page its device token: the page learns whether the app is linked and how its request ended. In a
// browser, or in an app that does not answer (1.0.14 and older), nothing shows.
type Webview={postMessage(message:unknown):void;addEventListener(type:'message',listener:(e:{data:any})=>void):void};
type AppState={paired:boolean;requested:boolean;version:string};
let app:AppState|null=null;
/** True inside a FullAIon2Companion that answered and is not linked yet. */
export function appLinkReady(){return !!app&&!app.paired;}

export function appLink(cloud:ProgressCloud,signIn:()=>void){
 const webview:Webview|undefined=(window as any).chrome?.webview;
 if(!webview?.postMessage)return ()=>{};
 const esc=(s:unknown)=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
 let busy=false,note='',kind:'info'|'ok'|'error'='info',failure:unknown=null,asked=false,shown='',waiting:ReturnType<typeof setTimeout>|undefined;
 const banner=document.createElement('section');banner.className='app-link';banner.setAttribute('aria-label','Vincular FullAIon2Companion');banner.hidden=true;
 (document.getElementById('main-content')||document.body).prepend(banner);
 const changed=()=>{render();document.dispatchEvent(new Event('daeva-app-link'));};
 webview.addEventListener('message',e=>{
  const m=e.data;
  if(m?.type==='daeva-app-state'){
   app={paired:m.paired===true,requested:m.requested===true,version:String(m.version||'').slice(0,20)};
   // Opened from the app's «Iniciar sesión y vincular»: the sign-in comes up by itself, the link still takes a click.
   if(app.requested&&!app.paired&&!asked){asked=true;void cloud.refreshSession().catch(()=>{}).then(()=>{if(!cloud.user)signIn();});}
   changed();
  }else if(m?.type==='daeva-pair-result'&&busy){
   clearTimeout(waiting);busy=false;kind=m.ok===true?'ok':'error';note=String(m.message||'');failure=null;
   if(m.ok===true&&app)app.paired=true;
   changed();
  }
 });
 webview.postMessage({type:'daeva-app-query'});

 async function link(){
  const uid=cloud.user?.id;if(busy||!appLinkReady())return;if(!uid){signIn();return;}
  busy=true;kind='info';note='Pidiendo un código de vinculación…';failure=null;render();
  try{
   await cloud.refreshSession();if(cloud.user?.id!==uid)throw Error('La sesión cambió. Vuelve a intentarlo.');
   const r=await fetch('/api/companion.php?action=pair',{method:'POST',credentials:'same-origin',headers:{'Content-Type':'application/json','X-CSRF-Token':cloud.csrf},body:JSON.stringify({deviceName:'FullAIon2Companion · Windows'})});
   const d=await apiJson(r);if(!r.ok)throw Error(d.error||'No pudimos crear el código de vinculación.');
   if(typeof d.code!=='string'||!/^[A-F0-9]{12}$/.test(d.code))throw Error('El servidor no devolvió un código válido. Vuelve a intentarlo.');
   note='Vinculando la app con tu cuenta…';render();
   // Only to the app hosting this page; redeeming it there takes up to two requests of 15 s.
   webview!.postMessage({type:'daeva-pair',code:d.code});
   waiting=setTimeout(()=>{if(!busy)return;busy=false;kind='error';note='La app no respondió. Vuelve al panel y usa «¿Prefieres usar un código?» en Configuración → Cuenta.';render();},45000);
  }catch(e){busy=false;kind='error';note=(e as Error).message;failure=e;render();}
 }

 // Called on every account status change (every sync): the banner is rebuilt only when what it shows changes.
 function render(){
  const show=!!app&&(!app.paired||kind==='ok');banner.hidden=!show;
  const user=cloud.user;
  const text=kind==='ok'?'Listo. Volviendo al panel de la app…':user?`Vincula la app con tu cuenta ${esc(user.username)} en un clic, sin copiar códigos.`:'Estás dentro de FullAIon2Companion. Inicia sesión aquí y vincula la app con tu cuenta en un clic.';
  const action=kind==='ok'?'':user?`<button class="button" data-app-link ${busy?'disabled':''}>${busy?'Vinculando…':'Vincular esta app'}</button>`:'<button class="button" data-app-signin>Iniciar sesión</button>';
  const html=show?`<i data-lucide="monitor" aria-hidden="true"></i><div class="app-link-text"><strong>FullAIon2Companion</strong><p>${text}</p>${note?`<p class="app-link-note" data-kind="${kind}" role="status">${esc(note)}</p>`:''}</div><div class="app-link-actions">${action}${reloadButton(failure)}</div>`:'';
  if(shown===html)return;shown=html;banner.innerHTML=html;
  banner.querySelector<HTMLButtonElement>('[data-app-signin]')?.addEventListener('click',signIn);
  if(show)document.dispatchEvent(new Event('daeva-redraw-icons'));
 }
 // «Vincular esta app» may also be in Mi cuenta → Aplicación de escritorio: that dialog closes so the progress shows here.
 document.addEventListener('click',e=>{
  if(!(e.target instanceof Element)||!e.target.closest('[data-app-link]'))return;
  // Closing hands the focus back, which can scroll the page: back to the top right after, and again on its close event.
  e.preventDefault();const dialog=e.target.closest('dialog'),top=()=>window.scrollTo({top:0,behavior:'instant'});
  if(dialog?.open){dialog.addEventListener('close',top,{once:true});dialog.close();}
  top();void link();
 });
 return render;
}
