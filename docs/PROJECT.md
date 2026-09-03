# D-player 项目文档

> 一个轻量级、本地优先的 Windows 音乐播放器（WPF + .NET 10 + NAudio）。
>
> 文档日期：2026/06/25（对应 HEAD `808fb98`） · 对应分支：`master` · 当前阶段：**Phase 13 完成 + 频谱调优**（音频可视化 - FFT 频谱条形图，参数/布局/主题打磨） · **项目名：D-player（原 UmaPlayer；C# 命名空间 DPlayer）**

---

## 1. 项目简介

**D-player** 是一款面向 Windows 桌面的本地音乐播放器，灵感来源于 foobar2000 / Winamp。Phase 1 实现单曲播放骨架，Phase 2 加入内存播放队列（多选入队、自动推进、随机/循环模式）。Phase 3 重构 ViewModel 层（按职责拆分 + 抽象元数据读取 + 修正持久化合并纪律），偿还 4 项技术债。Phase 4 加入队列持久化（关闭时写 `queue.json`，启动时恢复列表 + Shuffle/Repeat 模式 + CurrentIndex）。Phase 5 加入拖拽支持（外部音频文件拖入入队、队列内项拖拽重排含多选、视觉反馈含边框高亮 + 插入线 Adorner），同时偿还 in-flight `RemoveTrack`/`MoveTracks` 的 `_playToken` 残留债。Phase 6 加入多命名歌单支持（Spotify 双指针模型：Viewed vs Current）、xUnit 测试骨架、BytesToBitmapImageConverter（Debt #1 部分偿还）。Phase 7 完成债务 #1 完整偿还（PlayerViewModel.BitmapImage → byte[]），VM 层不再依赖 WPF 类型。Phase 8 建立 ViewModel 单元测试体系（50 个测试覆盖 PlayerVM / PlaylistVM / PlaylistsVM）。Phase 9 加入 sidebar 歌单拖拽重排（复用 Phase 5 的 Adorner + 多选拖拽保护模式）。Phase 10 加入文件夹绑定歌单（指定文件夹递归扫描 → 创建/更新歌单，启动后台自动同步增删，手动刷新，JSON 元数据缓存），同时将音频后缀白名单从 View 层提取到 Models.AudioConstants 消除层级违规。Phase 11 添加设置对话框（默认音量滑块 + 音频输出灰色占位 + PlayerBar ⚙ 按钮 + Ctrl+, 快捷键）。Phase 12 UI 界面重构（PlayerBar 移到底部 + 圆形播放键 + PlaylistView 时长列/表头/行分隔线 + Sidebar 图标/选中态背景色 + 色板微调）。Phase 12 continued: 全局 Shuffle/Repeat（所有歌单共享）+ TrackInfoView 独立面板 + #列元数据 TrackNumber + 表头点击排序 + 导入文件夹改为添加到当前歌单 + 移除 Stop/OpenAndPlay 按钮 + Sidebar + 按钮直接新建歌单 + GridSplitter 列宽限制 + ViewBox 封面缩放 + 封面 ClipToBounds 圆角裁切。Phase 13 音频可视化（SampleAggregator FFT 频谱分析 + SpectrumView 自定义控件 + 32 条垂直频谱柱 + 4 种颜色主题 + 灵敏度/平滑度配置 + 设置持久化）。Phase 13 后续调优：FFT 尺寸 1024→2048→8192 提升低频分辨率、立体声先混单声道再加汉宁窗做 FFT、50% FFT 重叠提高更新率、对数频率分组 20Hz–16kHz + RMS + gamma 曲线、彩虹主题改为红→紫水平渐变、频谱移入 TrackInfoView 底部（高 120px）、全局 Slider 加 IsMoveToPointEnabled、SettingsDialog 保存留在 UI 线程即时同步 VM + 失败弹窗。

### 1.1 关键特性（已实现）

| 类别   | 能力                                                             |
|------|----------------------------------------------------------------|
| 文件导入 | Win32 OpenFileDialog 多文件选择                                     |
| 支持格式 | MP3 / WMA / FLAC / AAC / WAV（基于 Windows Media Foundation 原生解码） |
| 播放控制 | 播放 / 暂停 / 上一首 / 下一首 / 随机 / 循环                                  |
| 进度控制 | 拖拽 + 单击跳转的进度条；位置实时更新（≈30 Hz，节流）                                |
| 音量控制 | 0~1 线性滑块、一键静音/取消静音；通过 `VolumeSampleProvider` 实现                |
| 元数据  | 标题 / 艺术家 / 专辑 / 流派 / 年份 / 采样率 / 曲目号 / 内嵌封面（z440.atl.core）     |
| 主题   | 内置深色主题（深紫强调色）                                                  |
| 持久化  | 窗口位置/尺寸、默认音量保存到 `%LocalAppData%\D-player\settings.json`       |
| 播放列表 | 内存队列：多选入队、单项删除、清空、上/下一首、自然播完自动推进、#列元数据 TrackNumber、表头点击排序 |
| 队列持久化 | 关闭时写 `%LocalAppData%\D-player\queue.json`；启动恢复列表 + CurrentIndex + Shuffle/Repeat（Phase 4） |
| 拖拽 | 外部音频文件拖入末尾入队（白名单 .mp3/.wma/.flac/.aac/.wav）；队列内单/多选拖拽重排（含 ▶ 当前曲跟随、Shuffle 历史按对象身份重映射）；插入线 Adorner + 圆角列表框边框高亮（Phase 5） |
| 多命名歌单 | 创建/删除/重命名多个独立歌单；Viewed vs Current 双指针（切查看不打断播放，双击才跨歌单切换音频）；全局 Shuffle/Repeat（所有歌单共享）；各歌单独立 CurrentIndex；v1→v2 schema 自动迁移（Phase 6） |
| 文件夹绑定歌单 | 指定文件夹扫描 → 创建歌单; 启动后台自动同步增删; 手动刷新; 元数据缓存; 导入文件夹添加到当前歌单 (Phase 10) |
| 设置 | 模态对话框：默认音量滑块；音频输出占位（Phase 12）；Ctrl+, 快捷键 (Phase 11) |
| 曲目信息面板 | 右侧独立 TrackInfoView：封面（ViewBox 自动缩放）+ 标题/艺术家/专辑/采样率；BackgroundSecondary 背景 (Phase 12 continued) |
| 音频可视化 | 32 条垂直频谱柱（8192 点 FFT + 汉宁窗 + 50% 重叠 + 对数分组 20Hz–16kHz + RMS/gamma）；4 种颜色主题（紫/蓝/绿/彩虹，彩虹为红→紫水平渐变）；灵敏度/平滑度配置；启用/禁用开关；置于 TrackInfoView 底部（高 120px）；设置持久化 (Phase 13) |

### 1.2 后续增量（未实现）

