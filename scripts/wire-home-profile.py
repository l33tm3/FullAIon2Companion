from pathlib import Path
import re

p=Path('src/pages/index.astro');s=p.read_text(encoding='utf-8').replace("import Home from '../components/Home.astro';","import Home from '../components/Home.astro';\nimport AccountDialog from '../components/AccountDialog.astro';")
s,n=re.subn(r'<dialog id="account-dialog">[\s\S]*?</dialog>', '<AccountDialog/>',s,count=1)
assert n==1;p.write_text(s,encoding='utf-8')
p=Path('src/scripts/app.ts');s=p.read_text(encoding='utf-8').replace("from './account-ui'","from './account-email-ui'")
s=s.replace("$('search-open').onclick", "document.querySelectorAll<HTMLElement>('[data-home-search]').forEach(b=>b.onclick=()=>openSearch('mine'));$('search-open').onclick",1)
needle="companionUI($('companion-profile'),ref,esc);"
s=s.replace(needle,"host.querySelector<HTMLButtonElement>('[data-profile-ranking]')!.onclick=async()=>{showView('ranking');($('ranking-region') as HTMLSelectElement).value=ref.region;await loadRankingServers();($('ranking-server') as HTMLSelectElement).value=String(ref.serverId);void loadRanking();};"+needle)
p.write_text(s,encoding='utf-8')
p=Path('src/scripts/character-profile.ts');s=p.read_text(encoding='utf-8').replace("import {characterSummary}","import {profilePvp} from '../lib/profile-pvp';\nimport {characterSummary}")
s=s.replace('<div id="profile-source-history"','${profilePvp(c.info,esc)}<div id="profile-source-history"',1)
p.write_text(s,encoding='utf-8')
p=Path('src/scripts/character-search.ts');s=p.read_text(encoding='utf-8')
s="import {profileUrl} from './character-profile';\n"+s
s=s.replace("if(submit)await choose(ref,o.which());", "if(submit){location.hash=profileUrl(ref);($('search-dialog') as HTMLDialogElement).close();}")
s=s.replace('<strong>${o.esc(name)}</strong>', '<strong><a href="${profileUrl({region:x.region||region,serverId:x.serverId,characterId:decodeURIComponent(x.characterId)})}" data-open-profile>${o.esc(name)}</a></strong>')
p.write_text(s,encoding='utf-8')
p=Path('src/components/Home.astro');p.write_text(p.read_text(encoding='utf-8').replace('ISR aphel','ISRaphel').replace('ISRaphel','ISRaphel'.upper()),encoding='utf-8')
