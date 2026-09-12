# Phase 15 耦合健康度审计 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 对 D-player 代码库做一次客观、可复现的耦合健康度审计（M1–M6 脚本度量 + M7/D1–D5 人工裁决），产出审计报告 + COUPLING.md 摘要 + 入库的可复跑审计脚本，并给出明确的「解耦 / 不解耦」结论。

**Architecture:** 只读 PowerShell 分析脚本（`tools/coupling-audit/`）解析 7 个层目录的 `using DPlayer.*` 与声明，产出命名空间依赖图（M1）、环检测（M2）、层级违规（M3）、接口宽度（M4）、文件 LOC（M5）、DI 注册-消费差（M6）；M7 隐式契约漂移与 D1–D5 由人工按决策框架 C 裁决；结果写入审计报告并摘要进 COUPLING.md。审计**不改功能代码**。

**Tech Stack:** PowerShell 5.1（分析脚本）· C#/.NET 源码（被审计对象）· Markdown（报告/文档）

**设计稿：** [`docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-design.md`](../specs/2026-09-12-d-player-phase15-coupling-audit-design.md)

**验证基线（脚本跑出的指标须匹配）：** M2=0 环；M3=0 层级违规；M4 `IPlaybackService`=20 成员；M5 无 >600 行文件（PlaylistViewModel ~564）；M6 未消费服务 = `IAudioDeviceManager` + `IAudioOutputFactory`。

---

## 文件结构

| 文件 | 责任 |
|------|------|
| `tools/coupling-audit/Invoke-CouplingAudit.ps1` | 只读审计脚本：M1–M6 指标产出（Task 1 建 M1–M3，Task 2 增 M4–M6） |
| `docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md` | 审计报告：方法学 + M1–M7 原始指标 + D1–D5 verdict + 总体结论（Task 4） |
| `docs/COUPLING.md` | 活登记册：TL;DR 刷新 + §4 stub 决策 + §5 新契约 + §6 Phase 15 行 + 报告链接（Task 5） |

---

### Task 1: 审计脚本 — M1 依赖图 / M2 环 / M3 层级违规

**Files:**
- Create: `tools/coupling-audit/Invoke-CouplingAudit.ps1`

- [ ] **Step 1: 创建脚本（M1–M3 部分）**

创建 `tools/coupling-audit/Invoke-CouplingAudit.ps1`，内容如下（完整、可运行）：

```powershell
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
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
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
```

- [ ] **Step 2: 运行脚本，验证 M2/M3 基线**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/coupling-audit/Invoke-CouplingAudit.ps1`
Expected: 输出含 `== M2 cycles ==` 段为 `0 cycles`；`== M3 layer violations ==` 段为 `0 violations`；M1 层边应出现 `ViewModels->Services`、`Views->ViewModels`、`Views->Services`、`Services->Models`、`ViewModels->Models` 等（与 COUPLING.md §2 一致），且**不应**出现 `Services->Views` / `ViewModels->Views` / `Models->*（非 Models）`。

- [ ] **Step 3: 提交**

```bash
git add tools/coupling-audit/Invoke-CouplingAudit.ps1
git commit -m "feat(tools): add coupling audit script (M1 dependency graph, M2 cycles, M3 layer violations)"
```

---

### Task 2: 审计脚本 — M4 接口宽度 / M5 文件 LOC / M6 DI 注册-消费差

**Files:**
- Modify: `tools/coupling-audit/Invoke-CouplingAudit.ps1`（在文件末尾追加 M4–M6 段）

- [ ] **Step 1: 在脚本末尾追加 M4–M6**

在 `Invoke-CouplingAudit.ps1` 文件**末尾**（M3 输出段之后）追加：

```powershell

# ================= Task 2: M4 / M5 / M6 =================

# ---- M4: interface width (member count) ----
function Get-InterfaceWidth([string]$name) {
    $f = Get-ChildItem -Path $RepoRoot -Filter "$name.cs" -Recurse -File | Select-Object -First 1
    if (-not $f) { return -1 }
    $text = Get-Content -LiteralPath $f.FullName -Raw
    $props = ([regex]::Matches($text, '\{\s*get;')).Count
    $meths = ([regex]::Matches($text, '(?m)^    [^\s/].*;\s*$')).Count
    return $props + $meths
}

