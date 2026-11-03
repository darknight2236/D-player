# Phase 24 设计：艺术家 / 专辑库视图（WPF 主壳）

- 日期：2026-11-02
- 状态：已评审的范围与方案（用户已确认：独立音乐库文件夹、侧栏固定「音乐库」节点、专用库播放队列、钻取式视图；暂不引入 AlbumArtist）
- 前置：Phase 23 频谱/EQ 已验收；2026-11-02 路线决策已记录（WinUI 壳开发搁置，后续功能回归 Core + WPF）

> **2026-11-02 修订（合辑分组，验收后）**：实测合辑的 16 首在同一文件夹、逐曲 Artist 不同且 AlbumArtist 无共同值；专辑身份改为 `(Album, 所在文件夹)`，艺术家列表按 `Track.Artist` 建并保留每位贡献者，混合专辑显示「多位艺术家」。原 `(Album, Artist)` 的已知限制由本修订取代；多碟子文件夹仍可能拆开。测试 221（216 默认 + 5 音频）。
>
> **2026-11-03 修订（导航/标记/行高）**：关闭时把库页签与专辑/艺术家详情写入 `AppSettings.LibraryView*`，重启在库缓存水化后恢复；原 §9“库视图页签不持久化”的限制由本修订取代。专辑详情增加当前曲 ▶ 标记，库行高对齐歌单队列；初始化完成前隐藏中间内容以消除启动闪烁。测试 228（223 默认 + 5 音频）。

## 1. 目标与非目标

在 WPF 主壳新增一个真正的本地音乐库视图：用户指定一个或多个库根目录，应用递归扫描并缓存元数据，按艺术家/专辑浏览，双击即可从库中播放。

目标：

- 多音乐库根目录（settings 持久化），复用现有文件夹扫描、`library-cache.json` 缓存与元数据读取。
- 侧栏固定「音乐库」区（艺术家 / 专辑两个节点），中间面板切换到库视图；点击歌单可切回队列视图。
- 艺术家列表 / 专辑封面网格 / 艺术家详情 / 专辑详情四级钻取。
- 从库中播放有独立、可持久化的播放队列：上一首/下一首、Shuffle/Repeat、断点续播、重启恢复全部复用现有 `PlaylistViewModel` 语义，不污染用户歌单。

非目标：

- 不做 WinUI 壳的库 UI（WinUI 壳自 2026-11-02 起冻结；只要求 `-Full` 门禁 3 继续编译）。
- 不做搜索、从库创建歌单、拖拽入队、文件系统实时监听、标签编辑。
- 暂不引入 `AlbumArtist` 字段；专辑按 `(Album, 所在文件夹)` 分组（2026-11-02 修订，合辑的逐曲 Artist 不再拆开）。
- 不改音频输出/ASIO、不改 EQ/频谱。

## 2. 现状事实（实施时以此为准）