- M3U / PLS 等播放列表格式导入导出
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
D-player/
├── App.xaml(.cs)                # 应用入口；构建 DI 容器、加载主窗口
├── AssemblyInfo.cs              # ThemeInfo（资源字典位置）
├── D-player.csproj / .sln      # 项目/解决方案
├── appsettings.json             # 启动默认配置（构建时复制到输出目录）
│
├── Configuration/
│   └── AppSettings.cs           # 强类型配置 record（绑定到 "Player" section）
│
├── Models/
│   ├── Track.cs                 # 不可变 record：音轨信息（含封面字节数组）
│   ├── PlayState.cs             # enum: Stopped / Playing / Paused
│   ├── RepeatMode.cs            # enum: Off / List / One  (Phase 2)
│   ├── Playlist.cs              # 不可变 record: 歌单 (Id, Name, Items, CurrentIndex, ShuffleEnabled*, RepeatMode*, SourceFolder) (*=Phase 12 continued 写入时固定 false/Off, 实际全局状态在 QueueState 根级别) (Phase 6/10)
│   ├── QueueState.cs            # 不可变 record: queue.json schema v3 (Playlists + CurrentPlaylistId + ShuffleEnabled + RepeatMode + SourceFolder) (Phase 4/6/10/12 continued)
│   ├── MoveTracksArgs.cs        # 不可变 record: 队列内拖拽重排命令参数 (Phase 5)
│   ├── AudioConstants.cs         # 音频后缀白名单 (Phase 10, 从 DragDropExtensions 提取)
│   ├── LibraryCacheEntry.cs      # 缓存条目 record (Phase 10)
│   ├── LibraryDiff.cs            # 扫描增量同步 record (Phase 10)
│   ├── SpectrumConfig.cs        # 频谱分析配置 record (FftSize=8192/BarCount=32/Sensitivity/Smoothing) (Phase 13)
│   └── AudioDeviceInfo.cs       # 预留：设备信息
│
├── Services/                    # 业务/基础设施服务（全部基于接口）
│   ├── IPlaybackService.cs      # 核心播放抽象
│   ├── NAudioPlaybackService.cs # NAudio 实现（WASAPI + MediaFoundation）
│   ├── SampleAggregator.cs      # ISampleProvider 透明中间件：FFT 频谱分析 (Phase 13)
│   ├── IFileDialogService.cs
│   ├── Win32FileDialogService.cs# Microsoft.Win32.OpenFileDialog 封装
│   ├── ISettingsPersistence.cs
│   ├── JsonSettingsPersistence.cs # 持久化到 %LocalAppData%\D-player\settings.json
│   ├── LegacyDataMigration.cs   # 启动一次性迁移旧数据目录 UmaPlayer → D-player
│   ├── IPlaylistService.cs      # 多歌单持久化抽象 (Phase 6, 替换 IQueuePersistence)
│   ├── JsonPlaylistService.cs   # 持久化到 %LocalAppData%\D-player\queue.json; 内置 v1→v2 迁移 (Phase 6)
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
│   ├── PlaylistViewModel.cs    # 队列子 VM：Queue/推进算法 + Id/Name/IsActivePlaylist + SortedView/SortBy + ImportFolderToCurrent (Phase 3/6/12 continued)
│   └── PlaylistsViewModel.cs   # 多歌单容器：ObservableCollection<PlaylistVM> + 全局 Shuffle/Repeat + Add/Remove/Rename + HandleDoubleClickPlay + ImportFolder/Rescan/Refresh (Phase 6/10/12 continued)
│
├── Views/
│   ├── MainWindow.xaml(.cs)     # 主窗口；5 列布局(Sidebar | Splitter | Playlist | Splitter | TrackInfo) + PlayerBar 底部
│   ├── Dialogs/
│   │   ├── PromptDialog.xaml(.cs)    # 共享单输入对话框（新建/重命名歌单）(Phase 6)
│   │   └── SettingsDialog.xaml(.cs)  # 设置对话框（音量 + 音频输出占位）(Phase 11)
│   └── Controls/
│       ├── PlayerBar.xaml(.cs)  # 播放栏（进度/控制/音量 + 随机/循环按钮）
│       ├── PlaylistView.xaml(.cs)    # 播放队列（Phase 2 + Phase 5 拖拽 + Phase 6 IsActivePlaylist guard + #列/表头排序/导入文件夹到当前歌单）
│       ├── PlaylistsSidebarView.xaml(.cs) # 左侧歌单栏（+/- 按钮、ListBox、双击重命名、▶ 标记、📂 文件夹图标、🔄 扫描指示；+ 按钮直接新建歌单）(Phase 6/10/12 continued)
│       ├── TrackInfoView.xaml(.cs)   # 右侧曲目信息面板（封面 ViewBox 缩放 + 标题/艺术家/专辑/采样率 + 底部 SpectrumView）(Phase 12 continued/13)
│       ├── SpectrumView.xaml(.cs)    # 频谱可视化控件：32 柱 Canvas + CompositionTarget.Rendering 60fps + 4 色主题 (Phase 13)
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
│   └── ServiceCollectionExtensions.cs # AddDPlayerServices(...) DI 注册
│
├── Tests/                       # xUnit 测试项目 (Phase 6+，共 77 个测试)
│   ├── D-player.Tests.csproj   # 测试项目文件 (xUnit + NSubstitute + Coverlet)
│   ├── Smoke/
│   │   └── SmokeTests.cs                 # 冒烟测试：Track record 结构相等 (1)
│   ├── Services/
│   │   ├── LibraryScannerServiceTests.cs # 库扫描 (17) (Phase 10)
│   │   └── JsonLibraryCacheTests.cs      # 元数据缓存 (5) (Phase 10)
│   └── ViewModels/
│       ├── PlayerViewModelTests.cs        # Transport (15) (Phase 8)
│       ├── PlayerViewModelSpectrumTests.cs# 频谱 (5) (Phase 13)
│       ├── PlaylistViewModelTests.cs      # 队列 (13) (Phase 8)
│       └── PlaylistsViewModelTests.cs     # 多歌单 (21) (Phase 8/12)
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
   │   2. 构建 ServiceCollection (AddDPlayerServices)    │
   │   3. 解析 MainViewModel + ISettingsPersistence        │
   │      + IPlaylistService                               │
   │   4. new MainWindow(vm, persistence).Show()           │
   └──────────────┬─────────────────────────┬─────────────┘
                  │                         │
                  ▼                         ▼
   ┌──────────────────────┐    ┌──────────────────────────┐
   │     MainWindow       │    │  MainViewModel (Facade)  │
   │  (View, code-behind) │◀──▶│  ~130 行：持有子 VM       │
   │ - 窗口位置恢复/保存   │    │  + InitializeAsync()      │
   │ - 关闭写 queue.json  │    │  + CleanupAsync()         │
   │   (cancel-and-close) │    │  + debounce save          │
   │ - 5 列布局           │    └──────┬──────────┬─────────┘
   │ - PlayerBar 底部     │           │          │
   └──────────────────────┘           ▼          ▼
                              ┌─────────────┐ ┌──────────────────┐
                              │ PlayerVM    │ │ PlaylistsVM      │
                              │ Transport:  │ │ 多歌单容器:       │
                              │ Play/Pause/ │ │ 全局 Shuffle/    │
                              │ Position/Vol│ │ Repeat + 增删    │
                              │             │ │ 歌单 + 导入文件夹 │
                              └──────┬──────┘ └───────┬──────────┘
                                     │                │ 持有 N 个 PlaylistVM
                                     │  互不持引用     │
                                     ▼  仅共享 Service ▼
           ┌──────────────────────────┴──────────────────────────┐
           ▼                          ▼                          ▼
┌────────────────────┐  ┌────────────────────────┐  ┌──────────────────────┐
│ IPlaybackService   │  │ ITrackMetadataReader   │  │ ISettingsPersistence │
│ (NAudio impl)      │  │ (ATL impl) [Phase 3]   │  │ UpdateAsync(Func<>)  │
└────────────────────┘  └────────────────────────┘  └──────────────────────┘
┌────────────────────────┐  ┌──────────────────────┐  ┌──────────────────────┐
│ ILibraryScannerService │  │ ILibraryCache        │  │ IPlaylistService     │
│ (Recursive scan+Diff)  │  │ (JSON impl) [Ph10]   │  │ (JSON impl) [Ph6]    │
│ [Phase 10]             │  │                      │  │                      │
└────────────────────────┘  └──────────────────────┘  └──────────────────────┘

