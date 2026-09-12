# Phase 15：耦合健康度审计报告（Coupling Health Audit Report）

> 审计日期：2026-09-12 · 分支：`feature/phase15-coupling-audit` · 基线 HEAD：`5dd64d7`（本次报告提交前的分支 tip）
>
> 注：本报告提交仅含 docs 变更，源码 tree 与基线 `5dd64d7` 一致；脚本输入可用 `git checkout 5dd64d7` 复现。
>
> 关联文档：设计规格 [`docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-design.md`](./2026-09-12-d-player-phase15-coupling-audit-design.md) · 活耦合登记册 [`docs/COUPLING.md`](../../COUPLING.md)（Phase 14 状态）
>
> 审计性质：**只读分析 + 报告撰写**，未修改任何功能代码或审计脚本。

---

## 方法学（Methodology）

### 审计脚本

- 路径：[`tools/coupling-audit/Invoke-CouplingAudit.ps1`](../../../tools/coupling-audit/Invoke-CouplingAudit.ps1)
- 调用：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/coupling-audit/Invoke-CouplingAudit.ps1`
- 产出：M1–M6 六项客观指标（M7 为人工核对，脚本仅辅助定位）。

### 复跑指引

- **环境**：Windows PowerShell 5.1+；**无需构建前置**——脚本仅读取 `.cs` 源文件，不依赖编译产物。
- **命令**（在仓库根目录或任意 cwd 执行均可，脚本内 `$RepoRoot` 会自动解析仓库根）：

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/coupling-audit/Invoke-CouplingAudit.ps1
  ```

- **耗时与确定性**：通常数秒内完成；输出为确定性（边、表项均经排序），同一源码 tree 上重复运行结果完全一致，可直接 diff 比对。

### 启发式规则（heuristic）

| 指标 | 计算方法 |
|------|---------|
| **M1 依赖图** | 从各源文件顶部的 `using DPlayer.*` 抽取命名空间级有向边；layer = 命名空间的第 2 段（如 `DPlayer.Views.Controls` → layer `Views`）。据此汇总 layer 边、每命名空间 fan-in / fan-out。 |
| **M2 环检测** | 在命名空间依赖图上做环搜索。 |
| **M3 层级违规** | 断言禁止边：Services→Views / Services→ViewModels / ViewModels→Views / Models→（任何上层，Models 应为基座 out=0）。 |
| **M4 接口宽度** | 统计核心接口的**单行成员签名**数量（属性 / 方法 / 事件各计 1；继承自 `IDisposable` 的 `Dispose` 不计入实现者自定义宽度）。 |
| **M5 文件规模** | 每文件物理 LOC = `(Get-Content <file>).Count`；标记 >600 行者做职责复核；top-10 以 LOC 降序、同值以路径确定性二次排序。 |
| **M6 DI 注册-消费差** | 对比 `ServiceCollectionExtensions` 注册的服务接口 vs 全仓消费点（substring mention，排除接口声明本身 + 排除顶层 public class 的实现声明），检出「注册但无人消费」。 |

### 局限性（limitations）

1. **命名空间级、非完整语义分析**：M1 仅从 `using` 建边，同层/嵌套引用可能有噪声；未做 Roslyn 全量符号解析（设计规格 §7 已声明此为刻意取舍）。
2. **layer = 命名空间第 2 段**：假定文件均使用 file-scoped namespace + 顶层 `using`；若出现块级命名空间或方法内 `using` 别名，抽取可能偏差。
3. **M4 仅计单行成员签名**：跨多行书写的成员签名可能漏计（本项目接口成员均为单行，实测无影响）。
4. **M5 = 物理 LOC**：含注释与空行，非纯 SLOC；规模阈值仅作复核触发器，不等于职责判定。
5. **M6 消费判定 = substring 提及**：可能对同名符号产生误判；已用「排除声明 + 排除实现类」降噪，最终以人工复核兜底。
6. **决策框架 C（混合）**：客观结构性违规（环 / 层级违规）**零容忍**；宽度 / 规模用**软阈值 + 人工裁决**，不为凑阈值而重构。

### 交叉校验

