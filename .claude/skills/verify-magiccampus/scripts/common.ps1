# Shared paths for verify-magiccampus helpers. Dot-source only.
$Project = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$VerifyRoot = Join-Path $Project 'Tests\Results\verify'

function Get-PlayerExe([string]$Build) {
    if ($Build -notmatch '^Stage5(-[A-Za-z0-9]+)*$') { throw "Build name must match Stage5[-Name]: $Build" }
    return (Join-Path $Project "Builds\$Build\MagicCampus.exe")
}

function New-RunDir([string]$Run) {
    if (-not $Run) { $Run = Get-Date -Format 'yyyyMMdd-HHmmss' }
    $dir = Join-Path $VerifyRoot $Run
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

# Stops a process only if it is a MagicCampus.exe inside this project's Builds folder.
function Stop-OwnPlayer([int]$ProcessId, [switch]$Graceful) {
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $p) { return 'already-exited' }
    $buildsRoot = (Join-Path $Project 'Builds\').ToLowerInvariant()
    if (-not $p.Path -or -not $p.Path.ToLowerInvariant().StartsWith($buildsRoot) -or $p.ProcessName -ne 'MagicCampus') {
        throw "Refuse to stop pid $ProcessId ($($p.Path)): not a MagicCampus build from this project"
    }
    if ($Graceful) {
        [void]$p.CloseMainWindow()
        if ($p.WaitForExit(8000)) { return 'closed' }
    }
    Stop-Process -Id $ProcessId -Force
    $p.WaitForExit(10000) | Out-Null
    return 'killed'
}
