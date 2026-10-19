<?php
declare(strict_types=1);
if(!defined('DAEVA_COMPANION_CONTEXT')){http_response_code(404);exit;}
// Self-service ownership: the player swaps one equipped title in the game and the server reads that change on the
// official AION 2 profile itself. The client only names the character; the proof comes from NCSOFT. Up to 3 per account.
const CHARACTER_LIMIT=3;
const TITLE_CATEGORIES=['Attack'=>'Ataque','Defense'=>'Defensa','Etc'=>'Otros'];
function characterKey(array $ref):string{return hash('sha256',json_encode($ref));}
function characterLinks(string $uid):array{global $root;$r=[];foreach(glob($root.'/link-*.json')?:[] as $p){$l=json_decode((string)file_get_contents($p),true);if(is_array($l)&&($l['userId']??'')===$uid&&!empty($l['verifiedAt']))$r[]=$l;}return $r;}
function officialCharacter(array $ref):?array{
 global $officialFetch;if(isset($officialFetch)){$x=$officialFetch($ref);}else{
  $ch=curl_init('https://aion2.plaync.com/api/character/info?lang=es-ES&characterId='.rawurlencode($ref['characterId']).'&serverId='.$ref['serverId'].'&region='.$ref['region']);
  curl_setopt_array($ch,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_CONNECTTIMEOUT=>5,CURLOPT_TIMEOUT=>10,CURLOPT_USERAGENT=>'DaevaCompanion (+https://governanceai.me)',CURLOPT_HTTPHEADER=>['Accept: application/json']]);
  $raw=curl_exec($ch);$status=(int)curl_getinfo($ch,CURLINFO_RESPONSE_CODE);curl_close($ch);$x=$raw!==false&&$status===200?json_decode((string)$raw,true):null;
 }
 $p=is_array($x)?($x['profile']??null):null;
 return is_array($p)&&($p['characterId']??null)===$ref['characterId']&&(int)($p['serverId']??0)===$ref['serverId']&&($p['characterName']??'')!==''?$x:null;
}
// Equipped title per category, as the official profile shows it.
function titleSnapshot(array $info):array{$s=[];foreach($info['title']['titleList']??[] as $t)if(is_array($t)&&isset(TITLE_CATEGORIES[$t['equipCategory']??''])&&is_int($t['id']??null))$s[$t['equipCategory']]=['id'=>$t['id'],'name'=>mb_substr((string)($t['name']??''),0,80),'owned'=>(int)($t['ownedCount']??0)];return $s;}
// Another title in that category, or none while the rest of the list is still there (an empty list proves nothing).
function titleChanged(array $x,array $now):bool{$after=$now[$x['category']]??null;return $after?$after['id']!==$x['before']['id']:count($now)>0;}
function titlePublic(array $x):array{$status=$x['status']==='pending'&&$x['expires']<time()?'expired':$x['status'];return ['challengeId'=>$x['id'],'character'=>$x['character'],'characterName'=>$x['characterName'],'serverName'=>$x['serverName'],'category'=>$x['category'],'categoryLabel'=>TITLE_CATEGORIES[$x['category']],'currentTitle'=>$x['before']['name'],'status'=>$status,'createdAt'=>gmdate('c',$x['created']),'expiresAt'=>gmdate('c',$x['expires'])];}
function titleChallenges(string $uid):array{global $root;$r=[];foreach(glob($root.'/titlechk-*.json')?:[] as $p){$x=json_decode((string)file_get_contents($p),true);if(is_array($x)&&($x['userId']??'')===$uid&&$x['status']==='pending'&&$x['expires']>=time())$r[]=titlePublic($x);}usort($r,fn($a,$b)=>strcmp($b['createdAt'],$a['createdAt']));return array_slice($r,0,5);}
function characterAudit(string $username,string $action,array $detail):void{global $db,$user;$q=$db->prepare('INSERT INTO companion_admin_audit(actor_id,actor_name,target_id,action,detail_json) VALUES (?,?,?,?,?)');$q->execute([$user['id'],$username,$user['id'],$action,json_encode($detail+['source'=>'self-service'],JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR)]);}
if(in_array($action,['title-challenge','title-check','title-cancel','character-unlink'],true)){
 expectedMethod('POST');$q=$db->prepare('SELECT username,email_verified_at FROM companion_users WHERE id=?');$q->execute([$user['id']]);$me=$q->fetch(PDO::FETCH_ASSOC);
 $limitMessage='Ya tienes '.CHARACTER_LIMIT.' personajes verificados, el máximo por cuenta. Quita uno para añadir otro.';
 if($action==='title-challenge'){
  if(!$me['email_verified_at'])stop(409,'Confirma tu correo antes de verificar un personaje.');
  $ref=charRef($body['character']??null);$prior=readRecord('link-'.characterKey($ref));
  if($prior&&($prior['userId']??'')===$user['id'])stop(409,'Este personaje ya está verificado en tu cuenta.');
  if(count(characterLinks($user['id']))>=CHARACTER_LIMIT)stop(409,$limitMessage);
  if(random_int(1,20)===1)foreach(glob($root.'/titlechk-*')?:[] as $old)if(filemtime($old)<time()-7*86400)@unlink($old);
  foreach(glob($root.'/titlechk-*.json')?:[] as $p){$x=json_decode((string)file_get_contents($p),true);if(is_array($x)&&($x['userId']??'')===$user['id']&&$x['status']==='pending'&&$x['expires']>=time()&&$x['character']===$ref)reply(titlePublic($x));}
  rate('title-challenge'.$user['id'],6,3600);
  $info=officialCharacter($ref);if(!$info)stop(502,'No pudimos consultar ese personaje en la web oficial de AION 2. Inténtalo de nuevo en unos minutos.');
  $now=titleSnapshot($info);if(!$now)stop(409,'Este personaje no tiene títulos equipados en la web oficial. Equipa uno en el juego y vuelve a intentarlo.');
  // Ask for a category where the player owns more than one title, so there is something to swap to.
  $swappable=array_keys(array_filter($now,fn($t)=>$t['owned']>1))?:array_keys($now);$category=$swappable[random_int(0,count($swappable)-1)];
  $id=bin2hex(random_bytes(16));$p=$info['profile'];
  $x=['id'=>$id,'userId'=>$user['id'],'character'=>$ref,'characterName'=>mb_substr((string)$p['characterName'],0,40),'serverName'=>mb_substr((string)($p['serverName']??''),0,40),'category'=>$category,'before'=>$now[$category],'created'=>time(),'expires'=>time()+3600,'status'=>'pending','checks'=>0,'lastCheck'=>0];
  writeRecord('titlechk-'.$id,$x);reply(titlePublic($x));
 }
 if($action==='title-check'||$action==='title-cancel'){
  $id=idValue($body['challengeId']??null);$h=fopen($root.'/titlechk-'.$id.'.lock','c');flock($h,LOCK_EX);$x=readRecord('titlechk-'.$id);
  if(!$x||$x['userId']!==$user['id'])stop(404,'Verificación no encontrada.');
  if($action==='title-cancel'){if($x['status']==='pending'){$x['status']='cancelled';writeRecord('titlechk-'.$id,$x);}reply(titlePublic($x));}
  if($x['status']==='verified')reply(titlePublic($x)+['verified'=>true,'message'=>'Este personaje ya quedó verificado.']);
  if($x['status']!=='pending'||$x['expires']<time())stop(409,'Esta verificación ya no está activa. Empieza una nueva.');
  if(!$me['email_verified_at'])stop(409,'Confirma tu correo antes de verificar un personaje.');
  if(time()-(int)$x['lastCheck']<10){header('Retry-After: 10');stop(429,'Espera unos segundos antes de volver a comprobar.');}
  if($x['checks']>=60)stop(429,'Demasiadas comprobaciones. Empieza una verificación nueva.');
  $x['checks']++;$x['lastCheck']=time();writeRecord('titlechk-'.$id,$x);
  $info=officialCharacter($x['character']);if(!$info)stop(502,'No pudimos consultar la web oficial de AION 2. Inténtalo de nuevo en unos minutos.');
  $now=titleSnapshot($info);
  if(!titleChanged($x,$now))reply(titlePublic($x)+['verified'=>false,'message'=>'La web oficial todavía muestra «'.$x['before']['name'].'». Puede tardar unos minutos en actualizarse; si no cambia, cierra la sesión del personaje en el juego y vuelve a comprobar.']);
  $key=characterKey($x['character']);$lh=fopen($root.'/link-'.$key.'.lock','c');flock($lh,LOCK_EX);$prior=readRecord('link-'.$key);
  $previous=$prior&&($prior['userId']??'')!==$user['id']?(string)($prior['userId']??''):null;
  if(!$prior||$previous!==null){
   if(count(characterLinks($user['id']))>=CHARACTER_LIMIT)stop(409,$limitMessage);
   // Proving control of the character now wins over an earlier link from another account; the audit keeps who had it.
   $after=$now[$x['category']]??null;$link=['userId'=>$user['id'],'character'=>$x['character'],'verifiedAt'=>gmdate('c'),'deviceId'=>null,'approval'=>['kind'=>'official-title-change','challengeId'=>$id,'characterName'=>$x['characterName'],'category'=>$x['category'],'before'=>$x['before']['id'],'after'=>$after['id']??null,'previousUserId'=>$previous]];
   $db->beginTransaction();
   try{characterAudit((string)$me['username'],'self-title-ownership',['character'=>$x['character'],'characterName'=>$x['characterName'],'challengeId'=>$id,'category'=>$x['category'],'before'=>$x['before'],'after'=>$after,'previousUserId'=>$previous]);writeRecord('link-'.$key,$link);$db->commit();}
   catch(Throwable $e){if($db->inTransaction())$db->rollBack();if($prior)writeRecord('link-'.$key,$prior);else @unlink($root.'/link-'.$key.'.json');throw $e;}
  }
  $x['status']='verified';$x['verifiedAt']=gmdate('c');writeRecord('titlechk-'.$id,$x);
  reply(titlePublic($x)+['verified'=>true,'message'=>'Personaje verificado. Ya puedes volver a ponerte tu título anterior.']);
 }
 if($action==='character-unlink'){
  $ref=charRef($body['character']??null);$key=characterKey($ref);$lh=fopen($root.'/link-'.$key.'.lock','c');flock($lh,LOCK_EX);$l=readRecord('link-'.$key);
  if(!$l||($l['userId']??'')!==$user['id'])stop(404,'Ese personaje no está verificado en tu cuenta.');
  $db->beginTransaction();
  try{characterAudit((string)$me['username'],'self-character-unlink',['character'=>$ref,'characterName'=>$l['approval']['characterName']??null,'verifiedAt'=>$l['verifiedAt']??null]);if(!unlink($root.'/link-'.$key.'.json'))throw new RuntimeException('No se pudo retirar el vínculo');$db->commit();}
  catch(Throwable $e){if($db->inTransaction())$db->rollBack();if(!is_file($root.'/link-'.$key.'.json'))writeRecord('link-'.$key,$l);throw $e;}
  reply(['removed'=>true,'character'=>$ref]);
 }
}
