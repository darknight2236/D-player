#Requires -Version 5.1
<#
.SYNOPSIS
  D-player Phase 15 coupling audit (read-only). Task 1 scope: M1 dependency graph,
  M2 cycle detection, M3 layer-violation scan. (M4-M6 appended in Task 2.)
.NOTES
  Heuristic, namespace-level: edges from `using DPlayer.*`. Layer = 2nd namespace segment
  (DPlayer.Views.Controls -> Views). Root files (namespace DPlayer) -> layer Root.
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
)
$ErrorActionPreference = 'Stop'

$layerDirs = @('Models','Services','ViewModels','Views','Configuration','Extensions','Converters')

function Get-Layer([string]$ns) {
    $parts = $ns -split '\.'
    if ($parts.Count -ge 2) { return $parts[1] }
    return 'Root'
}

# ---- collect sources (layer dirs + repo root) ----
$files = @()
foreach ($d in $layerDirs) {
    $p = Join-Path $RepoRoot $d
    if (Test-Path $p) { $files += @(Get-ChildItem -Path $p -Filter *.cs -Recurse -File) }
}
$files += @(Get-ChildItem -Path $RepoRoot -Filter *.cs -File)

# ---- namespace of each file + its DPlayer usings ----
$uses = @{}
foreach ($f in $files) {
    $text = Get-Content -LiteralPath $f.FullName -Raw
    $m = [regex]::Match($text, '(?m)^namespace\s+(DPlayer(?:\.[A-Za-z0-9_]+)*)\s*;')
    $ns = if ($m.Success) { $m.Groups[1].Value } else { 'DPlayer' }
    if (-not $uses.ContainsKey($ns)) { $uses[$ns] = [System.Collections.Generic.HashSet[string]]::new() }
    foreach ($u in [regex]::Matches($text, '(?m)^using\s+(DPlayer(?:\.[A-Za-z0-9_]+)*)\s*;')) {
        $t = $u.Groups[1].Value
        if ($t -ne $ns) { [void]$uses[$ns].Add($t) }
    }
}

# ---- M1: namespace edges / layer edges / fan-in+fan-out ----
$nsEdges = @()
foreach ($ns in $uses.Keys) { foreach ($t in $uses[$ns]) { $nsEdges += [pscustomobject]@{ From=$ns; To=$t } } }

$layerEdges = @{}
foreach ($e in $nsEdges) {
    $lf = Get-Layer $e.From; $lt = Get-Layer $e.To
    if ($lf -ne $lt) { $k = "$lf->$lt"; $layerEdges[$k] = ([int]$layerEdges[$k]) + 1 }
}

$fanIn = @{}
foreach ($e in $nsEdges) {
    if (-not $fanIn.ContainsKey($e.To)) { $fanIn[$e.To] = 0 }
    $fanIn[$e.To] = $fanIn[$e.To] + 1
}

# ---- M2: cycles via self-reachability (BFS) ----
function Test-SelfReachable([string]$start, $adj) {
    $seen = @{}
    $q = [System.Collections.Generic.Queue[string]]::new()
    foreach ($nb in @($adj[$start])) { if (-not $seen.ContainsKey($nb)) { $seen[$nb]=$true; $q.Enqueue($nb) } }
    while ($q.Count -gt 0) {
        $n = $q.Dequeue()
        if ($n -eq $start) { return $true }
        foreach ($nb in @($adj[$n])) { if (-not $seen.ContainsKey($nb)) { $seen[$nb]=$true; $q.Enqueue($nb) } }
    }
    return $false
}
$cyclic = @($uses.Keys | Sort-Object | Where-Object { Test-SelfReachable $_ $uses })

# ---- M3: layer violations (forbidden layer edges) ----
$forbidden = @(
    'Services->ViewModels','Services->Views',
    'ViewModels->Views',
    'Models->Services','Models->ViewModels','Models->Views','Models->Configuration','Models->Extensions','Models->Converters',
    'Configuration->Services','Configuration->ViewModels','Configuration->Views','Configuration->Extensions','Configuration->Converters',
    'Converters->Services','Converters->ViewModels','Converters->Views','Converters->Extensions'
)
$violations = @($layerEdges.Keys | Where-Object { $forbidden -contains $_ } | Sort-Object)

# ---- output ----
"== M1 namespace edges =="
$nsEdges | Sort-Object From,To | ForEach-Object { "{0} -> {1}" -f $_.From, $_.To }
"== M1 layer edges (namespace-edge count) =="
$layerEdges.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0} : {1}" -f $_.Key, $_.Value }
"== M1 fan-in / fan-out per namespace =="
foreach ($ns in ($uses.Keys | Sort-Object)) {
    $in = if ($fanIn.ContainsKey($ns)) { $fanIn[$ns] } else { 0 }
    "in={0} out={1}  {2}" -f $in, $uses[$ns].Count, $ns
}
"== M2 cycles =="
if ($cyclic.Count -eq 0) { "0 cycles" } else { $cyclic | ForEach-Object { "cycle involves: $_" } }
"== M3 layer violations =="
if ($violations.Count -eq 0) { "0 violations" } else { $violations }
