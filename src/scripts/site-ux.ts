/** Shared shell behavior. Personal tools and account authentication remain owned by app.ts. */
export function siteUX() {
 const sidebar=document.querySelector<HTMLElement>('#site-navigation')!;
 const workspace=document.querySelector<HTMLElement>('.workspace')!;
 const topbar=document.querySelector<HTMLElement>('.topbar')!;
 const backdrop=document.querySelector<HTMLButtonElement>('#navigation-backdrop')!;
 const close=document.querySelector<HTMLButtonElement>('#navigation-close')!;
 const toggle=document.createElement('button');
 toggle.id='navigation-toggle';toggle.className='icon-button';toggle.type='button';
 toggle.setAttribute('aria-label','Abrir navegación');toggle.setAttribute('aria-controls','site-navigation');toggle.setAttribute('aria-expanded','false');
 toggle.innerHTML='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16"/></svg>';
 topbar.prepend(toggle);
 const mobile=matchMedia('(max-width: 1024px)');
 let opened=false;
 function setOpen(value:boolean,restore=true) {
  opened=value&&mobile.matches;
  document.body.classList.toggle('navigation-open',opened);
  backdrop.hidden=!opened;workspace.inert=opened;
  sidebar.inert=mobile.matches&&!opened;
  toggle.setAttribute('aria-expanded',String(opened));
  if(opened)requestAnimationFrame(()=>{if(opened)close.focus();});else if(restore&&mobile.matches)toggle.focus();
 }
 toggle.onclick=()=>setOpen(!opened);close.onclick=()=>setOpen(false);backdrop.onclick=()=>setOpen(false);
 mobile.addEventListener('change',()=>setOpen(false,false));setOpen(false,false);
 sidebar.querySelectorAll<HTMLElement>('.nav-item').forEach(b=>b.setAttribute('aria-label',b.textContent?.trim()||'Navegar'));
 document.addEventListener('keydown',e=>{
  if(!opened)return;
  if(e.key==='Escape'){e.preventDefault();setOpen(false);return;}
  if(e.key==='Tab') {
   const items=Array.from(sidebar.querySelectorAll<HTMLElement>('a[href],button:not(:disabled),summary,[tabindex="0"]')).filter(x=>x.getClientRects().length);
   const first=items[0],last=items[items.length-1];
   if(!sidebar.contains(document.activeElement)){e.preventDefault();first?.focus();}
   else if(e.shiftKey&&document.activeElement===first){e.preventDefault();last?.focus();}
   else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first?.focus();}
  }
 });
 sidebar.addEventListener('click',e=>{
  if(!(e.target instanceof Element)||!e.target.closest('[data-view],[data-database-section],[data-own-profile],[data-download-open],.brand'))return;
  if(opened){setOpen(false,false);requestAnimationFrame(()=>document.querySelector<HTMLElement>('#main-content')?.focus({preventScroll:true}));}
 });
 const top=document.createElement('button');top.id='back-to-top';top.className='button secondary';top.type='button';top.textContent='↑ Volver arriba';top.hidden=true;
 document.body.append(top);
 const scrollState=()=>{top.hidden=scrollY<700||document.body.classList.contains('atlas-view');};
 window.addEventListener('scroll',scrollState,{passive:true});
 top.onclick=()=>{window.scrollTo({top:0,behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'});document.querySelector<HTMLElement>('#main-content')?.focus({preventScroll:true});};
 const update=()=>{
  const downloads=location.hash==='#home?download=1';sidebar.querySelector('[data-view=home]')?.classList.toggle('active',!downloads&&document.querySelector('#view-home')?.classList.contains('active')===true);sidebar.querySelector('[data-download-open]')?.classList.toggle('active',downloads);
  sidebar.querySelectorAll<HTMLElement>('.nav-item').forEach(b=>{if(b.classList.contains('active'))b.setAttribute('aria-current','page');else b.removeAttribute('aria-current');});
  const active=document.querySelector<HTMLElement>('.view.active')?.id.replace('view-','');
  const planViews=['overview','equipment','daevanion','roadmap'];
  const exportButton=document.querySelector<HTMLElement>('#export');if(exportButton)exportButton.hidden=!planViews.includes(active||'');
  const freshness=document.querySelector<HTMLElement>('#freshness');if(freshness)freshness.hidden=!planViews.includes(active||'');
  const title=document.querySelector<HTMLElement>('#page-title');if(title)title.tabIndex=-1;
  if(downloads)document.querySelector('#crumb')!.textContent='Descargas';
  document.title=downloads?'Descargas · Daeva Companion':active==='home'?'Daeva Companion · AION 2':`${document.querySelector('#crumb')?.textContent||'Atreia'} · Daeva Companion`;
  scrollState();
 };
 document.addEventListener('daeva-view-change',update);update();
}
