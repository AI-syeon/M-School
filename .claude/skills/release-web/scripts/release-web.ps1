# Builds the WebGL player, tags the current commit, publishes a GitHub Release with the web zip,
# and lets .github/workflows/pages.yml deploy that release to GitHub Pages.
# Usage: powershell -ExecutionPolicy Bypass -File release-web.ps1 [-Version v0.2.0] [-Notes "text"] [-SkipBuild]
param([string]$Version, [string]$Notes = '', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$Project = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$Repo = 'AI-syeon/M-School'
$Api = "https://api.github.com/repos/$Repo"
Set-Location $Project

function Invoke-Git { $ErrorActionPreference = 'Continue'; $out = & git.exe @args 2>&1 | ForEach-Object { "$_" }; if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed: $out" }; return $out }

# 1. Release only committed, pushed work.
if (Invoke-Git status --porcelain) { throw 'Working tree has uncommitted changes. Commit them first.' }
$branch = (Invoke-Git rev-parse --abbrev-ref HEAD) | Select-Object -First 1
if ($branch -ne 'main') { throw "Release from main only (current: $branch)." }
Invoke-Git fetch origin --tags | Out-Null
$head = (Invoke-Git rev-parse HEAD) | Select-Object -First 1
$remote = (Invoke-Git rev-parse origin/main) | Select-Object -First 1
if ($head -ne $remote) { throw 'main is not pushed (HEAD differs from origin/main). Push first.' }

# 2. Version: given, or next patch after the newest vX.Y.Z tag (first release v0.1.0).
if (-not $Version) {
    $tags = @(Invoke-Git tag --list 'v*') | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | ForEach-Object { [version]$_.Substring(1) } | Sort-Object
    if ($tags.Count -eq 0) { $Version = 'v0.1.0' } else { $t = $tags[-1]; $Version = "v$($t.Major).$($t.Minor).$($t.Build + 1)" }
}
if ($Version -notmatch '^v\d+\.\d+\.\d+$') { throw "Version must look like v1.2.3: $Version" }
if (Invoke-Git tag --list $Version) { throw "Tag $Version already exists." }

# 3. Build and zip.
$web = Join-Path $Project 'Builds\Web'
if (-not $SkipBuild) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $Project '.claude\skills\verify-magiccampus\scripts\build-web.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'WebGL build failed.' }
}
if (-not (Test-Path (Join-Path $web 'index.html'))) { throw "No WebGL build at $web" }
$zip = Join-Path $Project "Builds\MagicCampus-web-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($web, $zip)
Write-Output ("zip {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

# 4. Token from Git Credential Manager (same sign-in as git push). Never printed.
$cred = "protocol=https`nhost=github.com`n`n" | git credential fill
$token = ($cred | Where-Object { $_ -like 'password=*' } | Select-Object -First 1) -replace '^password=', ''
if (-not $token) { throw 'No GitHub credential. Run git push once to sign in.' }
$h = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28'; 'User-Agent' = 'release-web' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# 5. Make sure Pages publishes from GitHub Actions.
try { $pages = Invoke-RestMethod -Uri "$Api/pages" -Headers $h -Method Get } catch { $pages = $null }
$body = '{"build_type":"workflow"}'
if (-not $pages) { Invoke-RestMethod -Uri "$Api/pages" -Headers $h -Method Post -Body $body -ContentType 'application/json' | Out-Null }
elseif ($pages.build_type -ne 'workflow') { Invoke-RestMethod -Uri "$Api/pages" -Headers $h -Method Put -Body $body -ContentType 'application/json' | Out-Null }

# 6. Tag, release, upload. Publishing the release triggers the Pages workflow.
Invoke-Git tag -a $Version -m "Web release $Version" | Out-Null
Invoke-Git push origin $Version | Out-Null
# Draft first so the zip is attached before the "published" event starts the workflow.
$req = @{ tag_name = $Version; name = "MagicCampus $Version"; body = $Notes; generate_release_notes = $true; draft = $true } | ConvertTo-Json
$rel = Invoke-RestMethod -Uri "$Api/releases" -Headers $h -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($req)) -ContentType 'application/json; charset=utf-8'
$upload = ($rel.upload_url -replace '\{.*\}$', '') + '?name=' + [Uri]::EscapeDataString((Split-Path $zip -Leaf))
Invoke-RestMethod -Uri $upload -Headers $h -Method Post -InFile $zip -ContentType 'application/zip' | Out-Null
$rel = Invoke-RestMethod -Uri "$Api/releases/$($rel.id)" -Headers $h -Method Patch -Body '{"draft":false}' -ContentType 'application/json'

$owner, $name = $Repo.Split('/')
Write-Output "released $Version : $($rel.html_url)"
Write-Output "play (after the Pages workflow finishes): https://$($owner.ToLowerInvariant()).github.io/$name/"
Write-Output "workflow runs: https://github.com/$Repo/actions"
