<?php
declare(strict_types=1);
if(!defined('DAEVA_ADMIN_CONTEXT')){http_response_code(404);exit;}
function opsAuditWrite(string $action,?string $uid,array $details):void {global $db,$actor,$permissions;$q=$db->prepare('SELECT role,status,auth_version FROM companion_users WHERE id=? FOR UPDATE');$q->execute([$actor['id']]);$fresh=$q->fetch(PDO::FETCH_ASSOC);if(!$fresh||$fresh['role']!=='admin'||$fresh['status']!=='active'||(int)$fresh['auth_version']!==(int)$actor['auth_version']||!in_array($action,opsPermissions(opsStaff($db,$actor)),true))reject(403,'Tus permisos cambiaron.');adminAudit($action,$uid,$details);}
function opsTicketDetail(string $id,bool $staff,string $uid):array {
 global $db;$q=$db->prepare('SELECT t.*,u.username FROM companion_tickets t JOIN companion_users u ON u.id=t.user_id WHERE t.id=?'.($staff?'':' AND t.user_id=?'));$q->execute($staff?[$id]:[$id,$uid]);$t=$q->fetch(PDO::FETCH_ASSOC);if(!$t)opsFail(404,'Consulta no encontrada.');
 $q=$db->prepare('SELECT m.id,m.body,m.internal,m.created_at,u.username FROM companion_ticket_messages m JOIN companion_users u ON u.id=m.author_id WHERE ticket_id=?'.($staff?'':' AND internal=0').' ORDER BY id LIMIT 200');$q->execute([$id]);$t['messages']=$q->fetchAll(PDO::FETCH_ASSOC);$t['character']=$t['character_json']?json_decode($t['character_json'],true):null;unset($t['character_json']);return $t;
}
function opsIniBytes(string $v):int {$n=(int)$v;return $n===0?PHP_INT_MAX:$n*(match(strtolower(substr(trim($v),-1))){'g'=>1073741824,'m'=>1048576,'k'=>1024,default=>1});}
function opsQuality():array {
 $p=dirname(__DIR__).'/data/game/catalog.json';$catalog=json_decode(file_get_contents($p),true,512,JSON_THROW_ON_ERROR);$issues=[];$names=[];$ids=[];$counts=['missingIcons'=>0,'missingFiles'=>0,'nameVariants'=>0,'untranslated'=>0,'duplicateIds'=>0];
 foreach($catalog['entries'] as $e){$key=($e['detail']??$e['kind']).':'.$e['id'];$base=['id'=>$e['id'],'name'=>$e['name'],'kind'=>$e['kind'],'key'=>$key];$icon=$e['icon']??'';
  if(!$icon){$counts['missingIcons']++;$issues[]=$base+['type'=>'missingIcons'];}
  elseif(str_starts_with($icon,'/assets/')&&!is_file(dirname(__DIR__).$icon)){$counts['missingFiles']++;$issues[]=$base+['type'=>'missingFiles'];}
  if(isset($ids[$key])){$counts['duplicateIds']++;$issues[]=$base+['type'=>'duplicateIds'];}$ids[$key]=true;
  if(isset($e['originalName'])&&$e['name']===$e['originalName']){$counts['untranslated']++;$issues[]=$base+['type'=>'untranslated'];}
  $n=($e['detail']??'').':'.mb_strtolower(trim($e['name']));$names[$n]=($names[$n]??0)+1;
 }
 $counts['nameVariants']=count(array_filter($names,fn($n)=>$n>1));
 $priority=['missingFiles'=>0,'missingIcons'=>1,'duplicateIds'=>2,'untranslated'=>3];usort($issues,fn($a,$b)=>$priority[$a['type']]<=>$priority[$b['type']]);$issues=array_slice($issues,0,1000);
 $r=['scannedAt'=>gmdate('c'),'sourceUpdatedAt'=>$catalog['enrichedAt']??$catalog['fetchedAt'],'entries'=>count($catalog['entries']),'counts'=>$counts,'issues'=>$issues,'issuesLimit'=>1000,'note'=>'Los nombres compartidos pueden ser variantes legítimas. Un nombre igual al original requiere revisión, no demuestra un error.','resources'=>[]];
 foreach(['events.json','atlas-index.json','game/catalog.json','pets.json'] as $f){$p=dirname(__DIR__).'/data/'.$f;$r['resources'][]=['name'=>$f,'available'=>is_file($p),'updatedAt'=>is_file($p)?gmdate('c',filemtime($p)):null];}return $r;
}
function opsAdminRoute():void {
 global $action,$body,$db,$actor,$private,$staffRole,$permissions;
 if(!str_starts_with($action,'x-'))return;
 if($action==='x-companion'){
  $rows=adminDevices();$diag=$db->query('SELECT * FROM companion_diagnostics')->fetchAll(PDO::FETCH_UNIQUE|PDO::FETCH_ASSOC);foreach($rows as &$r){$r['diagnostics']=isset($diag[$r['id']])?array_diff_key($diag[$r['id']],['user_id'=>1]):null;}
  answer(['devices'=>$rows,'releases'=>opsReleases($db),'verificationReady'=>!empty($GLOBALS['config']['captureVerifierPublicKey']),'note'=>'Los diagnósticos son declarados por el ejecutable; no certifican propiedad.']);
 }
 if($action==='x-releases')answer(['releases'=>$db->query('SELECT * FROM companion_releases ORDER BY created_at DESC LIMIT 100')->fetchAll(PDO::FETCH_ASSOC),'maxUploadBytes'=>min(104857600,opsIniBytes((string)ini_get('upload_max_filesize')),opsIniBytes((string)ini_get('post_max_size'))-65536),'billingEnabled'=>false]);
 if($action==='x-quality'){$p=$private.'/operations/quality.json';answer(['report'=>is_file($p)?json_decode(file_get_contents($p),true):null]);}
 if($action==='x-quality-scan'){
  limit('scan',2,300,$actor['id']);$reason=adminReason($body);$r=opsQuality();$db->beginTransaction();opsAuditWrite($action,null,['reason'=>$reason,'counts'=>$r['counts']]);$tmp=$private.'/operations/quality.'.bin2hex(random_bytes(5));file_put_contents($tmp,json_encode($r,JSON_UNESCAPED_UNICODE),LOCK_EX);chmod($tmp,0600);rename($tmp,$private.'/operations/quality.json');$db->commit();answer(['message'=>'Revisión del catálogo terminada.','report'=>$r]);
 }
 if($action==='x-tickets'){
  [$page,$offset]=adminPage();$status=(string)($_GET['status']??'');if($status!=='')opsEnum($status,['open','pending','closed']);$w=$status!==''?' WHERE t.status=?':'';$q=$db->prepare('SELECT COUNT(*) FROM companion_tickets t'.$w);$q->execute($status!==''?[$status]:[]);$total=(int)$q->fetchColumn();$q=$db->prepare('SELECT t.*,u.username,a.username assigned_name FROM companion_tickets t JOIN companion_users u ON u.id=t.user_id LEFT JOIN companion_users a ON a.id=t.assigned_to'.$w.' ORDER BY t.updated_at DESC LIMIT 25 OFFSET '.$offset);$q->execute($status!==''?[$status]:[]);$staff=$db->query("SELECT id,username FROM companion_users WHERE role='admin' AND status='active'")->fetchAll(PDO::FETCH_ASSOC);answer(['tickets'=>$q->fetchAll(PDO::FETCH_ASSOC),'staff'=>$staff,'total'=>$total,'page'=>$page,'pages'=>max(1,(int)ceil($total/25))]);
 }
 if($action==='x-ticket')answer(['ticket'=>opsTicketDetail(opsId($_GET['id']??null),true,$actor['id'])]);
 if($action==='x-notices')answer(['notices'=>$db->query('SELECT * FROM companion_notices ORDER BY updated_at DESC LIMIT 100')->fetchAll(PDO::FETCH_ASSOC)]);
 if($action==='x-staff')answer(['staff'=>$db->query("SELECT u.id,u.username,u.status,COALESCE(s.staff_role,'admin') staff_role FROM companion_users u LEFT JOIN companion_staff s ON s.user_id=u.id WHERE u.role='admin' ORDER BY u.username")->fetchAll(PDO::FETCH_ASSOC),'permissions'=>['owner'=>opsPermissions('owner'),'admin'=>opsPermissions('admin'),'support'=>opsPermissions('support')]]);
 if($action==='x-metrics'){
  $days=[];for($i=29;$i>=0;$i--)$days[gmdate('Y-m-d',time()-$i*86400)]=['day'=>gmdate('Y-m-d',time()-$i*86400),'users'=>0,'hits'=>0];
  foreach($db->query('SELECT day,COUNT(DISTINCT user_id) users,SUM(hits) hits FROM companion_usage WHERE day>=UTC_DATE()-INTERVAL 29 DAY GROUP BY day')->fetchAll(PDO::FETCH_ASSOC) as $d)$days[$d['day']]=['day'=>$d['day'],'users'=>(int)$d['users'],'hits'=>(int)$d['hits']];
  $features=$db->query('SELECT feature,COUNT(DISTINCT user_id) users,SUM(hits) hits FROM companion_usage WHERE day>=UTC_DATE()-INTERVAL 29 DAY GROUP BY feature ORDER BY hits DESC')->fetchAll(PDO::FETCH_ASSOC);
  $expiry=$db->query("SELECT u.username,s.expires_at FROM companion_subscriptions s JOIN companion_users u ON u.id=s.user_id WHERE u.status='active' AND s.plan='plus' AND s.status='active' AND s.expires_at>UTC_TIMESTAMP() AND s.expires_at<UTC_TIMESTAMP()+INTERVAL 7 DAY ORDER BY s.expires_at")->fetchAll(PDO::FETCH_ASSOC);
  answer(['days'=>array_values($days),'features'=>$features,'expiring'=>$expiry,'note'=>'Usuarios autenticados con actividad recibida. Se cuentan visitas a secciones y sincronizaciones, no tiempo de juego. Los datos empiezan con este módulo.']);
 }
 if($action==='x-release-upload'){
  $reason=adminReason($_POST);$version=opsText($_POST['version']??null,3,40);if(!preg_match('/^\d+\.\d+\.\d+(?:-[a-z0-9.-]+)?$/D',$version))reject(400,'Usa una versión como 1.0.0 o 1.0.0-beta.1.');$channel=opsEnum($_POST['channel']??null,['stable','beta']);$notes=opsText($_POST['notes']??null,5,5000);
  $f=$_FILES['executable']??null;if(!$f||$f['error']!==UPLOAD_ERR_OK||!is_uploaded_file($f['tmp_name']))reject(400,'No se recibió el ejecutable.');if($f['size']<64||$f['size']>104857600||strtolower(pathinfo($f['name'],PATHINFO_EXTENSION))!=='exe')reject(400,'Solo ejecutables .exe de hasta 100 MB.');
  $h=fopen($f['tmp_name'],'rb');$header=fread($h,64);$offset=unpack('V',substr($header,60,4))[1];if(substr($header,0,2)!=='MZ'||$offset<64||$offset>$f['size']-24){fclose($h);reject(400,'El archivo no tiene un encabezado PE válido.');}fseek($h,$offset);$pe=fread($h,6);fclose($h);if(substr($pe,0,4)!=="PE\0\0"||!in_array(unpack('v',substr($pe,4,2))[1],[0x14c,0x8664,0xaa64],true))reject(400,'Ejecutable Windows no válido.');
  $id=bin2hex(random_bytes(16));$hash=hash_file('sha256',$f['tmp_name']);$name='FullAIon2Companion-'.$version.'-'.$channel.'.exe';$path=$private.'/releases/'.$id.'.exe';$db->beginTransaction();opsAuditWrite($action,null,['reason'=>$reason,'id'=>$id,'version'=>$version,'channel'=>$channel,'sha256'=>$hash]);
  try{$q=$db->prepare('INSERT INTO companion_releases(id,version,channel,notes,filename,sha256,bytes,created_at) VALUES (?,?,?,?,?,?,?,UTC_TIMESTAMP())');$q->execute([$id,$version,$channel,$notes,$name,$hash,$f['size']]);if(!move_uploaded_file($f['tmp_name'],$path))throw new RuntimeException('Upload failed');chmod($path,0600);$db->commit();}catch(Throwable $e){if(is_file($path))unlink($path);if($db->inTransaction())$db->rollBack();if($e instanceof PDOException&&$e->getCode()==='23000')reject(409,'Esa versión y canal ya existen.');throw $e;}
  answer(['id'=>$id,'sha256'=>$hash,'message'=>'Ejecutable guardado como borrador. Revisa su origen antes de publicarlo.']);
 }
 if($action==='x-staff-save'&&!isset($body['userId'])){$name=opsText($body['username']??null,3,32);$q=$db->prepare('SELECT id FROM companion_users WHERE username=?');$q->execute([$name]);$id=$q->fetchColumn();if(!$id)reject(404,'Cuenta no encontrada.');$body['userId']=opsId($id);}
 if($action==='x-notice-save'&&!isset($body['id']))$body['id']=bin2hex(random_bytes(16));
 $reason=adminReason($body);$db->beginTransaction();opsAuditWrite($action,$body['userId']??null,['reason'=>$reason,'id'=>$body['id']??null,'staffRole'=>$body['staffRole']??null,'published'=>$body['published']??null,'status'=>$body['status']??null,'priority'=>$body['priority']??null,'internal'=>$body['internal']??null]);
 if($action==='x-ticket-update'){
  $id=opsId($body['id']??null);$q=$db->prepare('SELECT id FROM companion_tickets WHERE id=? FOR UPDATE');$q->execute([$id]);if(!$q->fetchColumn())reject(404,'Consulta no encontrada.');$status=opsEnum($body['status']??null,['open','pending','closed']);$priority=opsEnum($body['priority']??null,['normal','high']);$assigned=$body['assignedTo']??null;
  if($assigned!==null&&$assigned!==''){$assigned=opsId($assigned);$q=$db->prepare("SELECT id FROM companion_users WHERE id=? AND role='admin' AND status='active'");$q->execute([$assigned]);if(!$q->fetchColumn())reject(400,'Responsable no válido.');}else $assigned=null;
  $q=$db->prepare('UPDATE companion_tickets SET status=?,priority=?,assigned_to=?,updated_at=UTC_TIMESTAMP() WHERE id=?');$q->execute([$status,$priority,$assigned,$id]);
  if(trim((string)($body['message']??''))!==''){$q=$db->prepare('SELECT COUNT(*) FROM companion_ticket_messages WHERE ticket_id=?');$q->execute([$id]);if((int)$q->fetchColumn()>=200)reject(409,'Límite de mensajes alcanzado. Crea una nueva consulta.');$message=opsText($body['message'],1,5000);if(!is_bool($body['internal']??null))reject(400,'Visibilidad no válida.');$q=$db->prepare('INSERT INTO companion_ticket_messages(ticket_id,author_id,body,internal,created_at) VALUES (?,?,?,?,UTC_TIMESTAMP())');$q->execute([$id,$actor['id'],$message,(int)$body['internal']]);}
 }
 elseif($action==='x-notice-save'){
  $id=isset($body['id'])?opsId($body['id']):bin2hex(random_bytes(16));$title=opsText($body['title']??null,3,160);$text=opsText($body['text']??null,3,5000);$audience=opsEnum($body['audience']??null,['web','desktop','both']);$level=opsEnum($body['level']??null,['info','warning','maintenance']);$start=opsDate($body['startsAt']??null);$end=opsDate($body['endsAt']??null);if($end<=$start)reject(400,'El final debe ser posterior al inicio.');if(!is_bool($body['published']??null))reject(400,'Estado no válido.');
  $q=$db->prepare('INSERT INTO companion_notices(id,title,body,audience,level,published,starts_at,ends_at,updated_at) VALUES (?,?,?,?,?,?,?,?,UTC_TIMESTAMP()) ON DUPLICATE KEY UPDATE title=VALUES(title),body=VALUES(body),audience=VALUES(audience),level=VALUES(level),published=VALUES(published),starts_at=VALUES(starts_at),ends_at=VALUES(ends_at),updated_at=UTC_TIMESTAMP()');$q->execute([$id,$title,$text,$audience,$level,(int)$body['published'],$start,$end]);
 }
 elseif($action==='x-release-save'){
  $id=opsId($body['id']??null);if(!is_bool($body['published']??null))reject(400,'Estado no válido.');$q=$db->prepare('SELECT * FROM companion_releases WHERE id=? FOR UPDATE');$q->execute([$id]);$r=$q->fetch(PDO::FETCH_ASSOC);if(!$r)reject(404,'Versión no encontrada.');$path=$private.'/releases/'.$id.'.exe';if($body['published']&&(!is_file($path)||!hash_equals($r['sha256'],hash_file('sha256',$path))))reject(409,'El archivo no coincide con su huella.');$q=$db->prepare('UPDATE companion_releases SET published=? WHERE id=?');$q->execute([(int)$body['published'],$id]);
 }
 elseif($action==='x-staff-save'){
  $candidate=$body['userId']??null;if($candidate===null){$name=opsText($body['username']??null,3,32);$q=$db->prepare('SELECT id FROM companion_users WHERE username=?');$q->execute([$name]);$candidate=$q->fetchColumn();if(!$candidate)reject(404,'Cuenta no encontrada.');}$uid=opsId($candidate);if($uid===$actor['id'])reject(409,'No puedes cambiar tu propio acceso.');$role=opsEnum($body['staffRole']??null,['admin','support','user']);$q=$db->prepare('SELECT username FROM companion_users WHERE id=? FOR UPDATE');$q->execute([$uid]);$name=$q->fetchColumn();if(!$name)reject(404,'Cuenta no encontrada.');if(($body['confirmation']??'')!==$name)reject(400,'Confirma el nombre de la cuenta.');if(opsStaff($db,['id'=>$uid])==='owner')reject(409,'El propietario está protegido.');
  $q=$db->prepare('UPDATE companion_users SET role=?,auth_version=auth_version+1 WHERE id=?');$q->execute([$role==='user'?'user':'admin',$uid]);$q=$db->prepare('DELETE FROM companion_staff WHERE user_id=?');$q->execute([$uid]);if($role!=='user'){$q=$db->prepare('INSERT INTO companion_staff(user_id,staff_role) VALUES (?,?)');$q->execute([$uid,$role]);}
 }
 else reject(400,'Acción no válida.');
 $db->commit();answer(['message'=>'Cambio guardado y registrado.']);
}
