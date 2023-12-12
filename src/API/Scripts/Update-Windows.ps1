$nid=$args[0]
$appPath=$args[1]

Wait-Process -Id $nid

$process = Start-Process -FilePath $appPath  -ArgumentList "/S /v/qn" -PassThru

Wait-Process -Id $process

exit $process.ExitCode
```