# Shared helpers for the unit-6 Contracts reduction scripts. ASCII only (Windows PowerShell 5.1).
$ErrorActionPreference = "Stop"
$script:Root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\..")).Path
$script:Assets = Join-Path $script:Root "Assets\MagicCampus"

# Replaces comments, string and char literals with spaces (newlines kept) so positions line up with the original text.
$script:TokenRe = [regex]'(?s)/\*.*?\*/|//[^\r\n]*|@"(?:""|[^"])*"|\$@"(?:""|[^"])*"|\$?"(?:\\.|[^"\\\r\n])*"|''(?:\\.|[^''\\\r\n])+'''
$script:Blank = { param($m) $sb = New-Object System.Text.StringBuilder; foreach ($c in $m.Value.ToCharArray()) { if ($c -eq "`n" -or $c -eq "`r") { [void]$sb.Append($c) } else { [void]$sb.Append(' ') } }; $sb.ToString() }
function Strip-Code([string]$text) {
  return $script:TokenRe.Replace($text, [System.Text.RegularExpressions.MatchEvaluator]$script:Blank)
}

function Get-CsFiles([string]$dir) {
  if (-not (Test-Path $dir)) { return @() }
  Get-ChildItem $dir -Recurse -Filter *.cs -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
}

# Groups by assembly. The Runtime folder belongs to Domain (MagicCampus.Domain.asmdef owns it).
function Get-Groups {
  $a = $script:Assets
  $domainDirs = "Application","Content","Persistence","Runtime","State","Time","Transactions","World"
  $g = [ordered]@{}
  $g["Contracts"] = @(Get-CsFiles (Join-Path $a "Contracts"))
  $g["Domain"] = @($domainDirs | ForEach-Object { Get-CsFiles (Join-Path $a $_) })
  $g["Bootstrap"] = @(Get-CsFiles (Join-Path $a "Bootstrap"))
  $g["UnityAdapters"] = @(Get-CsFiles (Join-Path $a "UnityAdapters"))
  $g["Presentation"] = @(Get-CsFiles (Join-Path $a "Presentation"))
  $t = Join-Path $script:Root "Tests\PureDomain"
  $ciTests = @(Get-CsFiles (Join-Path $t "ContentIO")) + @(Get-Item (Join-Path $t "ProgramContentIO.cs"))
  $ciSet = @{}; foreach ($f in $ciTests) { $ciSet[$f.FullName] = $true }
  $g["ContentIOTests"] = $ciTests
  $g["Tests"] = @(Get-CsFiles $t | Where-Object { -not $ciSet.ContainsKey($_.FullName) })
  return $g
}

# Top-level type declarations of the files in Contracts: name, kind, file, span, body (stripped).
function Get-ContractTypes {
  $decl = [regex]'(?m)^    (?:public |internal )?(?:static |sealed |readonly |abstract |partial )*(class|struct|interface|enum)\s+(\w+)'
  $types = @()
  foreach ($f in (Get-CsFiles (Join-Path $script:Assets "Contracts"))) {
    $raw = [IO.File]::ReadAllText($f.FullName)
    $code = Strip-Code $raw
    foreach ($m in $decl.Matches($code)) {
      $open = $code.IndexOf('{', $m.Index)
      $depth = 0; $end = -1
      for ($i = $open; $i -lt $code.Length; $i++) {
        $c = $code[$i]
        if ($c -eq '{') { $depth++ } elseif ($c -eq '}') { $depth--; if ($depth -eq 0) { $end = $i; break } }
      }
      $lineEnd = $code.IndexOf("`n", $end); if ($lineEnd -lt 0) { $lineEnd = $code.Length - 1 }
      # leading doc comment / attribute lines directly above the declaration travel with the type
      $start = $m.Index
      while ($start -gt 0) {
        $prevStart = $raw.LastIndexOf("`n", [Math]::Max($start - 2, 0)) + 1
        $line = $raw.Substring($prevStart, $start - $prevStart).TrimEnd("`r", "`n")
        if ($line -match '^\s*(///|\[)') { $start = $prevStart } else { break }
      }
      $text = $raw.Substring($start, $lineEnd - $start + 1).TrimEnd("`r", "`n")
      $head = $code.Substring($m.Index, [Math]::Min(40, $code.Length - $m.Index))
      $types += [pscustomobject]@{
        Name = $m.Groups[2].Value; Kind = $m.Groups[1].Value; File = $f.FullName
        Start = $start; End = $lineEnd + 1; Stripped = $code.Substring($m.Index, $end - $m.Index + 1)
        Text = $text; Lines = ($text -split "`n").Count
        Internal = ($head -match '^    internal')
      }
    }
  }
  return $types
}