- [AppSettings.cs](../../../D-player.Core/Configuration/AppSettings.cs) 是 `record` + `init`，由 `JsonSettingsPersistence` 读写 `settings.json`；已有 `OutputMode`/`PreferredDeviceId` 等预留字段，尚无库目录字段。
- [LibraryScannerService.cs](../../../D-player.Core/Services/LibraryScannerService.cs) 已提供 `ScanFolder`（递归、白名单、失败返回空）、`ReadMetadataBatchAsync`（失败静默跳过）、`ComputeDiff`（路径大小写不敏感 + 零时长/零采样率视为需重读）。
- [JsonLibraryCache.cs](../../../D-player.Core/Services/JsonLibraryCache.cs) 以根目录路径为 key，读写同一个 `library-cache.json`；[LibraryCacheEntry.cs](../../../D-player.Core/Models/LibraryCacheEntry.cs) 不含 `AlbumArt`（按需加载）。
- [PlaylistsViewModel.cs](../../../D-player.Core/ViewModels/PlaylistsViewModel.cs) 持有可见歌单集合、`ViewedPlaylist`（查看指针）、`CurrentPlaylistId`（播放指针）；`PlaylistViewModel` 负责队列、`CurrentIndex`、Shuffle/Repeat、`TrackEnded` 推进；`IsActivePlaylist` 是 TrackEnded 守卫。
- [QueueState.cs](../../../D-player.Core/Models/QueueState.cs) 当前 schema v3：可见歌单数组 + 当前播放指针 + 全局 Shuffle/Repeat；旧文件缺失字段按默认值反序列化。
- [PlaylistsSidebarView.xaml](../../../Views/Controls/PlaylistsSidebarView.xaml) 只有一个歌单 `ListBox`，拖拽重排按 `ListBox` 索引调用 `MovePlaylistCommand`；因此**不能**把库播放队列塞进 `Playlists` 集合并靠隐藏 Item 容器绕过（会破坏索引映射）。
- [MainWindow.xaml](../../../Views/MainWindow.xaml) 中间列固定绑定 `Playlists.ViewedPlaylist`；`MainWindow.DataContext` 是 `MainViewModel`。
- [SettingsDialog.xaml](../../../Views/Dialogs/SettingsDialog.xaml) 已有「音频输出」灰色占位和频谱设置，窗口固定 420×520、无滚动。
- 测试 181 条（176 默认 + 5 真实音频集成）；`MainViewModel`/`PlaylistsViewModel` 都有直接构造的测试，构造签名变化需同步更新。
- WinUI 壳通过 DI 解析 `MainViewModel`，Core 公开面变化会由 `-Full` 门禁 3 检查。

## 3. 数据与设置

### 3.1 AppSettings

- 新增 `public string[] LibraryFolders { get; init; } = Array.Empty<string>();`
- 语义：库根目录列表；保存时规范化（`Path.GetFullPath`、去尾部目录分隔符）、大小写不敏感去重；顺序保留用户添加顺序。
- 不新增缓存 schema/版本；空数组是合法状态（库视图显示空态）。

### 3.2 库模型（Core）

新增 `DPlayer.Models`/`DPlayer.ViewModels` 下的库视图模型（命名以 plan 为准，以下为设计约束）：

- `LibraryArtist`：`Name`、`IsUnknown`、`Albums`（该艺术家的专辑）、`Tracks`（该艺术家全部曲目，供「播放全部」）、`AlbumCount`、`TrackCount`。
- `LibraryAlbum`：`Title`、`Artist`、`IsUnknown`、`Year`（首个非空年份）、`Tracks`（按曲目号/标题排序）、`CoverArt`（`byte[]?`，惰性填充）。
- 分组规则：
  - 艺术家 key：`Track.Artist` 去首尾空白后大小写不敏感；空值归「未知艺术家」。
  - 专辑 key：`(Track.Album, Track.Artist)`；空专辑名归「未知专辑」。同一专辑名、不同艺术家是不同专辑；同一艺术家同一专辑名合并。
  - 曲目排序：`TrackNumber ?? int.MaxValue`，然后 `Title`，最后 `FilePath`（保证确定性）。
  - 艺术家按名称排序；专辑按标题排序；未知组排在最后。
- 曲目对象复用 `Track`；从缓存构建时 `AlbumArt = null`，从新扫描构建时也剥离 `AlbumArt`（避免整个库把封面字节常驻内存）。封面单独按专辑惰性读取。

## 4. 库扫描与缓存

新增 `LibraryViewModel`（Core，DI Singleton；WPF-free）：

依赖：`ISettingsPersistence`、`ILibraryScannerService`、`ILibraryCache`、`ITrackMetadataReader`、`PlaylistsViewModel`。

### 4.1 启动/刷新

- `InitializeAsync()`：
  1. 读 settings 得到 `LibraryFolders`；
  2. 对每个根目录 `ILibraryCache.LoadAsync(root)`，用缓存条目构建 `Track` 列表并立即分组（快，不读标签）；
  3. 把缓存条目按路径暴露为 `IReadOnlyDictionary<string, LibraryCacheEntry> CachedEntriesByPath`，用于回填库播放队列元数据；
  4. 返回，不等待扫描；由 `MainViewModel` 以 fire-and-forget 启动后台扫描。
