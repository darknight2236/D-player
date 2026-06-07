# UmaPlayer 项目文档

> 一个轻量级、本地优先的 Windows 音乐播放器（WPF + .NET 10 + NAudio）。
>
> 文档日期：2026/06/08 · 对应分支：`master` · 当前阶段：**Phase 3 完成**（VM 拆分 + 技术债清算）

---

## 1. 项目简介

**UmaPlayer** 是一款面向 Windows 桌面的本地音乐播放器，灵感来源于 foobar2000 / Winamp。Phase 1 实现单曲播放骨架，Phase 2 加入内存播放队列（多选入队、自动推进、随机/循环模式）。Phase 3 重构 ViewModel 层（按职责拆分 + 抽象元数据读取 + 修正持久化合并纪律），偿还 4 项技术债。可视化、库扫描、多命名播放列表、队列持久化等放在 Phase 4+。

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

### 1.2 后续增量（未实现）

- 队列持久化（关闭即丢；Phase 4 计划项）
- 多个命名播放列表（创建 / 保存 / 加载 / 切换）—— 当前仅支持单个内存队列
- 拖拽入队 / 队列内拖拽重排序
- M3U / PLS 等播放列表格式导入导出
- 音频可视化（频谱 / 波形）
- 音乐库扫描（文件夹扫描、按艺术家/专辑组织）
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
│   └── AudioDeviceInfo.cs       # 预留：设备信息
│
├── Services/                    # 业务/基础设施服务（全部基于接口）
│   ├── IPlaybackService.cs      # 核心播放抽象
│   ├── NAudioPlaybackService.cs # NAudio 实现（WASAPI + MediaFoundation）
│   ├── IFileDialogService.cs
│   ├── Win32FileDialogService.cs# Microsoft.Win32.OpenFileDialog 封装
│   ├── ISettingsPersistence.cs
│   ├── JsonSettingsPersistence.cs # 持久化到 %LocalAppData%\UmaPlayer\settings.json
│   ├── ITrackMetadataReader.cs # 元数据读取抽象 (Phase 3)
│   ├── AtlMetadataReader.cs    # 基于 z440.atl.core 的实现 (Phase 3)
│   ├── IAudioDeviceManager.cs   # 预留：设备枚举/切换
│   ├── StubAudioDeviceManager.cs# 占位实现，返回空集
│   ├── IAudioOutputFactory.cs   # 预留：输出后端工厂
│   └── StubAudioOutputFactory.cs# 占位实现，固定返回 WASAPI Shared
│
├── ViewModels/
│   ├── MainViewModel.cs        # Strict Facade (~44 行)：仅暴露 Player/Playlist + CleanupAsync (Phase 3)
│   ├── PlayerViewModel.cs      # Transport 子 VM：播放/暂停/进度/音量 (Phase 3)
│   └── PlaylistViewModel.cs    # 队列子 VM：Queue/Shuffle/Repeat/推进算法 (Phase 3)
│
├── Views/
│   ├── MainWindow.xaml(.cs)     # 主窗口；窗口位置恢复 + 关闭时清理
│   └── Controls/
│       ├── PlayerBar.xaml(.cs)  # 全功能播放栏（封面/信息/进度/控制/音量）
│       └── PlaylistView.xaml(.cs)  # 播放队列（Phase 2）
│
├── Converters/
│   ├── PlayStateToIconConverter.cs       # ▶/⏸ 图标
│   ├── TimeSpanToStringConverter.cs      # 0:00 / 0:00:00
│   ├── RepeatModeToIconConverter.cs      # ⇄ / 🔁 / 🔂 (Phase 2)
│   └── BoolToAccentBrushConverter.cs     # 强调色/次要色画刷 (Phase 2)
│
├── Themes/                      # 深色主题资源字典（App.xaml 合并加载）
│   ├── Colors.xaml              # #1E1E2E 背景 + #7C4DFF 紫色强调
│   ├── Fonts.xaml               # Segoe UI + Header/Body/Caption 文本样式
│   └── Controls.xaml            # Window / Button / Slider 模板
│
├── Extensions/
│   └── ServiceCollectionExtensions.cs # AddUmaPlayerServices(...) DI 注册
│
└── docs/
    ├── PROJECT.md               # 本文档
    ├── COUPLING.md              # 耦合分析 / 风险登记册
    └── superpowers/             # 设计稿 & 实现计划（历史归档）
        ├── specs/
        │   ├── 2026-04-23-uma-player-design.md             # Phase 1 设计
        │   ├── 2026-06-06-uma-player-playlist-design.md    # Phase 2 设计
        │   └── 2026-06-07-uma-player-phase3-design.md       # Phase 3 设计
        └── plans/
            ├── 2026-04-24-uma-player-implementation.md           # Phase 1 计划
            ├── 2026-06-06-uma-player-playlist-implementation.md  # Phase 2 计划
            └── 2026-06-07-uma-player-phase3-implementation.md  # Phase 3
