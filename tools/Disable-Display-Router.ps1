param([switch]$RestoreMonitor)
$ErrorActionPreference='Stop'
$name='CodexProjectorDisplayRouter'
$installDir=Join-Path $env:ProgramData $name
$exe=Join-Path $installDir 'Projector-Display-Router.exe'
if(!(Test-Path -LiteralPath $exe)){$exe=Join-Path (Join-Path $env:LOCALAPPDATA $name) 'Projector-Display-Router.exe'}
Disable-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue | Out-Null
if(Test-Path -LiteralPath $exe){Start-Process -FilePath $exe -ArgumentList '--stop' -WindowStyle Hidden -Wait}
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $name -ErrorAction SilentlyContinue
if($RestoreMonitor){$restore=Start-Process -FilePath $exe -ArgumentList '--monitor-only' -WindowStyle Hidden -Wait -PassThru; if($restore.ExitCode -ne 0){throw 'Monitor restore failed'}}
Write-Output 'Display routing and its scheduled startup are disabled.'