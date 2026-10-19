<?php
declare(strict_types=1);
if(!defined('DAEVA_ADMIN_CONTEXT')&&!defined('DAEVA_OPERATIONS_CONTEXT')&&!defined('DAEVA_COMPANION_CONTEXT')){http_response_code(404);exit;}
function opsFail(int $code,string $message):never {if(function_exists('reject'))reject($code,$message);stop($code,$message);}
function opsId(mixed $id):string {if(!is_string($id)||!preg_match('/^[a-f0-9]{32}$/D',$id))opsFail(400,'Identificador no válido.');return $id;}
function opsText(mixed $x,int $min,int $max):string {if(!is_string($x))opsFail(400,'Texto no válido.');$x=trim($x);if(mb_strlen($x)<$min||mb_strlen($x)>$max||preg_match('/[\x00-\x08\x0b\x0c\x0e-\x1f]/',$x))opsFail(400,"El texto debe tener $min–$max caracteres.");return $x;}
function opsEnum(mixed $x,array $allowed):string {if(!is_string($x)||!in_array($x,$allowed,true))opsFail(400,'Opción no válida.');return $x;}
function opsIso(?string $date):?string {return $date===null?null:str_replace(' ','T',$date).'Z';}
function opsDate(mixed $x):string {if(!is_string($x)||!preg_match('/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/D',$x))opsFail(400,'Fecha UTC no válida.');$t=strtotime($x);if($t===false||gmdate('Y-m-d\TH:i:s\Z',$t)!==$x)opsFail(400,'Fecha no válida.');return gmdate('Y-m-d H:i:s',$t);}
function opsNotices(PDO $db,string $audience):array {
 $q=$db->prepare("SELECT id,title,body,level,starts_at,ends_at FROM companion_notices WHERE published=1 AND audience IN (?, 'both') AND starts_at<=UTC_TIMESTAMP() AND ends_at>UTC_TIMESTAMP() ORDER BY level='maintenance' DESC,starts_at DESC LIMIT 5");$q->execute([$audience]);$r=$q->fetchAll(PDO::FETCH_ASSOC);foreach($r as &$x){$x['startsAt']=opsIso($x['starts_at']);$x['endsAt']=opsIso($x['ends_at']);unset($x['starts_at'],$x['ends_at']);}return $r;
}
function opsReleases(PDO $db):array {
 $r=[];foreach(['stable','beta'] as $channel){$q=$db->prepare("SELECT id,version,channel,notes,sha256,bytes,created_at FROM companion_releases WHERE published=1 AND channel=? ORDER BY created_at DESC LIMIT 1");$q->execute([$channel]);if($x=$q->fetch(PDO::FETCH_ASSOC)){$x['url']='/api/operations.php?action=download&id='.$x['id'];$x['createdAt']=opsIso($x['created_at']);$x['bytes']=(int)$x['bytes'];unset($x['created_at']);$r[$channel]=$x;}}return $r;
}
function opsStaff(PDO $db,array $actor):string {$q=$db->prepare('SELECT staff_role FROM companion_staff WHERE user_id=?');$q->execute([$actor['id']]);return $q->fetchColumn()?:'admin';}
function opsPermissions(string $role):array {
 $read=['session','overview','accounts','account','devices','verifications','service','x-companion','x-tickets','x-ticket','x-security'];
 $support=array_merge($read,['x-ticket-update']);
 $admin=array_merge($support,['subscriptions','payments','billing-reconcile','license-status','audit','account-status','account-sessions','account-mail','subscription-grant','subscription-cancel','device-revoke','link-remove','x-releases','x-release-upload','x-release-save','x-quality','x-quality-scan','x-metrics','x-marketing','x-partner-save','x-discount-save','x-notices','x-notice-save','x-mfa-setup','x-mfa-enable','x-mfa-verify','x-mfa-disable']);
 // MFA management is available to each staff member, including support.
 $support=array_merge($support,['x-mfa-setup','x-mfa-enable','x-mfa-verify','x-mfa-disable']);
 return $role==='owner'?array_merge($admin,['account-role','account-rename','x-staff','x-staff-save']):($role==='support'?$support:$admin);
}
function opsUsage(PDO $db,string $uid,string $feature):void {$q=$db->prepare('INSERT INTO companion_usage(user_id,feature,day,hits) VALUES (?,?,UTC_DATE(),1) ON DUPLICATE KEY UPDATE hits=LEAST(hits+1,1000000)');$q->execute([$uid,$feature]);}
