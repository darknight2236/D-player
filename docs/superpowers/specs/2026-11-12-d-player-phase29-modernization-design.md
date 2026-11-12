# Phase 29 设计：非音频路径现代化（激进精简 Core/VM/View 手写基础设施）

- 日期：2026-11-12
- 状态：设计已由用户逐节确认（2026-11-12），待实施。
- 代码基线：`master` / `a1700af`；实施分支 `phase29-modernization`。
- 前置：[Phase 28 设计](./2026-11-12-d-player-phase28-net11-upgrade-design.md) · [Phase 28 验收](../../PHASE28-ACCEPTANCE.md) · [PROJECT.md](../../PROJECT.md) · [COUPLING.md](../../COUPLING.md)。Phase 28 已完成四个工程抬 net11.0；本阶段的代码事实以 2026-11-12 复核为准。

## 1. 已确认的范围

本阶段只做非音频路径的现代化：修两个真缺陷，激进精简 Core/VM/View 的手写基础设施，并把状态模型与 JSON 持久化换成更类型安全、更现代的写法。**音频实时路径一字不动**，留给 Phase 30。

| 决策 | 本期约定 |
|---|---|
| 采纳门槛 | **激进，含基础设施级替换**（用户 2026-11-11 已定；2026-11-12 再次确认三项高风险项全部纳入） |
| 真缺陷 | 修 `RootGates` 无界增长、`FindAncestor` 三份缺陷副本；本阶段最高优先级 |
| 基础设施 | `JsonFileStore`、`OneShotCleanup`、`ThrowIfNull`、后缀白名单统一、静态缓存、`SearchValues`、`GeneratedRegex`、primary constructor、Null Object 删除、分组/差集现代化、`Search` partial property、`System.Threading.Lock` |
| 模型 | `LibraryNavigationSnapshot` → union（失败则 closed 层级）；`LibraryScanResult` → closed 层级 |
| 序列化 | 三个 Json 服务切到 `JsonSerializerContext` 源生成元数据；保留现有 options/转换器语义 |
| 生命周期 | Json 服务/watcher/`LibraryViewModel` 的 `SemaphoreSlim`/CTS 释放；不改 DI 生命周期、不给 VM 新增公开 `IAsyncDisposable` |
| 验收 | 门禁 1/2/3 + 5 条真实音频 + ComputerUse GUI 走查（子代理执行 + 主代理独立复核）+ 独立核对项 |
| 提交 | 每个批次独立提交；设计稿单独提交；推送等用户明确指示 |

**不做**：

- 音频实时路径：`NAudioPlaybackService`、`EqualizerSampleProvider`、`SampleAggregator`、频谱热路径、EQ 插入点与锁纪律（Phase 30）。
- WinUI 壳代码：已冻结，只参与门禁 3 编译；Core 改动流入冻结壳的运行时不做走查。
- `ObservableCollection` 的 `Clear()+foreach Add` 改批量替换：会改通知次数与 UI 刷新行为；附录 C 的“计数断言会红”经复核实为夸大（只有两处订阅），但仍不做。
- `System.Threading.Lock` 替换 async 临界区：不能 await；`COUPLING.md` §5/§7 纪律。
- `PeriodicTimer`：本仓无轮询循环；`LibraryRefreshScheduler` 已有 `TimeProvider`/`ITimer` 骨架，不重写。
- 手写 debounce/throttle 的替换：`MainViewModel` 无专测，搜索 debounce 与音频邻接节流有专测；换通用定时器会改时序，风险高于收益，本期不动。
- `PlaylistImportReport` / `AddToPlaylistResult` 的 union 建模：本期只做用户确认的两个类型。
- AOT/trimming、发布形态、`global.json`、`D-player.slnf` 组成。
- 不修 `DropInsertionAdorner`、`HsvToRgb`、`SampleAggregator` 等无现代替代品或热路径项（Phase 28 附录 C.3）。

**阶段拆分理由**：Phase 28 已把 TFM 抬到 net11.0 并验证工具链；Phase 29 可以在稳定 TFM 上做纯非音频现代化；Phase 30 需要先建音频性能基线，不能与低风险语法精简混在一起。本阶段把高风险的三项（模型、源生成、生命周期）放在批次末尾，失败只回退本批。

**规模预估**：新增/改写约 18 个文件，删除约 300–400 行重复；新增测试 12–14 条，默认门禁从 310 增至约 322+（最终以实跑为准，音频 5 条不变）。

## 2. 复核结论（2026-11-12）

复核方法：Phase 28 附录 C 的只读盘点 + 两个只读代理逐条复核 + 主代理抽查关键文件。**行号取自 2026-11-12 实际输出，实施前必须再复核**（Phase 28 的纪律）。“硬证据”指当前代码事实，不是推测。

### 2.1 真缺陷（硬证据）

- `LibraryRootScanner.cs:10-11` 是 `static readonly ConcurrentDictionary<string, SemaphoreSlim> RootGates`；`:32` 只 `GetOrAdd`；`:41` 只 `Release`；全文件无 `TryRemove`。根来源为 `AppSettings.LibraryFolders` 与 `Playlist.SourceFolder`；`LibraryPathNormalizer.NormalizeMany` 去重但不设上限。删歌单/换目录后条目永不回收，会话内按根路径无界增长。DI 为 Singleton（`ServiceCollectionExtensions.cs:51`），进程生命周期内一直存在。
- `FindAncestor<T>` 四份：`LibraryView.xaml.cs:493-510` 处理 `Visual`/`Visual3D` 与 `ContentElement`（`ContentOperations.GetParent` + `FrameworkContentElement.Parent`）；`PlayerBar.xaml.cs:74-82`、`PlaylistsSidebarView.xaml.cs:207-215`、`PlaylistView.xaml.cs:523-531` 三份逐字相同，直接 `VisualTreeHelper.GetParent`，遇 `ContentElement` 抛 `InvalidOperationException`。`PROJECT.md:1199` 记录过同源进程崩溃（`Run` 元素），当时只修了 LibraryView 一份。三份缺陷副本目前是潜伏缺陷：其余模板只用 `TextBlock.Text`，但任何未来在模板里加 `Run` 都会复现。
- `FindChildByName<T>` 三份：`LibraryView.xaml.cs:479-491` 检查根节点自身；`PlaylistsSidebarView.xaml.cs:147-162` 与 `PlaylistView.xaml.cs:135-150` 逐字相同，不检查根节点。当前调用根是 `ListBoxItem`、T 是 `Path`/`TextBlock`，根检查不会命中，可等价合并，但合并时保留根检查以保持 LibraryView 语义。
- `ComputeInsertIndex` 两份逐行同构：`PlaylistsSidebarView.xaml.cs:441-455`、`PlaylistView.xaml.cs:485-499`，仅 `ListBox` 字段不同，可改 `static (ListBox, Point)`。
- Adorner Show/Hide 两份：`PlaylistView.xaml.cs:503-521`、`PlaylistsSidebarView.xaml.cs:457-475`，各 19 行，仅控件名不同。