- `RefreshAsync()`：重新读 settings → 重载缓存 → 重新分组 → 后台重扫。设置保存后由调用方触发。
- 后台扫描逐根执行：
  - `ScanFolder(root)` → `ComputeDiff(current, cached)`；
  - 仅对 `added`（含元数据失效项）调用 `ReadMetadataBatchAsync`；
  - 批次中缺失的路径用 `ITrackMetadataReader.CreateFallback(path)` 补成 fallback Track（保证文件不从库中消失；零时长缓存下次会被 `ComputeDiff` 重读）；
  - 合并 unchanged + added 生成该根的新缓存，`SaveAsync(root, entries)`（IO 失败不中断其他根）；
  - 每根完成后在 UI 线程重建分组；全部完成后状态行显示总数/不可用根数。
- 多根合并：以规范化完整路径为 key（`OrdinalIgnoreCase`）去重；嵌套根目录不会重复计数。
- 根目录不存在/无权限：`ScanFolder` 返回空，记为不可用根，不弹错；其他根继续。

### 4.2 封面

- `EnsureCoverAsync(LibraryAlbum album)`：若已尝试/已有封面则立即返回；否则后台读取 `album.Tracks[0].FilePath` 的完整元数据，取 `AlbumArt` 写入 `album.CoverArt`；失败/无图只标记尝试过，显示占位。
- WPF 视图在专辑 tile / 专辑详情头部 `Loaded` 时调用；只读取用户实际看到的专辑，避免几千张封面常驻。
- 串行执行（`SemaphoreSlim`/队列），避免并发读盘；重建库时递增 generation，丢弃旧封面结果。

### 4.3 并发

- `LibraryViewModel` 构造时捕获 `SynchronizationContext`（与现有 VM 一致）；所有 `ObservableCollection` 更新回到 UI 上下文。
- 扫描用 generation（`int` 自增）+ `CancellationTokenSource`：新的 refresh 取消旧扫描；旧扫描在每根/每批之间检查 generation，过期结果不写入 UI、不写缓存。
- `Cleanup()` 取消后台扫描/封面队列；由 `MainViewModel.CleanupAsync` 调用。

## 5. 库播放队列（专用上下文）

### 5.1 持久化

- [QueueState.cs](../../../D-player.Core/Models/QueueState.cs) 增加可选字段：
  ```csharp
  public Playlist? LibraryPlayback { get; init; }
  ```
- 保持 `SchemaVersion = 3`：这是向后兼容的加法字段；旧文件缺失 → `null`，旧程序忽略新字段。
- `JsonPlaylistService` 的「零歌单 → Seed」判断改为：可见歌单为空且 `LibraryPlayback` 为 null 时才 Seed；只有库播放队列时保留它（防御腐损文件）。

### 5.2 PlaylistsViewModel

- 新增 `private PlaylistViewModel? _libraryPlayback;` 与 `public PlaylistViewModel? LibraryPlayback => _libraryPlayback;`。
- 新增 `public const string LibraryPlaybackPlaylistId = "__library-playback__";`；新建时使用该 Id、`Name="音乐库播放"`、`SourceFolder=null`。
- `Hydrate(snapshot)`：
  - 先恢复可见歌单（现有逻辑）；
  - 若有 `snapshot.LibraryPlayback`，用工厂创建 `PlaylistViewModel`，设置 `Container`、挂 hook，存入 `_libraryPlayback`，**不加入 `Playlists`**；
  - 对库播放队列跳过同步标签读取（避免启动时逐首读标签）；
  - `CurrentPlaylist` 解析顺序：库播放队列（Id 匹配时）→ 可见歌单；
  - `ViewedPlaylist` 只解析可见歌单；若 `CurrentPlaylistId` 指向库播放队列，`ViewedPlaylist` 取第一个可见歌单。
