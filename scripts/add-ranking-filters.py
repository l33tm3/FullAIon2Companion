from pathlib import Path

page = Path('src/pages/index.astro')
html = page.read_text(encoding='utf-8')
needle = '<button class="button secondary" id="ranking-refresh">Actualizar</button>'
filters = '<label>Servidor<select id="ranking-server"><option value="">Todos los servidores</option></select></label><label>Clase<select id="ranking-class"><option value="">Todas las clases</option><option value="gladiator">Gladiador</option><option value="templar">Templario</option><option value="assassin">Asesino</option><option value="ranger">Explorador</option><option value="sorcerer">Hechicero</option><option value="spiritmaster">Elementalista</option><option value="cleric">Clérigo</option><option value="chanter">Cantor</option><option value="brawler">Luchador</option></select></label>'
if 'id="ranking-class"' not in html:
    assert needle in html
    page.write_text(html.replace(needle, filters + needle), encoding='utf-8')

app = Path('src/scripts/app.ts')
code = app.read_text(encoding='utf-8')
needle = "const r=await fetch('/api/rankings.php?region='+region);"
replacement = "const rankClass=($('ranking-class') as HTMLSelectElement).value,rankServer=($('ranking-server') as HTMLSelectElement).value;const params=new URLSearchParams({region,class:rankClass,server:rankServer});const r=await fetch('/api/rankings.php?'+params);"
if needle in code:
    code = code.replace(needle, replacement)
    code = code.replace("d.entries.map((x:any)=>{const cls=", "d.entries.filter((x:any)=>region==='all'||x.region===region).map((x:any)=>{const cls=")
    code = code.replace("'Primeros puestos actualizados por la fuente cada hora. Cada fila indica su última lectura.'", "d.entries.length?`${d.entries.length} personajes · filtros de región, clase y servidor aplicados en la fuente. Cada fila indica su última lectura.`:'No hay personajes para estos filtros.'")
    code = code.replace("$('ranking-region').onchange=()=>void loadRanking();", """let rankingServerVersion=0;async function loadRankingServers(){const version=++rankingServerVersion,region=($('ranking-region') as HTMLSelectElement).value,select=$('ranking-server') as HTMLSelectElement;select.value='';select.disabled=true;select.innerHTML='<option value="">Todos los servidores</option>';if(region==='all'){select.disabled=false;return;}try{const result=await request({type:'servers',region});if(version!==rankingServerVersion)return;select.innerHTML='<option value="">Todos los servidores</option>'+result.serverList.map((s:any)=>`<option value="${esc(s.serverId)}">${esc(s.serverName)}</option>`).join('');}catch{if(version===rankingServerVersion)$('ranking-status').textContent='No se pudo consultar la lista de servidores.';}finally{if(version===rankingServerVersion)select.disabled=false;}}
$('ranking-region').onchange=()=>{rankingVersion++;void loadRankingServers().then(loadRanking);};$('ranking-server').onchange=()=>void loadRanking();$('ranking-class').onchange=()=>void loadRanking();void loadRankingServers();""".replace('\n+', '\n'))
    app.write_text(code, encoding='utf-8')
