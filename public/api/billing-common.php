<?php
declare(strict_types=1);
if(!defined('DAEVA_BILLING_CONTEXT')&&!defined('DAEVA_ADMIN_CONTEXT')&&!defined('DAEVA_COMPANION_CONTEXT')&&!defined('DAEVA_OPERATIONS_CONTEXT')&&!defined('DAEVA_ACCOUNT_CONTEXT')){http_response_code(404);exit;}
const BILLING_PRICE=5990;
function billingMonthEnd(int $start):string {
 $d=(new DateTimeImmutable('@'.$start))->setTimezone(new DateTimeZone('UTC'));$next=$d->modify('first day of next month');$day=min((int)$d->format('d'),(int)$next->format('t'));return $next->setDate((int)$next->format('Y'),(int)$next->format('m'),$day)->setTime((int)$d->format('H'),(int)$d->format('i'),(int)$d->format('s'))->format('Y-m-d H:i:s');
}
function billingConfig():array {global $private;return json_decode(@file_get_contents($private.'/billing-config.json')?:'{}',true)?:[];}
function billingEnabled():bool {$c=billingConfig();return !empty($c['accessToken'])&&!empty($c['collectorId'])&&!empty($c['webhookToken']);}
function billingRef(mixed $c):array {
 if(!is_array($c)||!in_array($c['region']??null,['la','eu','nae','naw','as'],true)||!preg_match('/^\d{4,5}$/D',(string)($c['serverId']??''))||!preg_match('/^[A-Za-z0-9_=-]{10,120}$/D',(string)($c['characterId']??'')))throw new InvalidArgumentException('Personaje no válido.');
 return ['region'=>$c['region'],'serverId'=>(int)$c['serverId'],'characterId'=>$c['characterId']];
}
function billingKey(array $c):string {return hash('sha256',json_encode(billingRef($c)));}
function billingOwned(string $uid,array $c):bool {
 global $private;$l=json_decode(@file_get_contents($private.'/companion/link-'.billingKey($c).'.json')?:'null',true);
 return is_array($l)&&($l['userId']??null)===$uid&&!empty($l['verifiedAt'])&&billingRef($l['character'])===billingRef($c);
}
function billingEligibility(PDO $db,string $uid,array $c):string {
 $q=$db->prepare('SELECT status,email_verified_at FROM companion_users WHERE id=?');$q->execute([$uid]);$u=$q->fetch(PDO::FETCH_ASSOC);
 if(!$u||$u['status']!=='active')return 'account_inactive';if(!$u['email_verified_at'])return 'email_unverified';
 return billingOwned($uid,$c)?'eligible':'character_unverified';
}
function billingLicense(PDO $db,string $uid,?array $c=null):array {
 $reason='character_required';$l=null;$expiry=null;$active=false;$source=null;
 if($c){$c=billingRef($c);$reason=billingEligibility($db,$uid,$c);$q=$db->prepare('SELECT * FROM companion_licenses WHERE user_id=? AND character_key=?');$q->execute([$uid,billingKey($c)]);$l=$q->fetch(PDO::FETCH_ASSOC);
  if($reason==='eligible'){
   $reason=$l&&$l['status']==='revoked'?'license_revoked':'no_active_license';
   if($l&&$l['status']==='active'){
    $q=$db->prepare("SELECT MAX(o.period_end) until_at,SUM(p.status='approved') paid FROM companion_billing_orders o LEFT JOIN companion_billing_payments p ON p.id=o.payment_id WHERE o.license_id=? AND (p.status='approved' OR o.status='discount_grant') AND o.period_start<=UTC_TIMESTAMP() AND o.period_end>UTC_TIMESTAMP()");$q->execute([$l['id']]);$period=$q->fetch(PDO::FETCH_ASSOC);$paid=$period['until_at'];
    $courtesy=$l['courtesy_until']&&strtotime($l['courtesy_until'].' UTC')>time()?$l['courtesy_until']:null;
    $expiry=$paid&&$courtesy?max($paid,$courtesy):($paid?:$courtesy);$source=$paid?((int)$period['paid']>0?'mercado_pago':'discount'):($courtesy?'admin-grant':null);$active=(bool)$expiry;if($active)$reason='active';
   }
  }
 }
 return ['licenseId'=>$l['id']??null,'accountId'=>$uid,'character'=>$c,'plan'=>$active?'plus':'free','planName'=>$active?'Companion Plus':'Daeva gratuito','status'=>$active?'active':'inactive','reason'=>$reason,'source'=>$source,'expiresAt'=>$expiry?str_replace(' ','T',$expiry).'Z':null,'billingEnabled'=>billingEnabled(),'serverTime'=>gmdate('c'),'recheckAfterSeconds'=>60,'offlineGraceSeconds'=>0,'features'=>['builds'=>true,'worldMap'=>true,'events'=>true,'companionPvp'=>$active,'companionMinimap'=>$active]];
}
function billingLicenses(PDO $db,string $uid):array {$q=$db->prepare('SELECT character_json FROM companion_licenses WHERE user_id=? ORDER BY created_at DESC LIMIT 100');$q->execute([$uid]);return array_map(fn($r)=>billingLicense($db,$uid,json_decode($r['character_json'],true)),$q->fetchAll(PDO::FETCH_ASSOC));}
function billingActiveAccountSql():string {
 global $private;$keys=[];foreach(glob($private.'/companion/link-*.json')?:[] as $p){$l=json_decode(file_get_contents($p),true);if(!is_array($l)||!preg_match('/^[a-f0-9]{32}$/D',(string)($l['userId']??''))||empty($l['verifiedAt']))continue;try{$keys[]="'".$l['userId'].':'.billingKey($l['character'])."'";}catch(Throwable){}}
 $owned=$keys?'CONCAT(l.user_id,\':\',l.character_key) IN ('.implode(',',$keys).')':'0=1';return "u.status='active' AND u.email_verified_at IS NOT NULL AND EXISTS (SELECT 1 FROM companion_licenses l WHERE l.user_id=u.id AND l.status='active' AND ".$owned." AND (l.courtesy_until>UTC_TIMESTAMP() OR EXISTS (SELECT 1 FROM companion_billing_orders bo LEFT JOIN companion_billing_payments bp ON bp.id=bo.payment_id WHERE bo.license_id=l.id AND (bp.status='approved' OR bo.status='discount_grant') AND bo.period_start<=UTC_TIMESTAMP() AND bo.period_end>UTC_TIMESTAMP())))";
}
function billingAccountSummary(PDO $db,string $uid):array {
 $licenses=billingLicenses($db,$uid);$active=array_values(array_filter($licenses,fn($l)=>$l['status']==='active'));$q=$db->prepare('SELECT COUNT(*),SUM(status=\'revoked\') FROM companion_licenses WHERE user_id=?');$q->execute([$uid]);$count=$q->fetch(PDO::FETCH_NUM);$status=$active?'active':((int)$count[0]===0?'free':((int)$count[1]===(int)$count[0]?'cancelled':'expired'));
 $ends=array_filter(array_column($active,'expiresAt'));return ['plan'=>$active?'plus':'free','planName'=>$active?count($active).' licencia(s) Companion Plus activa(s)':'Sin licencia Plus activa','status'=>$status,'expiresAt'=>$ends?max($ends):null,'note'=>'Licencias individuales; consulta el personaje para autorizar funciones.','licenses'=>$licenses,'features'=>['companionPvp'=>false,'companionMinimap'=>false],'billingEnabled'=>billingEnabled()];
}
function billingEnsureLicense(PDO $db,string $uid,array $c):string {
 $q=$db->prepare("INSERT IGNORE INTO companion_licenses(id,user_id,character_key,character_json,status,created_at) VALUES (?,?,?,?,'active',UTC_TIMESTAMP())");$q->execute([bin2hex(random_bytes(16)),$uid,billingKey($c),json_encode($c)]);
 $q=$db->prepare('SELECT id FROM companion_licenses WHERE user_id=? AND character_key=?');$q->execute([$uid,billingKey($c)]);return (string)$q->fetchColumn();
}
function billingRequest(string $method,string $path,?array $data=null,?string $idempotency=null):array {
 $cfg=billingConfig();if(!billingEnabled())throw new RuntimeException('Pasarela no configurada.');
 if(!preg_match('~^/(?:users/me|checkout/preferences(?:/[\w-]+)?|v1/payments(?:/search|/\d+)?)$~D',explode('?',$path)[0]))throw new RuntimeException('Ruta de pasarela no admitida.');
 $ch=curl_init('https://api.mercadopago.com'.$path);$headers=['Authorization: Bearer '.$cfg['accessToken'],'Content-Type: application/json'];if($idempotency)$headers[]='X-Idempotency-Key: '.$idempotency;
 curl_setopt_array($ch,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_CONNECTTIMEOUT=>5,CURLOPT_TIMEOUT=>20,CURLOPT_CUSTOMREQUEST=>$method,CURLOPT_HTTPHEADER=>$headers]);if($data!==null)curl_setopt($ch,CURLOPT_POSTFIELDS,json_encode($data,JSON_THROW_ON_ERROR));$raw=curl_exec($ch);$status=(int)curl_getinfo($ch,CURLINFO_RESPONSE_CODE);curl_close($ch);
 if($raw===false||$status<200||$status>=300)throw new RuntimeException('Mercado Pago no disponible (HTTP '.$status.').');$x=json_decode($raw,true,32,JSON_THROW_ON_ERROR);if(!is_array($x))throw new RuntimeException('Respuesta no válida.');return $x;
}
function billingPrice():array {
 global $private;$x=json_decode(@file_get_contents($private.'/billing-fx.json')?:'{}',true);$rate=(float)($x['clpPerUsd']??0);$valid=$rate>100&&$rate<5000&&time()-(int)($x['at']??0)<7*86400;
 return ['amount'=>BILLING_PRICE,'currency'=>'CLP','period'=>'month','renewal'=>'manual','usdApprox'=>$valid?round(BILLING_PRICE/$rate,2):null,'fxAsOf'=>$valid?gmdate('c',$x['at']):null,'fxSource'=>'https://www.exchangerate-api.com','message'=>'Un mes por personaje verificado. Renovación manual; sin cargos automáticos. El cobro se realiza en pesos chilenos.'];
}
function billingPayments(PDO $db,?string $uid=null,int $offset=0):array {
 $q=$db->prepare('SELECT p.id,p.order_id,p.status,p.amount,p.updated_at,u.username,o.character_json,o.period_start,o.period_end,o.discount_code FROM companion_billing_payments p JOIN companion_billing_orders o ON o.id=p.order_id JOIN companion_users u ON u.id=o.user_id'.($uid?' WHERE o.user_id=?':'').' ORDER BY p.updated_at DESC LIMIT 25 OFFSET '.max(0,$offset));$q->execute($uid?[$uid]:[]);$r=$q->fetchAll(PDO::FETCH_ASSOC);foreach($r as &$p){$p['amount']=(int)$p['amount'];$p['character']=json_decode($p['character_json'],true);unset($p['character_json']);}return $r;
}
// Pure validation is also used by offline QA. The only production caller obtains this object from MP's authenticated API.
function billingValidatePayment(array $p,array $o,array $cfg):bool {
 if((string)($p['external_reference']??'')!=='daeva-'.$o['id']||(string)($p['collector_id']??'')!==(string)$cfg['collectorId']||($p['currency_id']??'')!=='CLP'||(float)($p['transaction_amount']??-1)!==(float)$o['amount']||($p['live_mode']??null)!==true)return false;
 $meta=$p['metadata']??[];return ($meta['product']??'')==='fullaion2companion'&&($meta['order_id']??'')===$o['id']&&($meta['character_key']??'')===$o['character_key'];
}
function billingApplyPayment(PDO $db,array $p):array {
 $id=(string)($p['id']??'');$ref=(string)($p['external_reference']??'');if(!ctype_digit($id)||!preg_match('/^daeva-([a-f0-9]{32})$/D',$ref,$m))return ['ignored'=>true];
 $db->beginTransaction();try{
  $q=$db->prepare('SELECT * FROM companion_billing_orders WHERE id=? FOR UPDATE');$q->execute([$m[1]]);$o=$q->fetch(PDO::FETCH_ASSOC);if(!$o||!billingValidatePayment($p,$o,billingConfig())){$db->rollBack();return ['ignored'=>true];}
  $status=(string)($p['status']??'unknown');$q=$db->prepare('SELECT order_id,status FROM companion_billing_payments WHERE id=?');$q->execute([$id]);$old=$q->fetch(PDO::FETCH_ASSOC);if($old&&$old['order_id']!==$o['id'])throw new RuntimeException('Pago vinculado a otra orden.');
  if($old&&$old['status']==='charged_back')$status='charged_back';elseif($old&&$old['status']==='refunded'&&$status!=='charged_back')$status='refunded';
  // Any partial refund also suspends the paid period. No client-provided return parameters are trusted.
  if($status==='approved'&&(float)($p['transaction_amount_refunded']??0)>0)$status='refunded';
  $q=$db->prepare('INSERT INTO companion_billing_payments(id,order_id,status,amount,updated_at) VALUES (?,?,?,?,UTC_TIMESTAMP()) ON DUPLICATE KEY UPDATE status=VALUES(status),updated_at=UTC_TIMESTAMP()');$q->execute([$id,$o['id'],$status,$o['amount']]);
  if($status==='approved'&&!$o['payment_id']){
   $date=strtotime((string)($p['date_approved']??''));if(!$date||$date>time()+300||$date<strtotime($o['created_at'].' UTC')-300||$date>strtotime($o['expires_at'].' UTC')){$db->prepare("UPDATE companion_billing_orders SET status='review' WHERE id=?")->execute([$o['id']]);$db->commit();return ['review'=>true];}
   if($o['discount_id']){$q=$db->prepare('SELECT max_uses,uses FROM companion_discounts WHERE id=? FOR UPDATE');$q->execute([$o['discount_id']]);$discount=$q->fetch(PDO::FETCH_ASSOC);if(!$discount||($discount['max_uses']!==null&&(int)$discount['uses']>=(int)$discount['max_uses'])){$db->prepare("UPDATE companion_billing_orders SET status='review' WHERE id=?")->execute([$o['id']]);$db->commit();return ['review'=>true];}}
   $q=$db->prepare('SELECT courtesy_until FROM companion_licenses WHERE id=? FOR UPDATE');$q->execute([$o['license_id']]);$courtesy=$q->fetchColumn();
   $q=$db->prepare("SELECT MAX(o.period_end) FROM companion_billing_orders o LEFT JOIN companion_billing_payments p ON p.id=o.payment_id WHERE o.license_id=? AND (p.status='approved' OR o.status='discount_grant')");$q->execute([$o['license_id']]);$end=$q->fetchColumn();$start=max($date,$end?strtotime($end.' UTC'):0,$courtesy?strtotime($courtesy.' UTC'):0);$until=billingMonthEnd($start);
   $q=$db->prepare("UPDATE companion_billing_orders SET status='approved',payment_id=?,period_start=?,period_end=? WHERE id=?");$q->execute([$id,gmdate('Y-m-d H:i:s',$start),$until,$o['id']]);
   if($o['discount_id'])$db->prepare('UPDATE companion_discounts SET uses=uses+1 WHERE id=?')->execute([$o['discount_id']]);
  }elseif($o['payment_id']===$id){$db->prepare('UPDATE companion_billing_orders SET status=? WHERE id=?')->execute([$status,$o['id']]);}
  elseif($status==='approved'){$db->prepare("UPDATE companion_billing_payments SET status='duplicate_review' WHERE id=?")->execute([$id]);}
  $db->commit();return ['processed'=>true,'status'=>$status];
 }catch(Throwable $e){if($db->inTransaction())$db->rollBack();throw $e;}
}
function billingReconcile(PDO $db,string $orderId):array {
 $q=$db->prepare('SELECT id,payment_id,amount,status FROM companion_billing_orders WHERE id=?');$q->execute([$orderId]);$o=$q->fetch(PDO::FETCH_ASSOC);if(!$o)throw new InvalidArgumentException('Orden no encontrada.');if((int)$o['amount']===0)return ['checked'=>true,'message'=>'Canje sin cobro. No hay un pago de Mercado Pago.'];
 if($o['payment_id']&&ctype_digit($o['payment_id']))return billingApplyPayment($db,billingRequest('GET','/v1/payments/'.$o['payment_id']));
 $x=billingRequest('GET','/v1/payments/search?'.http_build_query(['external_reference'=>'daeva-'.$orderId,'sort'=>'date_created','criteria'=>'desc','limit'=>20]));foreach($x['results']??[] as $p)billingApplyPayment($db,billingRequest('GET','/v1/payments/'.(string)$p['id']));if(empty($x['results'])&&in_array($o['status'],['creating','pending'],true))$db->prepare("UPDATE companion_billing_orders SET status='expired' WHERE id=? AND expires_at<UTC_TIMESTAMP() AND status IN ('creating','pending')")->execute([$orderId]);return ['checked'=>true];
}
