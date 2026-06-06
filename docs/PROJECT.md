# UmaPlayer 项目文档

> 一个轻量级、本地优先的 Windows 音乐播放器（WPF + .NET 10 + NAudio）。
>
> 文档日期：2026/06/06 · 对应分支：`master`

---

## 1. 项目简介

**UmaPlayer** 是一款面向 Windows 桌面的本地音乐播放器，灵感来源于 foobar2000 / Winamp。当前为首期 (Phase 1) 实现，定位为"能流畅地播放一首本地音乐文件"，刻意避免过度工程化；播放列表、上一曲/下一曲、可视化等功能放在后续增量。

### 1.1 关键特性（已实现）

| 类别   | 能力                                                             |
|------|----------------------------------------------------------------|
| 文件导入 | Win32 OpenFileDialog 单文件选择                                     |
| 支持格式 | MP3 / WMA / FLAC / AAC / WAV（基于 Windows Media Foundation 原生解码） |
| 播放控制 | 播放 / 暂停 / 停止                                                   |
| 进度控制 | 拖拽 + 单击跳转的进度条；位置实时更新（≈30 Hz，节流）                                |
| 音量控制 | 0~1 线性滑块、一键静音/取消静音；通过 `VolumeSampleProvider` 实现                |
| 元数据  | 标题 / 艺术家 / 专辑 / 流派 / 年份 / 采样率 / 内嵌封面（z440.atl.core）            |
| 主题   | 内置深色主题（深紫强调色）                                                  |
| 持久化  | 窗口位置/尺寸、默认音量保存到 `%LocalAppData%\UmaPlayer\settings.json`       |
| 播放列表 | 内存队列：多选入队、单项删除、上/下一首、自动推进、随机/循环模式（关闭即丢） |

### 1.2 后续增量（未实现）

- 多个命名播放列表（创建 / 保存 / 加载 / 切换）—— 当前仅支持单个内存队列
- 播放队列持久化（关闭即丢）
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
│   ├── IAudioDeviceManager.cs   # 预留：设备枚举/切换
│   ├── StubAudioDeviceManager.cs# 占位实现，返回空集
│   ├── IAudioOutputFactory.cs   # 预留：输出后端工厂
│   └── StubAudioOutputFactory.cs# 占位实现，固定返回 WASAPI Shared
│
├── ViewModels/
│   └── MainViewModel.cs         # 主 VM：聚合播放器/文件对话框/持久化
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
    └── superpowers/             # 设计稿 & 实现计划（历史归档）
        ├── specs/2026-04-23-uma-player-design.md
        └── plans/2026-04-24-uma-player-implementation.md
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
   │     MainWindow       │    │      MainViewModel       │
   │  (View, code-behind) │◀──▶│ (ObservableObject + cmds)│
   │ - 窗口位置恢复/保存   │    │ - 订阅 IPlaybackService   │
   │ - PlayerBar 容器     │    │ - RelayCommand: Play/... │
   └──────────────────────┘    └─────────┬────────────────┘
                                         │ 依赖 (接口)
              ┌──────────────────────────┼──────────────────────────┐
              ▼                          ▼                          ▼
   ┌────────────────────┐  ┌────────────────────────┐  ┌──────────────────────┐
   │ IPlaybackService   │  │ IFileDialogService     │  │ ISettingsPersistence │
   │ (NAudio impl)      │  │ (Win32 OpenFileDialog) │  │ (JSON @ LocalAppData)│
   └────────────────────┘  └────────────────────────┘  └──────────────────────┘
