<?php
declare(strict_types=1);
header('Content-Type: application/json; charset=utf-8');
header('X-Content-Type-Options: nosniff');
header('Cache-Control: no-store');
function fail(int $status,string $message): never { http_response_code($status); echo json_encode(['error'=>$message],JSON_UNESCAPED_UNICODE); exit; }
if($_SERVER['REQUEST_METHOD']!=='GET') fail(405,'Método no permitido.');
$private=getenv('AION_PRIVATE_DIR')?:dirname(__DIR__,2).'/aion2-private';
if(!is_dir($private)&&!mkdir($private,0700,true)) fail(503,'Caché no disponible.');
// All cache and request counters live outside the document root.
$cache=$private.'/cache'; if(!is_dir($cache)) mkdir($cache,0700,true);
$ip=hash('sha256',($_SERVER['REMOTE_ADDR']??'unknown').':'.date('YmdHi'));
$file=fopen($cache.'/rate-'.$ip,'c+');if(!$file)fail(503,'Servicio temporalmente no disponible.');flock($file,LOCK_EX);$count=(int)stream_get_contents($file);if($count>=40){flock($file,LOCK_UN);fclose($file);header('Retry-After: 60');fail(429,'Demasiadas consultas. Espera un minuto.');}rewind($file);ftruncate($file,0);fwrite($file,(string)($count+1));flock($file,LOCK_UN);fclose($file);
if(random_int(1,100)===1)foreach(glob($cache.'/rate-*')?:[] as $old)if(filemtime($old)<time()-180)@unlink($old);
$region=(string)($_GET['region']??'la');if(!in_array($region,['la','eu','naw','nae','as'],true))fail(400,'Región no válida.');
$type=(string)($_GET['type']??'');if(!in_array($type,['character','profile','search','servers','item','classes','pcdata','board'],true))fail(400,'Consulta no válida.');
$server=(string)($_GET['serverId']??'');$id=(string)($_GET['characterId']??'');
if(in_array($type,['character','profile','item','board'],true)&&(!preg_match('/^\d{4,5}$/',$server)||!preg_match('/^[A-Za-z0-9_=-]{10,120}$/',$id)))fail(400,'Identificador de personaje no válido.');
$redis=null;try{if(class_exists('Redis')&&getenv('AION_REDIS_HOST')){$redis=new Redis();$redis->connect(getenv('AION_REDIS_HOST'),(int)(getenv('AION_REDIS_PORT')?:6379),.2);if(getenv('AION_REDIS_PASSWORD'))$redis->auth(getenv('AION_REDIS_PASSWORD'));}}catch(Throwable){$redis=null;}
function upstream(string $host,string $path,array $params,int $ttl=240):array{
 global $cache,$redis;
 if(!in_array($host,['aion2.plaync.com','api-search.plaync.com'],true))throw new RuntimeException('Origen no permitido');
 $url='https://'.$host.$path.'?'.http_build_query($params);$key='aion2:'.hash('sha256',$url);$filename=$cache.'/'.substr($key,6).'.json';$cached=null;
 try{if($redis){$raw=$redis->get($key);if($raw)$cached=json_decode($raw,true,512,JSON_THROW_ON_ERROR);}}catch(Throwable){}
 if(!$cached&&is_file($filename))$cached=json_decode(file_get_contents($filename),true);
 if($cached&&($cached['at']??0)>time()-$ttl)return $cached;
 $lock=fopen($filename.'.lock','c');if($lock)flock($lock,LOCK_EX);
 try{
  if(is_file($filename)){$recent=json_decode(file_get_contents($filename),true);if($recent&&($recent['at']??0)>time()-$ttl)return $recent;}
  $curl=curl_init($url);curl_setopt_array($curl,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_FOLLOWLOCATION=>false,CURLOPT_CONNECTTIMEOUT=>6,CURLOPT_TIMEOUT=>15,CURLOPT_USERAGENT=>'DaevaCompanion/1.0 (+https://governanceai.me)',CURLOPT_HTTPHEADER=>['Accept: application/json'],CURLOPT_SSL_VERIFYPEER=>true,CURLOPT_SSL_VERIFYHOST=>2]);$body=curl_exec($curl);$status=curl_getinfo($curl,CURLINFO_RESPONSE_CODE);curl_close($curl);
  if($status!==200||!is_string($body))throw new RuntimeException('NCSOFT no respondió a esta consulta.');
  $data=json_decode($body,true,512,JSON_THROW_ON_ERROR);if(!is_array($data))throw new RuntimeException('Respuesta no válida.');$entry=['at'=>time(),'data'=>$data];$encoded=json_encode($entry,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);$tmp=$filename.'.'.bin2hex(random_bytes(5)).'.tmp';file_put_contents($tmp,$encoded,LOCK_EX);chmod($tmp,0600);rename($tmp,$filename);try{if($redis)$redis->setex($key,$ttl,$encoded);}catch(Throwable){}return $entry;
 }finally{if($lock){flock($lock,LOCK_UN);fclose($lock);}}
}
function numeric(string $key,int $min,int $max,int $default=0):int{$value=filter_var($_GET[$key]??$default,FILTER_VALIDATE_INT);if($value===false||$value<$min||$value>$max)fail(400,'Parámetro numérico no válido.');return $value;}
try{
 $params=['region'=>$region,'lang'=>'es-ES','serverId'=>$server,'characterId'=>$id];
 switch($type){
  case 'search':$keyword=trim((string)($_GET['keyword']??''));if(mb_strlen($keyword)<1||mb_strlen($keyword)>50)fail(400,'El nombre debe tener entre 1 y 50 caracteres.');if($server!==''&&!preg_match('/^\d{4,5}$/',$server))fail(400,'Servidor no válido.');$p=['keyword'=>$keyword,'region'=>$region,'localeInfo'=>'es-ES','page'=>numeric('page',1,50,1),'size'=>20];if($server!=='')$p['serverId']=$server;$result=upstream('api-search.plaync.com','/aion2global/search/v2/character',$p,120)['data'];break;
  case 'servers':case 'classes':case 'pcdata':$result=upstream('aion2.plaync.com','/es-es/api/gameinfo/'.$type,['region'=>$region,'lang'=>'es-ES'],86400)['data'];break;
  case 'profile':case 'character':$info=upstream('aion2.plaync.com','/api/character/info',$params);if(empty($info['data']['profile']))throw new RuntimeException('Personaje no disponible.');$equipment=upstream('aion2.plaync.com','/api/character/equipment',$params);$boards=[];$timestamps=[$info['at'],$equipment['at']];foreach($type==='character'?($info['data']['daevanion']['boardList']??[]):[] as $board){$entry=upstream('aion2.plaync.com','/api/character/daevanion/detail',$params+['boardId'=>$board['id']]);$boards[(string)$board['id']]=$entry['data'];$timestamps[]=$entry['at'];}$items=[];foreach($type==='character'?($equipment['data']['equipment']['equipmentList']??[]):[] as $piece){if((int)$piece['slotPos']!==1)continue;try{$detail=upstream('aion2.plaync.com','/api/character/equipment/item',$params+['id'=>$piece['id'],'enchantLevel'=>$piece['enchantLevel'],'exceedLevel'=>$piece['exceedLevel'],'slotPos'=>$piece['slotPos']],600);$items[(string)$piece['slotPos']]=$detail['data'];$timestamps[]=$detail['at'];}catch(Throwable){}}$result=['ref'=>['region'=>$region,'serverId'=>(int)$server,'characterId'=>$id],'info'=>$info['data'],'equipment'=>$equipment['data'],'boards'=>$boards,'items'=>(object)$items,'fetchedAt'=>gmdate('c',min($timestamps))];break;
  case 'item':$result=upstream('aion2.plaync.com','/api/character/equipment/item',$params+['id'=>numeric('id',1,2147483647),'enchantLevel'=>numeric('enchantLevel',0,30),'exceedLevel'=>numeric('exceedLevel',0,20),'slotPos'=>numeric('slotPos',1,100)],600)['data'];break;
  
  case 'board':$result=upstream('aion2.plaync.com','/api/character/daevanion/detail',$params+['boardId'=>numeric('boardId',1,100)],240)['data'];break;
 }
 echo json_encode($result,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);
}catch(Throwable $e){fail(502,'NCSOFT no está disponible para esta consulta. Reintenta más tarde.');}
