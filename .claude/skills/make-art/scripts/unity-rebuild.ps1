# Rebuilds CampusGrid tiles, prefabs and the generated courtyard scene in Unity batchmode.
# Same as the editor menu MagicCampus > Art > Rebuild Campus Courtyard.
# Usage: powershell -ExecutionPolicy Bypass -File unity-rebuild.ps1 [-TimeoutMinutes 20]
param(
    [int]$TimeoutMinutes = 20,
    [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
if (-not (Test-Path $UnityExe)) { throw "Unity editor not found: $UnityExe" }
$editors = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.ToLowerInvariant().Contains($Project.ToLowerInvariant()) }
if ($editors) {
    Write-Output 'EDITOR_OPEN: Unity already has this project open. Ask the user to run MagicCampus > Art > Rebuild Campus Courtyard in the editor.'
    exit 2
}
$logDir = Join-Path $Project ('Art\_runs\' + (Get-Date -Format 'yyyyMMdd-HHmm') + '-unity-rebuild')
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'unity.log'
$unityArgs = @('-batchmode', '-quit', '-projectPath', "`"$Project`"", '-executeMethod', 'MagicCampus.Game.Editor.CampusGridImport.Rebuild', '-logFile', "`"$log`"")
$p = Start-Process -FilePath $UnityExe -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
if (-not $p.WaitForExit($TimeoutMinutes * 60000)) {
    Stop-Process -Id $p.Id -Force
    throw "Unity rebuild timed out after $TimeoutMinutes min. See $log"
}
$errors = Select-String -Path $log -Pattern 'error CS|Exception|Aborting batchmode' -ErrorAction SilentlyContinue | Select-Object -First 10
Write-Output "exit=$($p.ExitCode) log=$log"
if ($errors) { $errors | ForEach-Object { Write-Output $_.Line } }
if ($p.ExitCode -ne 0) { exit 1 }