```

### 4.2 服务生命周期

注册位置：`Extensions/ServiceCollectionExtensions.cs`

| 服务 | 生命周期 | 说明 |
|------|----------|------|
| `IOptions<AppSettings>` | Singleton（框架） | 绑定 `appsettings.json` 的 `"Player"` 节，作为**启动默认快照** |
| `IPlaybackService` | **Singleton** | 持有 NAudio 设备资源，必须长生命周期 |
| `IFileDialogService` | Singleton | 无状态 |
| `ISettingsPersistence` | Singleton | 内部 `SemaphoreSlim` 并发互斥 |
| `IAudioDeviceManager` | Singleton（Stub） | 预留 |
| `IAudioOutputFactory` | Transient（Stub） | 预留；语义上由 `IPlaybackService` 创建即释放 |
| `MainViewModel` | **Transient** | VM 实例由窗口持有 |

### 4.3 关键设计决策

1. **配置双源**：启动只读默认值用 `IOptions<AppSettings>`；运行时可变状态（窗口、音量、最后播放路径）走 `ISettingsPersistence` 写入用户数据目录。两者通过 `record with` 不可变更新协同。

2. **位置事件节流**：`NAudioPlaybackService.PollPositionAsync` 以 ~30 Hz（33 ms）轮询，并通过 `PositionThrottle` 进一步节流，避免 UI 线程被淹没。

3. **UI 线程封送**：服务层捕获启动时的 `SynchronizationContext`（必然是 UI 线程，因为 `IPlaybackService` 由 `App.OnStartup` 间接解析），所有事件通过 `_syncContext.Post` 派发，VM 直接绑定即可。

4. **拖拽 Seek 防回跳**：`MainViewModel.IsSeeking` 标志在拖动期间抑制 `PositionChanged` → `Position` 写入，避免拖拽时滑块被服务回写"拽回去"。

5. **静音状态保留音量**：`ToggleMute` 把当前 `Volume` 存到 `_volumeBeforeMute`，置 `Volume=0`；取消静音恢复。拖动滑块若有非零值会自动取消静音。

6. **窗口可见性自愈**：`MainWindow.EnsureVisible()` 检查恢复的位置是否在虚拟屏内（防止外接屏拔掉后窗口飘到屏外），不在则回退到主屏居中。

7. **面向接口 + 占位实现**：`IAudioDeviceManager` / `IAudioOutputFactory` 已注册 Stub，便于后续替换为真实多设备/Exclusive/ASIO 实现而不动 VM。

---

## 5. 模块详解

### 5.1 `Models`

- **`Track`**：不可变 record。`Duration` 与 `SampleRate` 在加载时由 `NAudioPlaybackService.LoadAsync` 通过 `track with { Duration=..., SampleRate=... }` 补齐。`AlbumArt` 为原始字节数组，由 VM 转 `BitmapImage`（限 200px、`Freeze()` 跨线程安全）。
- **`PlayState`**：`Stopped / Playing / Paused`。
- **`AudioDeviceInfo`**：`(Id, Name, IsDefault)`，目前仅类型存在。

### 5.2 `Services/NAudioPlaybackService`

| 成员 | 说明 |
|------|------|
| `LoadAsync(Track)` | 在 `Task.Run` 上：销毁旧播放链 → 新建 `MediaFoundationReader` → `VolumeSampleProvider` → `WasapiOut(Shared, 100ms)`；触发 `DurationChanged` / `TrackChanged` |
| `Play / Pause / Stop` | 委派给 `IWavePlayer`；`Stop` 同时将 `CurrentTime` 归零 |
| `Seek(TimeSpan)` | 直接写 `reader.CurrentTime` |
| `Volume { get; set; }` | `Math.Clamp(0..1)`；运行时写入 `VolumeSampleProvider.Volume` |
| `PollPositionAsync` | 仅在 `PlaybackState==Playing` 时循环；每 33ms 派发一次 `PositionChanged` |
| `Dispose()` | 拆事件、停止、释放 reader/wavePlayer |

### 5.3 `Services/JsonSettingsPersistence`

- 路径：`%LocalAppData%\UmaPlayer\settings.json`
- 启动时若不存在则返回 `new AppSettings()`（默认值由 record 初始化器给出，**与 `appsettings.json` 不重复绑定**）
- 通过 `SemaphoreSlim(1,1)` 序列化读写，防止 VM 写音量与 `Window_Closing` 写窗口尺寸竞态

### 5.4 `ViewModels/MainViewModel`

CommunityToolkit 源生成器属性：

```
[ObservableProperty] _isSeeking, _position, _duration, _playState,
                    _currentTrack, _albumArtImage, _volume, _isMuted