注：IFileDialogService 由 PlaylistViewModel（AddToQueue / ImportFolderToCurrent）+ PlaylistsViewModel（ImportFolderAsync）消费。
注：IPlaylistService 由 MainViewModel（启动读盘 + 关闭写盘 + debounce save）统一消费。
```

### 4.2 服务生命周期

注册位置：`Extensions/ServiceCollectionExtensions.cs`

| 服务 | 生命周期 | 说明 |
|------|----------|------|
| `IOptions<AppSettings>` | Singleton（框架） | 绑定 `appsettings.json` 的 `"Player"` 节，作为**启动默认快照** |
| `IPlaybackService` | **Singleton** | 持有 NAudio 设备资源，必须长生命周期 |
| `IFileDialogService` | Singleton | 无状态 |
| `ISettingsPersistence` | Singleton | 内部 `SemaphoreSlim` 并发互斥 |
| `IPlaylistService` | Singleton (Phase 6，替换 Phase 4 `IQueuePersistence`) | 独立 `SemaphoreSlim`，与 settings 文件锁互不影响；`LoadAsync` 绝不抛 |
| `ITrackMetadataReader` | Singleton (Phase 3) | 无状态，封装 z440.atl.core；`ReadAsync` 不抛 |
| `ILibraryScannerService` | **Singleton** (Phase 10) | 递归文件夹扫描 + Diff 计算；无状态 |
| `ILibraryCache` | **Singleton** (Phase 10) | JSON 元数据缓存 (`library-cache.json`)；内部 `SemaphoreSlim` |
| `IAudioDeviceManager` | Singleton（Stub） | 预留 |
| `IAudioOutputFactory` | Transient（Stub） | 预留；语义上由 `IPlaybackService` 创建即释放 |
| `PlayerViewModel` | **Transient** (Phase 3) | Transport 子 VM；DI 中**必须先于** `PlaylistViewModel` 注册；Phase 13 订阅 `SpectrumDataAvailable` |
| `PlaylistViewModel` | **Transient**（经 `Func<Playlist, PlaylistViewModel>` 工厂） (Phase 3/6) | 队列子 VM；由 `PlaylistsViewModel` 用工厂按需创建，seed 为动态参数 |
| `PlaylistsViewModel` | **Singleton** (Phase 6) | 多歌单容器；View 层 code-behind 经 `App.GetService<PlaylistsViewModel>()` 取用（双击跨歌单播放），故必须单例 |
| `MainViewModel` | **Transient** | Strict Facade，构造时聚合两个子 VM |

### 4.3 关键设计决策

1. **配置双源**：启动只读默认值用 `IOptions<AppSettings>`；运行时可变状态（窗口、音量、最后播放路径）走 `ISettingsPersistence` 写入用户数据目录。两者通过 `record with` 不可变更新协同。

2. **位置事件节流**：`NAudioPlaybackService.PollPositionAsync` 以 ~30 Hz（33 ms）轮询，并通过 `PositionThrottle` 进一步节流，避免 UI 线程被淹没。

3. **UI 线程封送**：服务层捕获启动时的 `SynchronizationContext`（必然是 UI 线程，因为 `IPlaybackService` 由 `App.OnStartup` 间接解析），所有事件通过 `_syncContext.Post` 派发，VM 直接绑定即可。

4. **拖拽 Seek 防回跳**：`PlayerViewModel.IsSeeking` 标志在拖动期间抑制 `PositionChanged` → `Position` 写入，避免拖拽时滑块被服务回写"拽回去"。

5. **静音状态保留音量**：`ToggleMute` 把当前 `Volume` 存到 `_volumeBeforeMute`，置 `Volume=0`；取消静音恢复。拖动滑块若有非零值会自动取消静音。

6. **窗口可见性自愈**：`MainWindow.EnsureVisible()` 检查恢复的位置是否在虚拟屏内（防止外接屏拔掉后窗口飘到屏外），不在则回退到主屏居中。

7. **面向接口 + 占位实现**：`IAudioDeviceManager` / `IAudioOutputFactory` 已注册 Stub，便于后续替换为真实多设备/Exclusive/ASIO 实现而不动 VM。

8. **Strict VM Facade（Phase 3/6）**：`MainViewModel` 作为子 VM 容器（~130 行），暴露 `Player` + `Playlists` + `InitializeAsync` + `CleanupAsync`，集中 debounce save。`PlayerViewModel`（transport）与 `PlaylistViewModel`（队列）**互不持引用**，仅通过 `IPlaybackService` Singleton 间接协作（PlayerVM 订阅 transport 事件；PlaylistVM 单独订阅 `TrackEnded` 推进队列）。PlaylistVM 通过 `Container` 属性代理读取 PlaylistsVM 的全局 Shuffle/Repeat 状态。View 跨域命令（如 PlayerBar 上的 ⏮/⏭ 调用 PlaylistVM，🔀/🔁 调用 PlaylistsVM）通过 `{Binding DataContext.Playlists.<sub>.<cmd>, RelativeSource={RelativeSource AncestorType=Window}}` 跨级绑定到 MainWindow 的 DataContext 解决。

9. **持久化 read-modify-write 原子化（Phase 3）**：`ISettingsPersistence` 接口由 `SaveAsync(AppSettings)` 改为 `UpdateAsync(Func<AppSettings, AppSettings> mutator)`，把"读盘 → 应用 mutator → 写盘"整个序列封进 `SemaphoreSlim` 锁内，根治了 VM 写音量与 `Window_Closing` 写窗口尺寸的合并竞态（COUPLING.md 旧债 #3）。注意：`UpdateAsync` 内部 `.ConfigureAwait(false)`，故调用方若需要在 mutator 内读取 WPF DependencyProperty，必须在 await 之前先把值捕获到 UI 线程局部变量（见 `MainWindow.xaml.cs:Window_Closing`）。

10. **队列持久化与 settings 隔离（Phase 4）**：队列状态独立写到 `%LocalAppData%\D-player\queue.json`。**为什么不复用 settings.json**：(a) 队列条目数量级远大于 settings 字段，混在一起每次拖音量都会让队列 JSON 重新序列化；(b) settings 是高频更新（音量、窗口尺寸），queue 是低频快照（仅关闭时一次），写入节奏不同；(c) 模式失败隔离 —— queue.json 损坏不影响窗口/音量恢复。`IQueuePersistence` 故意比 `ISettingsPersistence` 简化：只有 `LoadAsync()` 与 `SaveAsync(QueueState)`，没有 `UpdateAsync` —— PlaylistViewModel 是唯一权威源（"读队列" = `SnapshotState()`），无需读-改-写原子化。

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

22. **全局 Shuffle/Repeat（Phase 12 continued）**：Shuffle/Repeat 从 Playlist 级别提升到 PlaylistsViewModel 全局共享。原因：用户切换歌单时 Shuffle/Repeat 状态被重置（每歌单独立）的体验反直觉，且与主流播放器（Spotify / foobar2000）行为不一致。实现：PlaylistsViewModel 持有 `[ObservableProperty] ShuffleEnabled/RepeatMode`，PlaylistViewModel 通过 `Container` 属性代理读取；Playlist record 的同名字段保留但写入时固定 `false/Off`（向后兼容旧 queue.json）；QueueState 根级别新增 `ShuffleEnabled/RepeatMode` 持久化字段。

---

## 5. 模块详解

### 5.1 `Models`

- **`Track`**：不可变 record。字段：`FilePath` / `Title` / `Artist` / `Album` / `Genre` / `Year` / `SampleRate` / `AlbumArt` / `Duration` / `TrackNumber`（元数据曲目号，Phase 12 continued 新增）。`Duration` 与 `SampleRate` 在加载时由 `NAudioPlaybackService.LoadAsync` 通过 `track with { Duration=..., SampleRate=... }` 补齐。`AlbumArt` 为原始字节数组，由 VM 转 `BitmapImage`（限 200px、`Freeze()` 跨线程安全）。**注意 record 的结构相等：** 两个 `CreateFallback("X.mp3")` 占位 Track 在结构上相等 —— 任何按相等性查找/去重的代码（`IndexOf` / 默认 `HashSet<Track>`）都会塌陷它们。Phase 5 重排算法因此改用引用身份（`ReferenceEquals` + `ReferenceEqualityComparer.Instance`）。
- **`PlayState`**：`Stopped / Playing / Paused`。
- **`RepeatMode`**：`Off / List / One`（Phase 2）。
- **`QueueState`**（Phase 4）：不可变 record；`SchemaVersion=3`（v2→v3 新增 `Playlist.SourceFolder`，无迁移：缺失字段反序列化为 null）/ `Playlists: IReadOnlyList<Playlist>` / `CurrentPlaylistId` / `ShuffleEnabled: bool` / `RepeatMode: RepeatMode`（Phase 12 continued：全局播放模式，所有歌单共享，从 Playlist 级别提升到 QueueState 根级别）。**只持久化路径与队列态**，不携带 Track 元数据或封面 —— 启动时由 `PlaylistsViewModel.Hydrate` 为每条路径创建占位 Track，用户首次播放时由 `PlayTrackAtAsync` 升级为完整元数据。
- **`MoveTracksArgs`**（Phase 5）：不可变 record；`SourceIndices: IReadOnlyList<int>`（升序无重复，每项 ∈ [0, Queue.Count)） / `TargetIndex: int`（∈ [0, Queue.Count]，i 表示插到 i 之前；Count 表示末尾）。由 View 层 Drop handler 构造，这些不变量由 View 保证（VM 信任入参，无校验代码）。
- **`LibraryCacheEntry`**（Phase 10）：不可变 record；`FilePath` / `Title` / `Artist` / `Album` / `Genre` / `Year` / `Duration` / `SampleRate` / `TrackNumber`（Phase 12 continued 新增，元数据曲目号）。不含 `AlbumArt` 字节——按需加载，保持缓存文件体积小。
- **`AudioDeviceInfo`**：`(Id, Name, IsDefault)`，目前仅类型存在。

### 5.2 `Services/NAudioPlaybackService`

| 成员 | 说明 |
|------|------|
| `LoadAsync(Track)` | 在 `Task.Run` 上：销毁旧播放链 → 新建 `MediaFoundationReader` → `ToSampleProvider` → **`SampleAggregator`（Phase 13 频谱中间件）** → `VolumeSampleProvider` → `WasapiOut(Shared, 100ms)`；触发 `DurationChanged` / `TrackChanged` · Phase 3：在 DurationChanged 之前先广播 PositionChanged(Zero)，防止切到时长更短的曲时旧 Position 与新 Duration 并存（"4:05 / 3:20" glitch） |
| `Play / Pause / Stop` | 委派给 `IWavePlayer`；`Stop` 同时将 `CurrentTime` 归零（保留底层资源，再 `Play()` 会重播同一首） |
| `Unload()` | **完全释放**底层 reader/wavePlayer，清掉 `_currentTrack`；之后 `Play()` 是 no-op。**Phase 3：同时广播 `TrackChanged(null) + DurationChanged(Zero) + PositionChanged(Zero)`** 让 VM 清屏（标题/封面/时长/进度全归零）。`PlaylistViewModel.UnloadCurrentTrack` 在清空队列/删当前曲时调用 |
| `Seek(TimeSpan)` | 写 `reader.CurrentTime` 后**主动广播 PositionChanged**（Phase 3：暂停态下 PollPositionAsync 已退出，否则进度条不刷新，看上去像"没跳转"）；通过 `ClampToDuration` 截到 [0, TotalTime] |
| `Volume { get; set; }` | `Math.Clamp(0..1)`；运行时写入 `VolumeSampleProvider.Volume` |
| `PollPositionAsync` | 仅在 `PlaybackState==Playing` 时循环；每 33ms 派发一次 `PositionChanged` · Phase 3：经 `ClampToDuration` 截断，避免解码器尾部浮点越界 |
| `OnPlaybackStopped` | 区分 (1) 异常 → `PlaybackError`；(2) 自然播完（距 `TotalTime` ≤ 200ms） → `TrackEnded`；(3) 用户 `Stop` → 仅 `Stopped` |
| `SpectrumConfig { get; set; }` | Phase 13：运行时可更新；setter 把 `Enabled` 传播到 in-flight `SampleAggregator`（禁用后 FFT 停转，不空耗 CPU） |
| `SpectrumDataAvailable` 事件 | Phase 13：`SampleAggregator.SpectrumDataReady`（音频线程）→ `OnSpectrumDataReady` 经 `_syncContext.Post` 封送到 UI 线程再广播 |
| `Dispose()` | 拆事件、停止、释放 reader/wavePlayer；`DisposePlayback` 先解绑并清空 `_sampleAggregator`（Phase 13） |

### 5.2a `Services/SampleAggregator` + `Models/SpectrumConfig`（Phase 13）

`SampleAggregator` 是实现 `ISampleProvider` 的**透明中间件**，插在 `MediaFoundationReader.ToSampleProvider()` 与 `VolumeSampleProvider` 之间：原样传递 PCM（`Read` 返回源读取数），同时截取样本做 FFT。

处理管线（每填满一个 FFT 缓冲区触发一次）：
1. **多声道混单声道**：立体声逐帧取各声道均值，避免声道相位干扰频谱
2. **汉宁窗**：FFT 前统一加窗（`0.5*(1-cos(2πj/(N-1)))`），重叠时窗口不错位
3. **FFT**：`NAudio.Dsp.FastFourierTransform.FFT`，`SpectrumConfig.FftSize`（当前 **8192**，历经 1024→2048→8192 提升低频分辨率）
4. **对数频率分组**：20Hz–16kHz 按对数均分到 `BarCount`（**32**）条柱（人耳对低频更敏感）
5. **RMS + dB + gamma**：每桶取均方根幅度 → 增益 8x → `20*log10` 映射到 -60~0dB → 归一化 [0,1] → `pow(x, 0.8)` gamma 增强对比
6. **50% 重叠**：保留缓冲区后半段，更新率翻倍，动画更平滑
7. **复制数组触发事件**：`SpectrumDataReady?.Invoke(_spectrumData.ToArray())`（复制防止订阅者篡改共享缓冲区）

`SpectrumConfig`（不可变 record）：`Enabled` / `BarCount=32` / `Sensitivity`（0.5~2.0）/ `Smoothing`（0.0~0.95）/ `FftSize=8192`。`Enabled` 运行时可切（`NAudioPlaybackService.SpectrumConfig` setter 传播到聚合器）。

**线程边界：** `SpectrumDataReady` 在 **NAudio 音频线程**触发；`NAudioPlaybackService.OnSpectrumDataReady` 负责封送到 UI 线程。灵敏度增益 + 指数平滑在 `PlayerViewModel.HandleSpectrumData`（UI 线程）做，**不在此重复 32-bar 映射**（聚合器已完成）。

### 5.3 `Services/JsonSettingsPersistence`

- 路径：`%LocalAppData%\D-player\settings.json`
- 启动时若不存在则返回 `new AppSettings()`（默认值由 record 初始化器给出，**与 `appsettings.json` 不重复绑定**）
- **Phase 3 重构**：接口由 `SaveAsync(AppSettings)` 改为 `UpdateAsync(Func<AppSettings, AppSettings> mutator)`，把整段"读盘 → 应用 mutator → 写盘"封进 `SemaphoreSlim(1,1)` 临界区。调用方仅需提供 `s => s with { Field = newValue }`，再无合并竞态
- 读盘失败（损坏/权限）→ 以 `new AppSettings()` 为起点喂给 mutator，写盘照常；写盘失败则抛出（关闭流程调用方自行 catch）
- 私有 `ReadFromDiskNoLockAsync()`：调用方负责持锁；供 `LoadAsync` 与 `UpdateAsync` 共用

### 5.3a `Services/JsonPlaylistService`（Phase 6，替换 JsonQueuePersistence，Schema v3）

- 路径：`%LocalAppData%\D-player\queue.json`
- 与 settings 持久化结构对称：独立 `SemaphoreSlim(1,1)`、`WriteIndented=true`、`JsonStringEnumConverter`（让 `RepeatMode` 序列化成字符串而非整数，跨版本稳定且方便手动调试）
- 接口仅 `LoadAsync()` / `SaveAsync(QueueState)`，**故意没有 `UpdateAsync`** —— PlaylistsViewModel 是队列状态的唯一权威源，无需读-改-写合并
- `LoadAsync` 隐式契约：**绝不抛**（catch-all 静默 fallback 到 `new QueueState()`）。文件不存在/JSON 损坏/版本号不匹配/反序列化得 null 全部走同一回退分支；旧文件保留供用户排查。内置 v1→v2 一次性迁移（单条"默认歌单"）+ v2→v3 字段补充（`SourceFolder` 缺失即 null，无需迁移）
- `SaveAsync` 失败抛出，由 `MainWindow.Window_Closing` 自行 catch（与 settings 写盘失败行为对称：用户下次启动队列丢失，但不打扰关闭流程）

### 5.4 `ViewModels/MainViewModel`（Strict Facade，~130 行）

Phase 6 后暴露四个公开成员：

```csharp
public PlayerViewModel Player { get; }
public PlaylistsViewModel Playlists { get; }
public Task InitializeAsync();
public Task CleanupAsync();
```

构造由 DI 注入 `(PlayerViewModel, PlaylistsViewModel, IPlaybackService, IPlaylistService)`。`InitializeAsync` 读 queue.json + Hydrate 容器 + 触发后台文件夹扫描。debounce save 500ms 集中在本类（`StateChanged` → `ScheduleSave` → `SaveAfterDelayAsync`）。

`CleanupAsync()` 顺序固定：解绑 `StateChanged` → 取消 debounce → flush in-flight save → 同步 `BuildSnapshot` + `SaveAsync` → 遍历所有 `PlaylistVM.Cleanup()` → `await Player.CleanupAsync()` → `_player.Dispose()`。**必须先解绑后 Dispose**，避免事件 handler 在底层资源销毁后被回调。

**架构不变量（COUPLING.md §5）：** `PlayerViewModel` 与 `PlaylistViewModel` **互不持引用**，仅共享 `IPlaybackService` Singleton；本 Facade 不暴露 Player/Playlists/InitializeAsync/CleanupAsync 之外的任何成员（否则倒退为"转发 Facade"反模式）。

### 5.4a `ViewModels/PlayerViewModel`（Transport 子 VM，~320 行）

源生成器属性：`_isSeeking`, `_position`, `_duration`, `_playState`, `_currentTrack`, `_albumArtBytes`（Phase 7：byte[]，非 BitmapImage）, `_volume`, `_isMuted`；**Phase 13 频谱**：`_spectrumData`(float[32]), `_spectrumEnabled`, `_spectrumSensitivity`, `_spectrumColorTheme`, `_spectrumSmoothing` + 私有 `_smoothedSpectrum`。派生：`VolumeIcon`（🔇/🔊）、`SampleRateText`、`PositionNormalized`（0..1）、`SpectrumColorThemes`（["紫色","蓝色","绿色","彩虹"]）。

**Phase 10 新增成员：**
- `SourceFolder`（`string?`，构造时从 `Playlist` seed 传入）：文件夹绑定歌单的源路径；null 表示普通手动歌单
- `HasSourceFolder`（`bool`，派生）：sidebar DataTemplate 用，决定是否显示文件夹图标
- `IsScanning`（`[ObservableProperty] bool`）：由 `PlaylistsViewModel` 设置，指示后台扫描进行中
- `HasScanError`（`[ObservableProperty] bool`）：由 `PlaylistsViewModel` 设置，指示最近一次扫描失败

构造时订阅 `IPlaybackService` 的 **6 个事件**（`PositionChanged / StateChanged / DurationChanged / TrackChanged / PlaybackError` + **Phase 13 `SpectrumDataAvailable`**），并阻塞读盘加载持久化音量 + 频谱设置（`_isInitializing` 标志抑制初始化期的写盘）。**不订阅 `TrackEnded`**（那是 PlaylistViewModel 的职责）。

`[RelayCommand]`：`SeekStarted / SeekCompleted(normalized) / PlayPause / ToggleMute` / **`ToggleSpectrum`（Phase 13）**。

`partial void OnVolumeChanged(value)`：同步到 `_player.Volume` → 拖滑块到非零自动取消静音 → `_persistence.UpdateAsync(s => s with { DefaultVolume = value })`。

**Phase 13 频谱管线：**`HandleSpectrumData(float[] rawData)` —— 聚合器已完成 FFT→32-bar 映射，此处仅 `rawData.ToArray()` 复制 → 逐柱乘灵敏度增益（Clamp 0.5~2.0）→ 指数移动平均平滑（`_smoothedSpectrum[i]*smoothing + data[i]*(1-smoothing)`，smoothing Clamp 0~0.95）→ 写 `SpectrumData` 触发绑定；`SpectrumEnabled==false` 时直接 return（不更新）。四个 `OnSpectrum*Changed` 钩子调 `SaveSpectrumSettings()` 持久化；`OnSpectrumEnabledChanged` 额外把 `_player.SpectrumConfig with { Enabled=value }` 回写，禁用后 FFT 停转。

`CleanupAsync()`：解绑 **6 个事件**（含 `SpectrumDataAvailable`）+ 用观察属性 `Volume`（**非**陈旧的 `_settings` 字段）持久化最后一次音量。**不 Dispose `IPlaybackService`**（PlaylistViewModel 还在用，Facade 层统一 Dispose）。

`HandleTrackChanged(Track? track)`：track 为 null 时把 `CurrentTrack` 和 `AlbumArtImage` 一起置 null（XAML 的 `FallbackValue='No track loaded'` 处理标题显示）。

### 5.4b `ViewModels/PlaylistViewModel`（队列子 VM，~650 行 / Phase 4 增加 LoadFromDisk + SnapshotState + PlayCurrent / Phase 10 增加 SourceFolder + IsScanning / Phase 12 continued 增加排序 + ImportFolderToCurrent）

源生成器属性：`_currentIndex`（-1 表示未选）, `_selectedTrack`（UI 列表选中项，与播放无关）。集合：`ObservableCollection<Track> Queue`。私有：`HashSet<int> _shuffleHistory` / `Random _random` / `int _playToken`（重入哨兵）。派生：`HasCurrentTrack`。

**Phase 12 continued 变更：**
- `ShuffleEnabled` / `RepeatMode` / `RepeatActive` / `ToggleShuffle` / `CycleRepeat` **已移除**（提升到 `PlaylistsViewModel` 全局共享）。本 VM 通过 `Container` 属性代理读取全局状态：`ShuffleEnabled => Container?.ShuffleEnabled ?? false`，`RepeatMode => Container?.RepeatMode ?? RepeatMode.Off`
- `OpenAndPlay` 命令**已移除**（PlayerBar 上的 📂 按钮已删除）
- `Container`（`PlaylistsViewModel?`，internal）：由 `PlaylistsViewModel.HookPlaylistVm` 设置，用于读取全局 Shuffle/Repeat 状态
- `SortedView`（`ICollectionView`）：排序后的队列视图，ListBox 绑定此属性而非直接绑 Queue
- `SortBy(string column)`：按指定列物理重排 Queue（TrackNumber / Title / Artist / Album / Duration）；再次点击同列切换升/降序；排序后更新 `CurrentIndex` 跟踪当前播放曲
- `GetSortKey(Track, string)`（私有静态）：排序键提取辅助方法
- `ClearShuffleHistory()`（internal）：由 `PlaylistsViewModel.ToggleShuffle` 调用，清空已播过历史
- `[RelayCommand] ImportFolderToCurrent()`：选文件夹 → 递归扫描音频文件 → 读取元数据入队当前歌单（不再创建新歌单）
- `AddToQueue` 现在读取元数据（`_metadataReader.ReadAsync`）而非仅创建占位 Track

构造时订阅 `IPlaybackService.TrackEnded` 用于自动推进；`Queue.CollectionChanged` 触发 Next/Prev/PlayCurrent 命令 `NotifyCanExecuteChanged`；构造尾段从 seed 恢复队列（`File.Exists` 过滤 + `MapCurrentIndexAfterFilter` 重映射）。

`[RelayCommand]`：`AddToQueue / RemoveTrack(int) / ClearQueue / PlayTrackAt(int) / NextTrack / PrevTrack / PlayCurrent` / `DropExternalFiles(IReadOnlyList<string>)`（Phase 5） / `MoveTracks(MoveTracksArgs)`（Phase 5） / `ImportFolderToCurrent`（Phase 12 continued）。

`ToRecord()`：把当前状态打包成 `Playlist` record（`ShuffleEnabled=false` / `RepeatMode=Off` 占位，实际全局状态由 `PlaylistsViewModel.BuildSnapshot` 负责）。

**Phase 5 新增成员：**
- `[RelayCommand] DropExternalFiles(IReadOnlyList<string> paths)`：与 `AddToQueue` 同语义入队管线（读元数据 → Queue.Add），不触发播放。路径白名单过滤由 View 层 `DragDropExtensions.FilterAudioPaths` 提前完成，VM 信任入参。
- `[RelayCommand] MoveTracks(MoveTracksArgs args)`：队列内重排算法（spec §4 八步算法）。**入口先 `_playToken++`** 顶替 in-flight `PlayTrackAtAsync`（同时偿还旧债 #5）。算法用对象身份（`ReferenceEquals` + `ReferenceEqualityComparer.Instance`）回找 `CurrentIndex` 与 `_shuffleHistory`，不做索引算术 —— Track record 的结构相等会让 `Queue.IndexOf(currentTrackObj)` 在出现重复占位时返回首个结构等价匹配而非原始那一个。不调 `_player.Unload()`，重排不中断播放。
- `RemoveTrack` 入口加 `_playToken++`（Phase 5 顺带还债 #5）：以前删除非当前曲不顶替 token，能让 in-flight `Queue[index] = meta` 写到错位；现已关闭。

**关键私有方法**（与旧 MainViewModel 等价）：
- `PlayTrackAtAsync(int, int skipCount=0)`：抢占 `_playToken` → 读元数据 → `Queue[i] = meta` → `LoadAsync` → `Play`；每个 `await` 后校验 token，被顶替则静默退出；失败连续 3 次自动停止
- `CalculateNextIndex(int? failedIndex)`：纯算法，按 (Shuffle × RepeatMode) 4 种组合返回下一索引；Shuffle 用 `_shuffleHistory` 排除已播
- `HandleTrackEnded()`：RepeatOne 重播当前，否则走 `CalculateNextIndex`
- `UnloadCurrentTrack()`：`_playToken++` 顶替 in-flight → `_player.Unload()`（NAudio 服务自动广播 TrackChanged(null) 让 PlayerVM 清屏）

`Cleanup()`：同步解绑 `TrackEnded`，由 `MainViewModel.CleanupAsync` 调用。

### 5.4c `ViewModels/PlaylistsViewModel`（多歌单容器，Phase 6 + Phase 10 + Phase 12 continued）

源生成器属性：`_viewedPlaylist`（UI 当前选中）、`_shuffleEnabled`、`_repeatMode`（Phase 12 continued：全局播放模式，所有歌单共享）。集合：`ObservableCollection<PlaylistViewModel> Playlists`。私有：`Func<Playlist, PlaylistViewModel>` 工厂委托、`IPlaybackService`、`IFileDialogService`、`ILibraryScannerService`、`ILibraryCache`、`ITrackMetadataReader`、`string _currentPlaylistId`。派生：`RepeatActive`（`RepeatMode != RepeatMode.Off`）。

**Phase 6 成员：**`[RelayCommand]`：`AddPlaylist / RemovePlaylist / RenamePlaylist`；`HandleDoubleClickPlay`（跨歌单双击路由）；`RecomputeIsActiveFlags`（切 CurrentPlaylistId 后批量刷新 sidebar ▶ 标记）；`StateChanged` 事件（debounce save 触发点）。

**Phase 12 continued 新增成员：**
- `IPlaybackService` 依赖（构造注入）：用于 `RemovePlaylist` 时停止播放
- `ITrackMetadataReader` 依赖（构造注入）：用于 `ApplyCachedMetadataSync` 回读旧缓存缺少 TrackNumber 的文件
- `ShuffleEnabled` / `RepeatMode`（`[ObservableProperty]`）：全局播放模式，从 Playlist 级别提升到容器级别
- `RepeatActive`（派生）：循环按钮激活状态
- `[RelayCommand] ToggleShuffle()`：切换 Shuffle → 同时清空当前歌单的 `_shuffleHistory`
- `[RelayCommand] CycleRepeat()`：循环模式三态循环 Off → List → One → Off
- `RemovePlaylist` 增强：若删除的是当前播放歌单，调 `_player.Stop()` + `_player.Unload()` 停止播放并清空 `CurrentPlaylistId`
- `HookPlaylistVm` 设置 `vm.Container = this`，让 PlaylistVM 代理读取全局 Shuffle/Repeat
- `Hydrate` 从 snapshot 加载全局 `ShuffleEnabled` / `RepeatMode`
- `BuildSnapshot` 保存全局 `ShuffleEnabled` / `RepeatMode`
- `ApplyCachedMetadataSync` 增强：当缓存条目 `TrackNumber` 为 null 时回读文件补全元数据

**Phase 10 新增成员：**
- 构造函数新增 `ILibraryScannerService scanner` + `ILibraryCache cache` 两个依赖
- `[RelayCommand] ImportFolderAsync()`：弹 `IFileDialogService.OpenFolder` → 递归扫描 → 创建文件夹绑定歌单（`Playlist` seed 带 `SourceFolder`）
- `RescanFolderBoundPlaylistsAsync()`：启动时由 `MainViewModel.InitializeAsync` 触发；遍历所有 `HasSourceFolder` 的 VM，逐个增量同步（`LibraryDiff`）
- `[RelayCommand] RefreshPlaylistAsync(PlaylistViewModel?)`：手动刷新单个文件夹绑定歌单
- `RescanSinglePlaylistAsync(vm, sourceFolder)`：核心扫描逻辑 —— 调 `ILibraryScannerService.ScanAsync` + `ILibraryCache.LoadAsync/SaveAsync` → 计算 diff → 增删 Queue → 设 `IsScanning`/`HasScanError`

### 5.5 `Views`

- **`MainWindow`**：两行 Grid —— Row 0 `ContentGrid`（5 列：Sidebar | Splitter | Playlist | Splitter | TrackInfo）+ Row 1 `PlayerBar`（底部，自适应高度）。`SidebarCol` 和 `TrackInfoCol` 各限制为窗口宽度一半（`ContentGrid_SizeChanged` + `DragDelta` 中到达上限直接锁死）。构造时同步读取窗口尺寸（`GetAwaiter().GetResult()`，启动阻塞 < 几 ms 可接受）；若持久化的 `WindowHeight < 500`（Phase 1 旧值）则一次性迁移到 650，避免列表不可见。关闭时采用 **cancel-and-close 模式**（Phase 4）：首次进入 `e.Cancel=true` + `_isClosing=true`，跑完 settings 写盘、`CleanupAsync`、`SnapshotState` + queue 写盘后调 `Close()` 重新触发 Closing 直接放行；这是为了让 `async void` 多 await 链不被 `Application.Shutdown → Dispatcher.InvokeShutdown` 截断。
- **`PlayerBar`** *(UserControl)*：播放栏。两行 Grid：①`Position | Slider | Duration` 进度条；②⏮ ▶/⏸ ⏭ 🔀 ⇄/🔁/🔂 按钮组（居中）+ 🔊音量 + ⚙设置（右对齐）。封面/信息已拆到 `TrackInfoView`。
  - Slider 的"单击跳转"由 `PreviewMouseLeftButtonDown` 手动从 `PART_Track` 计算比例并触发 `SeekCompletedCommand`；点击 Thumb 时不触发（通过 `FindAncestor<Thumb>` 检测，转交给原生 `DragStarted/DragCompleted`）。Thumb 默认 8px 圆点半透明，悬停/拖拽放大到 14px 不透明
  - `⏮` / `⏭` 通过 `{Binding DataContext.Playlists.ViewedPlaylist.<XxxCommand>, RelativeSource={RelativeSource AncestorType=Window}}` 跨级绑定到 `PlaylistViewModel`；`🔀` / `⇄/🔁/🔂` 绑到 `Playlists.ToggleShuffleCommand` / `Playlists.CycleRepeatCommand`
  - Stop 按钮和 📂 OpenAndPlay 按钮**已移除**（Phase 12 continued）
  - **▶/⏸ 按钮的双绑定（Phase 4）**：默认 `Command={Binding PlayPauseCommand}`（PlayerVM 的 transport 切换）；当 `CurrentTrack==null` 时通过 `<DataTrigger Binding="{Binding CurrentTrack}" Value="{x:Null}">` 切到 `Playlists.ViewedPlaylist.PlayCurrentCommand` —— 启动后队列已恢复但 transport 空闲，第一次按 ▶ 触发首次加载 + 播放，`TrackChanged(track)` 让 trigger 失活，回到 PlayPauseCommand。**注意 inline `<Style TargetType="Button">` 必须 `BasedOn="{StaticResource {x:Type Button}}"`**，否则会替换掉 `Themes/Controls.xaml` 中的隐式主题样式，按钮回退到 OS 原生白底（COUPLING.md §5）
- **`PlaylistView`** *(UserControl, Phase 2 + Phase 5 拖拽 + Phase 12 continued)*：队列界面。两行 Grid：①工具栏 `[+ 添加][清空][导入文件夹到当前歌单][刷新]` 左对齐；②`ListBox` 绑 `SortedView`，每项含 ▶ 当前曲标记 + `#` 列（TrackNumber）+ 标题/艺术家/专辑/时长 + `×` 删除按钮
  - **# 列**：显示元数据 `TrackNumber`（Phase 12 continued 新增），从 `ITrackMetadataReader` 读取
  - **表头排序**：点击列头触发 `SortBy(column)` 物理重排 Queue（Phase 12 continued）；表头用 TextBlock + MouseLeftButtonDown（非 Button，消除内边距对不齐问题）
  - 当前曲 ▶ 标记由 code-behind 维护：订阅 `PlaylistViewModel.PropertyChanged` (CurrentIndex) / `Queue.CollectionChanged` / `ItemContainerGenerator.StatusChanged`（应对虚拟化容器回收和 `Queue[i] = meta` 替换）；▶ 标记在 # 列之前（最左列）
  - 交互：双击播放、Delete 键删除、工具栏按钮触发命令
  - Shuffle / Repeat 按钮**已移到 PlayerBar**（Phase 12 continued）
  - **Phase 5 拖拽（XAML）：** 外层 `<Border AllowDrop="True">` 仅承载 OLE drop 区（覆盖工具栏 + 列表两行的 hit-test）；视觉高亮挂在 Row 1 的圆角 `<Border x:Name="QueueListBorder">`（用户期望仅看到列表区域被框住，不连带工具栏）。**BorderBrush 默认值放进 Style.Setter 而非 local 属性** —— WPF DP 优先级 `local > trigger setter > style setter`，写成 local 会让 `Style.Triggers` 失效（`docs/COUPLING.md §5` 隐式契约）。`ListBox` 加 `SelectionMode="Extended"` + `AllowDrop="True"` + 6 个事件挂接（`PreviewMouseLeftButton{Down,Up}` / `PreviewMouseMove` / `DragOver` / `DragLeave` / `Drop`）
  - **Phase 5 拖拽（code-behind）：** 拖拽启动用 `PreviewMouseLeftButtonDown` 记起点 + `PreviewMouseMove` 4px 阈值（`SystemParameters.MinimumHorizontal/VerticalDragDistance`）。**多选拖拽保护：** 用户 Ctrl+多选后再不带修饰键点击其中一项时，ListBox 默认会把选中塌成单项 —— `PreviewMouseLeftButtonDown` 在"已选 ≥ 2 项 + 无 Ctrl/Shift + 点中已选项"时 `e.Handled = true` 拦下默认塌选；若未过阈值就松手，`PreviewMouseLeftButtonUp` 手动塌成单选模拟原行为；过阈值真启动拖拽则保留多选。`DataObject` 自定义格式 `"DPlayer.QueueItems"` 区分内部重排，`DataFormats.FileDrop` 是外部文件。命中测试 `ComputeInsertIndex` 对每个 ListBoxItem 容器用 `TransformToAncestor(QueueList)` 算 bounds + 半高判定。`HideAdorner` 在 `Drop` / `DragLeave` 都清理插入线，避免残留
  - **Phase 5 高亮纪律：** `Root_DragEnter` 必须先 `FilterAudioPaths` 再决定是否高亮 —— 仅看 `FileDrop` 存在就亮会让文件夹/全非音频也亮（光标已显示禁止但边框还紫，视觉冲突）。`Root_Drop` 与 `QueueList_Drop` **都要清高亮** —— `QueueList_Drop` 设 `e.Handled=true` 后 Drop 事件不再冒泡到 `Root_Drop`，否则文件落到列表区高亮卡死
