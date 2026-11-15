# Runs one Codex art job non-interactively.
# Usage: powershell -ExecutionPolicy Bypass -File run-codex.ps1 -Brief <brief.md> -Name <short-name> [-Model gpt-6.1-sol] [-Effort medium]
# Writes Art/_runs/<stamp>-<Name>/ : prompt.md, codex-log.txt, last.txt, changed-files.txt
param(
    [Parameter(Mandatory = $true)][string]$Brief,
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$Model = 'gpt-6.1-sol',
    [string]$Effort = 'medium',
    [string]$Pipeline = 'C:\Users\syeon\Workspace\2D-pixel-asset-pipeline'
)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-z0-9][a-z0-9-]*$') { throw "Name must be lowercase letters, digits and hyphens: $Name" }
$Project = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$skill = Split-Path $PSScriptRoot
if (-not (Test-Path -LiteralPath $Brief)) { throw "Brief not found: $Brief" }

# Find codex.exe: PATH first, then the newest copy bundled with the Codex desktop app.
$codex = $null
$cmd = Get-Command codex -ErrorAction SilentlyContinue
if ($cmd) { $codex = $cmd.Source }
if (-not $codex) {
    $found = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin') -Recurse -Filter 'codex.exe' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($found) { $codex = $found.FullName }
}
if (-not $codex) { throw 'codex.exe not found (PATH or %LOCALAPPDATA%\OpenAI\Codex\bin).' }

$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$runDir = Join-Path $Project ("Art\_runs\$stamp-$Name")
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

# Build the prompt: shared rules + this job's brief (UTF-8, no BOM).
$utf8 = New-Object System.Text.UTF8Encoding $false
$common = [System.IO.File]::ReadAllText((Join-Path $skill 'briefs\common.md'), $utf8)
$job = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $Brief).Path, $utf8)
$runRel = "Art/_runs/$stamp-$Name"
$prompt = $common + "`n`n---`n`n" + $job.Replace('{{RUN_DIR}}', $runRel) + "`n`nRun folder for notes and decomposition files: $runRel`n"
$promptFile = Join-Path $runDir 'prompt.md'
[System.IO.File]::WriteAllText($promptFile, $prompt, $utf8)

$log = Join-Path $runDir 'codex-log.txt'
$last = Join-Path $runDir 'last.txt'
$start = Get-Date
Write-Output "codex=$codex"
Write-Output "run=$runDir"

$prevEnc = $OutputEncoding
$OutputEncoding = $utf8
# Codex writes progress to stderr; PowerShell 5.1 turns that into error records, so do not stop on them.
$ErrorActionPreference = 'Continue'
try {
    $prompt | & $codex exec -m $Model -c "model_reasoning_effort=`"$Effort`"" -s workspace-write -C $Project --add-dir $Pipeline -o $last - *> $log
    $code = $LASTEXITCODE
} finally {
    $OutputEncoding = $prevEnc
}

# Record every file changed during the run (outside Unity caches and git).
$skip = '\\(Library|Temp|Logs|obj|\.git|__pycache__)\\|\\Art\\_runs\\'
$changed = Get-ChildItem $Project -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -ge $start -and $_.FullName -notmatch $skip } |
    ForEach-Object { $_.FullName.Substring($Project.Length + 1) }
$header = '# Files modified during the run window. Other sessions working at the same time also show up here; check codex-log.txt before blaming Codex.'
@($header) + @($changed) | Set-Content -Path (Join-Path $runDir 'changed-files.txt') -Encoding UTF8
$pipeChanged = Get-ChildItem $Pipeline -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -ge $start -and $_.FullName -notmatch '\\(\.venv|__pycache__)\\' }
if ($pipeChanged) {
    Add-Content -Path (Join-Path $runDir 'changed-files.txt') -Value ('PIPELINE CHANGED: ' + (($pipeChanged | ForEach-Object { $_.FullName }) -join '; ')) -Encoding UTF8
}

$tokens = Select-String -Path $log -Pattern '^tokens used' -Context 0,1 -ErrorAction SilentlyContinue | Select-Object -Last 1
$tokText = ''
if ($tokens) { $tokText = ($tokens.Context.PostContext -join ' ').Trim() }
Write-Output "exit=$code changed=$(@($changed).Count) tokens=$tokText minutes=$([math]::Round(((Get-Date) - $start).TotalMinutes, 1))"
exit $code