本次脚本输出与 Phase 15 实现计划预录的期望基线逐项比对，结果一致（M1 layer 边集合、M2=0、M3=0、M4 各接口宽度、M5 >600 列表与 top-10 前三、M6 两个 stub）。**无差异，无需在报告中记录 discrepancy。**

---

## M1–M6 原始指标（Step-1 实测输出）

> 以下为 `Invoke-CouplingAudit.ps1` 在基线 HEAD `5dd64d7` 上的完整原始输出。

### M1a — 命名空间原始边（namespace edges）

```
DPlayer -> DPlayer.Extensions
DPlayer -> DPlayer.Services
DPlayer -> DPlayer.ViewModels
DPlayer.Converters -> DPlayer.Models
DPlayer.Extensions -> DPlayer.Configuration
DPlayer.Extensions -> DPlayer.Services
DPlayer.Extensions -> DPlayer.ViewModels
DPlayer.Services -> DPlayer.Configuration
DPlayer.Services -> DPlayer.Models
DPlayer.ViewModels -> DPlayer.Configuration
DPlayer.ViewModels -> DPlayer.Models
DPlayer.ViewModels -> DPlayer.Services
DPlayer.Views -> DPlayer.Services
DPlayer.Views -> DPlayer.ViewModels
DPlayer.Views -> DPlayer.Views.Dialogs
DPlayer.Views.Controls -> DPlayer
DPlayer.Views.Controls -> DPlayer.Models
DPlayer.Views.Controls -> DPlayer.Services
DPlayer.Views.Controls -> DPlayer.ViewModels
DPlayer.Views.Controls -> DPlayer.Views.Dialogs
DPlayer.Views.Dialogs -> DPlayer.Configuration
DPlayer.Views.Dialogs -> DPlayer.Models
DPlayer.Views.Dialogs -> DPlayer.Services
DPlayer.Views.Dialogs -> DPlayer.ViewModels
```

### M1b — layer 聚合边（namespace-edge count）

| layer 边 | 计数 |
|----------|------|
| Converters->Models | 1 |
| Extensions->Configuration | 1 |
| Extensions->Services | 1 |
| Extensions->ViewModels | 1 |
| Root->Extensions | 1 |
| Root->Services | 1 |
| Root->ViewModels | 1 |
| Services->Configuration | 1 |
| Services->Models | 1 |
| ViewModels->Configuration | 1 |
| ViewModels->Models | 1 |
| ViewModels->Services | 1 |
| Views->Configuration | 1 |
| Views->Models | 2 |
| Views->Root | 1 |
| Views->Services | 3 |
| Views->ViewModels | 3 |

**依赖方向核验：** 存在 `ViewModels->Services`、`Views->ViewModels`、`Views->Services`、`Services->Models`、`ViewModels->Models`（正向）；**不存在** `Services->Views`、`ViewModels->Views`（反向）；`Models` out=0（纯基座）。与 COUPLING.md §2「View → VM → Service → Model」一致。

### M1c — fan-in / fan-out（每命名空间）

| in | out | namespace |
|----|-----|-----------|
| 1 | 3 | DPlayer |
| 4 | 0 | DPlayer.Configuration |
| 0 | 1 | DPlayer.Converters |
| 1 | 3 | DPlayer.Extensions |
| 5 | 0 | DPlayer.Models |
| 6 | 2 | DPlayer.Services |
| 5 | 3 | DPlayer.ViewModels |
| 0 | 3 | DPlayer.Views |
| 0 | 5 | DPlayer.Views.Controls |
| 2 | 4 | DPlayer.Views.Dialogs |

> `Models` / `Configuration` out=0（基座，只被引用不引用他人）；`Services` fan-in=6 为最高（各层都依赖服务抽象），符合预期。

### M2 — 环检测

```
0 cycles
```

### M3 — 层级违规

```
0 violations
```

### M4 — 接口宽度

| 接口 | 成员数 |
|------|--------|
| IPlaybackService | **20** |
| IPlaylistService | 2 |
| ISettingsPersistence | 2 |
| ITrackMetadataReader | 2 |
| ILibraryScannerService | 3 |
| ILibraryCache | 2 |

