param([string]$InstallDir=(Join-Path $env:ProgramData 'CodexProjectorDisplayRouter'))
$ErrorActionPreference='Stop'
$name='CodexProjectorDisplayRouter'
$exe=Join-Path $InstallDir 'Projector-Display-Router.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Installed router not found'}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$userSid=$identity.User.Value
$action=New-ScheduledTaskAction -Execute $exe -WorkingDirectory $InstallDir
$login=New-ScheduledTaskTrigger -AtLogOn -User $userSid
$login.Delay='PT10S'
$retry=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5) -RepetitionInterval (New-TimeSpan -Minutes 5)
$principal=New-ScheduledTaskPrincipal -UserId $userSid -LogonType Interactive -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
$task=New-ScheduledTask -Action $action -Trigger @($login,$retry) -Principal $principal -Settings $settings -Description '当前用户登录后运行投影仪屏幕联动；未知状态保留电脑显示器；异常重试并每5分钟补启动。'
Register-ScheduledTask -TaskName $name -InputObject $task -Force | Out-Null
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $name -ErrorAction SilentlyContinue
Start-ScheduledTask -TaskName $name
Get-ScheduledTask -TaskName $name | Select-Object TaskName,State
