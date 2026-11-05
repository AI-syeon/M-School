# Runs built-in probe scenarios, one fresh player process per case, each with its own save and evidence folder.
# Usage: powershell -ExecutionPolicy Bypass -File run-probe.ps1 -Cases success-notes,cancel [-Build Stage5-verify] [-Run myrun]
param([string[]]$Cases = @('success-notes'), [string]$Build = 'Stage5-verify', [string]$Run = '', [int]$TimeoutSeconds = 90)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

# powershell -File passes "a,b" as one string; split it here.
$Cases = @($Cases | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$known = @('success-notes','success-tea','moved','a10','intro-false','cancel','day-save','ui-long','ui-200')
$exe = Get-PlayerExe $Build
if (-not (Test-Path $exe)) { throw "Missing $exe; run build.ps1 first" }
$root = New-RunDir $Run
Write-Output "evidence root $root"
$failed = 0
foreach ($case in $Cases) {
    if ($known -notcontains $case) { throw "Unknown case '$case'. Known: $($known -join ', ')." }
    $dir = Join-Path $root $case
    if (Test-Path (Join-Path $dir 'result.json')) { throw "Refuse to overwrite evidence: $dir (pick another -Run)" }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $a = @('--probe',$case,'--fresh','--save-dir',"`"$dir\save`"",'--evidence-dir',"`"$dir`"",'-logFile',"`"$dir\player.log`"",'-screen-fullscreen','0')
    if ($case -eq 'moved') { $a += @('--variant','variants/dest-moved') }
    if ($case -eq 'a10') { $a += @('--variant','variants/a10-mira') }
    if ($case -eq 'intro-false') { $a += @('--initial','start-intro-false.json') }
    # A visible window is required: screenshots read the real framebuffer.
    $p = Start-Process -FilePath $exe -ArgumentList $a -WindowStyle Normal -PassThru
    $p.Id | Set-Content (Join-Path $dir 'pid.txt')
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        $how = Stop-OwnPlayer $p.Id
        @{case=$case; timeout=$true; pid=$p.Id; stopped=$how} | ConvertTo-Json | Set-Content (Join-Path $dir 'process-result.json')
        Write-Output "$case TIMEOUT (pid $($p.Id) $how)"; $failed++; continue
    }
    @{case=$case; exitCode=$p.ExitCode; pid=$p.Id} | ConvertTo-Json | Set-Content (Join-Path $dir 'process-result.json')
    $rf = Join-Path $dir 'result.json'
    if (-not (Test-Path $rf)) { Write-Output "$case NO RESULT exit=$($p.ExitCode); read $dir\player.log"; $failed++; continue }
    $r = Get-Content $rf -Raw -Encoding UTF8 | ConvertFrom-Json
    $bad = @($r.checks | Where-Object { -not $_.pass })
    Write-Output "$case pass=$($r.pass) exit=$($p.ExitCode) checks=$($r.checks.Count) failedChecks=$($bad.Count)"
    $bad | ForEach-Object { Write-Output "  FAIL $($_.name): $($_.detail)" }
    $r.errors | ForEach-Object { Write-Output "  ERROR $_" }
    if (-not $r.pass -or $p.ExitCode -ne 0) { $failed++ }
}
if ($failed -gt 0) { exit 1 }
