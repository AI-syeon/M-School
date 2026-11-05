# Unit 6: moves the non-boundary types out of Assets/MagicCampus/Contracts into Domain folders, changes their
# namespace, and fixes the using lines of every consumer (Domain, Bootstrap, tests). Rerunnable: it classifies first
# (classify rules in ContractsCommon.ps1) and does nothing when no type is left to move.
# Whole source files that move entirely use git mv (file and .meta, so the Unity guid is kept); new files get a fresh .meta.
# Usage (repo root): powershell -ExecutionPolicy Bypass -File Tools\ContractsReduction\move-contracts.ps1
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "ContractsCommon.ps1")

# destination file (relative to Assets\MagicCampus) -> namespace, moving types in order, optional Contracts file to git mv
$Plan = @(
  @{ File = "Content\Parsing\CallTree.cs"; Ns = "MagicCampus.Content.Parsing"; Types = "CallKind","ArgKind","SourceSpan","CallArgument","CallNode","CallList" },
  @{ File = "Content\Compilation\FunctionDefs.cs"; Ns = "MagicCampus.Content.Compilation"; Types = "FunctionRole","ParamType","ParamDef","FunctionDef" },
  @{ File = "Content\Catalog\CompiledContent.cs"; Ns = "MagicCampus.Content.Catalog"; GitMoveFrom = "CompiledContentPorts.cs"
     Types = "ICompiledContentLookup","CompiledColumn","CompiledCall","ExecutionMode","RepeatPolicy","CompiledEvent","ICatalog","ICatalogProvider" },
  @{ File = "Time\Calendar\CalendarTypes.cs"; Ns = "MagicCampus.Timekeeping"; Types = "GameTime","DayBoundary","ClockAdvance","ICalendar" },
  @{ File = "Time\Clock\IClockService.cs"; Ns = "MagicCampus.Timekeeping"; Types = "IClockService" },
  @{ File = "Transactions\TransactionIds.cs"; Ns = "MagicCampus.Transactions"; Types = "TransactionId","CompletionKey" },
  @{ File = "Runtime\Control\RuntimePorts.cs"; Ns = "MagicCampus.Runtime"; Types = "ICompletionQueue","Lease","LeaseRequest","ILeaseService" },
  @{ File = "World\Movement\ActorState.cs"; Ns = "MagicCampus.World.Movement"; Types = "ActorState","IActorWorld" },
  @{ File = "World\Movement\MovePorts.cs"; Ns = "MagicCampus.World.Movement"; Types = "MoveRequest","MoveArrival","IMoveService","IDestinationResolver" },
  @{ File = "Persistence\SaveEnvelope.cs"; Ns = "MagicCampus.Persistence"; Types = "SafeBoundary","SaveEnvelope","ISaveService" },
  @{ File = "Application\Events\EventPorts.cs"; Ns = "MagicCampus.Events"; GitMoveFrom = "StagingPorts.cs"; Types = "EventStartRequest","EventCompletion","IEventStagingPort" },
  @{ File = "Application\Reservations\StartCandidate.cs"; Ns = "MagicCampus.Application.Reservations"; Types = "CandidatePriority","ReservationStatus","StartCandidate" }
)

$UsingHeuristics = [ordered]@{
  "System" = '\b(Action|Func|IEquatable|Exception|ArgumentException|ArgumentNullException|ArgumentOutOfRangeException|InvalidOperationException|NotSupportedException|Math|StringComparison|StringComparer|Enum|Tuple|Array|Convert|Nullable|IComparable|DateTime|TimeSpan|Guid|Lazy|Comparison|Predicate|OverflowException|FormatException|Uri|Version|Object|Type|Int32|Int64|Double|String|Boolean|ReadOnlyMemory|Memory|ReadOnlySpan|Span|ArraySegment|BitConverter|Buffer|Environment|Random|Index|Range|Char|Byte|Single|Decimal|Int16|UInt32|UInt64)\b|\b(int|long|double|string|bool|char|float|decimal)\.'
  "System.Collections.Generic" = '\b(IReadOnlyList|IReadOnlyDictionary|IReadOnlyCollection|List|Dictionary|HashSet|IEnumerable|IList|ICollection|IDictionary|ISet|KeyValuePair|SortedDictionary|SortedSet|Queue|Stack|IEqualityComparer|EqualityComparer|Comparer|IComparer)\b'
  "System.Collections.ObjectModel" = '\b(ReadOnlyCollection|ReadOnlyDictionary|Collection|ObservableCollection|KeyedCollection)\b'
  "System.Globalization" = '\b(CultureInfo|NumberStyles|DateTimeStyles|CompareOptions|NumberFormatInfo)\b'
}

function Sort-Usings($lines) {
  $u = @($lines | Select-Object -Unique)
  $keys = @($u | ForEach-Object { $n = ($_ -replace '^using\s+', '' -replace ';\s*$', ''); $k = if ($n -eq 'System' -or $n.StartsWith('System.')) { '0' } else { '1' }; $k + $n })
  if ($u.Count -gt 1) { [Array]::Sort($keys, $u, [StringComparer]::Ordinal) }
  return $u
}

