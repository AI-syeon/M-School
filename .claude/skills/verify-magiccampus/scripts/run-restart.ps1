# Force-kills the player at a scene checkpoint, then proves a NEW process restores the scene-entry save.
# Optionally proves a completed event survives restart (needs an earlier success-notes run).
# Usage: powershell -ExecutionPolicy Bypass -File run-restart.ps1 -Phases move,say [-SuccessFrom <run>] [-Build Stage5-verify] [-Run myrun]
#        -Phases none -SuccessFrom <run>  runs only the completed-event reload.
param([string[]]$Phases = @('move','say','choice'), [string]$SuccessFrom = '', [string]$Build = 'Stage5-verify', [string]$Run = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

# powershell -File passes "a,b" as one string; split it here. "none" skips the kill phases.
$Phases = @($Phases | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -ne 'none' })
foreach ($ph in $Phases) { if (@('move','say','choice') -notcontains $ph) { throw "Unknown phase '$ph'. Use move, say, choice or none." } }
$exe = Get-PlayerExe $Build
if (-not (Test-Path $exe)) { throw "Missing $exe; run build.ps1 first" }
$root = New-RunDir $Run
Write-Output "evidence root $root"
$failed = 0
function Launch($case, $dir, $save, $extras) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $a = @('--probe',$case,'--save-dir',"`"$save`"",'--evidence-dir',"`"$dir`"",'-logFile',"`"$dir\player.log`"",'-screen-fullscreen','0') + $extras
    $p = Start-Process -FilePath $exe -ArgumentList $a -WindowStyle Normal -PassThru
    $p.Id | Set-Content (Join-Path $dir 'pid.txt')
    return $p
}
# Prints with Write-Host so the lines do not leak into the boolean return value.
function Report($name, $dir, $p) {
    $rf = Join-Path $dir 'result.json'
    if (-not (Test-Path $rf)) { Write-Host "$name NO RESULT exit=$($p.ExitCode)"; return $false }
    $r = Get-Content $rf -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "$name pass=$($r.pass) exit=$($p.ExitCode) checks=$($r.checks.Count)"
    $r.checks | Where-Object { -not $_.pass } | ForEach-Object { Write-Host "  FAIL $($_.name): $($_.detail)" }
    $r.errors | ForEach-Object { Write-Host "  ERROR $_" }
    return [bool]($r.pass -and $p.ExitCode -eq 0)
}
foreach ($phase in $Phases) {
    $dir = Join-Path $root "kill-$phase"; $save = Join-Path $dir 'save'
    $p = Launch "kill-$phase" $dir $save @('--fresh')
    $deadline = [DateTime]::UtcNow.AddSeconds(75)
    while (-not (Test-Path "$dir\checkpoint.json") -and -not $p.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if (-not (Test-Path "$dir\checkpoint.json")) { Stop-OwnPlayer $p.Id | Out-Null; Write-Output "kill-$phase NO CHECKPOINT"; $failed++; continue }
    $cp = Get-Content "$dir\checkpoint.json" -Raw | ConvertFrom-Json
    $want = if ($phase -eq 'choice') { 'choose' } else { $phase }
    if ($cp.pid -ne $p.Id -or $cp.phase -ne $want) { Stop-OwnPlayer $p.Id | Out-Null; throw "checkpoint identity mismatch: $($cp.pid)/$($cp.phase)" }
    $how = Stop-OwnPlayer $p.Id
    @{phase=$phase; killedPid=$p.Id; method=$how; checkpoint=$cp} | ConvertTo-Json -Depth 5 | Set-Content "$dir\termination.json"
    $rdir = Join-Path $root "reload-$phase"
    $q = Launch 'reload-entry' $rdir $save @('--expected-entry',"`"$dir\entry.json`"")
    if (-not $q.WaitForExit(75000)) { Stop-OwnPlayer $q.Id | Out-Null; Write-Output "reload-$phase TIMEOUT"; $failed++; continue }
    @{killedPid=$p.Id; newPid=$q.Id; distinct=($p.Id -ne $q.Id); exitCode=$q.ExitCode} | ConvertTo-Json | Set-Content "$rdir\process-result.json"
    if (-not (Report "kill-$phase -> reload" $rdir $q)) { $failed++ }
}
if ($SuccessFrom) {
    $src = Join-Path (Join-Path $VerifyRoot $SuccessFrom) 'success-notes'
    if (-not (Test-Path "$src\final.json")) { throw "No success-notes evidence at $src; run run-probe.ps1 -Cases success-notes -Run $SuccessFrom first" }
    # Copy the save so the source evidence stays untouched.
    $dir = Join-Path $root 'reload-success-notes'; $save = Join-Path $dir 'save'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item "$src\save" $save -Recurse
    $q = Launch 'reload-success' $dir $save @('--expected-wallet','220','--event','event.snack','--expected-entry',"`"$src\final.json`"")
    if (-not $q.WaitForExit(75000)) { Stop-OwnPlayer $q.Id | Out-Null; Write-Output "reload-success TIMEOUT"; $failed++ }
    elseif (-not (Report 'reload-success-notes' $dir $q)) { $failed++ }
}
if ($failed -gt 0) { exit 1 }
