<?php
declare(strict_types=1);
if(!defined('DAEVA_COMPANION_CONTEXT')){http_response_code(404);exit;}
require_once __DIR__.'/operations-common.php';
// Support from the desktop app (2026-10-13): the same tickets as the website's support page (operations.php), authenticated by
// the device token instead of the web session. The ticket is linked to this device; the account's character is checked
// against its ownership link exactly as the website does.
if(in_array($action,['support-tickets','support-ticket','support-create','support-reply'],true)){
 $uid=$user['id'];
 if($action==='support-tickets'){expectedMethod('GET');rate('support-read'.$device['id'],60,60);$q=$db->prepare('SELECT id,subject,category,status,priority,created_at,updated_at FROM companion_tickets WHERE user_id=? ORDER BY updated_at DESC LIMIT 50');$q->execute([$uid]);reply(['tickets'=>$q->fetchAll(PDO::FETCH_ASSOC)]);}
 if($action==='support-ticket'){
  expectedMethod('GET');rate('support-read'.$device['id'],60,60);$id=opsId($_GET['id']??null);$q=$db->prepare('SELECT id,subject,category,status,priority,created_at,updated_at FROM companion_tickets WHERE id=? AND user_id=?');$q->execute([$id,$uid]);$t=$q->fetch(PDO::FETCH_ASSOC);if(!$t)stop(404,'Consulta no encontrada.');
  $q=$db->prepare('SELECT m.body,m.created_at,u.username,(m.author_id=?) AS mine FROM companion_ticket_messages m JOIN companion_users u ON u.id=m.author_id WHERE m.ticket_id=? AND m.internal=0 ORDER BY m.id LIMIT 200');$q->execute([$uid,$id]);$t['messages']=$q->fetchAll(PDO::FETCH_ASSOC);reply(['ticket'=>$t]);
 }
 expectedMethod('POST');if(array_diff(array_keys($body),['id','subject','category','message','character']))stop(400,'Campos no admitidos.');
 rate('support-write'.$uid,10,3600);$message=opsText($body['message']??null,5,5000);$db->beginTransaction();
 if($action==='support-create'){
  $subject=opsText($body['subject']??null,5,160);$category=opsEnum($body['category']??null,['account','desktop','map','data','subscription','other']);$id=bin2hex(random_bytes(16));$character=null;
  if(isset($body['character'])){$ref=charRef($body['character']);$link=readRecord('link-'.hash('sha256',json_encode($ref)));$character=json_encode($ref+['ownershipVerified'=>($link['userId']??null)===$uid]);}
  $q=$db->prepare('INSERT INTO companion_tickets(id,user_id,subject,category,device_id,character_json,created_at,updated_at) VALUES (?,?,?,?,?,?,UTC_TIMESTAMP(),UTC_TIMESTAMP())');$q->execute([$id,$uid,$subject,$category,$device['id'],$character]);
 }else{
  $id=opsId($body['id']??null);$q=$db->prepare('SELECT id,status FROM companion_tickets WHERE id=? AND user_id=? FOR UPDATE');$q->execute([$id,$uid]);$t=$q->fetch(PDO::FETCH_ASSOC);if(!$t)stop(404,'Consulta no encontrada.');if($t['status']==='closed')stop(409,'Esta consulta está cerrada. Crea una nueva si necesitas ayuda.');$q=$db->prepare("UPDATE companion_tickets SET status='open',updated_at=UTC_TIMESTAMP() WHERE id=?");$q->execute([$id]);
 }
 $q=$db->prepare('SELECT COUNT(*) FROM companion_ticket_messages WHERE ticket_id=?');$q->execute([$id]);if((int)$q->fetchColumn()>=200)stop(409,'Límite de mensajes alcanzado. Crea una nueva consulta.');
 $q=$db->prepare('INSERT INTO companion_ticket_messages(ticket_id,author_id,body,internal,created_at) VALUES (?,?,?,0,UTC_TIMESTAMP())');$q->execute([$id,$uid,$message]);$db->commit();
 reply(['id'=>$id,'message'=>'Consulta enviada. Te responderemos aquí y en la web.']);
}
if($action==='diagnostics'){
 expectedMethod('POST');rate('diagnostics'.$device['id'],6,60);
 $version=opsText($body['version']??null,3,40);if(!preg_match('/^\d+\.\d+\.\d+(?:-[a-z0-9.-]+)?$/D',$version))stop(400,'Versión no válida.');
 $channel=opsEnum($body['channel']??null,['stable','beta']);$capture=opsEnum($body['captureState']??null,['idle','capturing','unavailable','error']);$sync=opsEnum($body['syncState']??null,['ok','pending','error']);$error=opsText($body['errorCode']??'',0,80);if($error!==''&&!preg_match('/^[A-Z0-9_.-]+$/D',$error))stop(400,'Envía un código de diagnóstico, no contenido de paquetes ni credenciales.');
 if(array_diff(array_keys($body),['version','channel','captureState','syncState','errorCode']))stop(400,'Campos de diagnóstico no admitidos.');
 $q=$db->prepare('INSERT INTO companion_diagnostics(device_id,user_id,version,channel,capture_state,sync_state,error_code,received_at) VALUES (?,?,?,?,?,?,?,UTC_TIMESTAMP()) ON DUPLICATE KEY UPDATE version=VALUES(version),channel=VALUES(channel),capture_state=VALUES(capture_state),sync_state=VALUES(sync_state),error_code=VALUES(error_code),received_at=UTC_TIMESTAMP()');$q->execute([$device['id'],$user['id'],$version,$channel,$capture,$sync,$error]);opsUsage($db,$user['id'],'desktop');$device['lastSeen']=gmdate('c');writeRecord('token-'.hash('sha256',$token),$device);reply(['saved'=>true,'serverTime'=>gmdate('c'),'trustedCapture'=>false]);
}
