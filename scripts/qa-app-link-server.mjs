// Local QA for «Vincular esta app» (src/scripts/app-link.ts) and for API answers that are a web page instead of JSON
// (src/lib/api-response.mjs). Serves dist/ and fakes the account, the pairing code and the overviews; other GET /api calls
// are proxied read-only to production. Nothing is written anywhere.
// With ?qa-webview=1 the page gets a stand-in for FullAIon2Companion's WebView2 host that speaks the same messages as
// MainWindow.OnSiteMessage (daeva-app-query → daeva-app-state, daeva-pair → daeva-pair-result); &qa-requested=1 is the
// app's «Iniciar sesión y vincular», &qa-paired=1 an app already linked. GET /__qa/set?page=1 makes every /api call answer
// Imunify360's challenge page; /__qa/set?signedIn=1 signs in; /__qa/set?static=1 serves /descargas/; /__qa/state shows what happened.
// Usage: node scripts/qa-app-link-server.mjs [port]
import http from 'node:http';import fs from 'node:fs/promises';import path from 'node:path';
const port=Number(process.argv[2]||4331),dist=path.resolve('dist');
const state={signedIn:false,page:false,static:false,codes:[],log:[]};
const user={id:'f'.repeat(32),username:'qa_soul',email:'qa@example.test',emailVerified:true,role:'user'};
const types={'.html':'text/html; charset=utf-8','.js':'text/javascript','.css':'text/css','.json':'application/json','.svg':'image/svg+xml','.png':'image/png','.webp':'image/webp','.woff2':'font/woff2','.jpg':'image/jpeg'};
const json=(res,status,body)=>{res.writeHead(status,{'Content-Type':'application/json; charset=utf-8'});res.end(JSON.stringify(body));};
const imunify='<!DOCTYPE html>\n<html><head><meta charset="utf-8"><title>One moment, please...</title></head><body><h1>Please wait while your request is being verified...</h1></body></html>';
// window.__qaHost.emit(message) delivers a message as the app would (the Atlas «Tú estás aquí» QA sends daeva-position this way).
const host=(paired,requested)=>`<script>(()=>{const listeners=[],emit=data=>listeners.forEach(f=>f({data}));const s={paired:${paired},requested:${requested}};window.__qaHost={received:[],state:s,emit};
window.chrome=window.chrome||{};window.chrome.webview={addEventListener(t,f){if(t==='message')listeners.push(f);},removeEventListener(){},postMessage(m){window.__qaHost.received.push(m);setTimeout(()=>{
if(m&&m.type==='daeva-app-query')emit({type:'daeva-app-state',paired:s.paired,requested:s.requested,version:'1.0.15'});
else if(m&&m.type==='daeva-pair'){if(s.paired)emit({type:'daeva-pair-result',ok:false,message:'Esta app ya está vinculada. Para usar otra cuenta, desconéctala antes en Configuración → Cuenta.'});else if(/^[A-F0-9]{12}$/.test(m.code)){s.paired=true;emit({type:'daeva-pair-result',ok:true,message:'App vinculada con tu cuenta de Daeva Companion.'});}}},300);}};})();</script>`;
async function api(req,res,url,body){
 const file=url.pathname.slice(5),action=url.searchParams.get('action');state.log.push(`${req.method} ${file} ${action||''}`);
 if(state.page){res.writeHead(200,{'Content-Type':'text/html; charset=utf-8',Server:'imunify360-webshield/1.21'});return res.end(imunify);}
 if(file==='account.php'&&action==='session')return json(res,200,{user:state.signedIn?user:null,csrfToken:'qa-csrf',billingEnabled:false});
 if(file==='account.php'&&action==='login'){state.signedIn=true;return json(res,200,{user,message:'Sesión iniciada.',csrfToken:'qa-csrf'});}
 if(file==='account.php'&&action==='logout'){state.signedIn=false;return json(res,200,{message:'Sesión cerrada.'});}
 if(file==='account.php'&&action==='progress')return json(res,200,req.method==='POST'?{version:1}:{changes:[],version:1});
 if(file==='billing.php'&&action==='overview')return json(res,200,{plan:'free',status:'free',licenses:[],billingEnabled:false});
 if(file==='companion.php'&&action==='account-overview')return json(res,200,{devices:[],linkedCharacters:[],challenges:[],titleChallenges:[],characterLimit:3,verificationReady:false,subscription:{plan:'free'},payments:[],serverTime:new Date().toISOString()});
 if(file==='companion.php'&&action==='pair'){
  if(!state.signedIn)return json(res,401,{error:'Inicia sesión en Mi cuenta.'});
  if(req.method!=='POST'||req.headers['x-csrf-token']!=='qa-csrf')return json(res,403,{error:'Origen o sesión no válido.'});
  const code='ABCDEF'+String(100000+state.codes.length).slice(-6);state.codes.push({code,deviceName:body.deviceName});return json(res,200,{code,expiresIn:300});
 }
 if(req.method==='GET'){const r=await fetch('https://governanceai.me'+url.pathname+url.search,{headers:{Accept:'application/json'}});res.writeHead(r.status,{'Content-Type':r.headers.get('content-type')||'application/json'});return res.end(Buffer.from(await r.arrayBuffer()));}
 return json(res,200,{});
}
http.createServer(async(req,res)=>{
 try{
  const url=new URL(req.url,'http://localhost');let body={};if(req.method==='POST'){let raw='';for await(const c of req)raw+=c;try{body=JSON.parse(raw||'{}');}catch{}}
  if(url.pathname==='/__qa/state')return json(res,200,state);
  if(url.pathname==='/__qa/set'){for(const k of ['page','signedIn','static'])if(url.searchParams.has(k))state[k]=url.searchParams.get(k)==='1';return json(res,200,state);}
  if(url.pathname.startsWith('/api/'))return await api(req,res,url,body);
  // /__qa/set?static=1: the publish script's static installer copy exists (HEAD answers the published size of 1.0.15).
  if(state.static&&/^\/descargas\/FullAIon2Companion-\d+\.\d+\.\d+-setup\.exe$/.test(url.pathname)){state.log.push(req.method+' '+url.pathname);res.writeHead(200,{'Content-Type':'application/octet-stream','Content-Disposition':'attachment','Content-Length':req.method==='HEAD'?102534880:2});return res.end(req.method==='HEAD'?undefined:'MZ');}
  let p=path.join(dist,decodeURIComponent(url.pathname));if(!p.startsWith(dist))return json(res,403,{});
  try{if((await fs.stat(p)).isDirectory())p=path.join(p,'index.html');}catch{p=path.join(dist,'index.html');}
  let data=await fs.readFile(p);
  if(p.endsWith('.html')&&url.searchParams.get('qa-webview')==='1')data=Buffer.from(String(data).replace('<head>','<head>'+host(url.searchParams.get('qa-paired')==='1',url.searchParams.get('qa-requested')==='1')));
  res.writeHead(200,{'Content-Type':types[path.extname(p)]||'application/octet-stream'});res.end(data);
 }catch(e){json(res,500,{error:String(e)});}
}).listen(port,()=>console.log('QA server on http://localhost:'+port));
