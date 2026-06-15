# UmaPlayer 项目文档

> 一个轻量级、本地优先的 Windows 音乐播放器（WPF + .NET 10 + NAudio）。
>
> 文档日期：2026/06/14 · 对应分支：`master` · 当前阶段：**Phase 11 完成**（设置面板）

---

## 1. 项目简介

**UmaPlayer** 是一款面向 Windows 桌面的本地音乐播放器，灵感来源于 foobar2000 / Winamp。Phase 1 实现单曲播放骨架，Phase 2 加入内存播放队列（多选入队、自动推进、随机/循环模式）。Phase 3 重构 ViewModel 层（按职责拆分 + 抽象元数据读取 + 修正持久化合并纪律），偿还 4 项技术债。Phase 4 加入队列持久化（关闭时写 `queue.json`，启动时恢复列表 + Shuffle/Repeat 模式 + CurrentIndex）。Phase 5 加入拖拽支持（外部音频文件拖入入队、队列内项拖拽重排含多选、视觉反馈含边框高亮 + 插入线 Adorner），同时偿还 in-flight `RemoveTrack`/`MoveTracks` 的 `_playToken` 残留债。Phase 6 加入多命名歌单支持（Spotify 双指针模型：Viewed vs Current）、xUnit 测试骨架、BytesToBitmapImageConverter（Debt #1 部分偿还）。Phase 7 完成债务 #1 完整偿还（PlayerViewModel.BitmapImage → byte[]），VM 层不再依赖 WPF 类型。Phase 8 建立 ViewModel 单元测试体系（50 个测试覆盖 PlayerVM / PlaylistVM / PlaylistsVM）。Phase 9 加入 sidebar 歌单拖拽重排（复用 Phase 5 的 Adorner + 多选拖拽保护模式）。Phase 10 加入文件夹绑定歌单（指定文件夹递归扫描 → 创建/更新歌单，启动后台自动同步增删，手动刷新，JSON 元数据缓存），同时将音频后缀白名单从 View 层提取到 Models.AudioConstants 消除层级违规。Phase 11 添加设置对话框（默认音量滑块 + 音频输出灰色占位 + PlayerBar ⚙ 按钮 + Ctrl+, 快捷键）。

### 1.1 关键特性（已实现）

| 类别   | 能力                                                             |
|------|----------------------------------------------------------------|
| 文件导入 | Win32 OpenFileDialog 多文件选择                                     |
| 支持格式 | MP3 / WMA / FLAC / AAC / WAV（基于 Windows Media Foundation 原生解码） |
| 播放控制 | 播放 / 暂停 / 停止 / 上一首 / 下一首                                       |
| 进度控制 | 拖拽 + 单击跳转的进度条；位置实时更新（≈30 Hz，节流）                                |
| 音量控制 | 0~1 线性滑块、一键静音/取消静音；通过 `VolumeSampleProvider` 实现                |
| 元数据  | 标题 / 艺术家 / 专辑 / 流派 / 年份 / 采样率 / 内嵌封面（z440.atl.core）            |
| 主题   | 内置深色主题（深紫强调色）                                                  |
| 持久化  | 窗口位置/尺寸、默认音量保存到 `%LocalAppData%\UmaPlayer\settings.json`       |
| 播放列表 | 内存队列：多选入队、单项删除、清空、上/下一首、自然播完自动推进、随机/3 态循环（Off/List/One） |
| 队列持久化 | 关闭时写 `%LocalAppData%\UmaPlayer\queue.json`；启动恢复列表 + CurrentIndex + Shuffle/Repeat（Phase 4） |
| 拖拽 | 外部音频文件拖入末尾入队（白名单 .mp3/.wma/.flac/.aac/.wav）；队列内单/多选拖拽重排（含 ▶ 当前曲跟随、Shuffle 历史按对象身份重映射）；插入线 Adorner + 圆角列表框边框高亮（Phase 5） |
| 多命名歌单 | 创建/删除/重命名多个独立歌单；Viewed vs Current 双指针（切查看不打断播放，双击才跨歌单切换音频）；每个歌单独立 Shuffle/Repeat/CurrentIndex；v1→v2 schema 自动迁移（Phase 6） |
| 文件夹绑定歌单 | 指定文件夹扫描 → 创建歌单; 启动后台自动同步增删; 手动刷新; 元数据缓存 (Phase 10) |
| 设置 | 模态对话框：默认音量滑块；音频输出占位（Phase 12）；Ctrl+, 快捷键 (Phase 11) |

### 1.2 后续增量（未实现）

- M3U / PLS 等播放列表格式导入导出
- 音频可视化（频谱 / 波形）
- 音乐库按艺术家/专辑组织（文件夹扫描已在 Phase 10 实现）
- OGG/Vorbis 支持（MF 不原生支持，需额外解码器）
- 多设备 / 输出模式切换（WASAPI Shared / Exclusive / ASIO）—— 接口已预留

---

## 2. 技术栈