- `RecomputeIsActiveFlags`：可见歌单 + 库播放队列都按 `CurrentPlaylistId` 设置 `IsActivePlaylist`。
- `BuildSnapshot`：写入 `LibraryPlayback = _libraryPlayback?.ToRecord()`。
- 新增 `public bool IsLibraryPlaybackActive => _libraryPlayback is not null && CurrentPlaylistId == _libraryPlayback.Id;`；在 `OnCurrentPlaylistIdChanged` 与库队列创建时触发 `OnPropertyChanged`，供侧栏 ▶ 标记绑定。
- `FindPlaylistTrackByPath`：先查当前上下文，再查可见歌单与库播放队列；断点续播能找到库曲目。
- `Cleanup`/`Hydrate` 换出：对库播放队列调用 `Cleanup()` 解绑 `TrackEnded`。
- 删除最后一个可见歌单自动重建默认歌单时，若库播放队列正在播放，**不要**用新默认歌单顶掉 `CurrentPlaylistId`，只更新 `ViewedPlaylist`。

### 5.3 播放入口

`PlaylistsViewModel.PlayLibraryTracksAsync(IReadOnlyList<Track> tracks, int startIndex, string contextName)`：

- 懒创建 `_libraryPlayback`（保留 Id、名称 `contextName` 或「音乐库播放」）；
- 替换其 `Queue` 内容（保留全局 Shuffle/Repeat，不沿用旧 shuffle 历史），`CurrentIndex = startIndex`；
- `CurrentPlaylistId = _libraryPlayback.Id`；不修改 `ViewedPlaylist`；
- 调用 `PlayTrackAtAsync(startIndex)` 启动播放；
- 队列/索引变化触发现有 `StateChanged` → debounce 写 `queue.json`。

`LibraryViewModel` 的「播放全部」/双击曲目通过该方法进入；上下文曲目列表由库视图决定（专辑详情 = 该专辑曲目；艺术家详情/播放全部 = 该艺术家全部曲目）。

## 6. WPF UI 与导航

### 6.1 侧栏

- [PlaylistsSidebarView.xaml](../../../Views/Controls/PlaylistsSidebarView.xaml) 顶部新增固定「音乐库」区：两个节点「艺术家」「专辑」，下面是现有歌单工具栏与列表（中间加分组标题/分隔线）。
- 点击节点：`Window.GetWindow(this).DataContext as MainViewModel` → `ShowLibrary(page)`；节点高亮由 code-behind 维护。
- 点击歌单（ListBox `SelectionChanged`）：`MainViewModel.ShowPlaylist()`，切回队列视图。
- 库播放队列正在出声时，在「音乐库」区显示 ▶ 标记（绑定/订阅 `PlaylistsViewModel.IsLibraryPlaybackActive`）；点击歌单不改播放上下文（现有双指针语义）。
- 因为库播放队列不在 `Playlists` 集合中，现有拖拽重排、重命名、删除逻辑完全不受影响。

### 6.2 MainViewModel / MainWindow

- `MainViewModel` 新增 `public LibraryViewModel Library { get; }`（构造注入）；`MainViewModel` 构造签名变化需同步更新测试。
- 新增 `bool IsLibraryViewActive`（可观察）；`ShowLibrary(LibraryPage page)` / `ShowPlaylist()`。
- [MainWindow.xaml](../../../Views/MainWindow.xaml) 中间列放 `PlaylistView` 与新的 `LibraryView`，按 `IsLibraryViewActive` 切换可见性（新增反向布尔→可见性转换器，或在 Style 里用 DataTrigger）。
- 初始为队列视图；不持久化库视图页签（重启后回到队列视图，库播放队列仍按 `CurrentPlaylistId` 恢复播放）。

### 6.3 LibraryView

新文件 `Views/Controls/LibraryView.xaml(.cs)`，`DataContext = MainViewModel.Library`。

页面状态（`LibraryPage`）：`Artists`、`Albums`、`ArtistDetail`、`AlbumDetail`。

- 艺术家页：`ListBox` 行列表（名字、N 张专辑 · M 首）；双击/Enter 进入艺术家详情。
- 专辑页：`ListBox` + `WrapPanel` 封面网格（tile 约 140px）：封面/占位、专辑名、艺术家；双击/Enter 进入专辑详情。
- 艺术家详情：头部（名字、专辑/曲目数）+「播放全部」+ 该艺术家的专辑 tile 网格；点专辑进入专辑详情；返回回到艺术家页。
- 专辑详情：头部（封面、标题、艺术家、年份）+「播放全部」+ 曲目列表（曲目号、标题、时长）；双击曲目从该曲开始播放；返回按来源回到艺术家详情或专辑页。
- 空态：
  - 无根目录：「还没有音乐库文件夹」+ 打开设置按钮；
  - 有根但零曲目：扫描中/未找到音频文件的提示 + 刷新按钮。