### 2.2 JSON 持久化（硬证据）

- `JsonLibraryCache.cs:46-48,89-91` 与 `JsonPlaylistService.cs:46-48` 的读盘样板相同（`File.OpenRead` + `StreamReader.ReadToEndAsync`）；原子写 tmp+Move 在 `JsonLibraryCache.cs:111-114`、`JsonPlaylistService.cs:103-106,160-163` 两处；`JsonSettingsPersistence.cs:71-75` 每次 `new JsonSerializerOptions` 并直接 `File.WriteAllTextAsync`，**没有原子写**。
- `JsonPlaylistService.cs:130-168` 的 v1→v2 迁移、`:170-177` 的私有 `QueueStateV1` 无直接测试；`JsonPlaylistServiceTests` 目前只有 3 条。原子写的“中途失败保留旧文件”语义也无测试。
- 三个服务的 `JsonStringEnumConverter` 只用于 `RepeatMode`（`QueueState.RepeatMode` / `Playlist.RepeatMode`）；`AppSettings` 无枚举；`LibraryCacheEntry` 无枚举。`WriteIndented = true` 三者一致。

### 2.3 清理模板（硬证据）

- `MainViewModel.cs:260-290`、`LibraryViewModel.cs:167-180`、`PlaylistsViewModel.cs:115-155` 三处同构：lock + `TaskCompletionSource(RunContinuationsAsynchronously)` + 缓存 Task + 后台 `Complete...Async` + `TrySetResult/TrySetException`。
- 差异：MainVM 内部 flush save、`DisposeAsync`（`:353`）；LibraryVM 在锁内置 `_cleaned = true`、清理 watcher、Cancel `_lifetimeCts`、StopAsync、逐任务吞异常（`Scanning.cs:443-477`）；PlaylistsVM 额外置 `_acceptingFolderTasks = false`、Cancel + Dispose `_folderLifetimeCts`（`:149-153`）。
- 测试锁定：`LibraryViewModelTests.cs:590-625`、`PlaylistsViewModelTests.cs:1011-1043`、`PlaybackResumeTests.cs:496-503` 断言同 Task/幂等/晚到快照被丢弃。抽 helper 必须保留这三点。

### 2.4 后缀白名单（硬证据，修正附录 C）

附录 C 说“后缀白名单循环 3 处同构”，复核后实际是 **5 处以上**且形状不同：

| 位置 | 现状 |
|---|---|
| `AudioConstants.cs:11-14` | 音频后缀的权威常量（`.mp3/.wma/.flac/.aac/.wav`），但只是 `IReadOnlyList<string>` |
| `PlaylistViewModel.cs:370` | 文件对话框过滤器字符串硬编码 |
| `PlaylistViewModel.cs:388-389` | 导入文件夹时又建一个 `HashSet<string>` 硬编码同一组后缀 |
| `DragDropExtensions.cs:35-42` | WPF 侧音频后缀循环（`OrdinalIgnoreCase`） |
| `PlaylistFileService.cs:80` | 音频后缀 LINQ `Any`（`OrdinalIgnoreCase`） |
| `LibraryScannerService.cs:42` | 已用 `HashSet`（`OrdinalIgnoreCase`） |
| `PlaylistFileFormats.cs:17,32-36` | 播放列表白名单（`.m3u/.m3u8/.pls`），`IsPlaylistFile` 循环 |
| `DragDropExtensions.cs:48,58` | 已代理 `PlaylistFileFormats`，无重复 |

统一目标：`AudioConstants` 提供 `FrozenSet` + `IsAudioFile` + 文件对话框过滤器；`PlaylistFileFormats` 内部用 `FrozenSet`，但保留 `Extensions` 的**有序公开属性**（`PlaylistFileParserTests.cs:32` 断言顺序）。所有比较保持 `OrdinalIgnoreCase`、保持输入顺序、不去重。

### 2.5 模型候选（硬证据）

- `LibraryModels.cs:15-20` 的 `LibraryNavigationSnapshot(Page, ArtistName?, Album?, AlbumOpenedFromArtist, AlbumTrackPath?)` 是“枚举 + 可空载荷”的伪和类型。消费点：`LibraryViewModel.Search.cs:148-262`（构造/解析/恢复）、`MainViewModel.cs:290-340`（关闭时持久化导航）、`LibraryViewModelTests.cs:710`（断言 `.Page`）。非法组合（如 `Page=Artists` 却带 `ArtistName`）目前无法被类型系统排除。
- `LibraryScanResult.cs:10` 的 `(IReadOnlyList<string> Files, bool IsUnavailable)` 是“结果或不可用”的和类型；消费点：`LibraryScannerService.cs:27-68`、`LibraryRootScanner.cs:51-70`、`PlaylistsViewModel.cs:860`（Null 实现）、`LibraryRootScannerTests.cs:40,77,134,167,214,246,279`。
- `LibraryRootSnapshot`（`ILibraryRootScanner.cs:5-11`）有 5 个字段和多组布尔标志，不是自然和类型；本期不改。
- `RepeatMode`/`PlayState` 等持久化枚举不适合 union；`RepeatMode` 经 `JsonStringEnumConverter` 持久化为字符串，改类型会破契约。本期不改。

### 2.6 生命周期与 DI（硬证据）

- `LibraryViewModel.cs:28-29` 的 `_coverGate`/`_lifetimeCts`、`Search.cs:17` 的 `_queryCts` 只 Cancel/换新，不 Dispose；`FileSystemLibraryWatcher.cs:12` 的 `_replaceGate` 在 `:82-96` 的 `Dispose` 中未释放；三个 Json 服务的 `_lock`（`JsonLibraryCache.cs:22`、`JsonPlaylistService.cs:25`、`JsonSettingsPersistence.cs:17`）从不释放。
- `PlaylistsViewModel.cs:149-153` 已 Dispose `_folderLifetimeCts`；`MainViewModel.cs:306-307` 已 Dispose `_saveCts`；`MainViewModel` 实现 `IAsyncDisposable`（`:21,353`），`LibraryViewModel`/`PlaylistsViewModel` 不实现。
- DI 全 Singleton（`ServiceCollectionExtensions.cs:30-54`）；`App.xaml.cs:76-79` 在 `OnExit` Dispose ServiceProvider → 只释放实现了 `IDisposable` 的服务（当前只有 NAudioPlaybackService 等）；`MainViewModel` 是 Transient，由 `MainWindow.xaml.cs:118` 调 `CleanupAsync`，不经容器释放。给 Json 服务加 `IDisposable` 即可被容器回收；给 VM 加 `IAsyncDisposable` 会与手动 Cleanup 形成双重路径，本期不做。

