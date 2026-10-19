<?php
declare(strict_types=1);
header('Content-Type: application/json; charset=utf-8');header('Cache-Control: no-store');header('X-Content-Type-Options: nosniff');
function answer(array $x,int $s=200):never{http_response_code($s);echo json_encode($x,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);exit;}
function reject(int $s,string $m):never{answer(['error'=>$m],$s);}
$private=(getenv('AION_PRIVATE_DIR')?:dirname(__DIR__,2).'/aion2-private');$root=$private.'/companion';
try{$config=json_decode(file_get_contents($private.'/account-config.json'),true,512,JSON_THROW_ON_ERROR);if(!($config['accountsEnabled']??false))reject(503,'Servicio no disponible.');}catch(Throwable){reject(503,'Servicio no disponible.');}
function limit(string $key,int $cap,int $period,string $id=''):void{global $private;$dir=$private.'/auth-limits';if(!is_dir($dir))mkdir($dir,0700,true);$p=$dir.'/'.hash('sha256','admin:'.$key.($_SERVER['REMOTE_ADDR']??'').$id.floor(time()/$period));$h=fopen($p,'c+');if(!$h)reject(503,'Servicio no disponible.');flock($h,LOCK_EX);$n=(int)stream_get_contents($h);if($n>=$cap){fclose($h);header('Retry-After: '.$period);reject(429,'Demasiadas solicitudes. Espera antes de reintentar.');}rewind($h);ftruncate($h,0);fwrite($h,(string)($n+1));fclose($h);}
limit('all',150,60);
$action=(string)($_GET['action']??'session');$method=$_SERVER['REQUEST_METHOD']??'';
$reads=['session','overview','accounts','account','subscriptions','devices','verifications','audit','service','x-companion','x-releases','x-quality','x-tickets','x-ticket','x-staff','x-security','x-metrics','x-notices','x-marketing','payments'];
$writes=['account-status','account-role','account-sessions','account-rename','account-mail','subscription-grant','subscription-cancel','device-revoke','link-remove','x-quality-scan','x-ticket-update','x-staff-save','x-mfa-setup','x-mfa-enable','x-mfa-verify','x-mfa-disable','x-notice-save','x-release-upload','x-release-save','x-partner-save','x-discount-save','billing-reconcile','license-status'];
if(!in_array($action,array_merge($reads,$writes),true))reject(400,'Acción no válida.');
if($method!==(in_array($action,$reads,true)?'GET':'POST'))reject(405,'Método no permitido.');
ini_set('session.use_strict_mode','1');ini_set('session.use_only_cookies','1');ini_set('session.save_path',$private.'/sessions');session_name('__Host-aion_session');session_set_cookie_params(['path'=>'/','secure'=>true,'httponly'=>true,'samesite'=>'Lax']);session_start();
if(!isset($_SESSION['uid'])||time()-($_SESSION['born']??0)>43200||time()-($_SESSION['last']??0)>7200)reject(401,'Inicia sesión con tu cuenta administradora.');
$body=[];
if($method==='POST'){
 if(($_SERVER['HTTP_ORIGIN']??'')!==$config['origin']||!hash_equals($_SESSION['csrf']??'',(string)($_SERVER['HTTP_X_CSRF_TOKEN']??'')))reject(403,'Origen o sesión no válido.');
 if($action==='x-release-upload'){if(!str_starts_with(strtolower($_SERVER['CONTENT_TYPE']??''),'multipart/form-data'))reject(415,'Se requiere un archivo.');}
 else {if(!str_starts_with(strtolower($_SERVER['CONTENT_TYPE']??''),'application/json'))reject(415,'Se requiere JSON.');
 $raw=file_get_contents('php://input',false,null,0,16385);if(strlen($raw)>16384)reject(413,'Solicitud demasiado grande.');try{$body=json_decode($raw,true,16,JSON_THROW_ON_ERROR);}catch(Throwable){reject(400,'JSON no válido.');}if(!is_array($body)||array_is_list($body)&&$body!==[])reject(400,'Solicitud no válida.');}
}
try{
 $db=new PDO($config['db']['dsn'],$config['db']['user'],$config['db']['password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_EMULATE_PREPARES=>false]);
 $q=$db->prepare('SELECT id,username,role,status,auth_version FROM companion_users WHERE id=?');$q->execute([$_SESSION['uid']]);$actor=$q->fetch(PDO::FETCH_ASSOC);
 if(!$actor||(int)$actor['auth_version']!==($_SESSION['auth_version']??0)||$actor['status']!=='active')reject(401,'La sesión caducó o fue suspendida.');
 if($actor['role']!=='admin')reject(403,'Esta cuenta no tiene permisos de administración.');
 $_SESSION['last']=time();$csrf=$_SESSION['csrf'];
 define('DAEVA_ADMIN_CONTEXT',true);require __DIR__.'/admin-access.php';require __DIR__.'/admin-domain.php';
}catch(Throwable $e){if(isset($db)&&$db->inTransaction())$db->rollBack();error_log('Daeva admin: '.$e->getCode());reject(503,'No se pudo completar la operación.');}
