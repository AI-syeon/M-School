# Classifies every type in Assets/MagicCampus/Contracts (rules in ContractsCommon.ps1, Get-Classification).
# Output: Tests\Results\refactor\contracts-classification.tsv (git-ignored) and a summary on stdout.
param([string]$Out)
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "ContractsCommon.ps1")
if (-not $Out) { $Out = Join-Path $script:Root "Tests\Results\refactor\contracts-classification.tsv" }
$c = Get-Classification
New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
$c.Rows | Export-Csv -Path $Out -Delimiter "`t" -NoTypeInformation -Encoding UTF8
$c.Rows | ForEach-Object { "{0,-6} {1,-26} {2,-24} {3,4}  {4}  [{5}]" -f $_.Class, $_.Type, $_.File, $_.Lines, $_.Why, $_.Users }
$c.Rows | Group-Object Class | ForEach-Object { "{0}: {1} types, {2} lines" -f $_.Name, $_.Count, ($_.Group | Measure-Object Lines -Sum).Sum }