> `IPlaybackService` 20 成员经人工复核 [`Services/IPlaybackService.cs`](../../../Services/IPlaybackService.cs) 逐条确认（4 只读状态属性 + Volume + 6 控制方法 + 7 事件 + SpectrumConfig + EqualizerConfig = 20；`IDisposable.Dispose` 为继承成员不计）。

### M5 — 文件 >600 LOC

```
652 : ViewModels\PlaylistViewModel.cs
```

### M5 — top-10 LOC

| LOC | 文件 |
|-----|------|
| 652 | ViewModels\PlaylistViewModel.cs |
| 572 | ViewModels\PlaylistsViewModel.cs |
| 464 | Views\Controls\PlaylistView.xaml.cs |
| 349 | ViewModels\PlayerViewModel.cs |
| 327 | Services\NAudioPlaybackService.cs |
| 319 | Views\Controls\PlaylistsSidebarView.xaml.cs |
| 262 | Views\Dialogs\EqualizerDialog.xaml.cs |
| 200 | Views\Controls\SpectrumView.xaml.cs |
| 175 | Views\MainWindow.xaml.cs |
| 171 | Services\JsonPlaylistService.cs |

### M6 — 注册但无人消费的服务

```
IAudioDeviceManager
IAudioOutputFactory
```

> 复现 COUPLING.md §4 的 2 个 over-abstraction stub（`StubAudioDeviceManager` / `StubAudioOutputFactory` 已注册但无消费者）。

---

## M7 漂移核对结果（Step-2 findings）

对 COUPLING.md §5 隐式契约登记册做代表性 FULL pass 核对。**结论：全部一致，无需修正、无新增未登记契约。**

### Phase-14 契约逐条核验（8 条全部 consistent）

| # | 契约 | 代码证据 | 结果 |
|---|------|---------|------|
| 1 | EqualizerSampleProvider：Read（音频线程）/Update（UI 线程）buffer 粒度 `lock`；Update 用 `SetPeakingEq` 就地重算保留延迟线 | [`EqualizerSampleProvider.cs`](../../../Services/EqualizerSampleProvider.cs) L47/L72 `lock (_lock)`；L91 `existing.SetPeakingEq(...)`（仅新建时 `PeakingEQ`） | ✅ consistent |
| 2 | 立体声每声道独立 `BiQuadFilter?[channel][band]` | 同上 L21 `_filters` 声明为 `BiQuadFilter?[][]`（`[channel][band]`）；L84 逐声道填充 | ✅ consistent |
| 3 | EQ 插入点必须在 SampleAggregator 之前 | [`NAudioPlaybackService.cs`](../../../Services/NAudioPlaybackService.cs) `LoadAsync` L118-120：`sampleProvider → _equalizer → _sampleAggregator` | ✅ consistent |
| 4 | `IPlaybackService.EqualizerConfig` setter 语义对齐 `SpectrumConfig`：存字段 + `_equalizer?.Update` | 同上 L85-93：`_equalizerConfig = value; _equalizer?.Update(value);` | ✅ consistent |
| 5 | 加 `IPlaybackService` 成员须同步 `PlaylistsViewModel.NullPlaybackService`（第二生产实现者） | [`PlaylistsViewModel.cs`](../../../ViewModels/PlaylistsViewModel.cs) L548-571 `NullPlaybackService : IPlaybackService`，L569 含 `EqualizerConfig` | ✅ consistent |
| 6 | `EqualizerDialog.OnLoaded` 填充预设下拉须在 `_suppress` 窗口内 | [`EqualizerDialog.xaml.cs`](../../../Views/Dialogs/EqualizerDialog.xaml.cs) L67-74：`_suppress=true` → `PresetCombo.Items.Add` → `finally _suppress=false` | ✅ consistent |
| 7 | 取消回滚基准 `_initialConfig` 取「打开瞬间实时 `_playbackService.EqualizerConfig`」 | 同上 L50 `_initialConfig = _playbackService.EqualizerConfig;`（读盘失败保留此快照）；L259-260 `OnClosed` 用 `_initialConfig` 回滚 | ✅ consistent |
| 8 | PlayerBar EQ 竖直滑块须自定义模板 `EqBandSlider` | [`EqualizerDialog.xaml`](../../../Views/Dialogs/EqualizerDialog.xaml) L19-21：`<Style x:Key="EqBandSlider" TargetType="Slider">` + `Orientation=Vertical` + `Width=24` | ✅ consistent |