- 状态行：扫描中、`N 位艺术家 · M 张专辑 · K 首`、不可用根目录数量。
- 刷新按钮：`Library.RefreshAsync()`。
- 封面：tile/详情头部 `Loaded` 时调用 `EnsureCoverAsync`；占位图用现有图标/渐变，不额外引入图片资源。
- WPF View 层不做单测（项目惯例）；交互行为进手动验收。

### 6.4 设置对话框

- [SettingsDialog.xaml](../../../Views/Dialogs/SettingsDialog.xaml) 新增「音乐库」区：
  - 根目录 `ListBox`（显示完整路径、可滚动/裁剪）；
  - 「添加文件夹」走 `IFileDialogService.OpenFolderAsync()`，规范化/去重后加入列表；
  - 「移除」删除选中项；
  - 只改对话框内草稿，点「保存」才写 `AppSettings.LibraryFolders`；取消不落盘。
- 窗口改为可滚动内容（标题栏与底部按钮固定），高度可增至约 620px；避免新增区把频谱/EQ 区挤出窗口。
- `SettingsDialog` 构造/`Show` 增加 `IFileDialogService` 参数；两个调用点（[MainWindow.xaml.cs](../../../Views/MainWindow.xaml.cs) 的 Ctrl+, 与 [PlayerBar.xaml.cs](../../../Views/Controls/PlayerBar.xaml.cs) 的 ⚙）传入 `App.GetService<IFileDialogService>()`。
- 保存返回 true 后调用方触发 `Library.RefreshAsync()`（PlayerBar 通过 `Window.GetWindow(this).DataContext as MainViewModel` 取库 VM）。

## 7. 启动顺序、恢复与生命周期

`MainViewModel.InitializeAsync` 调整为：

1. 读 `queue.json` → `Playlists.Hydrate(snapshot)`（恢复可见歌单 + 库播放队列，库队列跳过同步标签读取）；
2. `await Library.InitializeAsync()`（读缓存、建分组、暴露缓存 map）；
3. `Playlists.ApplyCachedMetadataToLibraryPlayback(library.CachedEntriesByPath)`（按路径更新隐藏队列的标题/艺术家/时长/采样率/曲目号，避免启动逐首读标签；找不到的曲目保持 fallback）；
4. `RestoreLastPlayedAsync()`（此时库曲目也能按路径找到并恢复位置）；
5. fire-and-forget 后台扫描（现有文件夹歌单扫描与库扫描分别启动）。

`MainViewModel.CleanupAsync`：

- 先调用 `Library.Cleanup()` 取消后台扫描/封面队列；
- 现有可见歌单 `Cleanup()` 循环后，追加 `Playlists.LibraryPlayback?.Cleanup()`（或由 `PlaylistsViewModel` 提供统一 `Cleanup()`）。

## 8. 错误处理与边界

- 根目录不存在/无权限：不抛、不弹错，计入不可用根；其他根继续。
- `library-cache.json` 损坏：`LoadAsync` 已返回空，后台全量扫描重建。
- 新文件标签读取失败：fallback Track（文件名标题）保证可见；零时长条目下次扫描重试。
- 缓存写失败：记录状态、保留内存结果，不中断其他根。
- 扫描/封面旧结果：generation + cancellation 丢弃。
- 删除库根目录：不影响已在播放的库队列（路径已持久化）；下次扫描后库视图不再显示这些曲目。
- 移除最后一个可见歌单：保留库播放队列的当前指针（见 §5.2）。
- 库播放队列文件不存在：交给现有 `PlayPlaybackError` 路径；不新增静默跳过。
- 空库/首次启动：不自动创建根目录、不弹窗。

## 9. 已知限制