- **`PlaylistsSidebarView`** *(UserControl, Phase 6/9/10/12 continued)*：左侧歌单栏。`+` 按钮直接创建新歌单（Phase 12 continued 移除 ContextMenu 子菜单）；`-` 按钮删除选中歌单；ListBox 支持双击重命名、拖拽重排（Phase 9）。`▶` 标记由 `IsActivePlaylist` DataTrigger 驱动。文件夹绑定歌单显示 📂 图标 + 🔄 扫描指示。
- **`TrackInfoView`** *(UserControl, Phase 12 continued/13)*：右侧曲目信息面板。`DataContext = PlayerViewModel`。`BackgroundSecondary` 背景 + 圆角 Border。两行 Grid：Row0（`*`）曲目信息 —— 封面用 `Viewbox MaxWidth/MaxHeight=250` 包裹自动缩放（内含 `Border` 180×180 + `Image Stretch="UniformToFill"`），文本元数据（标题/艺术家/专辑/采样率）居中，无曲目时 DataTrigger 显示"播放曲目以查看信息"占位；Row1（`Auto`）**Phase 13 `SpectrumView`**（高 120px，绑 `SpectrumData`/`SpectrumColorTheme`，`Visibility` 绑 `SpectrumEnabled`）。封面 Border 加 `ClipToBounds=True` 圆角裁切（ViewBox 缩放后内容溢出问题）。
- **`SettingsDialog`** *(Window, Phase 11/13)*：设置对话框。模态 ToolWindow（**420×520**，Phase 13 因可视化区增高），9 行 Grid：通用（音量滑块 0..1）+ 音频输出灰色占位 + **音频可视化（Phase 13：启用 CheckBox + 灵敏度滑块 0.5~2.0 + 颜色主题 ComboBox + 平滑度滑块 0~0.95，DockPanel LastChildFill 布局：标签左/数值右/滑块填充）**。静态 `Show(Window?, ISettingsPersistence, IPlaybackService, PlayerViewModel?)` 工厂。构造注入 `IPlaybackService`（音量滑块实时调 `_playbackService.Volume`）+ 可选 `PlayerViewModel`（保存后即时同步频谱属性，免重启）。OnLoaded async 读盘加载音量 + 频谱设置并绑定滑块 ValueChanged 实时更新数值标签；Save_Click 通过 `UpdateAsync` 原子写盘后同步 VM —— **故意不 `ConfigureAwait(false)`，留在 UI 线程**才能直接写 `PlayerViewModel` 属性；失败弹 MessageBox（Phase 13）。

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

