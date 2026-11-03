# Read-only: is this build worth driving? Exit 0 = usable, 1 = not usable. Prints STALE when sources are newer than the build.
# Usage: powershell -ExecutionPolicy Bypass -File doctor.ps1 [-Build Stage5-verify]
param([string]$Build = 'Stage5-verify')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$ok = $true
$exe = Get-PlayerExe $Build
$resultFile = Join-Path (Split-Path $exe) 'build-result.json'
if (Test-Path $exe) { Write-Output "exe      OK   $exe" } else { Write-Output "exe      FAIL missing $exe (run build.ps1)"; $ok = $false }
if (Test-Path $resultFile) {
    $r = Get-Content $resultFile -Raw | ConvertFrom-Json
    $builtAt = (Get-Item $resultFile).LastWriteTime
    if ($r.result -eq 'Succeeded' -and $r.errors -eq 0) { Write-Output "build    OK   $($r.result) errors=$($r.errors) warnings=$($r.warnings) unity=$($r.unityVersion) at $builtAt" }
    else { Write-Output "build    FAIL $($r.result) errors=$($r.errors)"; $ok = $false }
    $sources = @('Assets\MagicCampus','Content\Fixtures\FirstConnection\generated','Art\Stage5-v1') | ForEach-Object { Join-Path $Project $_ } | Where-Object { Test-Path $_ }
    $newest = Get-ChildItem $sources -Recurse -File | Where-Object { $_.Extension -ne '.meta' -and $_.FullName -notmatch '\\Scenes\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest -and $newest.LastWriteTime -gt $builtAt) { Write-Output "fresh    STALE $($newest.FullName) changed $($newest.LastWriteTime) after build; rebuild before proving a change" }
    else { Write-Output "fresh    OK   no source newer than the build" }
} else { Write-Output "build    FAIL no build-result.json"; $ok = $false }

$players = Get-CimInstance Win32_Process -Filter "Name='MagicCampus.exe'"
if ($players) { foreach ($pl in $players) { Write-Output "running  INFO pid=$($pl.ProcessId) $($pl.CommandLine)" } }
else { Write-Output "running  OK   no MagicCampus.exe running" }
$editors = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.ToLowerInvariant().Contains($Project.ToLowerInvariant()) }
if ($editors) { Write-Output "editor   INFO Unity has this project open (pid $($editors.ProcessId -join ',')); build.ps1 will refuse" }
if (-not $ok) { exit 1 }
