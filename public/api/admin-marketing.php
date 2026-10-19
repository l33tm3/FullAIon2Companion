<?php
declare(strict_types=1);
if(!defined('DAEVA_ADMIN_CONTEXT')){http_response_code(404);exit;}
function opsMarketingRoute():void {
 global $action,$body,$db;
 if($action==='x-marketing'){
  $rows=$db->query('SELECT p.*, (SELECT COUNT(*) FROM companion_referral_visits v WHERE v.partner_id=p.id) visits,(SELECT COUNT(*) FROM companion_referrals r WHERE r.partner_id=p.id) registrations FROM companion_partners p ORDER BY p.created_at DESC')->fetchAll(PDO::FETCH_ASSOC);
  foreach($rows as &$p)$p['url']='https://governanceai.me/r/'.$p['slug'];
  answer(['partners'=>$rows,'discounts'=>$db->query('SELECT d.*,p.name partner_name FROM companion_discounts d LEFT JOIN companion_partners p ON p.id=d.partner_id ORDER BY d.code')->fetchAll(PDO::FETCH_ASSOC),'billingEnabled'=>billingEnabled(),'attribution'=>'Primer referido válido al crear la cuenta, hasta 30 días. Sin comisiones automáticas; los pagos se consultan en Pagos.']);
 }
 if(!in_array($action,['x-partner-save','x-discount-save'],true))return;
 $reason=adminReason($body);$id=isset($body['id'])?opsId($body['id']):bin2hex(random_bytes(16));$db->beginTransaction();opsAuditWrite($action,null,['reason'=>$reason,'id'=>$id,'code'=>$body['code']??null,'slug'=>$body['slug']??null,'percent'=>$body['percent']??null,'active'=>$body['active']??null]);
 if($action==='x-partner-save'){
  $name=opsText($body['name']??null,2,100);$slug=opsText($body['slug']??null,3,40);if(!preg_match('/^[a-z0-9][a-z0-9-]{2,39}$/D',$slug))reject(400,'Enlace: usa letras minúsculas, números o guiones.');$twitch=opsText($body['twitch']??'',0,40);if($twitch!==''&&!preg_match('/^[a-zA-Z0-9_]{3,25}$/D',$twitch))reject(400,'Canal Twitch no válido.');if(!is_bool($body['active']??null))reject(400,'Estado no válido.');
  $q=$db->prepare('SELECT slug FROM companion_partners WHERE id=?');$q->execute([$id]);$old=$q->fetchColumn();if($old&&$old!==$slug)reject(409,'El enlace de un streamer no se cambia para conservar sus referidos.');
  if(!$old){$q=$db->prepare('SELECT id FROM companion_partners WHERE slug=? FOR UPDATE');$q->execute([$slug]);if($q->fetchColumn())reject(409,'Ese enlace ya existe.');}
  $q=$db->prepare('INSERT INTO companion_partners(id,name,slug,twitch,active,created_at) VALUES (?,?,?,?,?,UTC_TIMESTAMP()) ON DUPLICATE KEY UPDATE name=IF(id=VALUES(id),VALUES(name),name),twitch=IF(id=VALUES(id),VALUES(twitch),twitch),active=IF(id=VALUES(id),VALUES(active),active)');
  try{$q->execute([$id,$name,$slug,$twitch,(int)$body['active']]);}catch(PDOException $e){if($e->getCode()==='23000')reject(409,'Ese enlace ya existe.');throw $e;}
 }
 else {
  $code=strtoupper(opsText($body['code']??null,3,40));if(!preg_match('/^[A-Z0-9][A-Z0-9_-]{2,39}$/D',$code))reject(400,'Código no válido.');$percent=filter_var($body['percent']??null,FILTER_VALIDATE_INT);if($percent===false||$percent<1||$percent>100)reject(400,'Descuento entre 1 y 100%.');$start=opsDate($body['startsAt']??null);$end=opsDate($body['endsAt']??null);if($end<=$start)reject(400,'Fecha final no válida.');$max=$body['maxUses']??null;if($max!==null&&(!is_int($max)||$max<1||$max>1000000))reject(400,'Límite de usos no válido.');$partner=$body['partnerId']??null;if($partner){$partner=opsId($partner);$q=$db->prepare('SELECT id FROM companion_partners WHERE id=?');$q->execute([$partner]);if(!$q->fetchColumn())reject(400,'Streamer no encontrado.');}else $partner=null;if(!is_bool($body['active']??null))reject(400,'Estado no válido.');$note=opsText($body['note']??'',0,500);
  $q=$db->prepare('SELECT code FROM companion_discounts WHERE id=?');$q->execute([$id]);$old=$q->fetchColumn();if($old&&$old!==$code)reject(409,'Un código existente no se renombra.');
  if(!$old){$q=$db->prepare('SELECT id FROM companion_discounts WHERE code=? FOR UPDATE');$q->execute([$code]);if($q->fetchColumn())reject(409,'Ese código ya existe.');}
  $q=$db->prepare('INSERT INTO companion_discounts(id,code,percent,active,starts_at,ends_at,max_uses,partner_id,note) VALUES (?,?,?,?,?,?,?,?,?) ON DUPLICATE KEY UPDATE percent=IF(id=VALUES(id),VALUES(percent),percent),active=IF(id=VALUES(id),VALUES(active),active),starts_at=IF(id=VALUES(id),VALUES(starts_at),starts_at),ends_at=IF(id=VALUES(id),VALUES(ends_at),ends_at),max_uses=IF(id=VALUES(id),VALUES(max_uses),max_uses),partner_id=IF(id=VALUES(id),VALUES(partner_id),partner_id),note=IF(id=VALUES(id),VALUES(note),note)');try{$q->execute([$id,$code,$percent,(int)$body['active'],$start,$end,$max,$partner,$note]);}catch(PDOException $e){if($e->getCode()==='23000')reject(409,'Ese código ya existe.');throw $e;}
 }
 $db->commit();answer(['id'=>$id,'message'=>'Guardado. El descuento se aplica al mes contratado; el canje se confirma con el pago.']);
}
