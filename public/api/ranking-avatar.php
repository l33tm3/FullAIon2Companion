<?php
declare(strict_types=1);
header('X-Content-Type-Options: nosniff');
if(($_SERVER['REQUEST_METHOD']??'')!=='GET'){http_response_code(405);exit;}
$server=(string)($_GET['server']??'');$id=(string)($_GET['id']??'');if(!preg_match('/^\d{4,5}$/D',$server)||!preg_match('/^\d{5,25}$/D',$id)){http_response_code(400);exit;}
$dir=(getenv('AION_PRIVATE_DIR')?:dirname(__DIR__,2).'/aion2-private').'/cache';$path=$dir.'/portrait-'.$server.'-'.$id;
if(!is_file($path)||filemtime($path)<time()-604800){$h=fopen($path.'.lock','c');flock($h,LOCK_EX);if(!is_file($path)||filemtime($path)<time()-604800){
 $rate=fopen($dir.'/portrait-limit-'.hash('sha256',($_SERVER['REMOTE_ADDR']??'').date('YmdHi')),'c+');flock($rate,LOCK_EX);$count=(int)stream_get_contents($rate);if($count>=100){fclose($rate);http_response_code(429);header('Retry-After: 60');exit;}rewind($rate);ftruncate($rate,0);fwrite($rate,(string)($count+1));fclose($rate);
 $c=curl_init('https://aion2index.com/img/p/'.$server.'/'.$id.'?w=96');curl_setopt_array($c,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_FOLLOWLOCATION=>false,CURLOPT_CONNECTTIMEOUT=>5,CURLOPT_TIMEOUT=>12,CURLOPT_SSL_VERIFYPEER=>true]);$b=curl_exec($c);$status=curl_getinfo($c,CURLINFO_RESPONSE_CODE);curl_close($c);$info=is_string($b)?@getimagesizefromstring($b):false;if($status!==200||!$info||strlen($b)>500000||!in_array($info['mime'],['image/webp','image/png','image/jpeg'],true)){http_response_code(502);exit;}$tmp=$path.'.tmp';file_put_contents($tmp,$b,LOCK_EX);chmod($tmp,0600);rename($tmp,$path);
 }}
$b=file_get_contents($path);$info=getimagesizefromstring($b);header('Content-Type: '.$info['mime']);header('Cache-Control: public, max-age=86400');echo $b;