function Write-Text([string]$path, [string]$text, [bool]$bom) {
  [IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($bom)))
}

function New-Meta([string]$path) {
  Write-Text $path ("fileFormatVersion: 2`nguid: " + [guid]::NewGuid().ToString("N")) $false
}

$c = Get-Classification
$moveNames = @($c.Rows | Where-Object { $_.Class -eq "MOVE" } | ForEach-Object { $_.Type })
$deleteNames = @($c.Rows | Where-Object { $_.Class -eq "DELETE" } | ForEach-Object { $_.Type })
$stayNames = @($c.Rows | Where-Object { $_.Class -eq "STAYS" } | ForEach-Object { $_.Type } | Select-Object -Unique)
if ($moveNames.Count -eq 0 -and $deleteNames.Count -eq 0) { Write-Output "Nothing to move or delete: Contracts holds only boundary types."; exit 0 }

$planned = @($Plan | ForEach-Object { $_.Types })
$missing = @($moveNames | Where-Object { $planned -notcontains $_ })
$extra = @($planned | Where-Object { $moveNames -notcontains $_ })
if ($missing.Count -gt 0 -or $extra.Count -gt 0) { throw ("Plan mismatch. Unplanned MOVE types: [{0}]. Planned but not MOVE: [{1}]" -f ($missing -join ","), ($extra -join ",")) }

$typeByName = @{}
foreach ($t in $c.Types) { if (-not $typeByName.ContainsKey($t.Name)) { $typeByName[$t.Name] = $t } }
$nsOf = @{}
foreach ($p in $Plan) { foreach ($n in $p.Types) { $nsOf[$n] = $p.Ns } }
$moveRe = @{}
foreach ($n in $moveNames) { $moveRe[$n] = [regex]("(?<![\w.@])" + [regex]::Escape($n) + "\b") }

# ---- 1. consumers: read their state before anything moves (Domain, Bootstrap, tests; Contracts files are edited in step 3)
$groups = Get-Groups
$consumers = @()
foreach ($k in "Domain","Bootstrap","Tests") { $consumers += @($groups[$k]) }

