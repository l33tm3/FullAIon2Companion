<?php
declare(strict_types=1);
if(!defined('DAEVA_ADMIN_CONTEXT')){http_response_code(404);exit;}
function opsBase32(string $raw):string {$bits='';foreach(str_split($raw) as $c)$bits.=str_pad(decbin(ord($c)),8,'0',STR_PAD_LEFT);$out='';foreach(str_split($bits,5) as $b)$out.='ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'[bindec(str_pad($b,5,'0'))];return $out;}
function opsUnbase32(string $text):string {$bits='';foreach(str_split($text) as $c){$i=strpos('ABCDEFGHIJKLMNOPQRSTUVWXYZ234567',$c);if($i===false)throw new RuntimeException('Secret invalid');$bits.=str_pad(decbin($i),5,'0',STR_PAD_LEFT);}$out='';foreach(str_split($bits,8) as $b)if(strlen($b)===8)$out.=chr(bindec($b));return $out;}
// RFC 6238 / HOTP SHA-1, dynamic truncation; separate digits argument for RFC vectors.
function opsTotp(string $raw,int $step,int $digits=6):string {$h=hash_hmac('sha1',pack('N2',($step>>32)&0xffffffff,$step&0xffffffff),$raw,true);$offset=ord($h[19])&15;$n=unpack('N',substr($h,$offset,4))[1]&0x7fffffff;return str_pad((string)($n%(10**$digits)),$digits,'0',STR_PAD_LEFT);}
function opsSeal(string $secret):string {global $private;$key=file_get_contents($private.'/admin-mfa.key');if(strlen($key)!==32)throw new RuntimeException('Invalid key');$iv=random_bytes(12);$tag='';$enc=openssl_encrypt($secret,'aes-256-gcm',$key,OPENSSL_RAW_DATA,$iv,$tag,'daeva-admin-mfa');if($enc===false)throw new RuntimeException('Encryption failed');return base64_encode($iv.$tag.$enc);}
function opsOpen(string $box):string {global $private;$b=base64_decode($box,true);if($b===false||strlen($b)<29)throw new RuntimeException('Invalid box');$s=openssl_decrypt(substr($b,28),'aes-256-gcm',file_get_contents($private.'/admin-mfa.key'),OPENSSL_RAW_DATA,substr($b,0,12),substr($b,12,16),'daeva-admin-mfa');if($s===false)throw new RuntimeException('Decryption failed');return $s;}
function opsMfa(PDO $db,string $uid):?array {$q=$db->prepare('SELECT * FROM companion_mfa WHERE user_id=?');$q->execute([$uid]);return $q->fetch(PDO::FETCH_ASSOC)?:null;}
function opsMfaCode(array $m,mixed $code):int {
 if(!is_string($code)||!preg_match('/^\d{6}$/D',$code))return -1;$step=intdiv(time(),30);$raw=opsOpen($m['secret_box']);
 foreach([$step,$step-1,$step+1] as $s)if($s>(int)$m['last_step']&&hash_equals(opsTotp($raw,$s),$code))return $s;return -1;
}
function opsSecurityRoute():void {
 global $action,$db,$actor,$body,$csrf,$private,$staffRole,$permissions,$mfa;
 if($action==='x-security'){
  $q=$db->query("SELECT actor_name,action,target_id,detail_json,CONVERT_TZ(created_at,@@session.time_zone,'+00:00') created_at FROM companion_admin_audit WHERE action IN ('account-role','account-status','device-revoke','x-staff-save','x-mfa-enable','x-mfa-disable','security.denied','security.mfa-failed','security.admin-access') ORDER BY id DESC LIMIT 40");$alerts=$mfa&&$mfa['enabled']&&($_SESSION['admin_mfa_until']??0)<time()?[]:$q->fetchAll(PDO::FETCH_ASSOC);foreach($alerts as &$a){$a['details']=json_decode($a['detail_json'],true);unset($a['detail_json']);}
  answer(['enabled'=>(bool)($mfa['enabled']??false),'verifiedUntil'=>$_SESSION['admin_mfa_until']??0,'staffRole'=>$staffRole,'permissions'=>$permissions,'alerts'=>$alerts]);
 }
 if(!str_starts_with($action,'x-mfa-'))return;
 limit('mfa',8,300,$actor['id']);
 if($action==='x-mfa-setup'){
  if($mfa&&$mfa['enabled'])reject(409,'El doble factor ya está activado.');$q=$db->prepare('SELECT password_hash FROM companion_users WHERE id=?');$q->execute([$actor['id']]);if(!is_string($body['password']??null)||strlen($body['password'])>128||!password_verify($body['password'],$q->fetchColumn()))reject(401,'Confirma tu contraseña actual.');$secret=random_bytes(20);
  $q=$db->prepare('INSERT INTO companion_mfa(user_id,secret_box,enabled,last_step,recovery_json) VALUES (?,?,0,-1,?) ON DUPLICATE KEY UPDATE secret_box=VALUES(secret_box),last_step=-1,recovery_json=VALUES(recovery_json)');$q->execute([$actor['id'],opsSeal($secret),'[]']);$base32=opsBase32($secret);
  answer(['secret'=>$base32,'uri'=>'otpauth://totp/'.rawurlencode('Daeva Companion:'.$actor['username']).'?secret='.$base32.'&issuer=Daeva%20Companion&algorithm=SHA1&digits=6&period=30','message'=>'Añade esta clave a tu aplicación de autenticación y confirma el código.']);
 }
 if(!$mfa)reject(409,'Configura primero tu autenticador.');
 $db->beginTransaction();$q=$db->prepare('SELECT * FROM companion_mfa WHERE user_id=? FOR UPDATE');$q->execute([$actor['id']]);$m=$q->fetch(PDO::FETCH_ASSOC);
 $step=opsMfaCode($m,$body['code']??null);$recovery=false;$hashes=json_decode($m['recovery_json'],true);
 if($step<0&&$action==='x-mfa-verify'&&$m['enabled']&&is_string($body['code']??null)){$hash=hash('sha256',strtoupper(trim($body['code'])));$pos=array_search($hash,$hashes,true);if($pos!==false){array_splice($hashes,$pos,1);$recovery=true;}}
 if($step<0&&!$recovery){$db->rollBack();adminAudit('security.mfa-failed',$actor['id'],['reason'=>'Código de doble factor rechazado']);reject(400,'Código incorrecto, caducado o ya utilizado.');}
 if($action==='x-mfa-enable'){
  if($m['enabled']){$db->rollBack();reject(409,'Ya está activado.');}$codes=[];for($i=0;$i<8;$i++)$codes[]=strtoupper(bin2hex(random_bytes(8)));
  $q=$db->prepare('UPDATE companion_mfa SET enabled=1,last_step=?,recovery_json=? WHERE user_id=?');$q->execute([$step,json_encode(array_map(fn($c)=>hash('sha256',$c),$codes)),$actor['id']]);adminAudit($action,$actor['id'],['reason'=>'Autenticador confirmado']);$db->commit();$_SESSION['admin_mfa_until']=time()+900;session_write_close();answer(['recoveryCodes'=>$codes,'message'=>'Doble factor activado. Guarda estos códigos de recuperación; se muestran una sola vez.']);
 }
 if(!$m['enabled']){$db->rollBack();reject(409,'El doble factor no está activado.');}
 if($action==='x-mfa-disable'){$q=$db->prepare('DELETE FROM companion_mfa WHERE user_id=?');$q->execute([$actor['id']]);adminAudit($action,$actor['id'],['reason'=>opsText($body['reason']??null,5,500)]);unset($_SESSION['admin_mfa_until']);}
 else {$q=$db->prepare('UPDATE companion_mfa SET last_step=?,recovery_json=? WHERE user_id=?');$q->execute([$recovery?$m['last_step']:$step,json_encode($hashes),$actor['id']]);$_SESSION['admin_mfa_until']=time()+900;}
 $db->commit();session_write_close();answer(['message'=>$action==='x-mfa-disable'?'Doble factor retirado.':'Sesión administrativa confirmada durante 15 minutos.']);
}