# A type is referenced by its name, or (static class of extension methods) by a call of one of its extension methods.
function Get-UseRegex($t) {
  $alts = @("\b" + [regex]::Escape($t.Name) + "\b")
  foreach ($m in [regex]::Matches($t.Stripped, 'static\s+[\w<>\[\],\s]+?\s+(\w+)\s*(?:<[^>]*>)?\(\s*this\s')) { $alts += "\." + [regex]::Escape($m.Groups[1].Value) + "\b" }
  return [regex]($alts -join "|")
}

# STAYS  : referenced by UnityAdapters, Presentation, the ContentIO tests (Contracts-only suite), or named by a staying Contracts type.
# MOVE   : referenced only by Domain, Bootstrap and tests (directly or through other moving types).
# DELETE : no remaining user.
function Get-Classification {
  $types = Get-ContractTypes
  $groups = Get-Groups
  $stripped = @{}
  foreach ($k in $groups.Keys) {
    if ($k -ne "Contracts") { $stripped[$k] = @($groups[$k] | ForEach-Object { [pscustomobject]@{ Path = $_.FullName; Code = (Strip-Code ([IO.File]::ReadAllText($_.FullName))) } }) }
  }
  $names = @($types | ForEach-Object { $_.Name } | Select-Object -Unique)
  $useRe = @{}
  foreach ($t in $types) { $useRe[$t.Name] = Get-UseRegex $t }
  $ext = @{}
  foreach ($t in $types) {
    $re = $useRe[$t.Name]
    if (-not $ext.ContainsKey($t.Name)) { $ext[$t.Name] = @() }
    foreach ($k in $stripped.Keys) {
      $hit = @($stripped[$k] | Where-Object { $re.IsMatch($_.Code) })
      if ($hit.Count -gt 0 -and ($ext[$t.Name] -notcontains $k)) { $ext[$t.Name] += $k }
    }
  }
  # same-named types (IHandle, IHandle<T>) are merged
  $refs = @{}
  foreach ($n in $names) { $refs[$n] = @() }
  foreach ($t in $types) {
    foreach ($o in $names) {
      if ($o -ne $t.Name -and $useRe[$o].IsMatch($t.Stripped) -and ($refs[$t.Name] -notcontains $o)) { $refs[$t.Name] += $o }
    }
  }
  $stay = @{}
  $queue = New-Object System.Collections.Generic.Queue[string]
  foreach ($t in $types) {
    $e = $ext[$t.Name]
    if (($e -contains "UnityAdapters") -or ($e -contains "Presentation") -or ($e -contains "ContentIOTests")) { if (-not $stay.ContainsKey($t.Name)) { $stay[$t.Name] = "boundary"; $queue.Enqueue($t.Name) } }
  }
  while ($queue.Count -gt 0) {
    $n = $queue.Dequeue()
    foreach ($r in $refs[$n]) { if (-not $stay.ContainsKey($r)) { $stay[$r] = "named by staying $n"; $queue.Enqueue($r) } }
  }
  $live = @{}
  foreach ($t in $types) {
    if (-not $stay.ContainsKey($t.Name) -and (@($ext[$t.Name] | Where-Object { $_ -in "Domain","Bootstrap","Tests" }).Count -gt 0)) { $live[$t.Name] = $true; $queue.Enqueue($t.Name) }
  }
  while ($queue.Count -gt 0) {
    $n = $queue.Dequeue()
    foreach ($r in $refs[$n]) { if (-not $stay.ContainsKey($r) -and -not $live.ContainsKey($r)) { $live[$r] = $true; $queue.Enqueue($r) } }
  }
  $rows = foreach ($t in $types) {
    if ($stay.ContainsKey($t.Name)) { $cls = "STAYS"; $why = $stay[$t.Name] }
    elseif ($live.ContainsKey($t.Name)) { $cls = "MOVE"; $why = "only " + (($ext[$t.Name]) -join "+") }
    else { $cls = "DELETE"; $why = "no user" }
    $by = @($types | Where-Object { $refs[$_.Name] -contains $t.Name } | ForEach-Object { $_.Name })
    [pscustomobject]@{ Class = $cls; Type = $t.Name; File = (Split-Path -Leaf $t.File); Lines = $t.Lines; Users = ($ext[$t.Name] -join "+"); Why = $why; UsedByContracts = ($by -join ",") }
  }
  return [pscustomobject]@{ Types = $types; Rows = @($rows); UseRe = $useRe }
}