### 附带核验的 Phase-13 契约（spot-check，仍成立）

- `Save_Click` 不得 `ConfigureAwait(false)`：EqualizerDialog L232 `UpdateAsync(...)` 未加 `ConfigureAwait(false)`，L243 注释显式说明「故意留在 UI 线程写 `PlayerViewModel.EqualizerEnabled`」——与 Phase 13 SettingsDialog 契约同构。✅
- 频谱事件经 UI 线程封送：`NAudioPlaybackService.OnSpectrumDataReady` → `RaiseOnUIThread`（`_syncContext.Post`）。✅

### 新增未登记契约扫描

自 Phase 14 合并（commit `5a8bcde`）以来，`git log` 仅含 **docs**（设计规格、README）与 **tools**（审计脚本 3 次提交）——**无任何功能代码变更**，故未引入新的隐式约定。

- **needs-correction（需修正）：无**
- **needs-registration（需补登）：无**

---

## D1–D5 verdict 表

| # | 决策点 | 判定标准（框架 C） | 实测 | verdict | 理由 |
|---|--------|-------------------|------|---------|------|
| **D1** | `IPlaybackService` 接口宽度 | 成员数 **>24** 或第 **3** 个 DSP/可视化关注点接入 → 建议拆分；否则记为可接受单一播放门面（watch-item） | **20** 成员；DSP/viz 关注点仅 **2** 个（Phase 13 频谱 `SpectrumDataAvailable`+`SpectrumConfig`；Phase 14 均衡器 `EqualizerConfig`） | **可接受单一门面 · watch-item** | 20 < 24 且未达第 3 个 DSP/viz 关注点。增长趋势：Phase 13 频谱 +2、Phase 14 均衡器 +1，当前 20；配置类关注点持续往单接口挂，列入观察项，未到拆分阈值。 |
| **D2** | 2 个 stub 去留（`IAudioDeviceManager`/`IAudioOutputFactory`） | 多设备/输出模式仍在近期路线（PROJECT.md §1.2）→ 保留；否则删（YAGNI） | M6 复现 2 个未消费 stub；[`PROJECT.md`](../../PROJECT.md) §1.2「后续增量（未实现）」明列「多设备 / 输出模式切换（WASAPI Shared/Exclusive/ASIO）—— 接口已预留」；§7 亦记「面向接口 + 占位实现」 | **保留** | 路线图上多设备/输出模式仍为规划增量，接口已显式预留；成本近零（空实现 + 注册）。维持 COUPLING §4「暂不删、但别再增此类预留接口」的判断。 |
| **D3** | 隐式契约漂移 | §5 任一条与代码不符、或有新增未登记契约 → 补登/修正 | 8 条 Phase-14 契约 + 附带 Phase-13 契约全部核验一致；Phase 14 后无功能代码变更 | **无漂移** | 登记册与代码同步；无需修正、无需补登。 |
| **D4** | 依赖环 / 层级违规 | **零容忍**：发现即「必须修」，另立重构任务 | M2 = **0 cycles**；M3 = **0 violations** | **无 must-fix** | 无环、无层级违规；依赖方向 View→VM→Service→Model 完整成立。 |
| **D5** | 职责过载文件 | **>600 行且多职责** → 建议拆分；否则记为观察项 | `PlaylistViewModel.cs` = **652** >600 | **观察项 · 不拆分** | 见下方 Step-3 职责复核：652 行虽越阈值，但职责高度内聚于 queue/playlist 单一域，非多职责堆叠；行数源自严密的重入守卫与详实注释，非关注点混杂。 |

### Step-3 职责复核（D5 支撑证据）

**`ViewModels/PlaylistViewModel.cs`（652 行）— 单职责、内聚**

所有成员服务于同一「播放队列 / 歌单」域，可归为几组紧密相关的职责，无跨界关注点：