### 5.5c `Views/Controls/SpectrumView`（Phase 13）

频谱可视化 UserControl，纯 code-behind 绘制（无 VM 逻辑）：

- **32 条 `Rectangle` 柱**挂在 `SpectrumCanvas`（`Height=120` / `ClipToBounds`）；`RecalculateBarLayout` 在 `SizeChanged` 时按 `ActualWidth` 重算柱宽（`(ActualWidth - 31*BarSpacing) / 32`，`BarSpacing=2`），`Canvas.SetBottom(0)` 底部对齐
- **两个 DependencyProperty**：`SpectrumData`（`float[32]`，默认全 0）、`ColorTheme`（`int`，默认 0）。`OnSpectrumDataChanged` 把每柱数据换算成目标高度 `_targetHeights[i] = MinBarHeight(2) + data[i]*(MaxBarHeight(118)-2)`
- **60fps 渲染循环**：`CompositionTarget.Rendering += OnRendering`，每帧 `newHeight = current + (target-current)*AnimationSmoothFactor(0.3)` 做缓动；`Unloaded` 时解绑（防内存泄漏）
- **4 种颜色主题**：紫/蓝/绿为 `LinearGradientBrush`（底→顶渐变，全部 `Freeze()`）；**彩虹（theme==3）为每柱一色** —— HSV 色相从左 0°(红) 线性到右 300°(紫)（`hue = 300/31*i`，`HsvToRgb`），水平渐变不循环。`OnColorThemeChanged` 切换 `Fill`
- 数据源：`TrackInfoView.xaml` 绑 `PlayerViewModel.SpectrumData` / `SpectrumColorTheme` / `SpectrumEnabled`