- 专辑身份 = `(Album, 所在文件夹)`（2026-11-02 修订；原 `(Album, Artist)` 在无共同 AlbumArtist 的合辑上会拆开）；艺术家列表按 `Track.Artist` 建，合辑的每位贡献者保留自己的艺术家页；多碟子文件夹可能拆开；同文件夹内同名不同专辑（罕见）会合并。
- 不做搜索；库很大时只能滚动浏览。
- 不做文件系统实时监听；库变化在启动/手动刷新/设置保存后生效。
- 封面按专辑第一首曲目读取；若封面只在后续曲目内嵌，则显示占位。
- 库播放队列不出现在侧栏、也没有独立队列管理界面；用户通过播放栏上一首/下一首控制。后续如需可加「正在播放」面板。
- 库视图页签不持久化；重启后默认队列视图。

## 10. 测试与门禁

单元测试（Core，新增/更新）：

- `LibraryViewModelTests`：
  - 艺术家/专辑分组、未知标签、`(Album, 文件夹)` 合并/同名不同文件夹拆分、贡献艺术家保留；
  - 多根聚合与完整路径去重（含嵌套根）；
  - 曲目排序（TrackNumber/Title/FilePath）；
  - 新增文件失败时 fallback 仍可见；
  - 缓存命中时不调用元数据读取；扫描完成后的分组结果。
- `PlaylistsViewModelTests`：
  - 首次 `PlayLibraryTracksAsync` 创建库队列且不进入 `Playlists`；
  - `CurrentPlaylistId`/`IsActivePlaylist` 正确、`ViewedPlaylist` 不变；
  - `BuildSnapshot` 包含 `LibraryPlayback`；`Hydrate` 恢复且跳过同步 `ReadAsync`；
  - 删除最后一个可见歌单不顶掉库播放指针；
  - `FindPlaylistTrackByPath` 能找到库队列曲目。
- `PlaybackResumeTests`：库队列曲目断点续播。
- `JsonPlaylistServiceTests`：含 `LibraryPlayback` 的快照往返；旧 JSON 无该字段时为 null。
- 设置持久化：`LibraryFolders` 往返与空数组。
- DI：`AddDPlayerCore` 能解析 `LibraryViewModel` 与更新后的 `MainViewModel`。

WPF View 层不写单测；手动验收覆盖：设置增删根目录、艺术家/专辑页、详情、播放全部、双击曲目、上一首/下一首、Shuffle/Repeat、重启恢复、断点续播、空态、窄窗、侧栏拖拽回归。

门禁：

- `powershell -File tools/verify-gates.ps1 -Fast`：build 0/0，测试全绿（新增用例后更新文档中的计数）。
- `powershell -File tools/verify-gates.ps1 -Full`：额外 WinUI 门禁 3 0/0（Core 公开面有变化，必须跑）。
- 不新增真实音频集成测试；现有 `Category=AudioDevice` 5 条保持独立。

## 11. 交付物

- 本 spec + `docs/superpowers/plans/2026-11-02-d-player-phase24-library-view-implementation.md`
- Core/WPF 实现与单测
- 文档更新：`README.md`、`docs/PROJECT.md`、`docs/COUPLING.md`、新增 `docs/PHASE24-ACCEPTANCE.md`（实现与验收后写）
- 提交按改动分批：Core/测试、WPF UI、文档/验收

## 12. 任务切分（供 implementation plan 展开）

1. **Core 数据与设置**：`AppSettings.LibraryFolders`；`QueueState.LibraryPlayback`；`JsonPlaylistService` 零歌单判断。
2. **库播放队列**：`PlaylistsViewModel` 的 `_libraryPlayback`、Hydrate/BuildSnapshot/CurrentPlaylist/IsActive/FindTrack/PlayLibraryTracksAsync + 单测。
3. **LibraryViewModel**：多根扫描/缓存/分组/封面/并发 + 单测。
4. **WPF 设置**：音乐库根目录区 + `IFileDialogService` 接线 + 保存后刷新。
5. **WPF 库视图与导航**：`LibraryView`、侧栏固定节点、MainWindow 切换、MainViewModel 导航状态。
6. **门禁、验收与文档**：`-Fast`/`-Full`、手动验收、`PHASE24-ACCEPTANCE.md`、README/PROJECT/COUPLING 更新。