- 队列状态：`Queue`、`CurrentIndex`、`SelectedTrack`、`SortedView`
- 播放推进算法：`CalculateNextIndex` / `CalculatePrevIndex` / `PlayTrackAtAsync` / `HandleTrackEnded`
- Shuffle/Repeat 代理：`ShuffleEnabled` / `RepeatMode` 代理到 `Container`（PlaylistsViewModel 持全局态）
- 拖拽重排 / 入队：`MoveTracks`（引用身份回找）、`DropExternalFiles`
- 排序：`SortBy` / `GetSortKey`
- 导入：`AddToQueue` / `ImportFolderToCurrent`
- 增删清：`RemoveTrack` / `ClearQueue` / `UnloadCurrentTrack`
- 持久化投影：`ToRecord`（纯读无副作用）、`MapCurrentIndexAfterFilter`（启动过滤重映射）
- 重入哨兵：`_playToken`

**未混入无关关注点**：无 DSP/均衡器、无频谱可视化、无直接磁盘 IO（元数据读取委托 `ITrackMetadataReader`、文件对话框委托 `IFileDialogService`）、无对 `PlayerViewModel` 的引用（维持 COUPLING §5「PlayerVM/PlaylistVM 互不持引用」硬规则）。行数偏大主要来自 `MoveTracks`（多选拖拽的引用身份映射，~65 行）与 `PlayTrackAtAsync` 的重入/跳过守卫，以及大量解释性注释——属**内聚复杂度**而非**多职责**。

> **观察项（非拆分触发）：** 该 VM 使用 `System.Windows.Data.ICollectionView` / `ListSortDirection`（WPF 类型）做排序视图。这是既有设计（Phase 12 表头排序），不在 Phase 15 拆分裁决范围内，仅记录以便未来若追求 VM 完全无 WPF 依赖时评估。

**`ViewModels/PlaylistsViewModel.cs`（572 行）— 未越阈值、内聚**

低于 600，未被 M5 标记。职责内聚于「多歌单容器 + 文件夹绑定库扫描」域：歌单集合增删改移、Current/Viewed 指针、全局 Shuffle/Repeat、`StateChanged` 聚合供 debounce save、`Hydrate`/`BuildSnapshot` 持久化投影、库增量扫描（`ImportFolderAsync`/`RescanSinglePlaylistAsync`）。

> **观察项：** 内含 ~50 行 `Null*` 空对象实现（`NullPlaybackService` 等，服务于向后兼容的 internal 构造器/测试）。属轻微关注点混入，但体量小、语义清晰，且 `NullPlaybackService` 是 §5 已登记的「IPlaybackService 第二生产实现者」契约的一部分，无需处理。

---

## 总体结论

- **耦合仍健康。** 客观结构性指标全绿：M2 = 0 cycles、M3 = 0 violations；依赖方向 View→VM→Service→Model 完整、Models/Configuration 为纯基座（out=0）；M7 隐式契约登记册与代码零漂移。印证 COUPLING.md「Phase 13/14 加功能不触碰核心架构」的判断。
- **无需大解耦。** 无任何「必须修」违规（D4），无裁定为拆分的决策点（D1/D5 均未达拆分条件）。
- **D1 = watch-item。** `IPlaybackService` 宽度 20（<24），DSP/viz 关注点 2 个（<3），暂列可接受单一播放门面；但配置类关注点持续挂载，需记录增长趋势供后续 phase 复查。
- **D2 = 保留。** 2 个 stub 对应 PROJECT.md §1.2 仍在路线的多设备/输出模式增量。
- **D5 = 观察项、不拆分。** `PlaylistViewModel` 652 行越阈值但单职责内聚，行数源自内聚复杂度而非关注点混杂。

---

## （条件性）重构立项清单

**无。**

触发条件为「D4 发现 must-fix 违规」或「D1/D5 裁决为拆分」——本次审计 D4 = 0 违规、D1 = watch-item（不拆分）、D5 = 观察项（不拆分），均未触发。故不立项任何重构任务。

> D1 watch-item 建议：后续每个新增 DSP/可视化关注点的 phase，在 COUPLING.md 复查 `IPlaybackService` 宽度；一旦成员数逼近 24 或出现第 3 个 DSP/viz 关注点（如混响/限速器等），再评估分离 effects/分析关注点的独立接口。