### 5.6 `Themes`

深色 + 紫色强调（Catppuccin Mocha 风格）。所有控件模板写入 `Themes/Controls.xaml`，包括自定义的 Slider 模板（紫色已填充段 + 圆形 Thumb）。资源在 `App.xaml` 合并为应用级资源。Phase 12 continued 色板微调：`AccentPrimary` #7C4DFF → #9E7CFF（提亮）、`AccentHover` → #B9A0FF、`SliderThumb` → #9E7CFF；随机/循环激活色改用 `AccentHover`（更亮，深色背景下易辨认）。Phase 13：全局 Slider 隐式样式加 `IsMoveToPointEnabled=True` setter —— 所有滑块（音量/灵敏度/平滑度）单击轨道即跳到点击位置，无需拖动 Thumb。

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

### 6.2 运行时持久化：`%LocalAppData%\D-player\settings.json`

由 `JsonSettingsPersistence` 读写，包含与 `AppSettings` 相同的字段；首次启动文件不存在时使用 record 默认值。

**更名数据迁移（UmaPlayer → D-player）**：数据目录从 `%LocalAppData%\UmaPlayer\` 改为 `%LocalAppData%\D-player\`。`App.OnStartup` 最早期调用 `LegacyDataMigration.MigrateIfNeeded()`（在任何持久化服务被 DI 构造前），把旧目录内文件逐个搬到新目录（新目录已有同名文件则跳过 → 幂等；失败静默吞掉不阻断启动，旧数据保留）。

**当前被持久化的字段**：`DefaultVolume`、`WindowLeft/Top/Width/Height`、**`SpectrumEnabled/SpectrumSensitivity/SpectrumColorTheme/SpectrumSmoothing`（Phase 13）**。
**已建模但未启用**：`OutputMode`、`PreferredDeviceId`、`LastPlayedPath`。

### 6.3 队列快照：`%LocalAppData%\D-player\queue.json`（Phase 4/6/10）

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
  "CurrentPlaylistId": "guid...",
  "ShuffleEnabled": false,
  "RepeatMode": "Off"
}
```