### 2.7 测试覆盖缺口（硬证据）

- 无直接测试：`RootGates` 回收、`JsonPlaylistService` 的 v1→v2 迁移与原子写、settings 原子写、全部 `Converters` 与 `Views`、五个私有桶类、`ArgumentNullException` 路径、CTS/Semaphore 释放、`union/closed` 候选、`AddToPlaylistResult`。
- 有覆盖：分组（`LibraryGroupingTests` 15 条）、差分（`LibraryScannerServiceTests`）、后缀（`PlaylistFileParserTests:32` 顺序断言）、清理时序（`LibraryViewModelTests:590-625`、`PlaylistsViewModelTests:1011-1043`、`PlaybackResumeTests:496-503`）、`SanitizeFileName`（`PlaylistViewModelTests:368`）、Json 读写（三文件间接）。
- `Tests/D-player.Tests.csproj` 只引用 Core，不引用 WPF → 壳侧 `Views`/`Converters` 无法单测，合并重构必须靠 GUI 走查兜底。

### 2.8 对 Phase 28 附录 C 的三处修正

1. Null Object/internal 构造器不止 `PlaylistsViewModelTests.cs:47` 一处；`:60` 的 `CreateContainerVmPreservingSeeds` 也被 3+ 测试调用，改 NSubstitute 时要同时改两个辅助方法。
2. JSON 原子写与 v1→v2 迁移**当前没有测试**，不是“被测试锁定”；Phase 29 必须先补契约测试再重构。
3. “`Clear()+foreach Add` 改批量替换会让通知计数断言红”被夸大：全 Tests 只有 `LibraryViewModelTests.cs:321-325`（无变化时 0 次）与 `:667-681`（等待一次变更）两处订阅 `CollectionChanged`，没有严格计数断言。即便如此仍不做批量替换，因为会改变 UI 刷新行为，不属于本阶段收益。

### 2.9 补充候选（2026-11-12 复核，用户 2026-11-12 确认全部纳入）

- **Null Object 与 internal 兼容构造器**：`PlaylistsViewModel.cs:110-113` 的 2 参 internal 构造器 + `:847-913` 的 6 个私有 Null* 服务；生产代码 0 使用，测试只有 `PlaylistsViewModelTests.cs:47`（`CreateContainerVm`）与 `:60`（`CreateContainerVmPreservingSeeds`）两个辅助方法使用。NSubstitute 已在测试依赖中；删除后测试辅助方法改用 mock，公开 8 参构造器不变。
- **分组/差集样板**：`LibraryGrouping.cs:32-44` 用 `Dictionary.TryAdd` 实现“先到者胜”，`:46-55` 依赖字典插入序；`LibraryScannerService.cs:104-154` 用字典做差集（重复路径后写覆盖）并按枚举顺序产出 added/unchanged。`LibraryGroupingTests.cs:65,89,112-231` 锁定合辑/多碟顺序与“重复路径取首条 Title”；`LibraryScannerServiceTests` 只断言计数/集合，无顺序断言。现代化只能替换同构样板，**先到者胜、后写覆盖、枚举顺序三条语义逐字保留**。
- **Search 手写 INPC**：`LibraryViewModel.Search.cs:21-51` 三个属性 `SearchQuery`/`SearchResults`/`SearchMessage` 手写 backing field + `OnPropertyChanged()`，私有 setter 绕开源生成器；其中 `SearchQuery`/`SearchMessage` 有相等性短路，`SearchResults` 每次必通知。先用编译探针确认 Toolkit 8.4.2 partial property 能否保留 private setter 与通知语义；能则替换，不能则保留手写并在验收记录偏离。
- **普通 `lock` 字段**：Core 内非音频的 `object` gate 至少有 `FileSystemLibraryWatcher.cs:7`、`LibraryRefreshScheduler.cs:32`、`LibraryScanLease.cs:7`、`LibraryViewModel.cs:30-31`、`MainViewModel` 的 `_cleanupGate`、`PlaylistsViewModel` 的 `_folderGate`。全仓无 `Monitor.Wait/Pulse`，可安全改为 `System.Threading.Lock`（C# 13+，`lock` 语句语义等价）。**音频路径的 `EqualizerSampleProvider.cs:22` 与 `NAudioPlaybackService.cs:42` 不改**；`SemaphoreSlim` 的 async 临界区不改。

## 3. 改动清单

### 3.1 真缺陷（批次 1）

| 位置 | 改动 |
|---|---|
| `LibraryRootScanner.cs:10-11,32,41` | 删除 static `RootGates`；新增内部 `RootGateRegistry`（实例、引用计数、归零移除并 Dispose）；`ScanAsync` 改为 `using var gateLease = await _rootGates.EnterAsync(normalized, lease.Token).ConfigureAwait(false)`（避免与现有 `LibraryScanLease lease` 参数重名），扫描结束后自动回收 |
| `Tests/Services/LibraryRootScannerTests.cs` | 保留同根串行测试；新增异根并行、取消不泄漏、`ActiveRootGates` 归零测试 |
| `Views/Controls/LibraryView.xaml.cs:493-510`、`PlayerBar.xaml.cs:74-82`、`PlaylistsSidebarView.xaml.cs:207-215`、`PlaylistView.xaml.cs:523-531` | 合并到 `Views/Controls/VisualTreeHelpers.cs` 的单一 `FindAncestor<T>`，统一处理 `Visual`/`Visual3D`/`ContentElement` |
| `Views/Controls/LibraryView.xaml.cs:479-491`、`PlaylistsSidebarView.xaml.cs:147-162`、`PlaylistView.xaml.cs:135-150` | 合并 `FindChildByName<T>`，保留根节点检查 |
| `Views/Controls/PlaylistsSidebarView.xaml.cs:441-455`、`PlaylistView.xaml.cs:485-499` | 合并 `ComputeInsertIndex(ListBox, Point)` |
| `Views/Controls/PlaylistView.xaml.cs:503-521`、`PlaylistsSidebarView.xaml.cs:457-475` | 合并 Adorner Show/Hide helper |

### 3.2 基础设施与机械现代化（批次 2）

