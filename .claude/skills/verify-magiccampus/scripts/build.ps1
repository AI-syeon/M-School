# Builds the Windows player with the project's own build method.
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1 [-Build Stage5-verify] [-TimeoutMinutes 25]
param([string]$Build = 'Stage5-verify', [int]$TimeoutMinutes = 25)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$exe = Get-PlayerExe $Build
if (-not (Test-Path $UnityExe)) { throw "Unity editor not found: $UnityExe" }
$editors = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.ToLowerInvariant().Contains($Project.ToLowerInvariant()) }
if ($editors) { throw "Unity editor already has this project open (pid $($editors.ProcessId -join ',')). Close it or ask the user; batchmode cannot share the project." }

$logDir = New-RunDir ("build-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$log = Join-Path $logDir 'unity-build.log'
$unityArgs = @('-batchmode','-quit','-projectPath',"`"$Project`"",'-executeMethod','MagicCampus.Game.Editor.GameBuild.Build','-logFile',"`"$log`"",'--stage5-build-name',$Build)
Write-Output "building $Build ; log $log"
$p = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
$p.Id | Set-Content (Join-Path $logDir 'unity-pid.txt')
if (-not $p.WaitForExit($TimeoutMinutes * 60000)) {
    Stop-Process -Id $p.Id -Force
    throw "Unity build timed out after $TimeoutMinutes min; killed pid $($p.Id). See $log"
}
$resultFile = Join-Path (Split-Path $exe) 'build-result.json'
if (-not (Test-Path $resultFile)) { throw "No build-result.json (exit $($p.ExitCode)). See $log" }
Copy-Item $resultFile (Join-Path $logDir 'build-result.json')
$r = Get-Content $resultFile -Raw | ConvertFrom-Json
Write-Output "result=$($r.result) errors=$($r.errors) warnings=$($r.warnings) exit=$($p.ExitCode) exe=$exe"
if ($r.result -ne 'Succeeded' -or $r.errors -gt 0 -or $p.ExitCode -ne 0) { exit 1 }
