<?php
declare(strict_types=1);
header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store'); header('X-Content-Type-Options: nosniff');
function answer(array $body,int $status=200):never {http_response_code($status);echo json_encode($body,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);exit;}
function reject(int $status,string $message):never {answer(['error'=>$message],$status);}
$private=(getenv('AION_PRIVATE_DIR')?:dirname(__DIR__,2).'/aion2-private');
try {$config=json_decode(file_get_contents($private.'/account-config.json'),true,512,JSON_THROW_ON_ERROR);}catch(Throwable){reject(503,'Cuentas temporalmente no disponibles.');}
if(!($config['accountsEnabled']??false))reject(503,'Cuentas no disponibles.');
$method=$_SERVER['REQUEST_METHOD']??'';$action=(string)($_GET['action']??'session');
if(!in_array($method,['GET','POST'],true))reject(405,'Método no permitido.');
if(!in_array($action,['session','register','login','logout','recover','progress','request-reset','reset-password','verify-email','resend-verification','set-email'],true))reject(400,'Acción no válida.');
$sessionDir=$private.'/sessions';if(!is_dir($sessionDir))mkdir($sessionDir,0700,true);
ini_set('session.use_strict_mode','1');ini_set('session.use_only_cookies','1');ini_set('session.save_path',$sessionDir);
session_name('__Host-aion_session');session_set_cookie_params(['lifetime'=>0,'path'=>'/','secure'=>true,'httponly'=>true,'samesite'=>'Lax']);session_start();
if(isset($_SESSION['born'])&&(time()-$_SESSION['born']>43200||time()-($_SESSION['last']??0)>7200)){$_SESSION=[];session_regenerate_id(true);}
$_SESSION['born']??=time();$_SESSION['last']=time();$_SESSION['csrf']??=bin2hex(random_bytes(32));
function limit(string $bucket,int $cap,int $seconds,string $identity=''):void {
 global $private;
 $dir=$private.'/auth-limits';if(!is_dir($dir))mkdir($dir,0700,true);
 $file=$dir.'/'.hash('sha256',$bucket.($_SERVER['REMOTE_ADDR']??'').$identity.(string)floor(time()/$seconds));
 $h=fopen($file,'c+');if(!$h)reject(503,'Servicio temporalmente no disponible.');flock($h,LOCK_EX);$n=(int)stream_get_contents($h);if($n>=$cap){flock($h,LOCK_UN);fclose($h);header('Retry-After: '.$seconds);reject(429,'Demasiados intentos. Vuelve a intentarlo más tarde.');}rewind($h);ftruncate($h,0);fwrite($h,(string)($n+1));flock($h,LOCK_UN);fclose($h);
 if(random_int(1,100)===1)foreach(glob($dir.'/*')?:[] as $old)if(filemtime($old)<time()-86400)@unlink($old);
}
limit('all',180,60);
$input=[];
if($method==='POST'){
 if(($_SERVER['HTTP_ORIGIN']??'')!==$config['origin'])reject(403,'Origen no permitido.');
 if(!hash_equals($_SESSION['csrf'],(string)($_SERVER['HTTP_X_CSRF_TOKEN']??'')))reject(403,'La sesión cambió. Abre tu cuenta e inténtalo de nuevo.');
 if(!str_starts_with(strtolower($_SERVER['CONTENT_TYPE']??''),'application/json'))reject(415,'Se requiere JSON.');
 if((int)($_SERVER['CONTENT_LENGTH']??0)>65536)reject(413,'Solicitud demasiado grande.');
 $raw=file_get_contents('php://input',false,null,0,65537);if(strlen($raw)>65536)reject(413,'Solicitud demasiado grande.');
 try{$input=json_decode($raw,true,32,JSON_THROW_ON_ERROR);}catch(Throwable){reject(400,'JSON no válido.');}if(!is_array($input))reject(400,'Solicitud no válida.');
}elseif(!in_array($action,['session','progress'],true))reject(405,'Esta acción requiere POST.');
try{
 $db=new PDO($config['db']['dsn'],$config['db']['user'],$config['db']['password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION,PDO::ATTR_EMULATE_PREPARES=>false]);
 $user=null;
 if(isset($_SESSION['uid'])){$q=$db->prepare('SELECT id,username,email,email_verified_at,auth_version,revision,role,status FROM companion_users WHERE id=?');$q->execute([$_SESSION['uid']]);$user=$q->fetch(PDO::FETCH_ASSOC);if(!$user||$user['status']!=='active'||(int)$user['auth_version']!==($_SESSION['auth_version']??0)){$user=null;unset($_SESSION['uid'],$_SESSION['auth_version']);}}
 if($action==='session')answer(['user'=>$user?['id'=>$user['id'],'username'=>$user['username'],'email'=>$user['email'],'emailVerified'=>(bool)$user['email_verified_at'],'role'=>$user['role']]:null,'csrfToken'=>$_SESSION['csrf'],'billingEnabled'=>false]);
 define('DAEVA_ACCOUNT_CONTEXT',true);require __DIR__.'/account-email.php';
 if(in_array($action,['register','login','recover'],true)){
  $username=strtolower(trim((string)($input['username']??'')));$password=(string)($input['password']??'');
  if(!($action==='login'&&filter_var($username,FILTER_VALIDATE_EMAIL))&&!preg_match('/^[a-z0-9_]{3,32}$/',$username)||strlen($password)<10||strlen($password)>128)reject(400,'Usa un usuario de 3–32 letras, números o guiones bajos y una contraseña de 10–128 caracteres.');
  limit($action,$action==='register'?3:8,$action==='register'?3600:900);
  $q=$db->prepare('SELECT * FROM companion_users WHERE username=? OR email=?');$q->execute([$username,$username]);$found=$q->fetch(PDO::FETCH_ASSOC);
  $recovery=null;if($found&&$found['status']!=='active')reject(403,'Esta cuenta está suspendida. Contacta con la administración.');
  if($action==='register'){
   if($found)reject(409,'Ese usuario no está disponible.');$email=accountEmail((string)($input['email']??''));$q=$db->prepare('SELECT id FROM companion_users WHERE email=?');$q->execute([$email]);if($q->fetchColumn())reject(409,'Ese correo no está disponible.');
   $uid=bin2hex(random_bytes(16));$recovery=bin2hex(random_bytes(32));$algorithm=defined('PASSWORD_ARGON2ID')?PASSWORD_ARGON2ID:PASSWORD_DEFAULT;
   $q=$db->prepare('INSERT INTO companion_users (id,username,password_hash,recovery_hash,email) VALUES (?,?,?,?,?)');$q->execute([$uid,$username,password_hash($password,$algorithm),hash('sha256',$recovery),$email]);$version=1;require_once __DIR__.'/referral.php';daevaAttributeReferral($db,$uid);$mailSent=accountToken($db,$uid,$email,'verify');
  }elseif($action==='recover'){
   $code=preg_replace('/[\s-]/','',strtolower((string)($input['recoveryCode']??'')));
   if(!$found||!preg_match('/^[a-f0-9]{64}$/',$code)||!hash_equals($found['recovery_hash'],hash('sha256',$code)))reject(401,'Usuario o código de recuperación no válido.');
   $uid=$found['id'];$recovery=bin2hex(random_bytes(32));$version=(int)$found['auth_version']+1;
   $q=$db->prepare('UPDATE companion_users SET password_hash=?,recovery_hash=?,auth_version=? WHERE id=? AND auth_version=?');$q->execute([password_hash($password,defined('PASSWORD_ARGON2ID')?PASSWORD_ARGON2ID:PASSWORD_DEFAULT),hash('sha256',$recovery),$version,$uid,$found['auth_version']]);if($q->rowCount()!==1)reject(409,'El código ya se utilizó.');
  }else{
   // A fixed valid hash keeps unknown-user password verification on the same path.
   $dummy='$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2uheWG/igi';
   if(!password_verify($password,$found['password_hash']??$dummy)||!$found)reject(401,'Usuario o contraseña no válido.');$uid=$found['id'];$username=$found['username'];$version=(int)$found['auth_version'];
  }
  $_SESSION=[];session_regenerate_id(true);$_SESSION=['uid'=>$uid,'auth_version'=>$version,'csrf'=>bin2hex(random_bytes(32)),'born'=>time(),'last'=>time()];
  answer(['user'=>['id'=>$uid,'username'=>$username],'csrfToken'=>$_SESSION['csrf'],'recoveryCode'=>$recovery,'message'=>$action==='register'?($mailSent?'Cuenta creada. Te enviamos un correo para confirmar tu dirección.':'Cuenta creada. No pudimos enviar el correo; usa Reenviar confirmación desde tu cuenta.'):'Cuenta conectada.']);
 }
 if($action==='logout'){$_SESSION=[];session_regenerate_id(true);$_SESSION=['csrf'=>bin2hex(random_bytes(32)),'born'=>time(),'last'=>time()];answer(['user'=>null,'csrfToken'=>$_SESSION['csrf']]);}
 if(!$user)reject(401,'Inicia sesión para sincronizar tu progreso.');
 if($method==='GET'){
  $since=filter_var($_GET['since']??0,FILTER_VALIDATE_INT);if($since===false||$since<0)reject(400,'Versión no válida.');
  $db->beginTransaction();$q=$db->prepare('SELECT revision FROM companion_users WHERE id=?');$q->execute([$user['id']]);$version=(int)$q->fetchColumn();
  if($since>$version){$db->rollBack();reject(409,'Versión no válida.');}
  $q=$db->prepare('SELECT entry_key,value_json,deleted,revision FROM companion_progress WHERE user_id=? AND revision>? ORDER BY revision,entry_key');$q->execute([$user['id'],$since]);$rows=$q->fetchAll(PDO::FETCH_ASSOC);$db->commit();
  answer(['version'=>$version,'changes'=>array_map(fn($x)=>['key'=>$x['entry_key'],'value'=>$x['deleted']?null:json_decode($x['value_json'],true),'deleted'=>(bool)$x['deleted'],'revision'=>(int)$x['revision']],$rows)]);
 }
 $ops=$input['changes']??null;if(!is_array($ops)||!array_is_list($ops)||count($ops)<1||count($ops)>100)reject(400,'Envía entre 1 y 100 cambios.');
 foreach($ops as $op){
  $key=$op['key']??'';if(!is_string($key)||strlen($key)>190||!preg_match('/^(visited:\d{1,6}:\d{1,12}|task:[A-Za-z0-9_=:-]{1,180}|ref:(mine|target)|reading:[a-f0-9-]{32,36}|note:\d{1,6}:[a-f0-9-]{32,36}|route:\d{1,6}|farm:\d{1,6}:\d{1,12}|build:(Gladiator|Templar|Assassin|Ranger|Sorcerer|Elementalist|Cleric|Chanter|Fighter)(?::(?:la|eu|naw|nae|as):\d{4,5}:[A-Za-z0-9_=-]{10,120})?|pref:(mode|map))$/D',$key))reject(400,'Clave de progreso no válida.');
  if(($op['deleted']??false)===true)continue;
  $value=$op['value']??null;$valid=false;
  if(str_starts_with($key,'visited:')||str_starts_with($key,'task:'))$valid=is_bool($value);
  elseif(str_starts_with($key,'ref:'))$valid=is_array($value)&&in_array($value['region']??'',['la','eu','naw','nae','as'],true)&&preg_match('/^\d{4,5}$/',(string)($value['serverId']??''))&&preg_match('/^[A-Za-z0-9_=-]{10,120}$/',(string)($value['characterId']??''));
  elseif(str_starts_with($key,'reading:'))$valid=is_array($value)&&strlen(json_encode($value))<1000&&is_numeric($value['power']??null)&&is_numeric($value['itemLevel']??null)&&$value['power']>=0&&$value['power']<1e9&&strlen((string)($value['name']??''))<150;
  elseif(str_starts_with($key,'note:'))$valid=is_array($value)&&strlen((string)($value['text']??''))<=2000&&strlen((string)($value['name']??''))<=120&&is_numeric($value['lat']??null)&&is_numeric($value['lng']??null)&&$value['lat']>=0&&$value['lat']<=4096&&$value['lng']>=0&&$value['lng']<=4096;
  elseif(str_starts_with($key,'route:')){$points=$value['points']??null;$valid=is_array($points)&&array_is_list($points)&&count($points)<=100;foreach($points??[] as $p)if(!is_array($p)||count($p)!==2||!is_numeric($p[0])||!is_numeric($p[1])||min($p)<0||max($p)>4096)$valid=false;}
  elseif(str_starts_with($key,'farm:'))$valid=is_array($value)&&is_string($value['name']??null)&&strlen($value['name'])<=180&&is_int($value['goal']??null)&&$value['goal']>=1&&$value['goal']<=9999&&is_int($value['killed']??null)&&$value['killed']>=0&&$value['killed']<=9999;
  elseif(str_starts_with($key,'build:')){
   $valid=is_array($value)&&strlen(json_encode($value))<=12000&&is_int($value['level']??null)&&$value['level']>=1&&$value['level']<=45;
   foreach(['extra','stigmaExtra'] as $field)if(!is_int($value[$field]??null)||$value[$field]<0||$value[$field]>1000)$valid=false;
   $levels=$value['levels']??null;if(!is_array($levels)||count($levels)>64)$valid=false;
   foreach(is_array($levels)?$levels:[] as $skill=>$level)if(!preg_match('/^\d{8}$/',(string)$skill)||!is_int($level)||$level<0||$level>40)$valid=false;
   $bar=$value['bar']??null;if(!is_array($bar)||!array_is_list($bar)||count($bar)!==12)$valid=false;
   foreach(is_array($bar)?$bar:[] as $skill)if($skill!==null&&(!is_int($skill)||$skill<10000000||$skill>99999999))$valid=false;
  }
  elseif($key==='pref:mode')$valid=in_array($value,['pve','pvp'],true);
  elseif($key==='pref:map')$valid=is_string($value)&&preg_match('/^\d{1,6}$/',$value);
  if(!$valid)reject(400,'Valor de progreso no válido.');
 }
 $db->beginTransaction();$q=$db->prepare('SELECT revision,auth_version FROM companion_users WHERE id=? FOR UPDATE');$q->execute([$user['id']]);$locked=$q->fetch(PDO::FETCH_ASSOC);if((int)$locked['auth_version']!==$_SESSION['auth_version']){$db->rollBack();reject(401,'Sesión caducada.');}
 $q=$db->prepare('SELECT COUNT(*) FROM companion_progress WHERE user_id=?');$q->execute([$user['id']]);if((int)$q->fetchColumn()+count($ops)>100000){$db->rollBack();reject(409,'Has alcanzado el límite de progreso de la cuenta.');}
 $revision=(int)$locked['revision']+1;$q=$db->prepare('INSERT INTO companion_progress (user_id,entry_key,value_json,deleted,revision) VALUES (?,?,?,?,?) ON DUPLICATE KEY UPDATE value_json=VALUES(value_json),deleted=VALUES(deleted),revision=VALUES(revision),updated_at=CURRENT_TIMESTAMP');
 foreach($ops as $op){$deleted=($op['deleted']??false)===true;$q->execute([$user['id'],$op['key'],$deleted?null:json_encode($op['value'],JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR),(int)$deleted,$revision]);}
 $q=$db->prepare('UPDATE companion_users SET revision=? WHERE id=?');$q->execute([$revision,$user['id']]);$db->commit();answer(['version'=>$revision]);
}catch(Throwable $e){if(isset($db)&&$db->inTransaction())$db->rollBack();error_log('AION account service: '.$e->getCode());reject(503,'No se pudo completar la operación. Inténtalo de nuevo.');}
