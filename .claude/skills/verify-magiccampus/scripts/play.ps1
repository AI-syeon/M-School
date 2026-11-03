# Starts or stops a normal (no probe) game session that writes live state evidence.
# Usage: powershell -ExecutionPolicy Bypass -File play.ps1 -Action start [-Build Stage5-verify] [-Run myrun]
#        powershell -ExecutionPolicy Bypass -File play.ps1 -Action status -Run myrun
#        powershell -ExecutionPolicy Bypass -File play.ps1 -Action stop -Run myrun
param([ValidateSet('start','status','stop')][string]$Action, [string]$Build = 'Stage5-verify', [string]$Run = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if ($Action -ne 'start' -and -not $Run) { throw "-Run is required for $Action" }
$dir = Join-Path (New-RunDir $Run) 'play'
$pidFile = Join-Path $dir 'pid.txt'
switch ($Action) {
    'start' {
        $exe = Get-PlayerExe $Build
        if (-not (Test-Path $exe)) { throw "Missing $exe; run build.ps1 first" }
        if (Test-Path $pidFile) { throw "A play session was already started in $dir; stop it or use another -Run" }
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $a = @('--fresh','--save-dir',"`"$dir\save`"",'--evidence-dir',"`"$dir`"",'-logFile',"`"$dir\player.log`"",'-screen-fullscreen','0')
        $p = Start-Process -FilePath $exe -ArgumentList $a -WindowStyle Normal -PassThru
        $p.Id | Set-Content $pidFile
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        while (-not (Test-Path "$dir\initial-state.json") -and -not $p.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
        if (-not (Test-Path "$dir\initial-state.json")) { Write-Output "NOT READY pid=$($p.Id) exited=$($p.HasExited); read $dir\player.log"; exit 1 }
        $s = Get-Content "$dir\initial-state.json" -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Output "READY pid=$($p.Id) phase=$($s.phase) map=$($s.mapId) evidence=$dir"
    }
    'status' {
        $procId = [int](Get-Content $pidFile)
        $alive = [bool](Get-Process -Id $procId -ErrorAction SilentlyContinue)
        Write-Output "pid=$procId alive=$alive"
        if (Test-Path "$dir\last-state.json") { Get-Content "$dir\last-state.json" -Raw -Encoding UTF8 }
    }
    'stop' {
        $procId = [int](Get-Content $pidFile)
        $how = Stop-OwnPlayer $procId -Graceful
        Write-Output "pid=$procId $how; evidence kept in $dir"
    }
}
