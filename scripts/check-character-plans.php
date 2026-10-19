<?php
declare(strict_types=1);
if(PHP_SAPI!=='cli')exit(1);
function reject(int $status,string $message):never{throw new RuntimeException($message,$status);}
$source=file_get_contents($argv[1]??__DIR__.'/../public/api/account.php');
$start=strpos($source," \$ops=\$input['changes']");
$end=strpos($source,' $db->beginTransaction();',$start);
if($start===false||$end===false)throw new RuntimeException('Validation block not found');
$block=substr($source,$start,$end-$start);
$plan=['level'=>45,'extra'=>17,'stigmaExtra'=>0,'levels'=>[],'bar'=>array_fill(0,12,null)];
$a='build:Gladiator:la:2401:H8WHJE_su6dZM2K913s8qowlE5DugO6YQwS_XG7yRJM=';
$b='build:Gladiator:la:2401:H8WHJE_su6dZM2K913s8qmTK7G_euqgtprPWR1ZWqLU=';
$cases=[
 'Two characters with separate keys'=>[[['key'=>$a,'value'=>$plan],['key'=>$b,'value'=>$plan]],true],
 'Legacy plan remains readable/writable'=>[[['key'=>'build:Gladiator','value'=>$plan]],true],
 'Another region and server'=>[[['key'=>str_replace('la:2401','eu:1401',$a),'value'=>$plan]],true],
 'Unknown class'=>[[['key'=>str_replace('Gladiator','Admin',$a),'value'=>$plan]],false],
 'Invalid server'=>[[['key'=>str_replace('2401','oops',$a),'value'=>$plan]],false],
 'Invalid region'=>[[['key'=>str_replace(':la:',':admin:',$a),'value'=>$plan]],false],
 'Client cannot grant Plus'=>[[['key'=>'license:plus','value'=>true]],false],
 'Malformed skill bar'=>[[['key'=>$a,'value'=>array_replace($plan,['bar'=>[42]])]],false],
 'Delete character plan'=>[[['key'=>$a,'deleted'=>true]],true],
];
$checks=[];foreach($cases as $name=>[$ops,$expected]){$input=['changes'=>$ops];$accepted=true;try{eval($block);}catch(RuntimeException $e){if($e->getCode()!==400)throw $e;$accepted=false;}if($accepted!==$expected)throw new RuntimeException('Failed: '.$name);$checks[]=$name;}
echo json_encode(['ok'=>true,'checks'=>count($checks),'cases'=>$checks],JSON_PRETTY_PRINT|JSON_UNESCAPED_UNICODE).PHP_EOL;
