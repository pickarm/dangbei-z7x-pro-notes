$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputDir = Join-Path $repoRoot 'artifacts\windows'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler not found' }
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$sourceDir = Join-Path $repoRoot 'sources\windows-display-router'
$outputFile = Join-Path $outputDir 'Projector-Display-Router.exe'
& $compiler /nologo /target:winexe /optimize+ /r:System.Web.Extensions.dll "/out:$outputFile" (Join-Path $sourceDir 'DisplayConfig.cs') (Join-Path $sourceDir 'DisplayRouter.cs')
if ($LASTEXITCODE -ne 0) { throw 'Windows compilation failed' }
Write-Output "Built (not started): $outputFile"