| 位置 | 改动 |
|---|---|
| `Services/JsonLibraryCache.cs`、`JsonPlaylistService.cs`、`JsonSettingsPersistence.cs` | 抽 `JsonFileStore`：`ReadAllTextAsync`、`WriteAtomicAsync`（tmp + `File.Move(overwrite: true)`）；服务保留各自的 `_lock`，存储 helper 不持锁 |
| `JsonSettingsPersistence.cs:71-75` | 改用 `JsonFileStore.WriteAtomicAsync` + 静态 `JsonSerializerOptions`；失败时旧文件保留 |
| `MainViewModel.cs:260-290`、`LibraryViewModel.cs:167-180`、`PlaylistsViewModel.cs:115-155` | 抽 `OneShotCleanup`（同 Task、幂等、`RunContinuationsAsynchronously`、晚到完成用 `TrySet*` 丢弃、异常经 Task 外抛）；三处保留各自的关闭逻辑 |
| Core 33 处 + Views 5 处 `?? throw new ArgumentNullException` | 改 `ArgumentNullException.ThrowIfNull`；`ThrowIfNullOrWhiteSpace` 保持现状 |
| `AudioConstants.cs`、`PlaylistViewModel.cs:370,388-389`、`DragDropExtensions.cs:35-42`、`PlaylistFileService.cs:80`、`LibraryScannerService.cs:42` | 统一音频后缀到 `AudioConstants`（`FrozenSet` + `IsAudioFile` + `FileDialogFilter`）；消除硬编码 HashSet 与循环 |
| `PlaylistFileFormats.cs:17,32-36` | 内部改 `FrozenSet`；保留 `Extensions` 有序公开属性 |
| `PlaylistViewModel.cs:484-495` | `SanitizeFileName` 的 `Array.IndexOf` → `SearchValues<char>` 静态字段 |
| `LibraryIdentity.cs:14-16` | `RegexOptions.Compiled` → `[GeneratedRegex(...)]`，`LibraryIdentity` 改 `partial` |
| `LibraryGrouping.cs:16-28`、`LibraryRefreshScheduler.cs:506-568` | 五个私有桶类改 primary constructor；**不用 record**（避免 CTS/TCS 的结构相等） |
| `PlaylistsViewModel.cs:110-113,847-913`、`PlaylistsViewModelTests.cs:47,60` | 删除 internal 兼容构造器与 6 个私有 Null* 服务；两个测试辅助方法改用 NSubstitute；公开 8 参构造器不变 |
| `LibraryGrouping.cs:32-55`、`LibraryScannerService.cs:104-154` | 手写去重/建桶/字典差集换成 `DistinctBy`/集合表达式/字典样板；先到者胜、后写覆盖、枚举顺序逐字保留，现有分组/差分测试断言不改 |
| `LibraryViewModel.Search.cs:21-51` | 编译探针通过后，三个手写 INPC 属性改 Toolkit 8.4.2 partial property；保留 private setter 与相等性/必通知语义；探针失败则保留手写并记录偏离 |
| 非音频 `lock` 字段（`FileSystemLibraryWatcher.cs:7`、`LibraryRefreshScheduler.cs:32`、`LibraryScanLease.cs:7`、`LibraryViewModel.cs:30-31`、`MainViewModel` `_cleanupGate`、`PlaylistsViewModel` `_folderGate`） | `object` gate → `System.Threading.Lock`；全仓无 `Monitor.Wait/Pulse`；音频 `EqualizerSampleProvider`/`NAudioPlaybackService` 与 async `SemaphoreSlim` 不改 |

### 3.3 模型（批次 3）

| 位置 | 改动 |
|---|---|
| `LibraryModels.cs:15-20` | `LibraryNavigationSnapshot` → union（首选）；case：`ArtistsNavigation`、`AlbumsNavigation`、`ArtistDetailNavigation(string ArtistName)`、`AlbumDetailNavigation(string? ArtistName, LibraryAlbumKey Album, bool OpenedFromArtist, string? AlbumTrackPath)` |
| `LibraryViewModel.Search.cs:148-262` | 构造/解析/恢复改为 switch 穷尽匹配；`_searchReturn` 类型不变（union 或 closed 基类） |
| `MainViewModel.cs:290-340` | 关闭时持久化改为 switch；`LibraryViewPage` 字符串保持现有取值 |
| `LibraryViewModelTests.cs:710` | `.Page` 断言改为模式匹配 |
| `LibraryScanResult.cs:10` | 改 closed：`ScanAvailable(IReadOnlyList<string> Files)` / `ScanUnavailable` |
| `LibraryScannerService.cs:27-68`、`LibraryRootScanner.cs:51-70`、`PlaylistsViewModel.cs:860`、`LibraryRootScannerTests.cs` | 按 closed 分支更新构造与判断 |

**union 探针**：批次 3 开始前在系统临时目录建最小 `net11.0-windows` 工程，验证 union 与 record、可空载荷、`switch` 穷尽性、`LibraryNavigationSnapshot?` 可空性。探针失败 → 两个类型都用 `closed record` 层级（`ArtistsNavigation` 等继承基类），并在设计稿追加偏离记录；仍失败 → 停止并报告。

### 3.4 JSON 源生成（批次 4）

| 位置 | 改动 |
|---|---|
| 新增 `Services/DPlayerJsonContext.cs` | `[JsonSerializable(typeof(QueueState))]`、`AppSettings`、`Dictionary<string, List<LibraryCacheEntry>>`、`QueueStateV1`；`internal partial class DPlayerJsonContext : JsonSerializerContext` |
| `JsonLibraryCache.cs:16-20`、`JsonPlaylistService.cs:19-23` | 现有 `JsonSerializerOptions` 增挂 `TypeInfoResolver = DPlayerJsonContext.Default`，保留 `WriteIndented` 与 `JsonStringEnumConverter` |
| `JsonSettingsPersistence.cs` | 静态 options 同样挂 resolver |
| `JsonPlaylistService.cs:170-177` | `QueueStateV1` 移为 `internal sealed record` 顶层类型（源生成器需可访问） |
| `Tests/Services/*Json*Tests.cs` | 新增黄金 JSON 对照（改实现前用现有反射序列化固化）、v1 fixture 迁移、未知字段容忍、枚举字符串、缓存字典往返；settings 原子写失败保留旧文件 |

**注意**：源生成只替换元数据来源，不改变 `JsonSerializerOptions` 的转换器与写入选项；黄金 JSON 必须逐项对照。若 `JsonStringEnumConverter` 与源生成冲突，改用 `UseStringEnumConverter` 或对该类型保留反射元数据，并在设计稿记录偏离。

### 3.5 生命周期（批次 5）