| 层 | 选型 |
|----|------|
| 运行时 | .NET 10 (`net10.0-windows`) |
| UI 框架 | WPF (`UseWPF=true`) |
| MVVM | [CommunityToolkit.Mvvm 8.x](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) |
| DI 容器 | `Microsoft.Extensions.DependencyInjection` 10.x |
| 配置 | `Microsoft.Extensions.Configuration.Json` + `IOptions<AppSettings>` |
| 音频引擎 | [NAudio 2.2.x](https://github.com/naudio/NAudio) |
| 音频解码 | `MediaFoundationReader` |
| 输出后端 | WASAPI Shared (`WasapiOut`) |
| 元数据/标签 | [z440.atl.core 7.13](https://github.com/Zeugma440/atldotnet) |

---

## 3. 目录结构

```
UmaPlayer/
├── App.xaml(.cs)                # 应用入口；构建 DI 容器、加载主窗口
├── AssemblyInfo.cs              # ThemeInfo（资源字典位置）
├── UmaPlayer.csproj / .sln      # 项目/解决方案
├── appsettings.json             # 启动默认配置（构建时复制到输出目录）
│
├── Configuration/
│   └── AppSettings.cs           # 强类型配置 record（绑定到 "Player" section）
│
├── Models/
│   ├── Track.cs                 # 不可变 record：音轨信息（含封面字节数组）
│   ├── PlayState.cs             # enum: Stopped / Playing / Paused
│   ├── RepeatMode.cs            # enum: Off / List / One  (Phase 2)
│   ├── Playlist.cs              # 不可变 record: 歌单 (Id, Name, Items, CurrentIndex, ShuffleEnabled, RepeatMode) (Phase 6)
│   ├── QueueState.cs            # 不可变 record: queue.json schema v3 (Playlists + CurrentPlaylistId + SourceFolder) (Phase 4/6/10)
│   ├── MoveTracksArgs.cs        # 不可变 record: 队列内拖拽重排命令参数 (Phase 5)
│   ├── AudioConstants.cs         # 音频后缀白名单 (Phase 10, 从 DragDropExtensions 提取)
│   ├── LibraryCacheEntry.cs      # 缓存条目 record (Phase 10)
│   ├── LibraryDiff.cs            # 扫描增量同步 record (Phase 10)
│   └── AudioDeviceInfo.cs       # 预留：设备信息
│
├── Services/                    # 业务/基础设施服务（全部基于接口）
│   ├── IPlaybackService.cs      # 核心播放抽象
│   ├── NAudioPlaybackService.cs # NAudio 实现（WASAPI + MediaFoundation）
│   ├── IFileDialogService.cs
│   ├── Win32FileDialogService.cs# Microsoft.Win32.OpenFileDialog 封装
│   ├── ISettingsPersistence.cs
│   ├── JsonSettingsPersistence.cs # 持久化到 %LocalAppData%\UmaPlayer\settings.json
│   ├── IPlaylistService.cs      # 多歌单持久化抽象 (Phase 6, 替换 IQueuePersistence)
│   ├── JsonPlaylistService.cs   # 持久化到 %LocalAppData%\UmaPlayer\queue.json; 内置 v1→v2 迁移 (Phase 6)
│   ├── ILibraryScannerService.cs      # 库扫描抽象 (Phase 10)
│   ├── LibraryScannerService.cs       # 递归扫描 + Diff 实现 (Phase 10)
│   ├── ILibraryCache.cs               # 元数据缓存抽象 (Phase 10)
│   └── JsonLibraryCache.cs            # JSON 缓存实现 (Phase 10)
│   ├── ITrackMetadataReader.cs # 元数据读取抽象 (Phase 3)
│   ├── AtlMetadataReader.cs    # 基于 z440.atl.core 的实现 (Phase 3)
│   ├── IAudioDeviceManager.cs   # 预留：设备枚举/切换
│   ├── StubAudioDeviceManager.cs# 占位实现，返回空集
│   ├── IAudioOutputFactory.cs   # 预留：输出后端工厂
│   └── StubAudioOutputFactory.cs# 占位实现，固定返回 WASAPI Shared
│
├── ViewModels/
│   ├── MainViewModel.cs        # Strict Facade (~44 行)：仅暴露 Player/Playlists + debounce save + CleanupAsync (Phase 3/6)
│   ├── PlayerViewModel.cs      # Transport 子 VM：播放/暂停/进度/音量 (Phase 3)
│   ├── PlaylistViewModel.cs    # 队列子 VM：Queue/Shuffle/Repeat/推进算法 + Id/Name/IsActivePlaylist (Phase 3/6)
│   └── PlaylistsViewModel.cs   # 多歌单容器：ObservableCollection<PlaylistVM> + Add/Remove/Rename + HandleDoubleClickPlay + ImportFolder/Rescan/Refresh (Phase 6/10)
│
├── Views/
│   ├── MainWindow.xaml(.cs)     # 主窗口；Phase 6 改为 PlayerBar + 2 列(Sidebar + PlaylistView)
│   ├── Dialogs/
│   │   ├── PromptDialog.xaml(.cs)    # 共享单输入对话框（新建/重命名歌单）(Phase 6)
│   │   └── SettingsDialog.xaml(.cs)  # 设置对话框（音量 + 音频输出占位）(Phase 11)
│   └── Controls/
│       ├── PlayerBar.xaml(.cs)  # 全功能播放栏（封面/信息/进度/控制/音量）
│       ├── PlaylistView.xaml(.cs)    # 播放队列（Phase 2 + Phase 5 拖拽 + Phase 6 IsActivePlaylist guard + Phase 10 导入文件夹/刷新按钮）
│       ├── PlaylistsSidebarView.xaml(.cs) # 左侧歌单栏（+/- 按钮、ListBox、双击重命名、▶ 标记、📂 文件夹图标、🔄 扫描指示）(Phase 6/10)
│       ├── DragDropExtensions.cs     # IsDragOver attached DP + 音频后缀白名单/过滤 (Phase 5)
│       └── DropInsertionAdorner.cs   # ListBox AdornerLayer 插入线绘制 (Phase 5)
│
├── Converters/
│   ├── PlayStateToIconConverter.cs       # ▶/⏸ 图标
│   ├── TimeSpanToStringConverter.cs      # 0:00 / 0:00:00
│   ├── RepeatModeToIconConverter.cs      # ⇄ / 🔁 / 🔂 (Phase 2)
│   ├── BoolToAccentBrushConverter.cs     # 强调色/次要色画刷 (Phase 2)
│   └── BytesToBitmapImageConverter.cs    # byte[] → Frozen BitmapImage (Phase 6, 债务 #1 部分偿还)
│
├── Themes/                      # 深色主题资源字典（App.xaml 合并加载）
│   ├── Colors.xaml              # #1E1E2E 背景 + #7C4DFF 紫色强调
│   ├── Fonts.xaml               # Segoe UI + Header/Body/Caption 文本样式
│   └── Controls.xaml            # Window / Button / Slider 模板
│
├── Extensions/
│   └── ServiceCollectionExtensions.cs # AddUmaPlayerServices(...) DI 注册
│
├── Tests/                       # xUnit 测试项目 (Phase 6)
│   ├── UmaPlayer.Tests.csproj   # 测试项目文件 (xUnit + Coverlet)
│   └── Smoke/
│       └── SmokeTests.cs        # 冒烟测试：Track record 结构相等
│
└── docs/
    ├── PROJECT.md               # 本文档
    ├── COUPLING.md              # 耦合分析 / 风险登记册
    └── superpowers/             # 设计稿 & 实现计划（历史归档）
        ├── specs/
        │   ├── 2026-04-23-uma-player-design.md                          # Phase 1 设计
        │   ├── 2026-06-06-uma-player-playlist-design.md                 # Phase 2 设计
        │   ├── 2026-06-07-uma-player-phase3-design.md                   # Phase 3 设计
        │   ├── 2026-06-12-uma-player-phase4-queue-persistence-design.md # Phase 4 设计
        │   ├── 2026-06-12-uma-player-phase5-drag-drop-design.md         # Phase 5 设计
        │   └── 2026-06-13-uma-player-phase6-named-playlists-design.md   # Phase 6 设计
        └── plans/
            ├── 2026-04-24-uma-player-implementation.md                          # Phase 1 计划
            ├── 2026-06-06-uma-player-playlist-implementation.md                 # Phase 2 计划
            ├── 2026-06-07-uma-player-phase3-implementation.md                   # Phase 3 计划
            ├── 2026-06-12-uma-player-phase4-queue-persistence-implementation.md # Phase 4 计划
            ├── 2026-06-12-uma-player-phase5-drag-drop-implementation.md         # Phase 5 计划
            └── 2026-06-13-uma-player-phase6-named-playlists.md                  # Phase 6 计划
```

---

## 4. 架构

### 4.1 高层架构（MVVM + DI）

```
   ┌──────────────────────────────────────────────────────┐
   │                       App.xaml.cs                    │
   │   1. 读取 appsettings.json                            │
   │   2. 构建 ServiceCollection (AddUmaPlayerServices)    │
   │   3. 解析 MainViewModel + ISettingsPersistence        │
   │      + IQueuePersistence                              │
   │   4. new MainWindow(vm, persistence, queue).Show()    │
   └──────────────┬─────────────────────────┬─────────────┘
                  │                         │
                  ▼                         ▼
   ┌──────────────────────┐    ┌──────────────────────────┐
   │     MainWindow       │    │  MainViewModel (Facade)  │
   │  (View, code-behind) │◀──▶│  ~44 行：仅持有子 VM       │
   │ - 窗口位置恢复/保存   │    │  + CleanupAsync()         │
   │ - 关闭写 queue.json  │    └──────┬──────────┬─────────┘
   │   (cancel-and-close) │           │          │
   │ - PlayerBar 容器     │           ▼          ▼
   │ - PlaylistView 容器  │   ┌─────────────┐ ┌──────────────┐
   └──────────────────────┘   │ PlayerVM    │ │ PlaylistVM   │
                              │ Transport:  │ │ Queue/Shuffle│
                              │ Play/Pause/ │ │ /Repeat/推进 │
                              │ Position/Vol│ │ /TrackEnded  │
                              │             │ │ +LoadFromDisk│
                              │             │ │ +SnapshotState│
                              └──────┬──────┘ └──────┬───────┘
                                     │  互不持引用      │
                                     ▼  仅共享 Service ▼
           ┌──────────────────────────┴──────────────────────────┐
           ▼                          ▼                          ▼
┌────────────────────┐  ┌────────────────────────┐  ┌──────────────────────┐
│ IPlaybackService   │  │ ITrackMetadataReader   │  │ ISettingsPersistence │
│ (NAudio impl)      │  │ (ATL impl) [Phase 3]   │  │ UpdateAsync(Func<>)  │
└────────────────────┘  └────────────────────────┘  └──────────────────────┘
                                                    ┌──────────────────────┐
                                                    │ IQueuePersistence    │
                                                    │ (JSON impl) [Phase4] │
                                                    └──────────────────────┘
┌────────────────────────┐  ┌──────────────────────┐
│ ILibraryScannerService │  │ ILibraryCache        │
│ (Recursive scan+Diff)  │  │ (JSON impl) [Ph10]   │
│ [Phase 10]             │  │                      │
└────────────────────────┘  └──────────────────────┘

注：IFileDialogService 由 PlaylistViewModel 直接消费（OpenAndPlay / AddToQueue）。
注：IQueuePersistence 由 PlaylistViewModel（启动读盘）+ MainWindow.Window_Closing（关闭写盘）双方消费。
```

### 4.2 服务生命周期

注册位置：`Extensions/ServiceCollectionExtensions.cs`

| 服务 | 生命周期 | 说明 |
|------|----------|------|
| `IOptions<AppSettings>` | Singleton（框架） | 绑定 `appsettings.json` 的 `"Player"` 节，作为**启动默认快照** |
| `IPlaybackService` | **Singleton** | 持有 NAudio 设备资源，必须长生命周期 |
| `IFileDialogService` | Singleton | 无状态 |
| `ISettingsPersistence` | Singleton | 内部 `SemaphoreSlim` 并发互斥 |
| `IQueuePersistence` | Singleton (Phase 4) | 独立 `SemaphoreSlim`，与 settings 文件锁互不影响 |
| `ITrackMetadataReader` | Singleton (Phase 3) | 无状态，封装 z440.atl.core；`ReadAsync` 不抛 |
| `ILibraryScannerService` | **Singleton** (Phase 10) | 递归文件夹扫描 + Diff 计算；无状态 |
| `ILibraryCache` | **Singleton** (Phase 10) | JSON 元数据缓存 (`library-cache.json`)；内部 `SemaphoreSlim` |
| `IAudioDeviceManager` | Singleton（Stub） | 预留 |
| `IAudioOutputFactory` | Transient（Stub） | 预留；语义上由 `IPlaybackService` 创建即释放 |
| `PlayerViewModel` | **Transient** (Phase 3) | Transport 子 VM；DI 中**必须先于** `PlaylistViewModel` 注册 |
| `PlaylistViewModel` | **Transient** (Phase 3) | 队列子 VM |
| `MainViewModel` | **Transient** | Strict Facade，构造时聚合两个子 VM |

### 4.3 关键设计决策

1. **配置双源**：启动只读默认值用 `IOptions<AppSettings>`；运行时可变状态（窗口、音量、最后播放路径）走 `ISettingsPersistence` 写入用户数据目录。两者通过 `record with` 不可变更新协同。

2. **位置事件节流**：`NAudioPlaybackService.PollPositionAsync` 以 ~30 Hz（33 ms）轮询，并通过 `PositionThrottle` 进一步节流，避免 UI 线程被淹没。

3. **UI 线程封送**：服务层捕获启动时的 `SynchronizationContext`（必然是 UI 线程，因为 `IPlaybackService` 由 `App.OnStartup` 间接解析），所有事件通过 `_syncContext.Post` 派发，VM 直接绑定即可。

4. **拖拽 Seek 防回跳**：`PlayerViewModel.IsSeeking` 标志在拖动期间抑制 `PositionChanged` → `Position` 写入，避免拖拽时滑块被服务回写"拽回去"。

5. **静音状态保留音量**：`ToggleMute` 把当前 `Volume` 存到 `_volumeBeforeMute`，置 `Volume=0`；取消静音恢复。拖动滑块若有非零值会自动取消静音。

6. **窗口可见性自愈**：`MainWindow.EnsureVisible()` 检查恢复的位置是否在虚拟屏内（防止外接屏拔掉后窗口飘到屏外），不在则回退到主屏居中。

7. **面向接口 + 占位实现**：`IAudioDeviceManager` / `IAudioOutputFactory` 已注册 Stub，便于后续替换为真实多设备/Exclusive/ASIO 实现而不动 VM。

8. **Strict VM Facade（Phase 3）**：`MainViewModel` 仅作为子 VM 容器（~44 行），无 `[ObservableProperty]` 与 `[RelayCommand]`。`PlayerViewModel`（transport）与 `PlaylistViewModel`（队列）**互不持引用**，仅通过 `IPlaybackService` Singleton 间接协作（PlayerVM 订阅 transport 事件；PlaylistVM 单独订阅 `TrackEnded` 推进队列）。View 跨域命令（如 PlayerBar 上的 ⏮/⏭/📂 调用 PlaylistVM）通过 `{Binding DataContext.<sub>.<cmd>, RelativeSource={RelativeSource AncestorType=Window}}` 跨级绑定到 MainWindow 的 DataContext 解决。

9. **持久化 read-modify-write 原子化（Phase 3）**：`ISettingsPersistence` 接口由 `SaveAsync(AppSettings)` 改为 `UpdateAsync(Func<AppSettings, AppSettings> mutator)`，把"读盘 → 应用 mutator → 写盘"整个序列封进 `SemaphoreSlim` 锁内，根治了 VM 写音量与 `Window_Closing` 写窗口尺寸的合并竞态（COUPLING.md 旧债 #3）。注意：`UpdateAsync` 内部 `.ConfigureAwait(false)`，故调用方若需要在 mutator 内读取 WPF DependencyProperty，必须在 await 之前先把值捕获到 UI 线程局部变量（见 `MainWindow.xaml.cs:Window_Closing`）。

10. **队列持久化与 settings 隔离（Phase 4）**：队列状态独立写到 `%LocalAppData%\UmaPlayer\queue.json`。**为什么不复用 settings.json**：(a) 队列条目数量级远大于 settings 字段，混在一起每次拖音量都会让队列 JSON 重新序列化；(b) settings 是高频更新（音量、窗口尺寸），queue 是低频快照（仅关闭时一次），写入节奏不同；(c) 模式失败隔离 —— queue.json 损坏不影响窗口/音量恢复。`IQueuePersistence` 故意比 `ISettingsPersistence` 简化：只有 `LoadAsync()` 与 `SaveAsync(QueueState)`，没有 `UpdateAsync` —— PlaylistViewModel 是唯一权威源（"读队列" = `SnapshotState()`），无需读-改-写原子化。

11. **Cancel-and-close 关闭模式（Phase 4）**：`MainWindow.Window_Closing` 是 `async void`；从 Phase 3 的 2 个 await（settings + CleanupAsync）涨到 Phase 4 的 4 个（+ snapshot + queue 写盘）后撞上致命 race —— 第一个 await yield 后 WPF 立即继续关闭流程，`ShutdownMode.OnLastWindowClose` 触发 `Application.Shutdown` → `Dispatcher.InvokeShutdown`，把后续 await 续延 post 到死 dispatcher 上永不运行（settings 通常能抢到，queue 永远丢）。修复：首次进入 `e.Cancel = true` 拦下，跑完所有异步工作后调 `Close()` 重新触发 Closing，第二次进入凭 `_isClosing` 标志直接 fall-through。这是 WPF `async void` Closing 的标准纪律 —— 任何新增 await 都该用此模式。

12. **拖拽 View/VM 边界（Phase 5）**：所有 OLE DragDrop 事件、命中测试（`ComputeInsertIndex`）、文件后缀过滤（`DragDropExtensions.FilterAudioPaths`）、Adorner 绘制（`DropInsertionAdorner`）都在 View 层；VM 仅暴露纯数据命令 —— `DropExternalFiles(IReadOnlyList<string>)` 和 `MoveTracks(MoveTracksArgs)`。VM 不感知 `DataObject` / `DragEventArgs` / `AdornerLayer`，仍可单测。

13. **重排算法用对象身份而非索引算术（Phase 5）**：`MoveTracks` 缓存被移动的 Track 引用 + 当前曲引用 + Shuffle 历史引用集合，删-插完成后用 `ReferenceEquals` 扫一遍 Queue 重建 `CurrentIndex` 和 `_shuffleHistory`。**不能用 `Queue.IndexOf`**：Track 是 `sealed record`（结构相等），多个 `CreateFallback("X.mp3")` 占位是结构相等但引用不同的对象，IndexOf 会返回首个结构等价匹配而非原始那一个，导致重排后 ▶ 跟到错的曲、Shuffle 历史塌陷。HashSet 同理需 `ReferenceEqualityComparer.Instance`。

14. **拖拽期间 in-flight 重入用 `_playToken++` 顶替（Phase 5）**：`PlaylistViewModel.RemoveTrack` 与新增的 `MoveTracks` 入口都自增 `_playToken`，关上 in-flight `PlayTrackAtAsync` 在 `await` 元数据期间 `Queue[index] = meta` 写到错位的窗口（COUPLING.md 旧债 #5）。`MoveTracks` 不调 `_player.Unload()` —— 重排不中断播放，NAudio 在另一线程继续推流，仅 ▶ 标记跟到新位置。

15. **GUID 主键, Name 仅展示（Phase 6）**：`Playlist.Id` 是 GUID 字符串，创建时一次性确定，不可变。`Name` 是显示名，可重命名、可重复，不破坏持久化绑定。

16. **Viewed vs Current 双指针（Phase 6）**：`ViewedPlaylist`（UI 选中，不持久化）与 `CurrentPlaylistId`（正在播放，持久化）解耦。用户切查看不打断播放，只有双击才跨歌单切换音频（Spotify 模型）。

17. **Schema v2 一次性自动迁移（Phase 6）**：`JsonPlaylistService.LoadAsync` 检测 v1 包成单条 "默认歌单"，立即写盘；迁移失败则吞掉，内存仍是 v2，下次重迁（幂等）。

18. **删除最后一个歌单自动重建（Phase 6）**：永远不存在 0 歌单状态；UI 不需要"空状态"分支。

19. **Debounce save 集中在 MainViewModel（Phase 6）**：子 VM 不感知存盘；`PlaylistsViewModel.StateChanged` → MainVM 500ms debounce → `SaveAsync`。`CleanupAsync` 同步 flush 一次。

21. **Phase 11 SettingsDialog 不加 SettingsViewModel（YAGNI）**：仅 DefaultVolume 可编辑，OutputMode/PreferredDeviceId 无消费者。对话框直接读写 `ISettingsPersistence`。Phase 12 音频设置有复杂交互（如切换输出模式需重载设备列表）时再引入 SettingsViewModel。

20. **debt #1 偿还（Phase 6/7）**：Phase 6 交付 `BytesToBitmapImageConverter`；Phase 7 完成数据流切换 —— `PlayerViewModel.AlbumArtBytes` 改为 `byte[]`，XAML 通过 Converter 转为 Frozen BitmapImage。VM 层不再依赖任何 WPF 类型。

---

## 5. 模块详解

### 5.1 `Models`

- **`Track`**：不可变 record。`Duration` 与 `SampleRate` 在加载时由 `NAudioPlaybackService.LoadAsync` 通过 `track with { Duration=..., SampleRate=... }` 补齐。`AlbumArt` 为原始字节数组，由 VM 转 `BitmapImage`（限 200px、`Freeze()` 跨线程安全）。**注意 record 的结构相等：** 两个 `CreateFallback("X.mp3")` 占位 Track 在结构上相等 —— 任何按相等性查找/去重的代码（`IndexOf` / 默认 `HashSet<Track>`）都会塌陷它们。Phase 5 重排算法因此改用引用身份（`ReferenceEquals` + `ReferenceEqualityComparer.Instance`）。
- **`PlayState`**：`Stopped / Playing / Paused`。
- **`RepeatMode`**：`Off / List / One`（Phase 2）。
- **`QueueState`**（Phase 4）：不可变 record；`SchemaVersion=3`（v2→v3 新增 `Playlist.SourceFolder`，无迁移：缺失字段反序列化为 null）/ `Playlists: IReadOnlyList<Playlist>` / `CurrentPlaylistId`。**只持久化路径与队列态**，不携带 Track 元数据或封面 —— 启动时由 `PlaylistViewModel.LoadFromDisk` 为每条路径创建占位 Track（与 OpenAndPlay 流程一致），用户首次播放时由 `PlayTrackAtAsync` 升级为完整元数据。
- **`MoveTracksArgs`**（Phase 5）：不可变 record；`SourceIndices: IReadOnlyList<int>`（升序无重复，每项 ∈ [0, Queue.Count)） / `TargetIndex: int`（∈ [0, Queue.Count]，i 表示插到 i 之前；Count 表示末尾）。由 View 层 Drop handler 构造，这些不变量由 View 保证（VM 信任入参，无校验代码）。
- **`AudioDeviceInfo`**：`(Id, Name, IsDefault)`，目前仅类型存在。

### 5.2 `Services/NAudioPlaybackService`

| 成员 | 说明 |
|------|------|
| `LoadAsync(Track)` | 在 `Task.Run` 上：销毁旧播放链 → 新建 `MediaFoundationReader` → `VolumeSampleProvider` → `WasapiOut(Shared, 100ms)`；触发 `DurationChanged` / `TrackChanged` · Phase 3：在 DurationChanged 之前先广播 PositionChanged(Zero)，防止切到时长更短的曲时旧 Position 与新 Duration 并存（"4:05 / 3:20" glitch） |
| `Play / Pause / Stop` | 委派给 `IWavePlayer`；`Stop` 同时将 `CurrentTime` 归零（保留底层资源，再 `Play()` 会重播同一首） |
| `Unload()` | **完全释放**底层 reader/wavePlayer，清掉 `_currentTrack`；之后 `Play()` 是 no-op。**Phase 3：同时广播 `TrackChanged(null) + DurationChanged(Zero) + PositionChanged(Zero)`** 让 VM 清屏（标题/封面/时长/进度全归零）。`PlaylistViewModel.UnloadCurrentTrack` 在清空队列/删当前曲时调用 |
| `Seek(TimeSpan)` | 写 `reader.CurrentTime` 后**主动广播 PositionChanged**（Phase 3：暂停态下 PollPositionAsync 已退出，否则进度条不刷新，看上去像"没跳转"）；通过 `ClampToDuration` 截到 [0, TotalTime] |
| `Volume { get; set; }` | `Math.Clamp(0..1)`；运行时写入 `VolumeSampleProvider.Volume` |
| `PollPositionAsync` | 仅在 `PlaybackState==Playing` 时循环；每 33ms 派发一次 `PositionChanged` · Phase 3：经 `ClampToDuration` 截断，避免解码器尾部浮点越界 |
| `OnPlaybackStopped` | 区分 (1) 异常 → `PlaybackError`；(2) 自然播完（距 `TotalTime` ≤ 200ms） → `TrackEnded`；(3) 用户 `Stop` → 仅 `Stopped` |
| `Dispose()` | 拆事件、停止、释放 reader/wavePlayer |

### 5.3 `Services/JsonSettingsPersistence`

- 路径：`%LocalAppData%\UmaPlayer\settings.json`
- 启动时若不存在则返回 `new AppSettings()`（默认值由 record 初始化器给出，**与 `appsettings.json` 不重复绑定**）
- **Phase 3 重构**：接口由 `SaveAsync(AppSettings)` 改为 `UpdateAsync(Func<AppSettings, AppSettings> mutator)`，把整段"读盘 → 应用 mutator → 写盘"封进 `SemaphoreSlim(1,1)` 临界区。调用方仅需提供 `s => s with { Field = newValue }`，再无合并竞态
- 读盘失败（损坏/权限）→ 以 `new AppSettings()` 为起点喂给 mutator，写盘照常；写盘失败则抛出（关闭流程调用方自行 catch）
- 私有 `ReadFromDiskNoLockAsync()`：调用方负责持锁；供 `LoadAsync` 与 `UpdateAsync` 共用

### 5.3a `Services/JsonPlaylistService`（Phase 6，替换 JsonQueuePersistence，Schema v3）

- 路径：`%LocalAppData%\UmaPlayer\queue.json`
- 与 settings 持久化结构对称：独立 `SemaphoreSlim(1,1)`、`WriteIndented=true`、`JsonStringEnumConverter`（让 `RepeatMode` 序列化成字符串而非整数，跨版本稳定且方便手动调试）
- 接口仅 `LoadAsync()` / `SaveAsync(QueueState)`，**故意没有 `UpdateAsync`** —— PlaylistsViewModel 是队列状态的唯一权威源，无需读-改-写合并
- `LoadAsync` 隐式契约：**绝不抛**（catch-all 静默 fallback 到 `new QueueState()`）。文件不存在/JSON 损坏/版本号不匹配/反序列化得 null 全部走同一回退分支；旧文件保留供用户排查。内置 v1→v2 一次性迁移（单条"默认歌单"）+ v2→v3 字段补充（`SourceFolder` 缺失即 null，无需迁移）
- `SaveAsync` 失败抛出，由 `MainWindow.Window_Closing` 自行 catch（与 settings 写盘失败行为对称：用户下次启动队列丢失，但不打扰关闭流程）

### 5.4 `ViewModels/MainViewModel`（Strict Facade，~44 行）

Phase 3 拆分后只剩三个公开成员：

```csharp
public PlayerViewModel Player { get; }
public PlaylistViewModel Playlist { get; }
public Task CleanupAsync();
```

构造由 DI 注入 `(PlayerViewModel, PlaylistViewModel, IPlaybackService)`，本类**不持有** transport / 队列 / 命令 / Observable 状态。

`CleanupAsync()` 顺序固定：`Playlist.Cleanup()`（同步：解绑 `TrackEnded`）→ `await Player.CleanupAsync()`（异步：解绑 5 个 transport 事件 + 写最后一次音量）→ `_player.Dispose()`。**必须先解绑后 Dispose**，避免事件 handler 在底层资源销毁后被回调。

**架构不变量（COUPLING.md §5）：** `PlayerViewModel` 与 `PlaylistViewModel` **互不持引用**，仅共享 `IPlaybackService` Singleton；本 Facade 不暴露 Player/Playlist/CleanupAsync 之外的任何成员（否则倒退为"转发 Facade"反模式）。

### 5.4a `ViewModels/PlayerViewModel`（Transport 子 VM，~230 行）

源生成器属性：`_isSeeking`, `_position`, `_duration`, `_playState`, `_currentTrack`, `_albumArtImage`, `_volume`, `_isMuted`。派生：`VolumeIcon`（🔇/🔊）、`SampleRateText`、`PositionNormalized`（0..1）。

**Phase 10 新增成员：**
- `SourceFolder`（`string?`，构造时从 `Playlist` seed 传入）：文件夹绑定歌单的源路径；null 表示普通手动歌单
- `HasSourceFolder`（`bool`，派生）：sidebar DataTemplate 用，决定是否显示文件夹图标
- `IsScanning`（`[ObservableProperty] bool`）：由 `PlaylistsViewModel` 设置，指示后台扫描进行中
- `HasScanError`（`[ObservableProperty] bool`）：由 `PlaylistsViewModel` 设置，指示最近一次扫描失败

构造时订阅 `IPlaybackService` 的 5 个事件（`PositionChanged / StateChanged / DurationChanged / TrackChanged / PlaybackError`），并阻塞读盘加载持久化音量（`_isInitializing` 标志抑制初始化期的写盘）。**不订阅 `TrackEnded`**（那是 PlaylistViewModel 的职责）。

`[RelayCommand]`：`SeekStarted / SeekCompleted(normalized) / PlayPause / Stop / ToggleMute`。

`partial void OnVolumeChanged(value)`：同步到 `_player.Volume` → 拖滑块到非零自动取消静音 → `_persistence.UpdateAsync(s => s with { DefaultVolume = value })`。

`CleanupAsync()`：解绑 5 个事件 + 用观察属性 `Volume`（**非**陈旧的 `_settings` 字段）持久化最后一次音量。**不 Dispose `IPlaybackService`**（PlaylistViewModel 还在用，Facade 层统一 Dispose）。

`HandleTrackChanged(Track? track)`：track 为 null 时把 `CurrentTrack` 和 `AlbumArtImage` 一起置 null（XAML 的 `FallbackValue='No track loaded'` 处理标题显示）。

### 5.4b `ViewModels/PlaylistViewModel`（队列子 VM，~528 行 / Phase 4 增加 LoadFromDisk + SnapshotState + PlayCurrent / Phase 10 增加 SourceFolder + IsScanning）

源生成器属性：`_currentIndex`（-1 表示未选）, `_selectedTrack`（UI 列表选中项，与播放无关）, `_shuffleEnabled`, `_repeatMode`。集合：`ObservableCollection<Track> Queue`。私有：`HashSet<int> _shuffleHistory` / `Random _random` / `int _playToken`（重入哨兵）。派生：`HasCurrentTrack`、`RepeatActive`、`ShuffleBrushKey`。

构造时订阅 `IPlaybackService.TrackEnded` 用于自动推进；`Queue.CollectionChanged` 触发 Next/Prev/PlayCurrent 命令 `NotifyCanExecuteChanged`；构造尾段同步调 `LoadFromDisk()` 恢复队列。

`[RelayCommand]`：`AddToQueue / RemoveTrack(int) / ClearQueue / PlayTrackAt(int) / NextTrack / PrevTrack / ToggleShuffle / CycleRepeat / OpenAndPlay / PlayCurrent`（Phase 4） / `DropExternalFiles(IReadOnlyList<string>)`（Phase 5） / `MoveTracks(MoveTracksArgs)`（Phase 5）。

**Phase 4 新增成员：**
- `LoadFromDisk()`（私有，构造期调用）：同步 `LoadAsync().GetAwaiter().GetResult()` 读 queue.json → 用 `File.Exists` 过滤丢失文件 → `MapCurrentIndexAfterFilter` 把过滤前 `CurrentIndex` 映射到过滤后位置（项还在直接定位；项丢失则向后滑找到第一个仍存在的，找不到再向前回退）→ 为每个 surviving 路径 `CreateFallback` 占位入队 → 恢复 Shuffle/Repeat
- `SnapshotState()`（公有，纯读无副作用）：把 `Queue.Select(t => t.FilePath).ToArray()` + 当前模式/索引打包成 `QueueState`，由 `MainWindow.Window_Closing` 在 `CleanupAsync` 之后调用
- `[RelayCommand(CanExecute=HasCurrentTrack)] PlayCurrent`：启动后用户首次按 ▶ 走的命令；触发 `PlayTrackAtAsync(CurrentIndex)`，把占位 Track 升级为完整元数据并 `LoadAsync + Play`

**跨域命令 OpenAndPlay** —— PlayerBar 上的 📂 按钮归属本 VM（本质是"批量入队 + 播首项"，前者属于队列域）；PlayerBar 通过 `{Binding DataContext.Playlist.OpenAndPlayCommand, RelativeSource={RelativeSource AncestorType=Window}}` 跨级访问。同理 ⏮/⏭ 也用这个模式绑到 `Playlist.PrevTrackCommand / NextTrackCommand`。Phase 4 ▶ 按钮在 `CurrentTrack==null` 时通过 DataTrigger 跨级绑到 `PlayCurrentCommand`，`TrackChanged(track)` 后回退到 `PlayPauseCommand`。

**Phase 5 新增成员：**
- `[RelayCommand] DropExternalFiles(IReadOnlyList<string> paths)`：与 `AddToQueue` 同语义入队管线（`_metadataReader.CreateFallback(path) → Queue.Add`），不触发播放；与 `OpenAndPlay` 区别仅在入口（OS DragDrop vs OpenFileDialog）。路径白名单过滤由 View 层 `DragDropExtensions.FilterAudioPaths` 提前完成，VM 信任入参。
- `[RelayCommand] MoveTracks(MoveTracksArgs args)`：队列内重排算法（spec §4 八步算法）。**入口先 `_playToken++`** 顶替 in-flight `PlayTrackAtAsync`（同时偿还旧债 #5）。算法用对象身份（`ReferenceEquals` + `ReferenceEqualityComparer.Instance`）回找 `CurrentIndex` 与 `_shuffleHistory`，不做索引算术 —— Track record 的结构相等会让 `Queue.IndexOf(currentTrackObj)` 在出现重复占位时返回首个结构等价匹配而非原始那一个。不调 `_player.Unload()`，重排不中断播放。
- `RemoveTrack` 入口加 `_playToken++`（Phase 5 顺带还债 #5）：以前删除非当前曲不顶替 token，能让 in-flight `Queue[index] = meta` 写到错位；现已关闭。

**关键私有方法**（与旧 MainViewModel 等价）：
- `PlayTrackAtAsync(int, int skipCount=0)`：抢占 `_playToken` → 读元数据 → `Queue[i] = meta` → `LoadAsync` → `Play`；每个 `await` 后校验 token，被顶替则静默退出；失败连续 3 次自动停止
- `CalculateNextIndex(int? failedIndex)`：纯算法，按 (Shuffle × RepeatMode) 4 种组合返回下一索引；Shuffle 用 `_shuffleHistory` 排除已播
- `HandleTrackEnded()`：RepeatOne 重播当前，否则走 `CalculateNextIndex`
- `UnloadCurrentTrack()`：`_playToken++` 顶替 in-flight → `_player.Unload()`（NAudio 服务自动广播 TrackChanged(null) 让 PlayerVM 清屏）

`Cleanup()`：同步解绑 `TrackEnded`，由 `MainViewModel.CleanupAsync` 调用。

### 5.4c `ViewModels/PlaylistsViewModel`（多歌单容器，Phase 6 + Phase 10）

源生成器属性：`_viewedPlaylist`（UI 当前选中）。集合：`ObservableCollection<PlaylistViewModel> Playlists`。私有：`Func<Playlist, PlaylistViewModel>` 工厂委托、`IPlaylistService`、`ILibraryScannerService`、`ILibraryCache`、`string _currentPlaylistId`。

**Phase 6 成员：**`[RelayCommand]`：`AddPlaylist / RemovePlaylist / RenamePlaylist`；`HandleDoubleClickPlay`（跨歌单双击路由）；`RecomputeIsActiveFlags`（切 CurrentPlaylistId 后批量刷新 sidebar ▶ 标记）；`StateChanged` 事件（debounce save 触发点）。

**Phase 10 新增成员：**
- 构造函数新增 `ILibraryScannerService scanner` + `ILibraryCache cache` 两个依赖
- `[RelayCommand] ImportFolderAsync()`：弹 `IFileDialogService.OpenFolder` → 递归扫描 → 创建文件夹绑定歌单（`Playlist` seed 带 `SourceFolder`）
- `RescanFolderBoundPlaylistsAsync()`：启动时由 `MainViewModel.InitializeAsync` 触发；遍历所有 `HasSourceFolder` 的 VM，逐个增量同步（`LibraryDiff`）
- `[RelayCommand] RefreshPlaylistAsync(PlaylistViewModel?)`：手动刷新单个文件夹绑定歌单
- `RescanSinglePlaylistAsync(vm, sourceFolder)`：核心扫描逻辑 —— 调 `ILibraryScannerService.ScanAsync` + `ILibraryCache.LoadAsync/SaveAsync` → 计算 diff → 增删 Queue → 设 `IsScanning`/`HasScanError`

### 5.5 `Views`

- **`MainWindow`**：两行 Grid 容器 —— `PlayerBar`（顶部，自适应高度）+ `PlaylistView`（底部填充）。构造时同步读取窗口尺寸（`GetAwaiter().GetResult()`，启动阻塞 < 几 ms 可接受）；若持久化的 `WindowHeight < 500`（Phase 1 旧值）则一次性迁移到 650，避免列表不可见。关闭时采用 **cancel-and-close 模式**（Phase 4）：首次进入 `e.Cancel=true` + `_isClosing=true`，跑完 settings 写盘、`CleanupAsync`、`SnapshotState` + queue 写盘后调 `Close()` 重新触发 Closing 直接放行；这是为了让 `async void` 多 await 链不被 `Application.Shutdown → Dispatcher.InvokeShutdown` 截断。
- **`PlayerBar`** *(UserControl)*：播放栏。三行 Grid：①封面+元数据；②`Position | Slider | Duration`；③⏮ ▶/⏸ ⏹ ⏭ 📂 + 音量。
  - Slider 的"单击跳转"由 `PreviewMouseLeftButtonDown` 手动从 `PART_Track` 计算比例并触发 `SeekCompletedCommand`；点击 Thumb 时不触发（通过 `FindAncestor<Thumb>` 检测，转交给原生 `DragStarted/DragCompleted`）
  - `⏮` / `⏭` / `📂` 通过 `{Binding DataContext.Playlist.<XxxCommand>, RelativeSource={RelativeSource AncestorType=Window}}` 跨级绑定到 `PlaylistViewModel`（PlayerBar 自身的 DataContext 已切为 PlayerViewModel），`HasCurrentTrack` 守卫；队列空时按钮自动禁用
  - **▶/⏸ 按钮的双绑定（Phase 4）**：默认 `Command={Binding PlayPauseCommand}`（PlayerVM 的 transport 切换）；当 `CurrentTrack==null` 时通过 `<DataTrigger Binding="{Binding CurrentTrack}" Value="{x:Null}">` 切到 `Playlist.PlayCurrentCommand` —— 启动后队列已恢复但 transport 空闲，第一次按 ▶ 触发首次加载 + 播放，`TrackChanged(track)` 让 trigger 失活，回到 PlayPauseCommand。**注意 inline `<Style TargetType="Button">` 必须 `BasedOn="{StaticResource {x:Type Button}}"`**，否则会替换掉 `Themes/Controls.xaml` 中的隐式主题样式，按钮回退到 OS 原生白底（COUPLING.md §5）
- **`PlaylistView`** *(UserControl, Phase 2 + Phase 5 拖拽 + Phase 10)*：队列界面。两行 Grid：①工具栏 `[+ 添加][清空][导入文件夹][刷新]` 左对齐、`🔀` `⇄/🔁/🔂` 右对齐；②`ListBox` 绑 `Queue`，每项含 ▶ 当前曲标记 + 标题 + `×` 删除按钮
  - 当前曲 ▶ 标记由 code-behind 维护：订阅 `PlaylistViewModel.PropertyChanged` (CurrentIndex) / `Queue.CollectionChanged` / `ItemContainerGenerator.StatusChanged`（应对虚拟化容器回收和 `Queue[i] = meta` 替换）
  - 交互：双击播放、Delete 键删除、右上角按钮触发命令
  - Shuffle / Repeat 图标用 `Segoe UI Emoji` 字体（默认 `Segoe UI` 不含 U+1F500 完整字形）
  - **Phase 5 拖拽（XAML）：** 外层 `<Border AllowDrop="True">` 仅承载 OLE drop 区（覆盖工具栏 + 列表两行的 hit-test）；视觉高亮挂在 Row 1 的圆角 `<Border x:Name="QueueListBorder">`（用户期望仅看到列表区域被框住，不连带工具栏）。**BorderBrush 默认值放进 Style.Setter 而非 local 属性** —— WPF DP 优先级 `local > trigger setter > style setter`，写成 local 会让 `Style.Triggers` 失效（`docs/COUPLING.md §5` 隐式契约）。`ListBox` 加 `SelectionMode="Extended"` + `AllowDrop="True"` + 6 个事件挂接（`PreviewMouseLeftButton{Down,Up}` / `PreviewMouseMove` / `DragOver` / `DragLeave` / `Drop`）
  - **Phase 5 拖拽（code-behind）：** 拖拽启动用 `PreviewMouseLeftButtonDown` 记起点 + `PreviewMouseMove` 4px 阈值（`SystemParameters.MinimumHorizontal/VerticalDragDistance`）。**多选拖拽保护：** 用户 Ctrl+多选后再不带修饰键点击其中一项时，ListBox 默认会把选中塌成单项 —— `PreviewMouseLeftButtonDown` 在"已选 ≥ 2 项 + 无 Ctrl/Shift + 点中已选项"时 `e.Handled = true` 拦下默认塌选；若未过阈值就松手，`PreviewMouseLeftButtonUp` 手动塌成单选模拟原行为；过阈值真启动拖拽则保留多选。`DataObject` 自定义格式 `"UmaPlayer.QueueItems"` 区分内部重排，`DataFormats.FileDrop` 是外部文件。命中测试 `ComputeInsertIndex` 对每个 ListBoxItem 容器用 `TransformToAncestor(QueueList)` 算 bounds + 半高判定。`HideAdorner` 在 `Drop` / `DragLeave` 都清理插入线，避免残留
  - **Phase 5 高亮纪律：** `Root_DragEnter` 必须先 `FilterAudioPaths` 再决定是否高亮 —— 仅看 `FileDrop` 存在就亮会让文件夹/全非音频也亮（光标已显示禁止但边框还紫，视觉冲突）。`Root_Drop` 与 `QueueList_Drop` **都要清高亮** —— `QueueList_Drop` 设 `e.Handled=true` 后 Drop 事件不再冒泡到 `Root_Drop`，否则文件落到列表区高亮卡死
- **`SettingsDialog`** *(Window, Phase 11)*：设置对话框。模态 ToolWindow（420×280），General 区域音量滑块（0..1, IsMoveToPointEnabled）+ Audio Output 灰色占位。静态 `Show(Window?, ISettingsPersistence)` 工厂方法。Loaded async 读盘加载当前音量；Save_Click 通过 `UpdateAsync(s => s with { DefaultVolume = v })` 原子写盘。

### 5.5a `Views/Controls/DragDropExtensions`（Phase 5）

静态类，承担拖拽相关的 attached DependencyProperty 与文件过滤辅助：

- `IsDragOver`（attached DP，bool，默认 false）：由 `PlaylistView.xaml.cs` 的 `Root_DragEnter` / `Root_DragLeave` / `Root_Drop` / `QueueList_Drop` 切换；XAML 用 `Style.Trigger Property="local:DragDropExtensions.IsDragOver"` 给 `QueueListBorder` 的 `BorderBrush` 设 `AccentPrimary` 实现高亮。所有切换调用都显式传 `QueueListBorder`（不是 `sender`），保证视觉范围只在列表圆角矩形上
- `AudioExtensions`（`IReadOnlyList<string>`）：代理到 `Models.AudioConstants.Extensions`（Phase 10 提取），白名单 `.mp3 / .wma / .flac / .aac / .wav`，与 `IFileDialogService` 在 `OpenFiles` 中使用的过滤器单一来源
- `FilterAudioPaths(IEnumerable<string>?)`：大小写不敏感后缀匹配；null/空字符串/空后缀（文件夹路径 `Path.GetExtension` 返回 ""）/ 后缀不在白名单都返回不入结果。VM 层 `DropExternalFilesCommand` 信任此函数已过滤完成

### 5.5b `Views/Controls/DropInsertionAdorner`（Phase 5）

继承 `Adorner` 的 sealed 类，挂在 `QueueList` 的 `AdornerLayer` 上画拖拽插入线：

- 构造接收 `ListBox` 作为 `AdornedElement`，`IsHitTestVisible = false`（不抢鼠标事件）
- 内部 `_insertIndex`（int）；`Update(int)` 设值 + `InvalidateVisual()`
- `OnRender(DrawingContext)` 用 frozen `Pen`（颜色绑定 `AccentPrimary`，宽 2.0）画一条横线：`_insertIndex == Queue.Count` 时画在最后一项底部；否则画在 `Queue[insertIndex]` 项顶部。横线左右各缩 4px 留白
- 生命周期由 `PlaylistView.xaml.cs` 的 `_currentAdorner` 字段管理：`ShowAdorner` 懒构造一次后只调 `Update`；`HideAdorner` 在 Drop / DragLeave / 拖拽取消时 `AdornerLayer.Remove + null` 复位

### 5.6 `Themes`

深色 + 紫色强调（Catppuccin Mocha 风格）。所有控件模板写入 `Themes/Controls.xaml`，包括自定义的 Slider 模板（紫色已填充段 + 圆形 Thumb）。资源在 `App.xaml` 合并为应用级资源。

---

## 6. 配置

### 6.1 默认值：`appsettings.json`

```json
{
  "Player": {
    "DefaultVolume": 0.8,
    "OutputMode": "WasapiShared",
    "PreferredDeviceId": null,
    "LastPlayedPath": null,
    "WindowLeft": 100,
    "WindowTop": 100,
    "WindowWidth": 800,
    "WindowHeight": 650
  }
}
```

复制到输出目录（`<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`），通过 `services.Configure<AppSettings>(configuration.GetSection("Player"))` 绑定为 `IOptions<AppSettings>`。

### 6.2 运行时持久化：`%LocalAppData%\UmaPlayer\settings.json`

由 `JsonSettingsPersistence` 读写，包含与 `AppSettings` 相同的字段；首次启动文件不存在时使用 record 默认值。

**当前被持久化的字段**：`DefaultVolume`、`WindowLeft/Top/Width/Height`。
**已建模但未启用**：`OutputMode`、`PreferredDeviceId`、`LastPlayedPath`。

### 6.3 队列快照：`%LocalAppData%\UmaPlayer\queue.json`（Phase 4/6/10）

由 `JsonPlaylistService` 读写。Schema v3：

```json
{
  "SchemaVersion": 3,
  "Playlists": [
    {
      "Id": "guid...",
      "Name": "默认歌单",
      "Items": ["C:\\Music\\foo.mp3", "C:\\Music\\bar.flac"],
      "CurrentIndex": 0,
      "ShuffleEnabled": false,
      "RepeatMode": "Off",
      "SourceFolder": null
    }
  ],
  "CurrentPlaylistId": "guid..."
}
```

写时机：`MainWindow.Window_Closing`（每次关闭整队列覆盖一次）。读时机：`PlaylistViewModel` 构造期同步读盘。文件不存在/JSON 损坏/版本不匹配 → 静默 fallback 到空队列（保留旧文件供用户排查）。`Items` 中已被外部移动/删除的路径在加载时自动过滤；`CurrentIndex` 通过"向后滑、再向前回退"的算法映射到过滤后的位置（spec §5.1）。

### 6.4 元数据缓存：`%LocalAppData%\UmaPlayer\library-cache.json`（Phase 10）

由 `JsonLibraryCache` 读写。缓存文件夹绑定歌单扫描到的音频文件元数据，避免重复解析。Schema 为 `Dictionary<string, LibraryCacheEntry>`（key 为文件绝对路径）。`LibraryCacheEntry` 包含修改时间戳 (`LastWriteTimeUtc`) 与完整 `Track` 元数据。

读时机：`RescanSinglePlaylistAsync` 扫描前调 `ILibraryCache.LoadAsync` 加载缓存。写时机：扫描完成后调 `ILibraryCache.SaveAsync` 更新。`LoadAsync` 隐式契约：**绝不抛**（catch-all 静默 fallback 到空字典）。

---

## 7. 数据流：典型播放流程

### 7.1 启动 + v1→v2 迁移

```
App.OnStartup 构建 DI → MainWindow Show → MainWindow.Loaded → MainViewModel.InitializeAsync()
→ JsonPlaylistService.LoadAsync 读 queue.json:
    SchemaVersion==1 → 自动迁移成单条 "默认歌单" + 立即覆盖写
    SchemaVersion==2 → 正常反序列化
→ Hydrate 进 PlaylistsViewModel 后, ViewedPlaylist 默认对齐 CurrentPlaylistId
→ MainViewModel.InitializeAsync() 触发 Playlists.RescanFolderBoundPlaylistsAsync() 后台扫描
```

### 7.2 创建/重命名/删除歌单

```
PlaylistsSidebarView + 按钮 → PromptDialog.Show → AddPlaylistCommand/RenamePlaylistCommand
→ 容器结构变化触发 StateChanged → MainViewModel debounce 500ms 后 SaveAsync
→ 删最后一个 → RemovePlaylistCommand 自动重建 "默认歌单"
```

### 7.3 切换查看(viewed) 不打断播放

```
sidebar 选中 → ViewedPlaylist 改 → MainWindow.xaml DataContext 切换 → PlaylistView 重绑数据
→ PlaylistView.RefreshCurrentIndicator 检查 _vm.IsActivePlaylist —— 非当前播放歌单上不显示 ▶
→ 当前正在播放的 PlayerBar 仍指向 CurrentPlaylistId 对应的歌单, 不变
```

### 7.4 双击跨歌单播放

```
PlaylistView.QueueList_MouseDoubleClick → App.GetService<PlaylistsViewModel>() →
HandleDoubleClickPlay(target, index) → 若 target.Id != CurrentPlaylistId 先切 CurrentPlaylistId
(触发 RecomputeIsActiveFlags + StateChanged) → await target.PlayTrackAtCommand.ExecuteAsync(index)
→ sidebar ▶ 标记跟随 CurrentPlaylistId 移动; PlaylistView ▶ 标记按 IsActivePlaylist 显隐
```

```
用户点击 📂 (PlayerBar 按钮 → 跨级绑定 Playlist.OpenAndPlayCommand)
    │
    ▼
PlaylistViewModel.OpenAndPlay
  → IFileDialogService.OpenFiles("Audio Files|*.mp3;...")
  → 对每个 path：Queue.Add(metadataReader.CreateFallback(path))
  → PlayTrackAtAsync(firstNewIndex)
    │
    ▼
PlayTrackAtAsync (PlaylistViewModel)
  → ++_playToken（重入哨兵）
  → meta = await ITrackMetadataReader.ReadAsync(path)  ── Task.Run ──▶ ATL.Track 读元数据 (+封面)
  → 若 token 被顶替则静默退出
  → Queue[index] = meta（占位 Track → 完整 Track，ObservableCollection 触发 Replace）
  → await IPlaybackService.LoadAsync(meta)  ── Task.Run ──▶ MediaFoundationReader
                                                              VolumeSampleProvider
                                                              WasapiOut(Shared, 100ms)
                                                              track with { Duration, SampleRate }
    │
    │ 事件:  PositionChanged(Zero)  ─▶ PlayerViewModel.Position
    │       DurationChanged       ─▶ PlayerViewModel.Duration
    │       TrackChanged          ─▶ PlayerViewModel.CurrentTrack + AlbumArtImage
    ▼
IPlaybackService.Play() ─▶ WasapiOut.Play() + PollPositionAsync 循环
    │
    │ (每 33ms)
    ▼
PositionChanged ─▶ PlayerViewModel.HandlePositionChanged
                   → if (!IsSeeking) Position = ClampToDuration(pos)
    │
    ▼
PlayerBar.Slider 绑定 PositionNormalized (Mode=OneWay) → UI 实时更新

曲目自然播完：
IPlaybackService.OnPlaybackStopped 检测距 TotalTime ≤ 200ms
  → TrackEnded ─▶ PlaylistViewModel.HandleTrackEnded
                  → RepeatOne：PlayTrackAtAsync(CurrentIndex)
                  → 否则：CalculateNextIndex → PlayTrackAtAsync(next)
```

---

## 8. 构建与运行

### 8.1 先决条件

- Windows 10/11（开发于 Windows 11 IoT Enterprise LTSC 2024）
- .NET 10 SDK（`net10.0-windows`）
- 任意 IDE：Visual Studio 2026+ / JetBrains Rider / VSCode + C# Dev Kit

### 8.2 命令行构建

```bash
dotnet restore UmaPlayer.sln
dotnet build   UmaPlayer.sln -c Debug
dotnet run     --project UmaPlayer.csproj
```

输出目录：`bin/Debug/net10.0-windows/`，可执行：`UmaPlayer.exe`。

### 8.3 发布（独立可执行）

```bash
dotnet publish UmaPlayer.csproj -c Release -r win-x64 \
    --self-contained false /p:PublishSingleFile=true
```

---

## 9. 已知约束与陷阱

- **格式限制**：仅支持 Windows Media Foundation 原生解码的格式；OGG/Vorbis 需用户系统安装第三方编解码器或后续切换 reader。
- **静默错误**：`HandlePlaybackError` 仅 TODO，未弹窗或写日志；调试期可通过断点检视。
- **启动时同步 IO**：`PlayerViewModel.Initialize()` 与 `MainWindow` 构造函数中均使用 `LoadAsync().GetAwaiter().GetResult()`；`PlaylistViewModel.LoadFromDisk()` 同理（Phase 4）。三个文件都极小（settings 几百字节、queue 视队列长度，典型 < 10KB），可接受，未来若数据膨胀需重构。
- **DI 解析跨线程**：`IPlaybackService` 必须由 UI 线程首次构造（依赖 `SynchronizationContext.Current` 捕获），目前由 `App.OnStartup` 保证。
- **UpdateAsync 跨线程读 DP**：`JsonSettingsPersistence.UpdateAsync` 内部 `.ConfigureAwait(false)` 把 mutator 调用 / 继续上下文带到 threadpool；若 mutator 闭包内读 WPF DependencyProperty（如 `Left/Top/Width/ActualHeight`）会抛 `InvalidOperationException`。**调用方必须先在 UI 线程把 DP 值捕获到局部变量再 await**。`MainWindow.xaml.cs:Window_Closing` 即采用此模式。
- **`async void Window_Closing` 多 await dispatcher race**（Phase 4）：超过 1-2 个 await 时第一个 await yield 后 WPF 立即继续关闭流程，`ShutdownMode.OnLastWindowClose` 触发 `Application.Shutdown → Dispatcher.InvokeShutdown`，后续 await 续延 post 到死 dispatcher 上**永不运行**。修复纪律：用 cancel-and-close 模式（首次 `e.Cancel=true` 加 `_isClosing` 标志，做完异步工作再 `Close()`）。Phase 4 加入 queue.json 写盘后从 2 个 await 涨到 4 个，settings 还能写但 queue 永远不更新；commit `9bae7ce` 用此模式修复（COUPLING.md §5）。
- **WPF inline Style 必须 `BasedOn` 隐式主题样式**：`<X.Style><Style TargetType="X">` 没有 `BasedOn="{StaticResource {x:Type X}}"` 会完全替换 `Themes/Controls.xaml` 中的隐式 Style，回退到 OS 原生外观（Button 白底灰框、Slider 灰色等）。Phase 4 ▶ 按钮加 DataTrigger 时漏 BasedOn → 按钮变白底；commit `ca66fa9` 修复。
- **WPF DP 优先级 `local > trigger setter > style setter`**（Phase 5）：要被 `Style.Triggers` 改的属性，**默认值必须放进 `Style.Setter`**，不能作为元素的 local attribute 写。Phase 5 拖拽边框高亮初次落地时 `<Border BorderBrush="Transparent">` 写成 local，让 `IsDragOver=True` 的 trigger setter 永远赢不了 → 高亮失效；commit `214d595` 把默认值挪进 Style.Setter 修复。Code review checklist：看到 inline Style.Trigger 改某 DP 时，对照查这个 DP 在元素自身上没有 local 写法。
- **WPF DragDrop RoutedEvent 冒泡 + Handled 拦截**（Phase 5）：`Drop` / `DragOver` 等都是冒泡事件；子元素设 `e.Handled = true` 后父元素的同名 handler 不再触发。Phase 5 验收时撞过：`QueueList_Drop` 处理完入队/重排设 `Handled=true`，原本想靠 `Root_Drop` 清高亮的逻辑被吃掉 → 高亮卡死。修复：清高亮（清 Adorner、清 IsDragOver）必须在两条路径都做（`QueueList_Drop` finally + `Root_Drop`），不能假定事件会冒泡上来。
- **WPF ListBox `PreviewMouseLeftButtonDown` 不消费事件 → 多选拖拽塌选**（Phase 5）：Preview 阶段不 `Handled=true` 时，ListBox 自身的选中处理仍会执行；用户 Ctrl+多选后再不带修饰键按下其中一项，ListBox 默认行为会立刻塌成单选，让随后启动的 DoDragDrop 拿到 `SelectedItems.Count==1`。修复：在按下点是"已选 + 多选 ≥2 + 无 Ctrl/Shift"时拦掉 `Handled=true`，没真正拖起来时再在 `MouseUp` 手动塌成单选模拟原行为；commit `af51dde`。
- **WPF record 结构相等会让 `Queue.IndexOf` / `HashSet<Track>` 塌陷重复占位**（Phase 5）：Track 是 `sealed record`；两个 `CreateFallback("X.mp3")` 在结构上相等。基于相等性的查找/去重会把它们认作同一个，重排时 `Queue.IndexOf(currentTrackObj)` 返回首个结构等价匹配而非原始那一个 → ▶ 跟到错的曲、Shuffle 历史塌陷。修复纪律：所有需要"找回原来那一个 Track 实例"的代码用 `ReferenceEquals` + `ReferenceEqualityComparer.Instance`（见 `PlaylistViewModel.MoveTracks` 与 `PlaylistView.QueueList_PreviewMouseMove`）。

---

## 10. 历史与参考

- 耦合分析：[`docs/COUPLING.md`](./COUPLING.md) — 风险登记册 + Phase 6 启动检查清单
- 设计稿：
  - [`docs/superpowers/specs/2026-04-23-uma-player-design.md`](./superpowers/specs/2026-04-23-uma-player-design.md) — Phase 1 整体设计
  - [`docs/superpowers/specs/2026-06-06-uma-player-playlist-design.md`](./superpowers/specs/2026-06-06-uma-player-playlist-design.md) — Phase 2 播放列表设计
  - [`docs/superpowers/specs/2026-06-07-uma-player-phase3-design.md`](./superpowers/specs/2026-06-07-uma-player-phase3-design.md) — Phase 3 VM 拆分 + 技术债清算设计
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md`](./superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md) — Phase 4 队列持久化设计
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md`](./superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md) — Phase 5 拖拽支持设计
  - [`docs/superpowers/specs/2026-06-13-uma-player-phase6-named-playlists-design.md`](./superpowers/specs/2026-06-13-uma-player-phase6-named-playlists-design.md) — Phase 6 多命名歌单设计
- 实现计划:
  - [`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — Phase 1
  - [`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`](./superpowers/plans/2026-06-06-uma-player-playlist-implementation.md) — Phase 2
  - [`docs/superpowers/plans/2026-06-07-uma-player-phase3-implementation.md`](./superpowers/plans/2026-06-07-uma-player-phase3-implementation.md) — Phase 3
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md) — Phase 4
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md) — Phase 5
  - [`docs/superpowers/plans/2026-06-13-uma-player-phase6-named-playlists.md`](./superpowers/plans/2026-06-13-uma-player-phase6-named-playlists.md) — Phase 6
- 主要里程碑提交：
  - **Phase 1**
    - `02c7012` feat: implement NAudioPlaybackService with throttled position updates
    - `07f7957` feat: implement MainViewModel with playback commands and seek handling
    - `e2d9d7b` feat: add PlayerBar, MainWindow, and wire App.xaml with DI and dark theme
  - **Phase 2**（feature/playlist-queue → master `8efc109`）
    - `1efa70c` feat(playback): add TrackEnded event for natural-end detection
    - `c0f39d6` feat(vm): add Phase 2 commands and TrackEnded auto-advance
    - `3a6d8c9` feat(view): add PlaylistView UserControl (queue UI)
    - `7d23b2b` feat(view): integrate PlaylistView and migrate window height
    - `7a10674` feat(player-bar): add Prev/Next buttons
    - `3ee16a2` fix(playback): add IPlaybackService.Unload() and use it for queue clear
    - `8efc109` merge: Phase 2 playlist queue feature
  - **Phase 3**（feature/phase3-tech-debt → master `70bd36a`）
    - `476f08e` feat(services): add ITrackMetadataReader interface
    - `c9cd1bd` feat(services): add AtlMetadataReader (ATL-based metadata reader)
    - `a2a0ee7` refactor(vm): switch MainViewModel to ITrackMetadataReader
    - `fadae44` refactor(settings): replace SaveAsync with UpdateAsync(Func<>)
    - `9883dff` feat(vm): add PlayerViewModel (transport half of split)
    - `efd5b5d` feat(vm): add PlaylistViewModel (queue half of split)
    - `54edf9a` refactor(vm): split MainViewModel into Player/Playlist facade
    - `196eb64` fix(playback): broadcast PositionChanged on Seek so paused-state click jumps immediately
    - `da500f6` fix(playback): clear PlayerBar metadata on Unload
    - `ee76afe` fix(playback): clamp Position to [0, Duration] and reset on LoadAsync
    - `70bd36a` merge: Phase 3 tech-debt cleanup
  - **Phase 4**（feature/phase4-queue-persistence → master）
    - `4ad58c5` feat(models): add QueueState record (Phase 4 schema)
    - `f30ede8` feat(services): add IQueuePersistence interface
    - `b52de5d` feat(services): add JsonQueuePersistence (queue.json read/write with semaphore)
    - `7cb655d` feat(di): register IQueuePersistence singleton
    - `53f9cd6` feat(vm): inject IQueuePersistence into PlaylistViewModel + LoadFromDisk on ctor
    - `75e863b` feat(vm): add PlaylistViewModel.SnapshotState() + PlayCurrentCommand
    - `e565cb4` feat(view): wire MainWindow.Window_Closing to write queue.json
    - `519ead8` feat(view): PlayerBar ▶ button DataTrigger for null CurrentTrack → PlayCurrent
    - `ca66fa9` fix(view): PlayerBar ▶ Style must chain BasedOn implicit Button style
    - `9bae7ce` fix(view): cancel-and-close pattern in Window_Closing for queue.json write
  - **Phase 5**（feature/phase5-drag-drop → master）
    - `e10ae0d` docs: add Phase 5 drag-drop design spec
    - `73bdafe` docs: add Phase 5 drag-drop implementation plan
    - `b8d5d37` feat(models): add MoveTracksArgs record (Phase 5 reorder command param)
    - `a76a66e` feat(vm): add PlaylistViewModel.DropExternalFiles command
    - `170bca8` feat(vm): add PlaylistViewModel.MoveTracks command + _playToken bump in RemoveTrack
    - `451ff64` feat(view): add DragDropExtensions (IsDragOver attached prop + audio suffix filter)
    - `78f9310` feat(view): add DropInsertionAdorner (1px accent-color insertion line)
    - `c76d7a7` feat(view): PlaylistView XAML adds AllowDrop, IsDragOver border trigger, multi-select
    - `6351d9d` feat(view): wire up PlaylistView drag-drop handlers (Phase 5)
    - `214d595` fix(view): restore drag-over border highlight via Style.Setter default
    - `c21c0d4` fix(view): scope drag-over highlight to queue list rounded box only
    - `a80afdd` fix(view): clear drag-over highlight in QueueList_Drop
    - `7af6bec` fix(view): only highlight when drag payload contains audio (folder fix)
    - `af51dde` fix(view): preserve multi-select when starting drag from a selected item
    - `5aa0c25` test: Phase 5 manual acceptance pass
  - **Phase 6**（feature/phase6-named-playlists）
    - `041d3bc` test: add xUnit skeleton with smoke test (Phase 6 sub-project A)
    - `c2bb0b3` feat(models): add Playlist record (Phase 6 B1)
    - `ad6148d` feat(playlists): Phase 6 multi-playlist core — schema v2 + container VM (B1-B11)
    - `991bb9e` feat(views): Phase 6 sidebar + prompt dialog + dual-state PlaylistView (B12-B14)
    - `9f07eb4` feat(converters): add BytesToBitmapImageConverter (Phase 6 sub-project C, debt #1 partial)
    - `18ff69a` docs: update PROJECT.md and COUPLING.md for Phase 6
    - `dd24ab3` fix: address Phase 6 code review findings (W3/W4/I9 + W1 doc)
  - **Phase 7**（debt #1 完整偿还，master 直接提交）
    - `012d872` refactor(vm): complete debt #1 — PlayerViewModel.BitmapImage → byte[]
  - **Phase 8**（ViewModel 单元测试，master 直接提交）
    - `f2d6de8` test: add PlayerViewModel unit tests (15 tests)
    - `e71a5a5` test: add PlaylistViewModel unit tests (16 tests)
    - `aa4040e` test: add PlaylistsViewModel unit tests (17 tests)
  - **Phase 9**（sidebar 歌单拖拽重排，master 直接提交）
    - `01b7db3` feat(views): add sidebar playlist drag-reorder
  - **Phase 10**（文件夹绑定歌单，master 直接提交）
    - `9d6a032` docs: add Phase 10 library scan design spec (folder-bound playlists)
    - `2cf99e2` docs: add Phase 10 library scan implementation plan
    - `8ffad2c` feat(models): add Playlist.SourceFolder + QueueState v3 (Phase 10)
    - `0d1f522` feat(models): add LibraryCacheEntry + LibraryDiff records (Phase 10)
    - `daf356e` docs: add SourceFolder XML doc to Playlist record
    - `816e57d` feat(services): add ILibraryScannerService + LibraryScannerService (Phase 10)
    - `56bbcab` revert: remove premature DI registration for Phase 10 services (belongs to Task 6)
    - `1c680e5` refactor: extract AudioExtensions to Models.AudioConstants (fix layer violation)
    - `b46ab6a` feat(services): add ILibraryCache + JsonLibraryCache (Phase 10)
    - `82474b0` feat(services): add IFileDialogService.OpenFolder (Phase 10)
    - `0131423` feat(di): register ILibraryScannerService + ILibraryCache (Phase 10)
    - `02f9d0e` feat(vm): add PlaylistViewModel.IsScanning/HasScanError/SourceFolder (Phase 10)
    - `8fc5322` feat(vm): add PlaylistsViewModel ImportFolder/Rescan/Refresh (Phase 10)
    - `589d4d8` feat(vm): MainViewModel.InitializeAsync triggers background rescan (Phase 10)
    - `5309984` feat(view): PlaylistView toolbar adds ImportFolder + Refresh buttons (Phase 10)
    - `2bf93c0` feat(view): sidebar shows folder icon for folder-bound playlists + scanning indicator (Phase 10)
  - **Phase 11**（设置面板）
    - `6b5658d` docs: add Phase 11 settings panel design spec
    - `508c6b1` docs: add Phase 11 settings panel implementation plan
    - `eb6cfd4` feat(view): add SettingsDialog with volume slider (Phase 11)
    - `8949824` fix(view): SettingsDialog async OnLoaded + error handling
    - `c83cbfe` feat(view): PlayerBar adds gear button for settings (Phase 11)
    - `4940aed` feat(view): MainWindow adds Ctrl+, shortcut for settings (Phase 11)
    - `86c5b91` fix(view): MainWindow Ctrl+, uses existing _persistence field