# ---- 2. new destination files
$gitMoved = @{}
foreach ($p in $Plan) {
  $dest = Join-Path $script:Assets $p.File
  $bodies = @($p.Types | ForEach-Object { $typeByName[$_].Text })
  $code = (($p.Types | ForEach-Object { $typeByName[$_].Stripped }) -join "`n")
  $usings = @()
  foreach ($ns in $UsingHeuristics.Keys) { if ($code -cmatch $UsingHeuristics[$ns]) { $usings += "using $ns;" } }
  foreach ($n in $stayNames) { if ($c.UseRe[$n].IsMatch($code)) { $usings += "using MagicCampus.Contracts;"; break } }
  foreach ($o in $moveNames) {
    if ($nsOf[$o] -ne $p.Ns -and -not $p.Ns.StartsWith($nsOf[$o] + ".") -and $p.Types -notcontains $o -and $moveRe[$o].IsMatch($code)) { $usings += "using $($nsOf[$o]);" }
  }
  $usings = Sort-Usings $usings
  $head = if ($usings.Count -gt 0) { ($usings -join "`n") + "`n`n" } else { "" }
  $text = $head + "namespace " + $p.Ns + "`n{`n" + ($bodies -join "`n`n") + "`n}`n"
  if ($p.GitMoveFrom) {
    $src = Join-Path $script:Assets ("Contracts\" + $p.GitMoveFrom)
    $allMove = @($c.Types | Where-Object { (Split-Path -Leaf $_.File) -eq $p.GitMoveFrom } | Where-Object { $moveNames -notcontains $_.Name }).Count -eq 0
    if (-not $allMove) { throw "GitMoveFrom $($p.GitMoveFrom) still holds staying types" }
    & git -C $script:Root mv $src $dest; if ($LASTEXITCODE -ne 0) { throw "git mv failed: $src" }
    & git -C $script:Root mv ($src + ".meta") ($dest + ".meta"); if ($LASTEXITCODE -ne 0) { throw "git mv failed: $src.meta" }
    $gitMoved[$src] = $true
  } elseif (-not (Test-Path $dest)) {
    New-Meta ($dest + ".meta")
  }
  Write-Text $dest $text $false
  Write-Output ("wrote {0} ({1})" -f $p.File, ($p.Types -join ","))
}

# ---- 3. remove the moved and deleted types from the files that stay in Contracts
$removeNames = $moveNames + $deleteNames
$byFile = $c.Types | Where-Object { $removeNames -contains $_.Name } | Group-Object File
foreach ($g in $byFile) {
  if ($gitMoved.ContainsKey($g.Name)) { continue }
  $raw = [IO.File]::ReadAllText($g.Name)
  foreach ($t in ($g.Group | Sort-Object Start -Descending)) { $raw = $raw.Remove($t.Start, $t.End - $t.Start) }
  $crlf = $raw.Contains("`r`n"); if ($crlf) { $raw = $raw.Replace("`r`n", "`n") }
  $lines = [System.Collections.Generic.List[string]]($raw -split "`n")
  # drop section header comments (// ------) whose section no longer declares a type
  $i = 0
  while ($i -lt $lines.Count) {
    if ($lines[$i] -match '^    // -{8,}') {
      $j = $i + 1; $hasType = $false
      while ($j -lt $lines.Count -and $lines[$j] -notmatch '^    // -{8,}' -and $lines[$j] -notmatch '^}') {
        if ($lines[$j] -match '^    (public |internal )?(static |sealed |readonly |abstract )*(class|struct|interface|enum)\b') { $hasType = $true }
        $j++
      }
      if (-not $hasType) { $lines.RemoveRange($i, $j - $i); continue }
    }
    $i++
  }
  $raw = ($lines -join "`n")
  $raw = [regex]::Replace($raw, "\n([ \t]*\n){2,}", "`n`n")
  $raw = [regex]::Replace($raw, "(namespace [\w.]+\n\{)\n\n+", "`$1`n")
  $raw = [regex]::Replace($raw, "\n\n+\}", "`n}")
  # prune System usings the remaining text no longer needs
  $code = Strip-Code $raw
  foreach ($ns in $UsingHeuristics.Keys) {
    if ($code -cnotmatch $UsingHeuristics[$ns]) { $raw = [regex]::Replace($raw, "(?m)^using " + [regex]::Escape($ns) + ";\r?\n", "") }
  }
  if ($crlf) { $raw = $raw.Replace("`n", "`r`n") }
  Write-Text $g.Name $raw $false
  Write-Output ("edited {0}: removed {1}" -f (Split-Path -Leaf $g.Name), (($g.Group | ForEach-Object { $_.Name }) -join ","))
}

# ---- 4. consumers: add the new namespaces, drop MagicCampus.Contracts when no Contracts type is left in the file
$changed = 0
foreach ($f in $consumers) {
  $bytes = [IO.File]::ReadAllBytes($f.FullName)
  $bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
  $raw = [IO.File]::ReadAllText($f.FullName)
  $nl = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
  $code = Strip-Code $raw
  $m = [regex]::Match($code, '(?m)^namespace\s+([\w.]+)')
  $fileNs = if ($m.Success) { $m.Groups[1].Value } else { "" }
  $need = @{}
  foreach ($n in $moveNames) {
    if (-not $moveRe[$n].IsMatch($code)) { continue }
    $ns = $nsOf[$n]
    if ($fileNs -eq $ns -or $fileNs.StartsWith($ns + ".")) { continue }
    $need[$ns] = $true
  }
  $usingRe = [regex]'(?m)^using\s+(?!static\b)([\w.]+);\r?$'
  $existing = @($usingRe.Matches($raw) | ForEach-Object { $_.Groups[1].Value })
  $keepContracts = $false
  foreach ($n in $stayNames) { if ($c.UseRe[$n].IsMatch($code)) { $keepContracts = $true; break } }
  $dropContracts = ($existing -contains "MagicCampus.Contracts") -and -not $keepContracts
  $add = @($need.Keys | Where-Object { $existing -notcontains $_ })
  if ($add.Count -eq 0 -and -not $dropContracts) { continue }
  $lines = [System.Collections.Generic.List[string]]($raw -split "`r?`n")
  if ($dropContracts) { [void]$lines.Remove("using MagicCampus.Contracts;") }
  # indexes of the plain using lines in the top block
  $idx = @(); for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^using\s+(?!static\b)[\w.]+;$') { $idx += $i } }
  foreach ($ns in ($add | Sort-Object)) {
    $new = "using $ns;"
    $key = (& { if ($ns -eq 'System' -or $ns.StartsWith('System.')) { '0' } else { '1' } }) + $ns
    $pos = -1
    if ($idx.Count -eq 0) {
      $k = 0; while ($k -lt $lines.Count -and $lines[$k] -notmatch '^namespace\s') { $k++ }
      $lines.Insert($k, ""); $lines.Insert($k, $new); $idx = @($k); continue
    }
    $insertAt = $idx[-1] + 1
    foreach ($ix in $idx) {
      $n2 = ($lines[$ix] -replace '^using\s+', '' -replace ';\s*$', '')
      $k2 = (& { if ($n2 -eq 'System' -or $n2.StartsWith('System.')) { '0' } else { '1' } }) + $n2
      if ([string]::CompareOrdinal($k2, $key) -gt 0) { $insertAt = $ix; break }
    }
    $lines.Insert($insertAt, $new)
    $idx = @(); for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^using\s+(?!static\b)[\w.]+;$') { $idx += $i } }
  }
  Write-Text $f.FullName ($lines -join $nl) $bom
  $changed++
}
Write-Output ("consumers updated: {0}" -f $changed)