| 位置 | 改动 |
|---|---|
| `JsonLibraryCache.cs:22`、`JsonPlaylistService.cs:25`、`JsonSettingsPersistence.cs:17` | 实现 `IDisposable`，Dispose `_lock`；`App.OnExit` 的 ServiceProvider Dispose 负责回收 |
| `FileSystemLibraryWatcher.cs:12,82-96` | `Dispose` 中释放 `_replaceGate`；保持 `_disposed` 幂等 |
| `LibraryViewModel.cs:28-29`、`Search.cs:17`、清理路径 | 在 `CleanupAsync` 完成、所有内部任务（含封面任务）收尾后 Dispose `_lifetimeCts`、`_coverGate` 与当前 `_queryCts`；搜索换新 CTS 时确保旧的已 Cancel 且无后续使用 |
| `PlaylistsViewModel.cs:149-153` | 保持现有 Dispose；补幂等测试 |
| `Tests` | Dispose 幂等、关闭路径不抛 `ObjectDisposedException` |

### 3.6 文档（批次 6）

`docs/PROJECT.md`（当前状态段）、`docs/COUPLING.md`（若契约描述受影响）、`docs/README.md`（文档地图）、`README.md`（仅行为/限制变化时）、新增 `docs/PHASE29-ACCEPTANCE.md`。不回改 Phase 20–28 历史文档。

## 4. 关键设计决策

### 4.1 RootGateRegistry 的不变量

- `Users` 统计所有已进入 `EnterAsync` 但尚未 `Exit` 的调用（含等待者与持有者）；`Users == 0` 是移除并 Dispose 的唯一条件，且在 registry 锁内判断。
- `SemaphoreSlim.WaitAsync` 在 registry 锁外；取消时在 catch 中 `Exit`，不会泄漏计数。
- 移除后新调用创建新 gate；旧 gate 已无持有者/等待者，不会出现同根并行。
- `LibraryRootScanner` 实例持有 registry（Singleton），内存上限受进程内活跃/历史根数量影响，但每个根在最后一个使用者退出后立即归零；不再跨实例泄漏。
- 测试通过 `internal int ActiveRootGates` 观察归零。

### 4.2 壳侧 helper 的边界

- `VisualTreeHelpers.FindAncestor<T>` 只处理向上查找；`ContentElement` 分支与 `LibraryView` 现有实现逐字等价。
- 合并后的 `FindChildByName<T>` 保留根节点检查；`ComputeInsertIndex` 只依赖 `ListBox` + `Point`，不依赖视图字段。
- 新文件放在 `Views/Controls`，自动被 csproj 默认 glob 纳入；WinUI 有独立的 `Theme/VisualTree.cs`，**不改**。
- 这是行为修复（三份副本从抛异常变为可正常查找），GUI 走查必须覆盖含 `Run` 的文本与拖拽/右键路径。

### 4.3 JsonFileStore 的语义

- 读：`File.OpenRead` + `StreamReader.ReadToEndAsync`，与现状一致；文件不存在由调用方判断。
- 写：先写 `path + ".tmp"`，再 `File.Move(tmp, path, overwrite: true)`；写或 Move 失败时旧文件保留，tmp 尽力删除，异常外抛（settings 关闭路径由调用方吞掉）。
- 存储 helper 不持锁；三个服务保留各自的 `SemaphoreSlim`，锁内调用 helper，保持“读-改-写”原子性。
- settings 由直接写改为原子写是**行为改进**（写失败保留旧文件），非纯重构；验收要覆盖。

### 4.4 OneShotCleanup 的语义

- `RunAsync(Func<Task>)` 第一次调用创建 TCS 并启动 `CompleteAsync`；后续调用返回同一个 Task。
- `CompleteAsync` 用 `TrySetResult`/`TrySetException`，晚到完成被丢弃；异常通过返回的 Task 外抛。
- 各 VM 在调用前完成自己的状态切换（`_cleaned = true`、`_acceptingFolderTasks = false` 等），helper 不感知业务。
- 三处现有测试必须继续通过；新增 helper 级测试覆盖同 Task/幂等/晚到丢弃/异常外抛。

### 4.5 union/closed 的取舍

- 首选 union：`LibraryNavigationSnapshot` 是自然和类型，非法组合可被类型消除；`LibraryScanResult` 用 closed 层级更直观（两分支）。
- `closed` 的所有派生类型必须在同一程序集；Core 内满足。
- union/closed 都会改变 `LibraryNavigationSnapshot`/`LibraryScanResult` 的公开形状；它们只在 Core 内部与测试中使用，WPF/WinUI 通过 ViewModel 间接使用，不直接绑定这些类型。
- 若 union 探针失败，两个类型都降级为 `closed record` 层级；这仍满足“union/closed 建模”的用户选择，但要在验收文档记录偏离原因。

### 4.6 JSON 源生成的契约策略

- 不改 `JsonSerializerOptions` 的 `WriteIndented`/`Converters`/`PropertyNamingPolicy`（后者未设置，默认 PascalCase）；只挂 `TypeInfoResolver`。
- 黄金 JSON 在改实现之前用现有反射路径生成并写成测试常量；改后同一对象序列化必须语义一致。对 `QueueState`/`AppSettings`/缓存字典各一组。
- v1 fixture 直接以字符串常量写入测试，覆盖 `SchemaVersion=1`、Items/CurrentIndex/Shuffle/Repeat 缺失与越界；迁移后再读回断言 v2/v3 形状。
- 原子写失败测试：占用目标文件（`FileShare.None`）→ 保存抛异常 → 旧内容不变。
- 源生成失败（编译期 `SYSLIB` 诊断或运行时 `NotSupportedException`）→ 回退批次 4，保留批次 2 的 `JsonFileStore` 与契约测试；不压警告。

### 4.7 生命周期释放顺序

- `JsonSettingsPersistence` 在 `MainViewModel.CleanupCoreAsync` 的最后一次 `UpdateAsync` 之后才允许被 Dispose；`App.OnExit` 在窗口关闭后执行，顺序满足。
- `LibraryViewModel.CleanupAsync` 先 Cancel `_lifetimeCts`、停止 watcher、等待 cover/search 任务，再 Dispose；`_queryCts` 在搜索任务收尾后 Dispose。
- `FileSystemLibraryWatcher.Dispose` 先释放 watcher 集合，再释放 `_replaceGate`；防止正在 `ReplaceRootsAsync` 的调用在 gate 释放后访问。
- 不给 `LibraryViewModel`/`PlaylistsViewModel` 新增 `IAsyncDisposable`，避免与 `MainViewModel` 手动清理形成双重释放路径；释放逻辑封装在现有 `CleanupAsync` 内。

### 4.8 Null Object、分组/差集、Search INPC 与 Lock

