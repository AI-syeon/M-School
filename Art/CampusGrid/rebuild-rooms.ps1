param(
    [string]$Pipeline = "C:/Users/syeon/Workspace/2D-pixel-asset-pipeline",
    [string]$Art = "",
    [string]$Map = ""
)

$ErrorActionPreference = "Stop"
$pixelPython = Join-Path $Pipeline ".venv/Scripts/python.exe"
if (!(Test-Path -LiteralPath $pixelPython)) {
    throw "Set -Pipeline to the local 2D-pixel-asset-pipeline folder with its .venv installed."
}
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& $pixelPython -B (Join-Path $PSScriptRoot "build_lounge.py")
if ($LASTEXITCODE -ne 0) { throw "Lounge preparation failed" }
foreach ($prep in (Get-ChildItem -LiteralPath $PSScriptRoot -Filter "build_room_*.py" | Sort-Object Name)) {
    & $pixelPython -B $prep.FullName
    if ($LASTEXITCODE -ne 0) { throw "Room preparation failed: $($prep.Name)" }
}
if ($Map) {
    $roomMaps = @(Get-Item -LiteralPath $Map)
} else {
    $roomMaps = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter "room-*-map.json" | Sort-Object Name)
}
foreach ($roomMap in $roomMaps) {
    $room = $roomMap.BaseName -replace '-map$', ''
    $roomArt = Join-Path $PSScriptRoot ($room + "-art.json")
    if ($Art) { $roomArt = $Art }
    & $pixelPython -B (Join-Path $PSScriptRoot "build_grid.py") --art $roomArt --map $roomMap.FullName --unity-out (Join-Path $projectRoot ("Assets/MagicCampus/CampusGrid/Rooms/" + $room)) --review-out (Join-Path $PSScriptRoot ("review/" + $room))
    if ($LASTEXITCODE -ne 0) { throw "Room conversion failed: $room" }
}
