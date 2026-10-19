<?php
declare(strict_types=1);
if(!defined('DAEVA_COMPANION_CONTEXT')){http_response_code(404);exit;}
require_once __DIR__.'/admin-access.php';
function companionAccess():array {
 // Manual, expiring courtesy grants do not represent a payment or a subscription charge.
 global $db,$user;return daevaSubscription($db,$user['id']);
}
function companionChallenges(string $uid):array {
 global $root;$pending=[];
 foreach(glob($root.'/challenge-*.json')?:[] as $p){$c=json_decode(file_get_contents($p),true);if(($c['userId']??'')!==$uid||($c['status']??'')!=='pending'||($c['expires']??0)<time())continue;$pending[]=['challengeId'=>$c['id'],'character'=>$c['character'],'pattern'=>$c['pattern'],'nonce'=>$c['nonce'],'target'=>'training-dummy','status'=>'pending','createdAt'=>gmdate('c',$c['created']),'expiresAt'=>gmdate('c',$c['expires'])];}
 usort($pending,fn($a,$b)=>strcmp($b['createdAt'],$a['createdAt']));return array_slice($pending,0,3);
}
if($action==='license'){expectedMethod('POST');rate('license'.$device['id'],30,60);try{$ref=billingRef($body['character']??null);}catch(Throwable){stop(400,'Personaje no válido.');}reply(billingLicense($db,$user['id'],$ref)+['deviceId'=>$device['id']]);}
if($action==='account-overview'){
 expectedMethod('GET');$devices=[];$links=[];$challenges=[];
 foreach(glob($root.'/device-*.json')?:[] as $p){$entry=json_decode(file_get_contents($p),true);if(($entry['userId']??'')!==$user['id'])continue;$d=readRecord('token-'.$entry['hash']);if(!$d)continue;$active=!($d['revoked']??true)&&(int)($d['authVersion']??0)===(int)$user['auth_version'];$devices[]=['id'=>$d['id'],'name'=>$d['name'],'createdAt'=>$d['createdAt'],'lastSeen'=>$d['lastSeen'],'active'=>$active,'online'=>$active&&is_string($d['lastSeen'])&&strtotime($d['lastSeen'])>=time()-90];}
 foreach(glob($root.'/link-*.json')?:[] as $p){$l=json_decode(file_get_contents($p),true);if(($l['userId']??'')===$user['id'])$links[]=['character'=>$l['character'],'verifiedAt'=>$l['verifiedAt'],'deviceId'=>$l['deviceId'],'characterName'=>$l['approval']['characterName']??null,'method'=>$l['approval']['kind']??'capture'];}
 foreach(glob($root.'/challenge-*.json')?:[] as $p){$c=json_decode(file_get_contents($p),true);if(($c['userId']??'')!==$user['id'])continue;$challenges[]=['challengeId'=>$c['id'],'character'=>$c['character'],'status'=>$c['status']==='pending'&&$c['expires']<time()?'expired':$c['status'],'createdAt'=>gmdate('c',$c['created'])];}
 usort($challenges,fn($a,$b)=>strcmp($b['createdAt'],$a['createdAt']));
 reply(['devices'=>$devices,'linkedCharacters'=>$links,'challenges'=>array_slice($challenges,0,5),'titleChallenges'=>titleChallenges($user['id']),'characterLimit'=>CHARACTER_LIMIT,'verificationReady'=>!empty($cfg['captureVerifierPublicKey']),'subscription'=>companionAccess(),'payments'=>billingPayments($db,$user['id']),'serverTime'=>gmdate('c')]);
}