- **Null Object 删除**：只删 internal 2 参构造器与 6 个私有 Null* 类；公开 8 参构造器签名不变，生产代码调用点不变。测试两个辅助方法改用 NSubstitute 的 `Substitute.For<T>()`，现有测试断言不改。这是删除测试专用后门，不是行为变更。
- **分组/差集**：`DistinctBy` 会保留首次出现的元素与输入顺序，可等价替换 `LibraryGrouping` 的 `TryAdd` 去重；建桶仍用 `Dictionary`（插入序）；差集不能用 `ToDictionary`（重复键会抛且改变“后写覆盖”语义），只把样板写法换成集合表达式/`TryAdd` 等，语义逐字保留。现有 `LibraryGroupingTests` 与 `LibraryScannerServiceTests` 不放宽。
- **Search INPC**：先验证 Toolkit 8.4.2 的 partial property 生成器（C# 13 partial property）是否支持 `private set`、是否生成 `SetProperty` 相等性短路。`SearchQuery`/`SearchMessage` 当前有短路，`SearchResults` 当前必通知；生成器语义不能逐项对上就保留对应属性的手写实现，不为了统一而改变通知次数。
- **System.Threading.Lock**：只替换非音频的普通 `lock` 字段，且全仓无 `Monitor.Wait/Pulse`；`Lock` 用 `lock` 语句时与 `object` 语义等价。音频锁与 async `SemaphoreSlim` 不在范围内，避免触碰 Phase 30 的锁纪律。
- **延后**：手写 debounce/throttle 不改；替换会改变时序，且 `MainViewModel` 无专测，收益低于风险。

## 5. 失败域与回退

### 5.1 失败域

| 域 | 覆盖内容 | 抓它的证据 |
|---|---|---|
| **A. Core 行为** | RootGates 回收、cleanup 模板、参数校验、后缀白名单、小项现代化、union/closed 模型 | 门禁 1/2 + 新增专测；模型改动有编译期穷尽性检查 |
| **B. 持久化契约（最高风险）** | `JsonFileStore`、settings 原子写、JSON 源生成、v1→v2 迁移 | 黄金 JSON 对照 + 往返契约测试 + GUI 保存/重启走查 |
| **C. 壳侧树查找/拖拽（无单测）** | `FindAncestor`/`FindChildByName`/`ComputeInsertIndex`/Adorner 合并 | 只能靠 GUI 走查：库四级钻取、右键菜单、拖拽重排、歌单侧栏 |
| **D. 生命周期/关闭时序** | Json 服务 `IDisposable`、watcher gate、LibraryVM CTS 释放 | 现有清理时序测试 + 关闭/重启走查；新增幂等释放测试 |
| **E. 工具链/环境** | 构建进程占用、旧输出目录、脚本编码、MTP 退出码 | Phase 28 纪律：先查 `D-player.exe`、启动新目录 exe、脚本纯 ASCII、测试不加 `--nologo`、零匹配退出码 8 不算成功 |

### 5.2 分域风险与缓解

- **RootGates 回收的并发正确性**：引用计数在锁内增减，`WaitAsync` 在锁外；只有计数归零且无持有者/等待者时才移除并 Dispose。专测：同根串行、异根并行、取消不泄漏、扫描完成后归零。若暴露竞态，退回“实例字典 + 永不回收”并停下来报告，不硬凑无锁方案。
- **JSON 源生成的契约漂移**：先固化、后替换。先在**不改产品代码**的前提下用现有反射序列化生成黄金 JSON（固定 GUID/时间），再挂 `TypeInfoResolver`，断言同一对象的输出与黄金 JSON 语义一致；另测 v1 fixture 迁移、未知字段容忍、枚举字符串、缓存字典往返。`JsonStringEnumConverter` 若与源生成冲突，改用 `UseStringEnumConverter` 或对该类型保留反射元数据，并记录偏离。
- **settings 原子写**：写入路径从 `WriteAllText` 改为 tmp+Move；语义差异只有“中途失败保留旧文件”。测试用占用目标文件（`FileShare.None`）模拟 Move 失败，断言旧内容不变、tmp 可清理；正常路径断言内容可读。
- **union/closed 的语法/运行时约束**：实施前在系统临时目录做最小编译探针（net11.0-windows + 当前 SDK），确认 `union`/`closed` 在 record、可空载荷、`switch` 穷尽性下的实际行为。探针失败 → 降级为 `closed record` 层级；仍失败 → 停止并报告，不擅自改回普通 record 还声称完成该节。
- **壳侧合并改变命中行为**：三份缺陷副本原本遇 `ContentElement` 会抛，合并后变成能正常向上查找——这是修复，但属于行为变化。GUI 走查覆盖含 `Run` 的文本、右键菜单、拖拽插入、侧栏选择；若出现新的误命中，先回退壳侧批次再分析。
- **生命周期释放顺序**：`SemaphoreSlim.Dispose` 时若仍有等待者会抛 `ObjectDisposedException`。只在 `CleanupAsync` 完成、所有内部任务收尾后释放；`LibraryViewModel` 的 `_queryCts` 换新/取消路径确保旧实例在安全时释放。DI 生命周期不动，`App.OnExit` 的容器 Dispose 只负责 Singleton 服务；`MainViewModel` 仍手动 `CleanupAsync`。关闭挂起即回退该批次。
- **Rider 红线**：沿用 Phase 28 纪律——不为 IDE 诊断改产品代码、`#pragma` 保持 0、不用 `NoWarn` 压警告；本轮改动后重新观察。

### 5.3 回退策略

- 分支 `phase29-modernization`，**每批独立提交**。顺序刻意把高风险项放在最后：真缺陷 → 机械/基础设施 → 模型 → 源生成 → 生命周期 → 文档。
- 单项失败只回退该批：壳侧 helper 是新增独立文件，回退不影响 Core；源生成失败可只撤 `TypeInfoResolver` 与 context，保留 `JsonFileStore` 与契约测试；union 探针失败只影响模型批。
- 任一关门红：**失败即停**，记录真实错误原文与失败阶段，不在同一轮叠多个修法；域 B/C 尤其要先分清是契约、编译还是 UI 命中问题。
- **推送等用户明确指示**；不主动改 WinUI、不碰音频实时路径。

### 5.4 数据保护

GUI 走查前备份 `settings.json` / `queue.json` / `library-cache.json` 并记 SHA-256；用临时合成库隔离真实数据；结束后按字节还原并核对三文件 SHA-256 相等；临时库、备份、脚本、截图删除；不修改用户音乐文件。

## 6. 实施顺序与执行纪律

### 6.1 批次

分支 `phase29-modernization`，从 `master` / `a1700af` 切出。每批独立提交、独立回退，任何一批失败都不牵连前序批次。

