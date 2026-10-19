<?php
declare(strict_types=1);
// Internal helper. Attribution is only made at account creation, never during login.
if(!defined('DAEVA_ACCOUNT_CONTEXT')){http_response_code(404);exit;}
function daevaAttributeReferral(PDO $db,string $uid):void {
 $token=$_COOKIE['__Host-daeva_ref']??'';if(!is_string($token)||!preg_match('/^[a-f0-9]{64}$/D',$token))return;
 $q=$db->prepare('INSERT IGNORE INTO companion_referrals(user_id,partner_id,created_at) SELECT ?,v.partner_id,UTC_TIMESTAMP() FROM companion_referral_visits v JOIN companion_partners p ON p.id=v.partner_id WHERE v.token_hash=? AND v.expires_at>UTC_TIMESTAMP() AND p.active=1');$q->execute([$uid,hash('sha256',$token)]);
 setcookie('__Host-daeva_ref','',['expires'=>time()-3600,'path'=>'/','secure'=>true,'httponly'=>true,'samesite'=>'Lax']);
}