Phase 12 continued: `ShuffleEnabled` / `RepeatMode` 提升到 QueueState 根级别（全局共享），Playlist 级别的同名字段保留但写入时固定为 `false` / `Off`（向后兼容）。

写时机：`MainWindow.Window_Closing`（每次关闭整队列覆盖一次）。读时机：`PlaylistViewModel` 构造期同步读盘。文件不存在/JSON 损坏/版本不匹配 → 静默 fallback 到空队列（保留旧文件供用户排查）。`Items` 中已被外部移动/删除的路径在加载时自动过滤；`CurrentIndex` 通过"向后滑、再向前回退"的算法映射到过滤后的位置（spec §5.1）。

### 6.4 元数据缓存：`%LocalAppData%\D-player\library-cache.json`（Phase 10）

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
PlaylistsSidebarView + 按钮 → AddPlaylistCommand（直接创建新歌单，无 PromptDialog）
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
用户点击 ▶ (PlayerBar 按钮 → DataTrigger 跨级绑定 Playlists.ViewedPlaylist.PlayCurrentCommand)
    │
    ▼
PlaylistViewModel.PlayCurrent
  → PlayTrackAtAsync(CurrentIndex)
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

### 7.5 频谱数据流（Phase 13）

```
WasapiOut 拉流 → VolumeSampleProvider → SampleAggregator.Read(buffer)
  → 混单声道 + 填 FFT 缓冲区（满 8192 点）
  → 汉宁窗 → FFT → 对数分组 32 桶（RMS + gain8x + dB + gamma）→ 50% 重叠
  → SpectrumDataReady(float[32].ToArray())   ── 音频线程 ──▶
NAudioPlaybackService.OnSpectrumDataReady
  → _syncContext.Post 封送 ── UI 线程 ──▶ SpectrumDataAvailable(data)
PlayerViewModel.HandleSpectrumData(data)
  → if (!SpectrumEnabled) return
  → 复制 → 乘灵敏度增益 → 指数平滑(_smoothedSpectrum) → SpectrumData = smoothed.ToArray()
TrackInfoView → SpectrumView.SpectrumData (DP) → OnSpectrumDataChanged 算 _targetHeights
  → CompositionTarget.Rendering 每帧缓动 Rectangle.Height（60fps）

设置变更（SettingsDialog.Save_Click / PlayerViewModel.OnSpectrum*Changed）
  → UpdateAsync 写 settings.json + 同步 PlayerViewModel 属性
  → OnSpectrumEnabledChanged 额外回写 _player.SpectrumConfig.Enabled（禁用即停 FFT）
```

---

## 8. 构建与运行

### 8.1 先决条件

- Windows 10/11（开发于 Windows 11 IoT Enterprise LTSC 2024）
- .NET 10 SDK（`net10.0-windows`）
- 任意 IDE：Visual Studio 2026+ / JetBrains Rider / VSCode + C# Dev Kit

### 8.2 命令行构建

```bash
dotnet restore D-player.sln
dotnet build   D-player.sln -c Debug
dotnet run     --project D-player.csproj
```

输出目录：`bin/Debug/net10.0-windows/`，可执行：`D-player.exe`。

### 8.3 发布（独立可执行）