| 批次 | 内容 | 提交粒度 | 验证 |
|---|---|---|---|
| **1. 安全网 + 真缺陷** | 先补 Red 测试（RootGates 归零/并发）；补 Json 黄金契约测试（此步不改产品代码，测试应为绿）；修 RootGateRegistry；合并壳侧树查找/拖拽 helper | `test(core): ...` / `fix(core): recycle root scan gates` / `refactor(wpf): unify visual tree helpers` | 门禁 1/2；壳侧改动留到验收 GUI |
| **2. 机械现代化 + 基础设施** | ThrowIfNull、FrozenSet/AudioConstants、SearchValues、GeneratedRegex、静态 options、primary ctor、Null Object 删除、分组/差集样板、Search partial property 探针、非音频 Lock；`JsonFileStore` 统一；`OneShotCleanup` helper | 按关注点分 4–5 个 `refactor(...)` 提交 | 门禁 1/2 + 新增 cleanup/原子写测试 |
| **3. 模型** | 临时目录编译探针 → `LibraryNavigationSnapshot` union/closed、`LibraryScanResult` closed；更新消费点与测试 | `refactor(core): model library state as closed types` | 门禁 1/2；探针失败即停并按 4.5 降级或报告 |
| **4. JSON 源生成** | `DPlayerJsonContext` + `TypeInfoResolver`；黄金 JSON 断言不变；v1 fixture 迁移 | `refactor(core): switch json services to source-generated metadata` | 门禁 1/2；契约差异即回退本批 |
| **5. 生命周期** | Json 服务/watcher/CTS 释放；幂等与关闭时序测试 | `fix(core): dispose long-lived synchronization primitives` | 门禁 1/2 |
| **6. 验收 + 文档** | `verify-gates -Full -Audio`、GUI 走查、独立核对、文档同步、`PHASE29-ACCEPTANCE.md` | `docs(phase29): ...` | 全档验收 |

**顺序理由**：真缺陷先落地；机械/基础设施在模型之前，减少文件冲突面；三项高风险（模型、源生成、生命周期）放最后，失败只回退本批，前面已交付的价值不受影响。

### 6.2 测试驱动纪律

- **行为新增先写红测试**：RootGates 归零、OneShotCleanup 语义、生命周期释放。
- **重构前先固化**：Json 黄金契约、v1→v2 迁移、settings 原子写在改实现之前就应有测试（这些是特性测试，不是红测试）。
- **探针先行**：Search partial property 与 union/closed 各做独立编译探针；探针不过就保留原写法并记录偏离，不把“没换成”写成“已完成”。
- 每个批次结束必须门禁 1/2 全绿，不允许把红灯留到下一批。
- 现有测试可改写构造方式（如 `LibraryScanResult` 的 closed 化、Null Object 测试辅助方法改 mock），但不放宽断言来换绿。

### 6.3 执行纪律（沿用 Phase 28）

1. 构建前确认没有运行中的 `D-player.exe` / `D-player.WinUI.exe`；有则请用户关闭，不擅自杀进程。
2. 长命令给足前台超时，不用后台跑再轮询，不靠 `TaskStop`（本机不杀进程树）。
3. `tools/verify-gates.ps1` 的修改保持纯 ASCII。
4. 两条测试命令不加 `--nologo`；MTP 零匹配退出码 8 不算成功。
5. 确认编译真的跑了（全增量假绿）；GUI 必须启动 `bin/Debug/net11.0-windows/` 下新产物。
6. **失败即停，不叠修复**；先分清失败域，再报告取证。
7. 不碰音频实时路径文件、不碰 WinUI 壳、不加 `#pragma warning disable` / `NoWarn`。
8. `docs/superpowers/plans/` 保持未跟踪；**推送等用户明确指示**。
9. 发现隐藏复杂度或范围外问题：停下说明，由用户决定是否扩大或另开阶段，不顺手改。

### 6.4 每批完成定义

门禁 1/2 全绿 + 该批新增测试通过 + `git diff --stat` 记录；涉及 UI/持久化/关闭时序的批次在最终验收统一做 GUI 走查；全部门禁与走查通过后才写 `PHASE29-ACCEPTANCE.md`。

## 7. 测试与验收

### 7.1 统一门禁

入口：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full -Audio`，末行必须是 `all requested steps passed.`。

| 关 | 判据 | 本期基线 |
|---|---|---|
| 门禁 1 | `dotnet build D-player.slnf -c Debug --nologo -v q` | 0 警告 0 错误 |
| 门禁 2 | 默认测试（`Category!=AudioDevice`） | 310 + 本期新增；**实跑数字为唯一口径**，0 失败 0 跳过 |
| 门禁 3 | WinUI 单工程构建 | 0/0（壳冻结，只验证 Core 公开面仍编译） |
| 音频 | `Category=AudioDevice` | 5 通过（真实 WASAPI 设备） |

新增测试按节落位：

- **RootGates**：同根串行（现有测试保留）、异根并行、取消不泄漏、扫描完成后归零。
- **Json 契约**：QueueState / AppSettings / LibraryCache 的黄金 JSON 对照（固定 GUID/时间，先固化再改源生成）；v1 fixture → v2 迁移；settings 原子写失败保留旧文件；源生成往返。
- **cleanup helper**：同 Task、幂等、晚到完成丢弃、异常外抛。
- **生命周期**：Dispose 幂等，关闭路径不抛 `ObjectDisposedException`。
- **模型**：导航快照恢复（现有测试改写）、扫描结果 available/unavailable 分支。
- **Null Object 删除**：现有 `PlaylistsViewModel` 测试改用 NSubstitute 后全绿；公开构造器路径不变。
- **分组/差集与 Lock**：现有分组/差分/调度器测试断言不改；Lock 替换不新增行为断言，只靠现有测试与编译。
- **Search INPC**：若替换为 partial property，保留/补充属性通知测试；探针失败则不改。

### 7.2 独立核对项（不混在测试计数里）

- Core WPF-free 自检 grep 仍零命中。
- 耦合审计 `0 cycles / 0 violations`。
- `dotnet publish ... --self-contained false` 仍能产出。
- 输出目录为 `bin/Debug/net11.0-windows/`，`D-player.exe` 可启动。
- Rider 红线重新观察（由用户在 IDE 内确认）。
- `#pragma warning disable` 与 `NoWarn` 数量仍为 0。
- 改动文件清单与 `git diff --stat` 留档。

### 7.3 GUI 走查（ComputerUse 子代理执行 + 主代理独立复核）

沿用 Phase 28 的关键路径：起窗 → 播放/暂停/上一首/下一首 → 进度条单击与拖动 → 音量/静音 → 频谱 → EQ 打开/拖动/保存/取消 → 库四级钻取 + 搜索 → 库右键加入歌单 → 歌单拖拽重排 → 导入 GBK 编码 .m3u → 关闭落盘 → 重启断点续播。

