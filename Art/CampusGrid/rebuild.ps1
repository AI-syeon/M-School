param(
    [string]$Pipeline = "C:/Users/syeon/Workspace/2D-pixel-asset-pipeline",
    [string]$Art = (Join-Path $PSScriptRoot "art.json"),
    [string]$Map = (Join-Path $PSScriptRoot "map.json")
)

$ErrorActionPreference = "Stop"
$pixelPython = Join-Path $Pipeline ".venv/Scripts/python.exe"
if (!(Test-Path -LiteralPath $pixelPython)) {
    throw "Set -Pipeline to the local 2D-pixel-asset-pipeline folder with its .venv installed."
}

if ((Resolve-Path -LiteralPath $Art).Path -eq (Join-Path $PSScriptRoot "art.json")) {
    & $pixelPython -B (Join-Path $PSScriptRoot "build_fountain.py")
    if ($LASTEXITCODE -ne 0) { throw "Fountain frame preparation failed." }
    & $pixelPython -B (Join-Path $PSScriptRoot "build_targets.py")
    if ($LASTEXITCODE -ne 0) { throw "Target state preparation failed." }
    & $pixelPython -B (Join-Path $PSScriptRoot "build_canopy_mask.py")
    if ($LASTEXITCODE -ne 0) { throw "Canopy shadow mask preparation failed." }
}

& $pixelPython -B (Join-Path $PSScriptRoot "build_grid.py") --art $Art --map $Map
if ($LASTEXITCODE -ne 0) { throw "Campus grid conversion failed." }