```

---

## 4. 架构

### 4.1 高层架构（MVVM + DI）

```
   ┌──────────────────────────────────────────────────────┐
   │                       App.xaml.cs                    │
   │   1. 读取 appsettings.json                            │
   │   2. 构建 ServiceCollection (AddUmaPlayerServices)    │
   │   3. 解析 MainViewModel + ISettingsPersistence       │
   │   4. new MainWindow(vm, persistence).Show()          │
   └──────────────┬─────────────────────────┬─────────────┘
                  │                         │
                  ▼                         ▼
   ┌──────────────────────┐    ┌──────────────────────────┐
   │     MainWindow       │    │  MainViewModel (Facade)  │
   │  (View, code-behind) │◀──▶│  ~44 行：仅持有子 VM       │
   │ - 窗口位置恢复/保存   │    │  + CleanupAsync()         │
   │ - PlayerBar 容器     │    └──────┬──────────┬─────────┘
   │   (DC=Player)        │           │          │
   │ - PlaylistView 容器  │           ▼          ▼
   │   (DC=Playlist)      │   ┌─────────────┐ ┌──────────────┐
   └──────────────────────┘   │ PlayerVM    │ │ PlaylistVM   │
                              │ Transport:  │ │ Queue/Shuffle│
                              │ Play/Pause/ │ │ /Repeat/推进 │
                              │ Position/Vol│ │ /TrackEnded  │
                              └──────┬──────┘ └──────┬───────┘
                                     │  互不持引用      │
                                     ▼  仅共享 Service ▼
           ┌──────────────────────────┴──────────────────────────┐
           ▼                          ▼                          ▼
┌────────────────────┐  ┌────────────────────────┐  ┌──────────────────────┐
│ IPlaybackService   │  │ ITrackMetadataReader   │  │ ISettingsPersistence │
│ (NAudio impl)      │  │ (ATL impl) [Phase 3]   │  │ UpdateAsync(Func<>)  │
└────────────────────┘  └────────────────────────┘  └──────────────────────┘