本期**额外必测**：

- 壳侧 helper 合并后：库/歌单/侧栏三处的右键菜单、拖拽插入、含 `Run` 的文本行选择。
- 持久化：新建/重命名/删除歌单、文件夹绑定歌单、修改音量/EQ/频谱后关闭重启，设置与歌单均保留。
- 生命周期：扫描进行中关闭窗口、保存 debounce 进行中关闭窗口，均不挂起、不丢数据。

合成库要求：含 GBK(936) 编码、路径带中文的 `.m3u`；至少两个艺术家、多张专辑（含多碟）以覆盖四级钻取；一个文件夹绑定歌单。数据保护按 5.4 执行。

### 7.4 完成定义

- 四关全绿且测试计数真实增长；GUI 走查无失败项；三份用户数据 SHA-256 还原一致。
- 音频实时路径零文件改动；WinUI 零文件改动。
- 每批提交信息遵循仓库风格（`fix(core):` / `refactor(core):` / `refactor(wpf):` / `test(...)` / `docs(phase29):`）。
- 推送等用户指示。

## 8. 文档同步与提交边界

| 文件 | 改动 |
|---|---|
| `docs/PROJECT.md` | 更新当前状态：RootGateRegistry、JsonFileStore/源生成、壳侧 helper、union/closed 模型、测试计数；历史决策段落保留原文 |
| `docs/COUPLING.md` | 若 `RootGates`/cleanup 契约在文中有描述则同步；不动历史验收读数 |
| `docs/README.md` | 文档地图加 Phase 29 设计/验收链接 |
| `README.md` | 仅当行为或限制发生变化时更新（settings 原子写属稳健性改进，不改用户可见功能） |
| `docs/PHASE29-ACCEPTANCE.md` | 新增：环境、四步读数、新增测试清单、GUI 走查结果、数据还原 SHA-256、独立核对项、已知边界 |
| `docs/superpowers/specs/2026-11-12-d-player-phase29-modernization-design.md` | 本设计稿，单独提交 |
| `docs/superpowers/plans/` | 实施计划刻意未跟踪，不提交 |

不回改 Phase 20–28 的历史设计稿与验收记录。WinUI 运行时回归不在本期范围（壳冻结，只验证编译），这是一条明示的覆盖缺口。

提交边界：

- 设计稿单独提交一次。
- 实施按批次提交：① 安全网 + 真缺陷；② 机械/基础设施；③ 模型；④ 源生成；⑤ 生命周期；⑥ 文档 + 验收。
- **推送等用户明确指示。**

## 9. 已知限制与移交

- **WinUI 只有编译覆盖**：壳已冻结，XAML 运行时模板挂接、`NavigationView`、`AppWindow.Closing` 落盘没有自动回归；Core 改动流入 WinUI 的运行时行为不做走查。
- **音频实时路径零改动**：本阶段仍跑 5 条真实音频测试，但不改动 `NAudioPlaybackService`/`EqualizerSampleProvider`/`SampleAggregator`；音频性能与随机回归留给 Phase 30。
- **union 探针结果未定**：首选 union，失败降级 closed record 层级；最终形态与偏离原因写入 `PHASE29-ACCEPTANCE.md`。
- **Search partial property 探针结果未定**：Toolkit 8.4.2 能否保留 private setter 与通知语义；不能则保留手写并在验收记录偏离。
- **Lock 替换只覆盖非音频普通锁**：音频 `EqualizerSampleProvider`/`NAudioPlaybackService` 与 async `SemaphoreSlim` 不改，Phase 30 的锁纪律不受影响。
- **测试计数以实跑为准**：设计只给“310 + 新增”的口径，不预写具体总数；避免把推演数字写成观测值。
- **JSON 源生成是元数据替换，不是 AOT**：发布形态不变，仍为 framework-dependent；不要把它当成 trimming/AOT 改造。
- **settings 原子写是行为改进**：写失败保留旧文件；Rider 红线与 IDE 诊断按 Phase 28 纪律重新观察，不为 IDE 改产品代码。
- **未做**：`ObservableCollection` 批量替换、`System.Threading.Lock` 替换 async 临界区、`PeriodicTimer`、`PlaylistImportReport`/`AddToPlaylistResult` union 建模、AOT/trimming。

## 附录 A：对 Phase 28 附录 C 的复核修正

| # | 附录 C 原说法 | 复核结论 |
|---|---|---|
| 1 | Null Object/internal 构造器唯一使用者 `PlaylistsViewModelTests.cs:47` | 还有 `:60` 的 `CreateContainerVmPreservingSeeds`，被 3+ 测试调用；改 NSubstitute 时两个辅助方法都要改 |
| 2 | 后缀白名单循环 3 处同构 | 实际 5 处以上且形状不同；`AudioConstants` 已是权威常量但只是 `IReadOnlyList`，`PlaylistViewModel` 还有两处硬编码 |
| 3 | JSON 原子写/v1→v2 迁移“被测试锁定” | 实际无直接测试；本阶段必须先补契约测试再重构 |
| 4 | `Clear()+foreach Add` 改批量替换会让计数断言红 | 全 Tests 只有两处弱订阅，没有严格计数断言；仍不做，因为会改 UI 刷新行为 |
| 5 | `FindAncestor` 四份中三份缺陷 | 属实；三份逐字相同且未处理 `ContentElement` |

## 附录 B：新增测试清单（预估，最终以实跑为准）

1. `RootGateRegistry_ReleasesGateAfterLastUser`
2. `RootGateRegistry_DifferentRootsRunInParallel`
3. `RootGateRegistry_CancelledWaitDoesNotLeak`
4. `JsonFileStore_AtomicWrite_ReplacesExistingFile`
5. `JsonFileStore_AtomicWriteFailure_KeepsOldFile`
6. `JsonPlaylistService_MigratesV1Fixture_ToV3Shape`
7. `JsonPlaylistService_RoundTrip_PreservesQueueState`
8. `JsonSettingsPersistence_RoundTrip_PreservesSettings`
9. `JsonLibraryCache_RoundTrip_PreservesEntries`
10. `OneShotCleanup_ReturnsSameTask_AndPropagatesException`
11. `OneShotCleanup_LateCompletionIsDiscarded`
12. `Dispose_IsIdempotent_AndDoesNotThrow`
13. `SearchQuery_Set_RaisesPropertyChanged`（仅当现有测试未覆盖时）
14. `PlaylistsViewModel_PublicCtor_StillHydrates`（Null Object 删除后的回归）

测试数量随实现调整；门禁 2 的通过数以实跑输出为准。
