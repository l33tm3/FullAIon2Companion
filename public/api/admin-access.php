<?php
declare(strict_types=1);
if(!defined('DAEVA_ADMIN_CONTEXT')&&!defined('DAEVA_ACCOUNT_CONTEXT')&&!defined('DAEVA_COMPANION_CONTEXT')){http_response_code(404);exit;}
require_once __DIR__.'/billing-common.php';
function daevaSubscription(PDO $db,string $uid):array {
 // Legacy account-wide grants never authorize a character; desktop clients must query action=license.
 $s=billingLicense($db,$uid);$s['licenses']=billingLicenses($db,$uid);$s['price']=billingPrice();return $s;
}
