<?php
declare(strict_types=1);
if(!defined('DAEVA_ACCOUNT_CONTEXT')){http_response_code(404);exit;}
function accountEmail(string $value):string {
 $email=strtolower(trim($value));
 if(strlen($email)>254||!filter_var($email,FILTER_VALIDATE_EMAIL)||preg_match('/[\r\n]/',$email))reject(400,'Introduce un correo válido.');
 return $email;
}
function accountMail(string $to,string $subject,string $message):bool {
 global $private;
 try{$mail=json_decode(file_get_contents($private.'/mail-config.json'),true,512,JSON_THROW_ON_ERROR);}catch(Throwable){return false;}
 $from=$mail['from']??'';if(!filter_var($from,FILTER_VALIDATE_EMAIL)||!str_ends_with($from,'@governanceai.me'))return false;
 $headers=['From'=>'Daeva Companion <'.$from.'>','Reply-To'=>$from,'MIME-Version'=>'1.0','Content-Type'=>'text/plain; charset=UTF-8','Content-Transfer-Encoding'=>'8bit','X-Auto-Response-Suppress'=>'All'];
 return mail($to,'=?UTF-8?B?'.base64_encode($subject).'?=',$message,$headers,'-f'.$from);
}
function accountToken(PDO $db,string $uid,string $email,string $kind):bool {
 global $config;
 $token=bin2hex(random_bytes(32));$ttl=$kind==='reset'?1800:86400;
 $q=$db->prepare('INSERT INTO companion_email_tokens (token_hash,user_id,email,kind,expires_at) VALUES (?,?,?,?,?)');$q->execute([hash('sha256',$token),$uid,$email,$kind,gmdate('Y-m-d H:i:s',time()+$ttl)]);
 $url=$config['origin'].'/#'.($kind==='reset'?'reset-password':'verify-email').'/'.$token;
 $message=$kind==='reset'?"Recibimos una solicitud para restablecer tu contraseña.\n\nAbre este enlace para elegir una nueva contraseña:\n$url\n\nEl enlace caduca en 30 minutos y solo se puede utilizar una vez. Si no lo solicitaste, ignora este correo. Tu contraseña sigue igual.":"Bienvenido a Daeva Companion.\n\nConfirma tu correo para proteger tu cuenta:\n$url\n\nEl enlace caduca en 24 horas y solo se puede utilizar una vez. Después podrás guardar tus rutas, builds y metas. Si no creaste esta cuenta, ignora este correo.";
 $sent=accountMail($email,$kind==='reset'?'Recupera tu cuenta de Daeva Companion':'Confirma tu correo en Daeva Companion',$message."\n\nDaeva Companion\n".$config['origin']."\nProyecto de fans de AION 2.");
 if(!$sent){$q=$db->prepare('DELETE FROM companion_email_tokens WHERE token_hash=?');$q->execute([hash('sha256',$token)]);error_log('Daeva account mail: delivery submission failed');}
 return $sent;
}
if($action==='request-reset'){
 $email=accountEmail((string)($input['email']??''));limit('email-reset',5,900);limit('email-reset-recipient',3,3600,$email);
 $q=$db->prepare('SELECT id,email FROM companion_users WHERE email=?');$q->execute([$email]);$found=$q->fetch(PDO::FETCH_ASSOC);
 if($found)accountToken($db,$found['id'],$found['email'],'reset');
 answer(['message'=>'Si ese correo tiene una cuenta, recibirás un enlace para restablecer la contraseña. Revisa también la carpeta de spam.']);
}
if($action==='resend-verification'){
 if(!$user)reject(401,'Inicia sesión para confirmar tu correo.');if(!$user['email'])reject(400,'Añade primero tu correo.');
 limit('email-verify',3,3600,$user['id']);if(!$user['email_verified_at']&&!accountToken($db,$user['id'],$user['email'],'verify'))reject(503,'No pudimos enviar el correo. Inténtalo más tarde.');
 answer(['message'=>$user['email_verified_at']?'Tu correo ya está confirmado.':'Te enviamos un nuevo enlace de confirmación.']);
}
if($action==='set-email'){
 if(!$user)reject(401,'Inicia sesión para añadir tu correo.');limit('email-change',3,3600,$user['id']);
 $email=accountEmail((string)($input['email']??''));$q=$db->prepare('SELECT password_hash FROM companion_users WHERE id=?');$q->execute([$user['id']]);
 if(!password_verify((string)($input['password']??''),(string)$q->fetchColumn()))reject(401,'La contraseña no es válida.');
 $q=$db->prepare('SELECT id FROM companion_users WHERE email=? AND id<>?');$q->execute([$email,$user['id']]);if($q->fetchColumn())reject(409,'Ese correo no está disponible.');
 $q=$db->prepare('UPDATE companion_users SET email=?,email_verified_at=NULL WHERE id=?');$q->execute([$email,$user['id']]);
 if(!accountToken($db,$user['id'],$email,'verify'))reject(503,'Correo guardado. No pudimos enviar la confirmación; usa Reenviar correo.');answer(['message'=>'Correo guardado. Confírmalo desde el enlace que te enviamos.']);
}
if(in_array($action,['verify-email','reset-password'],true)){
 limit('email-token',12,900);$raw=(string)($input['token']??'');if(!preg_match('/^[a-f0-9]{64}$/D',$raw))reject(400,'Enlace no válido. Solicita uno nuevo.');
 $password=(string)($input['password']??'');if($action==='reset-password'&&(strlen($password)<10||strlen($password)>128))reject(400,'Usa una contraseña de 10–128 caracteres.');
 $db->beginTransaction();$q=$db->prepare('SELECT * FROM companion_email_tokens WHERE token_hash=? FOR UPDATE');$q->execute([hash('sha256',$raw)]);$token=$q->fetch(PDO::FETCH_ASSOC);$kind=$action==='reset-password'?'reset':'verify';
 if(!$token||$token['kind']!==$kind||$token['used_at']||strtotime($token['expires_at'].' UTC')<=time()){$db->rollBack();reject(400,'El enlace caducó o ya fue utilizado. Solicita uno nuevo.');}
 $q=$db->prepare('SELECT id,email FROM companion_users WHERE id=? FOR UPDATE');$q->execute([$token['user_id']]);$owner=$q->fetch(PDO::FETCH_ASSOC);
 if(!$owner||$owner['email']!==$token['email']){$db->rollBack();reject(400,'El correo de la cuenta cambió. Solicita un enlace nuevo.');}
 if($kind==='reset'){$q=$db->prepare('UPDATE companion_users SET password_hash=?,auth_version=auth_version+1,recovery_hash=?,email_verified_at=COALESCE(email_verified_at,UTC_TIMESTAMP()) WHERE id=?');$q->execute([password_hash($password,defined('PASSWORD_ARGON2ID')?PASSWORD_ARGON2ID:PASSWORD_DEFAULT),hash('sha256',random_bytes(32)),$owner['id']]);$q=$db->prepare("UPDATE companion_email_tokens SET used_at=UTC_TIMESTAMP() WHERE user_id=? AND kind='reset' AND used_at IS NULL");$q->execute([$owner['id']]);}
 else{$q=$db->prepare('UPDATE companion_users SET email_verified_at=COALESCE(email_verified_at,UTC_TIMESTAMP()) WHERE id=?');$q->execute([$owner['id']]);}
 $q=$db->prepare('UPDATE companion_email_tokens SET used_at=UTC_TIMESTAMP() WHERE token_hash=?');$q->execute([hash('sha256',$raw)]);$db->commit();
 answer(['message'=>$kind==='reset'?'Contraseña actualizada. Inicia sesión con tu nueva contraseña.':'Correo confirmado. Tu cuenta ya puede recuperarse por correo.']);
}
