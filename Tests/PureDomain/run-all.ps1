param(
  [Parameter(Mandatory = $true)][string]$Tag,
  [string]$Root
)
if (-not $Root) { $Root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\..")).Path }
$dn = "C:\Program Files\dotnet\dotnet.exe"
$outDir = Join-Path $Root "Tests\Results\refactor"
New-Item -ItemType Directory -Force $outDir | Out-Null
$failed = 0
Push-Location $Root
try {
  foreach ($x in "ContentIOTests", "Stage2Tests", "Stage3Tests", "Stage4Tests") {
    $log = Join-Path $outDir "$Tag-$x.log"
    $build = & $dn build "Tests\PureDomain\MagicCampus.$x.csproj" -v q -nologo 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
      Set-Content -Path $log -Value $build
      Write-Output "$x BUILD-FAILED (see $log)"
      $failed++
      continue
    }
    $dll = "Tests\PureDomain\bin\Debug\net10.0\MagicCampus.$x.dll"
    if ($x -eq "Stage4Tests") { $run = & $dn $dll --real-disk 2>&1 | Out-String } else { $run = & $dn $dll 2>&1 | Out-String }
    $code = $LASTEXITCODE
    Set-Content -Path $log -Value $run
    $lines = $run -split "`r?`n"
    $pass = @($lines | Where-Object { $_ -match '^PASS' }).Count
    $fail = @($lines | Where-Object { $_ -match '^FAIL' }).Count
    Write-Output ("{0} exit={1} PASS={2} FAIL={3}" -f $x, $code, $pass, $fail)
    if ($code -ne 0 -or $fail -gt 0) { $failed++ }
  }
}
finally {
  Pop-Location
  Get-ChildItem (Join-Path $Root "Tests\Results") -Directory -Filter "stage4-*" -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -gt (Get-Date).AddMinutes(-30) -and -not (git -C $Root ls-files $_.FullName) } |
    Remove-Item -Recurse -Force
}
exit $failed
