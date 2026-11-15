# Builds the WebGL player (Builds\Web by default) with the project's own build method.
# Usage: powershell -ExecutionPolicy Bypass -File build-web.ps1 [-Dir Web] [-TimeoutMinutes 40]
param([string]$Dir = 'Web', [int]$TimeoutMinutes = 40)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if ($Dir -notmatch '^Web(-[A-Za-z0-9]+)*$') { throw "Dir must match Web[-Name]: $Dir" }
$outDir = Join-Path $Project "Builds\$Dir"
if (-not (Test-Path $UnityExe)) { throw "Unity editor not found: $UnityExe" }
$editors = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.ToLowerInvariant().Contains($Project.ToLowerInvariant()) }
if ($editors) { throw "Unity editor already has this project open (pid $($editors.ProcessId -join ',')). Close it or ask the user; batchmode cannot share the project." }

$logDir = New-RunDir ("buildweb-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$log = Join-Path $logDir 'unity-build.log'
$resultFile = Join-Path $outDir 'build-result.json'
if (Test-Path $resultFile) { Remove-Item $resultFile -Force }
$unityArgs = @('-batchmode','-quit','-projectPath',"`"$Project`"",'-buildTarget','WebGL','-executeMethod','MagicCampus.Game.Editor.GameBuild.BuildWeb','-logFile',"`"$log`"",'--web-build-dir',$Dir)
Write-Output "building WebGL $Dir ; log $log"
$p = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
$p.Id | Set-Content (Join-Path $logDir 'unity-pid.txt')
if (-not $p.WaitForExit($TimeoutMinutes * 60000)) {
    Stop-Process -Id $p.Id -Force
    throw "Unity WebGL build timed out after $TimeoutMinutes min; killed pid $($p.Id). See $log"
}
if (-not (Test-Path $resultFile)) { throw "No build-result.json (exit $($p.ExitCode)). See $log" }
Copy-Item $resultFile (Join-Path $logDir 'build-result.json')
$r = Get-Content $resultFile -Raw | ConvertFrom-Json
Write-Output "result=$($r.result) errors=$($r.errors) warnings=$($r.warnings) exit=$($p.ExitCode) index=$($r.playerPath)"
if ($r.result -ne 'Succeeded' -or $r.errors -gt 0 -or $p.ExitCode -ne 0) { exit 1 }