# ---- M5: LOC per file, flag >600 ----
$loc = foreach ($f in $files) {
    [pscustomobject]@{
        File = $f.FullName.Substring($RepoRoot.Length).TrimStart('\','/')
        Loc  = (Get-Content -LiteralPath $f.FullName | Measure-Object -Line).Lines
    }
}
$big = @($loc | Where-Object { $_.Loc -gt 600 } | Sort-Object -Descending Loc)

# ---- M6: DI registered-but-unconsumed ----
$regFile = Join-Path $RepoRoot 'Extensions\ServiceCollectionExtensions.cs'
$regText = Get-Content -LiteralPath $regFile -Raw
$registered = @([regex]::Matches($regText, 'Add\w+<\s*(I[A-Za-z0-9_]+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
$unconsumed = @()
foreach ($svc in $registered) {
    $refs = 0
    foreach ($f in $files) {
        if ($f.FullName -eq $regFile) { continue }
        $t = Get-Content -LiteralPath $f.FullName -Raw
        if ($t -notmatch [regex]::Escape($svc)) { continue }
        $isInterfaceDecl = ($f.Name -eq "$svc.cs")
        $isImpl = ($t -match (':\s*' + [regex]::Escape($svc) + '\b')) -and ($t -match '\bclass\b')
        if (-not $isInterfaceDecl -and -not $isImpl) { $refs++ }
    }
    if ($refs -eq 0) { $unconsumed += $svc }
}

"== M4 interface width =="
foreach ($n in @('IPlaybackService','IPlaylistService','ISettingsPersistence','ITrackMetadataReader','ILibraryScannerService','ILibraryCache')) {
    "{0} = {1} members" -f $n, (Get-InterfaceWidth $n)
}
"== M5 files >600 LOC =="
if ($big.Count -eq 0) { "none" } else { $big | ForEach-Object { "{0} : {1}" -f $_.Loc, $_.File } }
"== M5 top-10 LOC =="
$loc | Sort-Object -Descending Loc | Select-Object -First 10 | ForEach-Object { "{0} : {1}" -f $_.Loc, $_.File }
"== M6 registered-but-unconsumed services =="
if ($unconsumed.Count -eq 0) { "none" } else { $unconsumed }
```

- [ ] **Step 2: 运行脚本，验证 M4/M5/M6 基线**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/coupling-audit/Invoke-CouplingAudit.ps1`
Expected: `== M4 ==` 段含 `IPlaybackService = 20 members`；`== M5 files >600 LOC ==` 段为 `none`；`== M6 ==` 段恰为 `IAudioDeviceManager` 与 `IAudioOutputFactory` 两行。

- [ ] **Step 3: 提交**

```bash
git add tools/coupling-audit/Invoke-CouplingAudit.ps1
git commit -m "feat(tools): coupling audit script adds M4 interface width, M5 LOC, M6 DI unconsumed"
```

---

### Task 3: 跑全量审计 + 人工裁决（M7 / D1–D5）+ 写审计报告

**Files:**
- Create: `docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md`

- [ ] **Step 1: 跑全量审计，捕获 M1–M6 原始输出**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/coupling-audit/Invoke-CouplingAudit.ps1`
把完整 stdout 保存（粘贴进报告 §附录 或存为工作笔记）。核对基线：M2=`0 cycles`、M3=`0 violations`、M4 `IPlaybackService = 20 members`、M5 >600=`none`、M6=`IAudioDeviceManager`+`IAudioOutputFactory`。

- [ ] **Step 2: M7 隐式契约漂移核对**

逐条（或抽查全部）核对 `docs/COUPLING.md` §5 各契约与当前代码是否仍一致；重点确认 Phase 14 块（EqualizerSampleProvider lock/SetPeakingEq、每声道 BiQuad、EQ 插入点、NullPlaybackService、ComboBox 首项自选、EqBandSlider 等）均已登记且与代码相符；再检查 Phase 14 之后是否引入**未登记**的新隐式契约。记录：一致 / 需修正 / 需补登 清单。

- [ ] **Step 3: M5 职责复核**

读 M5 top-LOC 文件（预期 `ViewModels/PlaylistViewModel.cs` ~564、`ViewModels/PlaylistsViewModel.cs` ~468 居前）。判断各自是否单一职责、有无 doing-too-much。因均 <600 阈值，预期结论=观察项（不拆分），除非复核发现明确多职责混杂。

- [ ] **Step 4: 按决策框架 C 裁决 D1–D5**

- **D1**（IPlaybackService 宽度）：实测成员数（预期 20）。<24 且无第 3 个 DSP 关注点 → verdict=可接受单一播放门面，记为观察项（记录增长趋势）。
- **D2**（2 stub 去留）：多设备/输出模式仍在 `docs/PROJECT.md` §1.2 后续增量 → verdict=保留；否则建议删。记录理由。
- **D3**（漂移）：依 Step 2 结果 → verdict=无漂移 / 或列出补登项。
- **D4**（环/层级违规）：M2/M3 均 0 → verdict=无 must-fix。
- **D5**（职责过载）：无 >600 且复核无多职责 → verdict=观察项。

- [ ] **Step 5: 写审计报告**

创建 `docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md`，必含以下小节（值取自 Step 1–4 实测）：
1. 标题 + 日期/分支/基线 HEAD；
2. **方法学**：脚本路径、启发式说明（namespace 级、layer=第 2 段）、M1–M6 各指标算法、M7 人工核对方法、决策框架 C；
3. **M1–M6 原始指标表**（粘贴/整理 Step 1 输出：namespace 边、layer 边、fan-in/out、cycles、violations、接口宽度、top-LOC、未消费服务）；
4. **M7 漂移核对结果**（Step 2）；
5. **D1–D5 verdict 表**（每点：标准 / 实测 / verdict / 理由）；
6. **总体结论**：耦合是否仍健康、是否需要解耦（预期：健康、无需大解耦；D1 列观察项）；
7. **（条件性）重构立项清单**：仅当 D4 must-fix 或 D1/D5 裁决拆分时列出；否则写「无」。

- [ ] **Step 6: 提交**

```bash
git add docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md
git commit -m "docs: add Phase 15 coupling audit report (M1-M7 metrics + D1-D5 verdicts)"
```

---

### Task 4: 更新 COUPLING.md（摘要）

**Files:**
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: 顶部 header + 报告链接**

更新第 3 行 metadata：更新日期=2026/09/12、HEAD=审计报告基线、阶段=**Phase 15 完成（耦合健康度审计）**；并在用途段或 §8 加审计报告链接 `docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md`。

- [ ] **Step 2: TL;DR 刷新**

依审计 verdict 刷新 TL;DR 表：`整体耦合度`（预期仍 **低**，备注补 Phase 15 审计结论）与 `是否需要立即重构`（依 D4/D1/D5 verdict，预期 ✅ 无）。

- [ ] **Step 3: §4 stub 决策（D2）**

在 §4 的「判断/建议」处记录 Phase 15 复核结论（预期：保留，理由=多设备仍在路线；或依裁决更新）。

- [ ] **Step 4: §5 漂移补登（D3）**

若 Step 2（Task 3）发现需补登/修正的契约 → 在 §5 增/改对应行；若无 → 在 §5 末尾或 Phase 15 块加一句「Phase 15 审计核对：§5 契约与代码一致，无新增未登记契约」。

- [ ] **Step 5: §6 检查清单加 Phase 15 行**

§6 标题/ intro 更新为 Phase 15 完成；候选范围或清单加 `[x] 耦合健康度审计（Phase 15 完成）—— M1–M6 脚本度量 + M7/D1–D5 裁决；结论：耦合低、无需解耦（D1 列观察项）`。

- [ ] **Step 6: 提交**

```bash
git add docs/COUPLING.md
git commit -m "docs: update COUPLING.md with Phase 15 coupling audit verdicts"
```

---

### Task 5: 条件性重构立项 + 终验

**Files:**
- （条件性）重构任务清单：仅当 Task 3 D4 must-fix 或 D1/D5 裁决拆分时，在审计报告 §7 列出；否则无文件改动。

- [ ] **Step 1: 条件性立项**

若 D4 发现 must-fix 或 D1/D5 裁决拆分 → 在审计报告 §7 写出重构任务清单（不实施）。否则确认报告 §7 为「无」。

- [ ] **Step 2: 终验——功能代码未受影响**

Run: `dotnet build D-player.sln -c Debug --nologo -v q` → Expected 0 错误。
Run: `dotnet test D-player.sln -c Debug --nologo -v q` → Expected 96 通过 / 0 失败。

- [ ] **Step 3: 终验——脚本可复跑（可复现性）**

连续跑两次审计脚本，比对输出一致：
Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/coupling-audit/Invoke-CouplingAudit.ps1 > a.txt; powershell -NoProfile -ExecutionPolicy Bypass -File tools/coupling-audit/Invoke-CouplingAudit.ps1 > b.txt; Compare-Object (Get-Content a.txt) (Get-Content b.txt)`
Expected: 无差异（空输出）。完成后删除 a.txt/b.txt。

- [ ] **Step 4: （仅当有文档改动）提交**

```bash
git add -A
git commit -m "docs: Phase 15 audit follow-ups (conditional refactor list)"
```

---

## 附：本计划自检结果

**1. 规格覆盖**：spec §2 M1–M3→Task 1；M4–M6→Task 2；§2 M7 + §3 D1–D5 + §4 报告→Task 3；§4 COUPLING 更新→Task 4；§4 条件性立项 + §5 验证→Task 5。无遗漏。
**2. 占位符扫描**：无 TBD/TODO；脚本为完整可运行代码；报告小节结构明确、值由脚本实测填入（基线已给定）。
**3. 类型/名称一致性**：脚本函数名（`Get-Layer`/`Test-SelfReachable`/`Get-InterfaceWidth`）、变量（`$uses`/`$nsEdges`/`$layerEdges`/`$fanIn`/`$loc`/`$registered`/`$unconsumed`）、报告路径、COUPLING 小节编号前后一致；基线值（20 成员 / 0 环 / 0 违规 / 2 stub / 无 >600）与 spec §2–§3 一致。

**预期产出**：审计脚本 1 个 + 审计报告 1 个 + COUPLING.md 更新；功能代码零改动；测试保持 96 全绿。