注：IFileDialogService 由 PlaylistViewModel 直接消费（OpenAndPlay / AddToQueue）。
```

### 4.2 服务生命周期

注册位置：`Extensions/ServiceCollectionExtensions.cs`

| 服务 | 生命周期 | 说明 |
|------|----------|------|
| `IOptions<AppSettings>` | Singleton（框架） | 绑定 `appsettings.json` 的 `"Player"` 节，作为**启动默认快照** |
| `IPlaybackService` | **Singleton** | 持有 NAudio 设备资源，必须长生命周期 |
| `IFileDialogService` | Singleton | 无状态 |
| `ISettingsPersistence` | Singleton | 内部 `SemaphoreSlim` 并发互斥 |
| `ITrackMetadataReader` | Singleton (Phase 3) | 无状态，封装 z440.atl.core；`ReadAsync` 不抛 |
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

---

## 5. 模块详解

### 5.1 `Models`

- **`Track`**：不可变 record。`Duration` 与 `SampleRate` 在加载时由 `NAudioPlaybackService.LoadAsync` 通过 `track with { Duration=..., SampleRate=... }` 补齐。`AlbumArt` 为原始字节数组，由 VM 转 `BitmapImage`（限 200px、`Freeze()` 跨线程安全）。
- **`PlayState`**：`Stopped / Playing / Paused`。
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

构造时订阅 `IPlaybackService` 的 5 个事件（`PositionChanged / StateChanged / DurationChanged / TrackChanged / PlaybackError`），并阻塞读盘加载持久化音量（`_isInitializing` 标志抑制初始化期的写盘）。**不订阅 `TrackEnded`**（那是 PlaylistViewModel 的职责）。

`[RelayCommand]`：`SeekStarted / SeekCompleted(normalized) / PlayPause / Stop / ToggleMute`。

`partial void OnVolumeChanged(value)`：同步到 `_player.Volume` → 拖滑块到非零自动取消静音 → `_persistence.UpdateAsync(s => s with { DefaultVolume = value })`。

`CleanupAsync()`：解绑 5 个事件 + 用观察属性 `Volume`（**非**陈旧的 `_settings` 字段）持久化最后一次音量。**不 Dispose `IPlaybackService`**（PlaylistViewModel 还在用，Facade 层统一 Dispose）。

`HandleTrackChanged(Track? track)`：track 为 null 时把 `CurrentTrack` 和 `AlbumArtImage` 一起置 null（XAML 的 `FallbackValue='No track loaded'` 处理标题显示）。

### 5.4b `ViewModels/PlaylistViewModel`（队列子 VM，~384 行）

源生成器属性：`_currentIndex`（-1 表示未选）, `_selectedTrack`（UI 列表选中项，与播放无关）, `_shuffleEnabled`, `_repeatMode`。集合：`ObservableCollection<Track> Queue`。私有：`HashSet<int> _shuffleHistory` / `Random _random` / `int _playToken`（重入哨兵）。派生：`HasCurrentTrack`、`RepeatActive`、`ShuffleBrushKey`。

构造时订阅 `IPlaybackService.TrackEnded` 用于自动推进；`Queue.CollectionChanged` 触发 Next/Prev 命令 `NotifyCanExecuteChanged`。

`[RelayCommand]`：`AddToQueue / RemoveTrack(int) / ClearQueue / PlayTrackAt(int) / NextTrack / PrevTrack / ToggleShuffle / CycleRepeat / OpenAndPlay`。

**跨域命令 OpenAndPlay** —— PlayerBar 上的 📂 按钮归属本 VM（本质是"批量入队 + 播首项"，前者属于队列域）；PlayerBar 通过 `{Binding DataContext.Playlist.OpenAndPlayCommand, RelativeSource={RelativeSource AncestorType=Window}}` 跨级访问。同理 ⏮/⏭ 也用这个模式绑到 `Playlist.PrevTrackCommand / NextTrackCommand`。

**关键私有方法**（与旧 MainViewModel 等价）：
- `PlayTrackAtAsync(int, int skipCount=0)`：抢占 `_playToken` → 读元数据 → `Queue[i] = meta` → `LoadAsync` → `Play`；每个 `await` 后校验 token，被顶替则静默退出；失败连续 3 次自动停止
- `CalculateNextIndex(int? failedIndex)`：纯算法，按 (Shuffle × RepeatMode) 4 种组合返回下一索引；Shuffle 用 `_shuffleHistory` 排除已播
- `HandleTrackEnded()`：RepeatOne 重播当前，否则走 `CalculateNextIndex`
- `UnloadCurrentTrack()`：`_playToken++` 顶替 in-flight → `_player.Unload()`（NAudio 服务自动广播 TrackChanged(null) 让 PlayerVM 清屏）

`Cleanup()`：同步解绑 `TrackEnded`，由 `MainViewModel.CleanupAsync` 调用。

### 5.5 `Views`

- **`MainWindow`**：两行 Grid 容器 —— `PlayerBar`（顶部，自适应高度）+ `PlaylistView`（底部填充）。构造时同步读取窗口尺寸（`GetAwaiter().GetResult()`，启动阻塞 < 几 ms 可接受）；若持久化的 `WindowHeight < 500`（Phase 1 旧值）则一次性迁移到 650，避免列表不可见。关闭时异步保存。
- **`PlayerBar`** *(UserControl)*：播放栏。三行 Grid：①封面+元数据；②`Position | Slider | Duration`；③⏮ ▶/⏸ ⏹ ⏭ 📂 + 音量。
  - Slider 的"单击跳转"由 `PreviewMouseLeftButtonDown` 手动从 `PART_Track` 计算比例并触发 `SeekCompletedCommand`；点击 Thumb 时不触发（通过 `FindAncestor<Thumb>` 检测，转交给原生 `DragStarted/DragCompleted`）
  - `⏮` / `⏭` / `📂` 通过 `{Binding DataContext.Playlist.<XxxCommand>, RelativeSource={RelativeSource AncestorType=Window}}` 跨级绑定到 `PlaylistViewModel`（PlayerBar 自身的 DataContext 已切为 PlayerViewModel），`HasCurrentTrack` 守卫；队列空时按钮自动禁用
- **`PlaylistView`** *(UserControl, Phase 2)*：队列界面。两行 Grid：①工具栏 `[+ 添加][清空]` 左对齐、`🔀` `⇄/🔁/🔂` 右对齐；②`ListBox` 绑 `Queue`，每项含 ▶ 当前曲标记 + 标题 + `×` 删除按钮
  - 当前曲 ▶ 标记由 code-behind 维护：订阅 `PlaylistViewModel.PropertyChanged` (CurrentIndex) / `Queue.CollectionChanged` / `ItemContainerGenerator.StatusChanged`（应对虚拟化容器回收和 `Queue[i] = meta` 替换）
  - 交互：双击播放、Delete 键删除、右上角按钮触发命令
  - Shuffle / Repeat 图标用 `Segoe UI Emoji` 字体（默认 `Segoe UI` 不含 U+1F500 完整字形）

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

---

## 7. 数据流：典型播放流程

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
- **内存队列**：播放列表关闭即丢（当前仅内存 `ObservableCollection` 存在）；Phase 4 计划加入队列持久化。
- **静默错误**：`HandlePlaybackError` 仅 TODO，未弹窗或写日志；调试期可通过断点检视。
- **启动时同步 IO**：`PlayerViewModel.Initialize()` 与 `MainWindow` 构造函数中均使用 `LoadAsync().GetAwaiter().GetResult()`；settings.json 极小，可接受，未来若数据膨胀需重构。
- **DI 解析跨线程**：`IPlaybackService` 必须由 UI 线程首次构造（依赖 `SynchronizationContext.Current` 捕获），目前由 `App.OnStartup` 保证。
- **UpdateAsync 跨线程读 DP**：`JsonSettingsPersistence.UpdateAsync` 内部 `.ConfigureAwait(false)` 把 mutator 调用 / 继续上下文带到 threadpool；若 mutator 闭包内读 WPF DependencyProperty（如 `Left/Top/Width/ActualHeight`）会抛 `InvalidOperationException`。**调用方必须先在 UI 线程把 DP 值捕获到局部变量再 await**。`MainWindow.xaml.cs:Window_Closing` 即采用此模式。

---

## 10. 历史与参考

- 耦合分析：[`docs/COUPLING.md`](./COUPLING.md) — 风险登记册 + Phase 3 启动检查清单
- 设计稿：
  - [`docs/superpowers/specs/2026-04-23-uma-player-design.md`](./superpowers/specs/2026-04-23-uma-player-design.md) — Phase 1 整体设计
  - [`docs/superpowers/specs/2026-06-06-uma-player-playlist-design.md`](./superpowers/specs/2026-06-06-uma-player-playlist-design.md) — Phase 2 播放列表设计
  - [`docs/superpowers/specs/2026-06-07-uma-player-phase3-design.md`](./superpowers/specs/2026-06-07-uma-player-phase3-design.md) — Phase 3 VM 拆分 + 技术债清算设计
- 实现计划：
  - [`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — Phase 1
  - [`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`](./superpowers/plans/2026-06-06-uma-player-playlist-implementation.md) — Phase 2
  - [`docs/superpowers/plans/2026-06-07-uma-player-phase3-implementation.md`](./superpowers/plans/2026-06-07-uma-player-phase3-implementation.md) — Phase 3
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
