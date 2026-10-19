<?php
declare(strict_types=1);
header('Cache-Control: no-store');header('Referrer-Policy: same-origin');
$slug=$_GET['slug']??'';if(!is_string($slug)||!preg_match('/^[a-z0-9][a-z0-9-]{2,39}$/D',$slug)){http_response_code(404);exit;}
try{
 $c=json_decode(file_get_contents((getenv('AION_PRIVATE_DIR')?:dirname(__DIR__,2).'/aion2-private').'/account-config.json'),true,32,JSON_THROW_ON_ERROR);$db=new PDO($c['db']['dsn'],$c['db']['user'],$c['db']['password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION]);
 $q=$db->prepare('SELECT id FROM companion_partners WHERE slug=? AND active=1');$q->execute([$slug]);$id=$q->fetchColumn();if(!$id){http_response_code(404);exit;}
 // Keep the first active referral for 30 days. Repeated clicks do not inflate visits.
 $existing=$_COOKIE['__Host-daeva_ref']??'';$valid=false;
 if(is_string($existing)&&preg_match('/^[a-f0-9]{64}$/D',$existing)){$q=$db->prepare('SELECT v.token_hash FROM companion_referral_visits v JOIN companion_partners p ON p.id=v.partner_id WHERE v.token_hash=? AND v.expires_at>UTC_TIMESTAMP() AND p.active=1');$q->execute([hash('sha256',$existing)]);$valid=(bool)$q->fetchColumn();}
 if(!$valid){
  $dir=(getenv('AION_PRIVATE_DIR')?:dirname(__DIR__,2).'/aion2-private').'/auth-limits';$p=$dir.'/'.hash('sha256','referral:'.($_SERVER['REMOTE_ADDR']??'').floor(time()/3600));$h=fopen($p,'c+');flock($h,LOCK_EX);$n=(int)stream_get_contents($h);if($n>=100){fclose($h);http_response_code(429);exit;}rewind($h);ftruncate($h,0);fwrite($h,(string)($n+1));fclose($h);
  $token=bin2hex(random_bytes(32));$q=$db->prepare('INSERT INTO companion_referral_visits(token_hash,partner_id,visited_at,expires_at) VALUES (?,?,UTC_TIMESTAMP(),UTC_TIMESTAMP()+INTERVAL 30 DAY)');$q->execute([hash('sha256',$token),$id]);setcookie('__Host-daeva_ref',$token,['expires'=>time()+2592000,'path'=>'/','secure'=>true,'httponly'=>true,'samesite'=>'Lax']);
 }
 header('Location: /#home',true,302);
}catch(Throwable){http_response_code(503);}
