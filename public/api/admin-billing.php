<?php
declare(strict_types=1);
if(!defined('DAEVA_ADMIN_CONTEXT')){http_response_code(404);exit;}
if($action==='payments'){
 [$page,$offset]=adminPage();$count=(int)$db->query('SELECT COUNT(*) FROM companion_billing_payments')->fetchColumn();$q=$db->query('SELECT l.id,u.username,l.character_json,l.status,l.courtesy_until,l.created_at FROM companion_licenses l JOIN companion_users u ON u.id=l.user_id ORDER BY l.created_at DESC LIMIT 100');$licenses=$q->fetchAll(PDO::FETCH_ASSOC);foreach($licenses as &$l){$l['character']=json_decode($l['character_json'],true);unset($l['character_json']);}
 $orders=$db->query('SELECT o.id,u.username,o.amount,o.status,o.discount_code,o.created_at,o.expires_at FROM companion_billing_orders o JOIN companion_users u ON u.id=o.user_id ORDER BY o.created_at DESC LIMIT 50')->fetchAll(PDO::FETCH_ASSOC);
 answer(['payments'=>billingPayments($db,null,$offset),'orders'=>$orders,'licenses'=>$licenses,'billingEnabled'=>billingEnabled(),'price'=>billingPrice(),'total'=>$count,'page'=>$page,'pages'=>max(1,(int)ceil($count/25))]);
}
if($action==='billing-reconcile'){
 $reason=adminReason($body);$id=adminId($body['orderId']??null);limit('billing-reconcile',6,300,$actor['id']);$q=$db->prepare('SELECT user_id FROM companion_billing_orders WHERE id=?');$q->execute([$id]);$uid=$q->fetchColumn();if(!$uid)reject(404,'Orden no encontrada.');$result=billingReconcile($db,$id);$db->beginTransaction();opsAuditWrite('billing-reconcile',$uid,['orderId'=>$id,'reason'=>$reason]);$db->commit();answer(['success'=>true,'message'=>'Estado contrastado con Mercado Pago.','result'=>$result]);
}
if($action==='license-status'){
 $reason=adminReason($body);$id=adminId($body['licenseId']??null);$status=opsEnum($body['status']??null,['active','revoked']);$db->beginTransaction();$q=$db->prepare('SELECT user_id FROM companion_licenses WHERE id=? FOR UPDATE');$q->execute([$id]);$uid=$q->fetchColumn();if(!$uid){$db->rollBack();reject(404,'Licencia no encontrada.');}opsAuditWrite('license-status',$uid,['licenseId'=>$id,'status'=>$status,'reason'=>$reason]);$db->prepare('UPDATE companion_licenses SET status=? WHERE id=?')->execute([$status,$id]);$db->commit();answer(['success'=>true,'message'=>'Estado de licencia actualizado. Rehabilitar exige además pago vigente y personaje verificado.']);
}