```bash
dotnet publish D-player.csproj -c Release -r win-x64 \
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
- **ViewBox + CornerRadius + ClipToBounds（Phase 12 continued）**：`ViewBox` 缩放子元素时会突破父 `Border` 的 `CornerRadius` 圆角裁切区域，导致封面方形直角溢出圆角边框。修复：给封面 `Border` 加 `ClipToBounds=True`，让 WPF 裁切到 Border 边界内。同时封面 `Border` 不能有 `CornerRadius`（与播放时直角不一致），圆角仅在外层容器 Border 上设置。
- **频谱事件跨线程（Phase 13）**：`SampleAggregator.SpectrumDataReady` 在 NAudio 音频渲染线程触发，`NAudioPlaybackService.OnSpectrumDataReady` 必须经 `_syncContext.Post` 封送到 UI 线程再广播 `SpectrumDataAvailable`；`PlayerViewModel.HandleSpectrumData` 直接在 UI 线程更新 `SpectrumData` 绑定属性。若跳过封送会跨线程触碰 DP 抛异常。`SettingsDialog.Save_Click` 同理故意不用 `ConfigureAwait(false)`，留在 UI 线程才能直接写 `PlayerViewModel` 属性。

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
  - **Phase 12**（UI 界面重构）
    - `9b0bf1d` feat(theme): adjust color palette — improve selected/disabled contrast, add DangerHover
    - `2b82984` feat(view): move PlayerBar to bottom (Spotify-style layout)
    - `36d083f` feat(view): PlayerBar — round play button, seek bar thumb hover, unified spacing
    - `1b0ea7c` feat(view): PlaylistView — header row, duration column, row dividers, red delete hover
    - `a14fc85` feat(view): Sidebar — music/folder icons, selected state background color, rounded corners
  - **Phase 12 continued**（UI 界面重构续）
    - `8dab75b` docs: update PROJECT.md and COUPLING.md for Phase 12 UI refactor
    - `d19599d` feat(theme): Button style — explicit default background, improved comments
    - `9d574e3` fix(view): seek bar Thumb visible by default — small (8px, 50% opacity) then enlarges on hover (14px, 100%)
    - `9b7a1ff` fix(view): SeekBar Thumb 圆形裁切 — 覆盖 Thumb 模板直接控制 Ellipse 尺寸
    - `3c0eaf3` fix(view): 进度条点击区域扩大 — 外层透明 Border 填满 Slider 高度
    - `9c17b6d` fix(view): 去掉按钮和 Thumb 的黑色虚线焦点框
    - `20bcc0b` refactor(view): 移除 PlayerBar 停止和导入歌曲按钮及相关逻辑
    - `def7770` fix(view): 修复上一首/下一首按钮绑定路径
    - `a8202cb` fix: 歌曲时长显示 0:00 — 用 ATL 读到的 DurationMs 替代硬编码 Zero
    - `a18df44` fix(view): 表头与数据列对齐 — 覆盖 ListBox 模板共享容器宽度
    - `013abb5` fix(view): 曲目列表标题/艺术家/专辑列居中对齐
    - `2800d41` refactor(view): 随机/循环按钮从 PlaylistView 移到 PlayerBar
    - `0ae9672` fix: 随机/循环按钮未激活时颜色与其它按钮统一 — ForegroundSecondary → ForegroundPrimary
    - `97de388` fix(view): 循环按钮 FontFamily 统一为 Segoe UI Emoji — 与随机按钮一致
    - `3d9a182` fix(view): 曲目列表顶部边距 0→8
    - `cb0b50c` refactor(view): 曲目信息从 PlayerBar 拆出到独立 TrackInfoView，置于曲目列表右侧
    - `6fcaf6d` fix(view): 全局关闭焦点虚线框 — ListBox/ListBoxItem/TextBox/Slider 加 FocusVisualStyle={x:Null}
    - `87d8f0d` fix(view): GridSplitter 拖拽时的黑色虚线框 — 加 FocusVisualStyle={x:Null}
    - `d44dd07` fix(view): 歌单/曲目列表项点击虚线框 — 显式 ListBoxItem 样式加 FocusVisualStyle={x:Null}
    - `06f8786` fix: 添加/拖入歌曲时立即读取元数据 — CreateFallback → ReadAsync
    - `7428c38` fix: 删除当前播放歌单后停止播放并清空指针
    - `0165c2c` refactor: Shuffle/Repeat 改为全局设置，所有歌单共享
    - `3ffd00e` fix(view): TrackInfoView 加 BackgroundSecondary 背景 + 内容顶部居中
    - `0eecaef` fix(view): TrackInfoView 封面随宽度缩放 — ViewBox 包裹 + 去掉文本固定 MaxWidth
    - `5c1c577` fix(view): TrackInfoView 封面去掉 MaxWidth/MaxHeight 限制，完全跟随容器缩放
    - `04ca124` fix(view): 三个区域底部对齐 — PlaylistView/TrackInfoView 底部 margin 改为 0
    - `0d4c31e` feat(view): 曲目列表添加 # 行号列
    - `7b8cdfe` feat: # 列改为读取元数据 TrackNumber + 修复 marker 索引
    - `e1b26e0` fix(view): ▶ 标记位置 — 用 x:Name 定位替代 FindChildByOrder 索引
    - `63ed02a` fix(view): ▶ 标记移到最左列（# 列之前）
    - `6c6392f` fix: 文件夹歌单旧缓存缺少 TrackNumber 时回读文件补全
    - `a8dc5f6` feat(view): 曲目列表表头点击排序
    - `6c5fc13` feat(view): # 列表头可排序 — Tag=TrackNumber
    - `fc91ab9` fix(view): 表头改回 TextBlock + MouseLeftButtonDown — 消除 Button 内边距导致的对不齐
    - `e9d948d` fix: 随机/循环激活色 AccentPrimary→AccentHover（更亮，深色背景下易辨认）
    - `37fdc00` fix: 强调色提亮 — AccentPrimary #7C4DFF→#9E7CFF, AccentHover→#B9A0FF
    - `d9db167` fix: Thumb 默认透明度 0.5→0.8，减少与轨道的明暗差异
    - `7425273` fix(view): 侧边栏/曲目信息列宽限制为窗口宽度一半
    - `b45ec3c` fix(view): 拖拽实时限制列宽 — DragDelta 中到达上限直接锁死
    - `d6073fb` fix: 曲目信息拖拽限制方向修正 — 向左拖(HorizontalChange<0)才拦截
    - `96d4189` fix: 排序改为物理重排 Queue — 上一曲/下一曲跟随新顺序
    - `3e1e959` refactor(view): 导入文件夹从曲目列表移到歌单列表 + 按钮子菜单
    - `cf71241` refactor: 导入文件夹改为添加到当前歌单
    - `1efda5d` refactor(view): + 按钮恢复直接新建歌单，去掉 ContextMenu
    - `862ae9a` fix(view): UI 布局改进五项
    - `2b2d5ad` fix(view): 封面 Border 加 ClipToBounds — ViewBox 缩放后保持圆角裁切
    - `a07bc6c` fix(view): 封面 Border 去掉 CornerRadius，避免与播放时直角不一致
    - `0b7137e` fix(view): 去掉 PlayerBar 上方分隔线
  - **Phase 13**（音频可视化）
    - `b78747d` feat(models): add SpectrumConfig record for audio visualization
    - `12d5def` feat(services): add SampleAggregator for FFT spectrum analysis
    - `0aae53f` feat(services): integrate SampleAggregator into NAudioPlaybackService
    - `5a56ae8` feat(config): add spectrum visualization settings to AppSettings
    - `aae42ca` feat(vm): add spectrum visualization properties and data processing
    - `75c9b59` fix(vm): copy spectrum data before applying sensitivity gain
    - `c0fcefc` feat(view): add SpectrumView custom control for audio visualization
    - `c6977a9` feat(view): integrate SpectrumView into TrackInfoView
    - `e1d4aa8` feat(view): add spectrum visualization settings to SettingsDialog
    - `dd28308` fix(view): update spectrum slider labels on settings load
    - `3814eb6` test: add unit tests for spectrum visualization in PlayerViewModel
    - `d030abe` fix(spectrum): copy array before passing to SpectrumDataReady event
    - `51a04c6` fix(spectrum): remove double-log binning, propagate Enabled, fix double-save
    - `4fc18d5` fix(settings): sync spectrum properties to PlayerViewModel on save
    - `305480e` test(spectrum): update tests for 32-bar input and SpectrumConfig mock
    - `0337aeb` docs: update PROJECT.md for Phase 13 audio visualization
  - **Phase 13 频谱调优**（doc 更新后 24 commits：FFT 参数/布局/主题打磨）
    - `825ceb3` fix(view): remove incorrect TextBlock style from CheckBox
    - `8e76842` fix(services): improve spectrum frequency mapping with proper logarithmic grouping
    - `eab1a7d` fix(services): improve spectrum response with RMS, gain, and gamma curve
    - `b6d08fa` fix(services): adjust spectrum range 60Hz-16kHz, reduce gain to 8x
    - `e8897b7` fix(services): raise minimum spectrum frequency to 80Hz
    - `0d076bd` fix(models): increase FFT size from 1024 to 2048 for better low-frequency resolution
    - `7979d24` fix(services): restore minimum spectrum frequency to 20Hz
    - `f0c3248` fix(services): mono-mix stereo before FFT + clamp smoothing/sensitivity range
    - `15679b2` fix(models): increase FFT size to 8192 for better frequency resolution
    - `ea31c32` fix(services): add 50% FFT overlap for smoother spectrum animation
    - `d447664` fix(view): move spectrum view to bottom of TrackInfoView
    - `04049cd` fix(view): use DockPanel layout to keep spectrum within background
    - `e932d34` fix(view): reduce spectrum bottom margin to fit within background
    - `b074175` fix(view): revert to StackPanel layout, set VerticalAlignment=Top
    - `2876b11` refactor(view): move spectrum to independent row between content and player bar
    - `80791ea` fix(view): add background to SpectrumView for visibility
    - `f1ceb78` refactor(view): move spectrum back into TrackInfoView as bottom section
    - `0135430` fix(view): increase spectrum height to 120px
    - `dbed4fa` fix(view): add detailed error message for settings save failure
    - `a76d92a` fix(view): remove ConfigureAwait(true) to stay on UI thread
    - `59932fc` fix(view): change rainbow gradient to horizontal (left to right)
    - `8c4774d` fix(view): rainbow gradient from red (left) to purple (right) without cycling
    - `781d35d` fix(theme): add IsMoveToPointEnabled to Slider style
    - `808fb98` fix(view): fix slider layout in SettingsDialog using DockPanel LastChildFill
