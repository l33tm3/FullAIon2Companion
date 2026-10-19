export function downloadRelease(releases){
 for(const channel of ['stable','beta']){
  const r=releases?.[channel];if(!r||typeof r.version!=='string'||!/^\d+(?:\.\d+){1,3}(?:[-+][\w.-]+)?$/.test(r.version)||r.version.length>40)continue;
  if(!Number.isSafeInteger(r.bytes)||r.bytes<=0||typeof r.sha256!=='string'||!/^[a-f0-9]{64}$/i.test(r.sha256)||typeof r.id!=='string'||!/^[a-f0-9]{32}$/.test(r.id))continue;
  if(r.url!==`/api/operations.php?action=download&id=${r.id}`)continue;
  return {...r,channel,notes:typeof r.notes==='string'?r.notes:'',sha256:r.sha256.toLowerCase()};
 }
 return null;
}
// Browsers get a plain static copy that the publish script places in /descargas/ for the stable channel: the .exe handed out
// by /api/operations.php?action=download&id=… looked like a malware dropper to Malwarebytes Browser Guard's heuristics
// (2026-10-16). The page links it only after a HEAD request finds it with the published size; the app keeps the API address.
export function staticDownloadUrl(r){
 return r&&r.channel==='stable'&&typeof r.version==='string'&&/^\d+\.\d+\.\d+$/.test(r.version)?`/descargas/FullAIon2Companion-${r.version}-setup.exe`:null;
}