```

派生属性：`VolumeIcon`（🔇/🔊）、`SampleRateText`（"44,100 Hz"）、`PositionNormalized`（0..1）。

`[RelayCommand]` 命令：

| 命令 | 行为 |
|------|------|
| `OpenFilesAsync` | 弹文件对话框 → `ATL.Track` 读元数据 → `player.LoadAsync` → `player.Play()` |
| `PlayPause` | 根据当前状态切换 |
| `Stop` | 调用底层 Stop |
| `SeekStarted / SeekCompleted(normalized)` | 由 `PlayerBar` 滑块的拖拽/单击事件转发 |
| `ToggleMute` | 静音 / 恢复音量 |

`partial void OnVolumeChanged(value)`：体积变化 → 写到播放器 → 持久化 `DefaultVolume`（初始化阶段 `_isInitializing` 跳过磁盘写入）。

`CleanupAsync()`：窗口关闭时由 `MainWindow.Window_Closing` 调用，解绑事件、释放播放器、保存设置。

### 5.5 `Views`

- **`MainWindow`**：仅作为 `PlayerBar` 容器；构造时同步读取窗口尺寸（`GetAwaiter().GetResult()`，启动阻塞 < 几 ms 可接受），关闭时异步保存。
- **`PlayerBar`** *(UserControl)*：唯一可视化组件。三行 Grid：①封面+元数据；②`Position | Slider | Duration`；③播放控制按钮 + 音量。
  - Slider 的"单击跳转"由 `PreviewMouseLeftButtonDown` 手动从 `PART_Track` 计算比例并触发 `SeekCompletedCommand`；点击 Thumb 时不触发（通过 `FindAncestor<Thumb>` 检测，转交给原生 `DragStarted/DragCompleted`）。

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
    "WindowHeight": 450
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
用户点击 📂 (OpenFilesCommand)
    │
    ▼
Win32FileDialogService.OpenFiles("Audio Files|*.mp3;...")
    │
    ▼
MainViewModel.ReadTrackMetadataAsync(path)  ─── Task.Run ───▶  ATL.Track 读元数据 (+封面)
    │                                                            │
    │  (回主线程)                                                 │
    ▼                                                            │
NAudioPlaybackService.LoadAsync(track)  ─── Task.Run ───▶ 创建 MediaFoundationReader
    │                                                          │ VolumeSampleProvider
    │                                                          │ WasapiOut(Shared, 100ms)
    │                                                          │ track with { Duration, SampleRate }
    │                                                          ▼
    │                                            事件: DurationChanged → VM.Duration
    │                                                  TrackChanged    → VM.CurrentTrack + AlbumArtImage
    ▼
NAudioPlaybackService.Play() ─▶ WasapiOut.Play() + PollPositionAsync 循环
    │
    │ (每 33ms)
    ▼
PositionChanged → MainViewModel.HandlePositionChanged → if (!IsSeeking) Position = ...
    │
    ▼
PlayerBar.Slider 绑定 PositionNormalized (Mode=OneWay) → UI 实时更新
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
- **单曲播放**：当前仅维护一个"当前曲目"；点击打开新文件会停止并释放上一首。
- **静默错误**：`HandlePlaybackError` 仅 TODO，未弹窗或写日志；调试期可通过断点检视。
- **启动时同步 IO**：`MainViewModel.Initialize()` 与 `MainWindow` 构造函数中均使用 `LoadAsync().GetAwaiter().GetResult()`；settings.json 极小，可接受，未来若数据膨胀需重构。
- **DI 解析跨线程**：`IPlaybackService` 必须由 UI 线程首次构造（依赖 `SynchronizationContext.Current` 捕获），目前由 `App.OnStartup` 保证。

---

## 10. 历史与参考

- 耦合分析：[`docs/COUPLING.md`](./COUPLING.md) — 风险登记册 + Phase 2 启动检查清单
- 设计稿：[`docs/superpowers/specs/2026-04-23-uma-player-design.md`](./superpowers/specs/2026-04-23-uma-player-design.md) — 完整设计推演（含取舍）
- 实现计划：[`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — 分步任务清单
- 最近提交：
  - `e2d9d7b` feat: add PlayerBar, MainWindow, and wire App.xaml with DI and dark theme
  - `07f7957` feat: implement MainViewModel with playback commands and seek handling
  - `56830d1` feat: add value converters and dark theme ResourceDictionaries
  - `ce5e823` feat: add file dialog service and DI registration
  - `02c7012` feat: implement NAudioPlaybackService with throttled position updates
