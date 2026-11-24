$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
if(!(Test-Path -LiteralPath $compiler)){throw '.NET Framework 4.x C# compiler not found'}
$output=Join-Path $repoRoot 'artifacts\tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$source=Join-Path $repoRoot 'sources\windows-display-router'
$tests=Join-Path $repoRoot 'tests\windows-display'
$modeTest=Join-Path $output 'DisplayModeProfiles.Tests.exe'
& $compiler /nologo /r:System.Runtime.Serialization.dll ('/out:'+$modeTest) (Join-Path $source 'DisplayConfig.cs') (Join-Path $tests 'DisplayModeProfiles.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Mode tests did not compile'}
& $modeTest
if($LASTEXITCODE -ne 0){throw 'Mode tests failed'}
$policyTest=Join-Path $output 'RouterPolicy.Tests.exe'
& $compiler /nologo /r:System.Web.Extensions.dll /r:System.Runtime.Serialization.dll /main:RouterPolicyTests ('/out:'+$policyTest) (Join-Path $source 'DisplayConfig.cs') (Join-Path $source 'DisplayRouter.cs') (Join-Path $tests 'RouterPolicy.Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Routing tests did not compile'}
& $policyTest
if($LASTEXITCODE -ne 0){throw 'Routing tests failed'}
Write-Output 'Offline tests passed; no display settings were changed.'
