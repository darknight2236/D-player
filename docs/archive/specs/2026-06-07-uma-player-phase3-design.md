# UmaPlayer Phase 3 设计 — 技术债清算期

> **状态：** 已批准（brainstorming 完成）
> **日期：** 2026/06/07
> **前置阶段：** Phase 2（播放队列）已合并至 `master @ 99a2fcd`
> **本期分支建议：** `feature/phase3-tech-debt`

---

## 1. 总览

### 1.1 目标

在**不引入新用户可见功能**的前提下，把 Phase 1/2 累积的 4 项技术债一次性清算，让架构进入"准备好接 Phase 4 多命名播放列表 / 库扫描 / 拖拽"的状态。

清算的债务（来自 `docs/COUPLING.md §6 #1-#4`）：

| # | 债 | 现状 |
|---|----|------|
| 1 | `MainViewModel` 单体化 | 643 行，已超拆分阈值 |
| 2 | View ↔ VM 硬转型 | `PlayerBar` / `PlaylistView` 的 code-behind 用 `as MainViewModel` |
| 3 | ATL 元数据读取硬编码 | VM 中直接 `new ATL.Track(...)` |
| 4 | settings.json 双写者无合并纪律 | VM `OnVolumeChanged` 与 Window `Closing` 都直接 `SaveAsync(_settings)`，可能复活旧值 |

### 1.2 非目标

明确**不在本期范围**：

- 队列持久化（`queue.json`）—— Phase 4 启动时再做
- 多命名播放列表 / 拖拽 / 库扫描 —— Phase 4+
- `BitmapImage` 在 VM 泄漏（债 #1）—— Phase 4 写 VM 单测时再还
- 视觉 / 交互改动 —— 用户不应该看出任何变化
- 引入测试基础设施（xUnit / Moq 等）—— 与债 #1 解耦后再做
- `PlaybackError` 静默处理（仍 TODO）
- 启动期同步 IO（`LoadAsync().GetAwaiter().GetResult()`）保持不变

### 1.3 成功标准

实现完成的判定线（按顺序检查）：

**静态：**
- `MainViewModel.cs` 行数 ≤ 50
- 仓库内 `grep -r "as MainViewModel"` 结果为 0
- 仓库内 `grep -r "SaveAsync(" Services/` 结果为 0（持久化层）
- 仓库内 `grep -r "new ATL.Track"` 仅出现在 `AtlMetadataReader.cs`

**构建：**
- `dotnet build` — 0 error / 0 warning

**行为：**
- 完整重跑 Phase 2 验收清单（plan Task 14 的全部 ~25 项），100% 通过

**手动 smoke：**
- 启动 → 拖音量 → 关窗 → 重启 → 音量与窗口尺寸保留

---

## 2. 架构变化

### 2.1 拆分前后对照（VM 层）

**拆分前（当前 643 行的 `MainViewModel`）：**
```
MainViewModel
├── Transport 状态        (Position/Duration/PlayState/Volume/IsMuted/IsSeeking)
├── 当前曲信息            (CurrentTrack/AlbumArtImage/SampleRateText/VolumeIcon)
├── Transport 命令         (PlayPause/Stop/Seek/ToggleMute)
├── 队列字段              (Queue/CurrentIndex/SelectedTrack/Shuffle/Repeat + 历史)
├── 推进算法              (CalculateNextIndex/CalculatePrevIndex/PlayTrackAtAsync)
├── 队列命令              (AddToQueue/RemoveTrack/ClearQueue/PlayTrackAt/Next/Prev/Shuffle/Repeat)
├── OpenFilesAsync       (跨两域：入队+自动播)
├── 元数据读取            (ReadTrackMetadataAsync + CreateFallbackTrack)
├── BitmapImage 转换      (CreateAlbumArtImage)
├── 设置持久化            (_settings + OnVolumeChanged + CleanupAsync)
└── 卸载逻辑              (UnloadCurrentTrack)
```

**拆分后：**
```
PlayerViewModel (~150 行)
├── 订阅 IPlaybackService 的事件刷新自身状态
├── 状态:  Position/Duration/PlayState/Volume/IsMuted/IsSeeking
│          CurrentTrack/AlbumArtImage/SampleRateText/VolumeIcon
│          PositionNormalized
├── 命令:  PlayPause/Stop/Seek(Started/Completed)/ToggleMute
└── 依赖:  IPlaybackService, ISettingsPersistence (仅写 DefaultVolume)

PlaylistViewModel (~350 行)
├── 订阅 IPlaybackService.TrackEnded 触发自动推进
├── 状态:  Queue/CurrentIndex/SelectedTrack/ShuffleEnabled/RepeatMode
│          HasCurrentTrack / RepeatActive / ShuffleBrushKey
├── 命令:  AddToQueue/RemoveTrack/ClearQueue/PlayTrackAt/NextTrack/PrevTrack
│          ToggleShuffle/CycleRepeat/OpenAndPlayAsync
├── 私有:  CalculateNextIndex/CalculatePrevIndex/PlayTrackAtAsync/HandleTrackEnded
│          UnloadCurrentTrack (调 _player.Unload + 自身状态清零)
└── 依赖:  IPlaybackService, IFileDialogService, ITrackMetadataReader

MainViewModel (~40 行, 严格 Facade)
├── public PlayerViewModel Player { get; }
├── public PlaylistViewModel Playlist { get; }
├── public Task CleanupAsync() (调两个子 VM 的 Cleanup + Dispose player)
└── 依赖:  PlayerViewModel, PlaylistViewModel
```

**`OpenAndPlayAsync` 归属：** 跨域命令（入队 + 自动播首项）归 **PlaylistViewModel**（语义上"打开音乐"更贴近播放列表域，且 PlaylistVM 已持有 `IFileDialogService` 和 `ITrackMetadataReader`）。`PlayerBar` 的 📂 按钮通过跨级绑定访问。

### 2.2 服务层变化

**新增服务：**

```csharp
// Services/ITrackMetadataReader.cs (~15 行)
public interface ITrackMetadataReader
{
    /// <summary>异步读元数据。读失败返回 fallback Track（绝不抛）。</summary>
    Task<Track> ReadAsync(string filePath);

    /// <summary>同步 fallback：仅文件名作为 Title，其它字段空。用于入队时占位。</summary>
    Track CreateFallback(string filePath);
}

// Services/AtlMetadataReader.cs (~60 行)
public sealed class AtlMetadataReader : ITrackMetadataReader { ... }
//   - ReadAsync: Task.Run + ATL.Track 解析；try/catch 内回落到 CreateFallback；绝不抛
//   - CreateFallback: 同步构造 Track，仅设 FilePath 和 Title=Path.GetFileNameWithoutExtension
```

**改造接口：**

```csharp
// Services/ISettingsPersistence.cs
public interface ISettingsPersistence
{
    Task<AppSettings> LoadAsync();

    /// <summary>读最新 → 应用 mutator → 写回，全程在锁内完成。读失败时把
    /// new AppSettings() 作为 mutator 输入。mutator 必须是纯函数。</summary>
    Task UpdateAsync(Func<AppSettings, AppSettings> mutator);  // 替代 SaveAsync
}
```

`JsonSettingsPersistence` 内部 `SemaphoreSlim` 行为不变，但保护范围从"单次读写原子"扩到"读-改-写整体原子"。VM/Window 不再持有 `AppSettings` 内存副本（除启动时 `LoadAsync` 拿到的初值快照）。

### 2.3 DI 注册更新

`Extensions/ServiceCollectionExtensions.cs`：

```csharp
public static IServiceCollection AddUmaPlayerServices(this IServiceCollection s)
{
    // 既有（不动）
    s.AddSingleton<IPlaybackService, NAudioPlaybackService>();
    s.AddSingleton<IFileDialogService, Win32FileDialogService>();
    s.AddSingleton<ISettingsPersistence, JsonSettingsPersistence>();
    s.AddSingleton<IAudioDeviceManager, StubAudioDeviceManager>();
    s.AddTransient<IAudioOutputFactory, StubAudioOutputFactory>();

    // 新增
    s.AddSingleton<ITrackMetadataReader, AtlMetadataReader>();

    // VM
    s.AddTransient<PlayerViewModel>();      // 新
    s.AddTransient<PlaylistViewModel>();    // 新
    s.AddTransient<MainViewModel>();        // 现有，内容变薄
}
```

### 2.4 View 层变化

| 文件 | 变化 |
|------|------|
| `MainWindow.xaml` | 子控件 DataContext 显式绑定：`<PlayerBar DataContext="{Binding Player}"/>` + `<PlaylistView DataContext="{Binding Playlist}"/>` |
| `MainWindow.xaml.cs` | 关闭时仍调 `_vm.CleanupAsync()`（facade 转发） |
| `PlayerBar.xaml` | 📂 按钮改跨级绑定：`Command="{Binding DataContext.Playlist.OpenAndPlayCommand, RelativeSource={RelativeSource AncestorType=Window}}"`；其它绑定路径不变 |
| `PlayerBar.xaml.cs` | `as MainViewModel` → `as PlayerViewModel` |
| `PlaylistView.xaml` | 0 改动（DataContext 已切到 `PlaylistViewModel`） |
| `PlaylistView.xaml.cs` | `as MainViewModel` → `as PlaylistViewModel`；订阅源类型替换 |

**关键洞察：** 由于子 VM 持有自己的命令和属性，且 View 的 DataContext 直接挂在对应子 VM 上，**XAML 内部 `{Binding ...}` 表达式基本不需改路径**。唯一例外是 📂 按钮（跨域命令需跨级绑定）。

---

## 3. 数据流（拆分前后对照）

### 3.1 用户双击列表项播放

**拆分前：**
```
ListBox.DoubleClick → PlaylistView code-behind
  → ((MainViewModel)DataContext).PlayTrackAtCommand.Execute(index)
    → MainViewModel.PlayTrackAtAsync
      → ReadTrackMetadataAsync(filePath)         [ATL.Track new in VM]
      → Queue[index] = meta
      → _player.LoadAsync(meta)
      → _player.Play()
                                ↓ TrackChanged event
                                  MainViewModel.HandleTrackChanged
                                    → CurrentTrack/AlbumArtImage 更新
```

**拆分后：**
```
ListBox.DoubleClick → PlaylistView code-behind
  → ((PlaylistViewModel)DataContext).PlayTrackAtCommand.Execute(index)
    → PlaylistViewModel.PlayTrackAtAsync
      → _metadataReader.ReadAsync(filePath)      [接口调用]
      → Queue[index] = meta
      → _player.LoadAsync(meta)
      → _player.Play()
                                ↓ TrackChanged event
                                  PlayerViewModel.HandleTrackChanged
                                    → Player.CurrentTrack/AlbumArtImage 更新
```

`IPlaybackService.TrackChanged` 订阅者从 MainVM 转给 PlayerVM；元数据读取走 reader 接口。**PlaylistVM 不订阅 TrackChanged**——它只关心 `_player.LoadAsync` 是否成功（异常仍由它捕获跳下一首）。

### 3.2 自然播完自动推进

```
NAudio PlaybackStopped → NAudioPlaybackService.OnPlaybackStopped
  → 判定 naturalEnd → RaiseOnUIThread(TrackEnded)
                       ↓ TrackEnded event (no payload)
                       PlayerViewModel.HandleStateChanged(Stopped) → Player.PlayState = Stopped
                       PlaylistViewModel.HandleTrackEnded()
                         → 根据 RepeatMode/Shuffle 算 next
                         → PlayTrackAtAsync(next)        [循环回到 3.1]
```

`TrackEnded` 是无参事件，两个订阅者互不知情。两个 handler 在 UI 线程独立执行，顺序不影响结果。

### 3.3 设置写入（音量变化）

**拆分前：**
```
Slider 拖动 → MainViewModel.OnVolumeChanged
  → _player.Volume = value
  → _settings = _settings with { DefaultVolume = value }   ← 内存副本可能过期
  → _persistence.SaveAsync(_settings)                       ← fire-and-forget，整盘覆盖
```

**拆分后：**
```
Slider 拖动 → PlayerViewModel.OnVolumeChanged
  → _player.Volume = value
  → _ = _persistence.UpdateAsync(s => s with { DefaultVolume = value })
                                  ↑ JsonSettingsPersistence 内部:
                                    锁内: var current = await ReadFromDiskAsync();
                                          var next = mutator(current);
                                          await WriteToDiskAsync(next);
```

VM 不再持有 `_settings`；mutator 描述"只改这个字段"，其他字段由实现从磁盘最新版本读出后保留。彻底消除 `docs/COUPLING.md §3 #3` 的 race。

`MainWindow.Window_Closing` 同样改为：

```csharp
await persistence.UpdateAsync(s => s with
{
    WindowLeft = Left, WindowTop = Top,
    WindowWidth = Width, WindowHeight = Height,
});
```

### 3.4 启动加载流程

```
App.OnStartup
  → ServiceProvider.GetRequiredService<MainViewModel>()
    → DI 构造 PlayerViewModel
      → ctor: var settings = _persistence.LoadAsync().GetAwaiter().GetResult();
              Volume = settings.DefaultVolume; _isInitializing = false;
              订阅 IPlaybackService 事件
    → DI 构造 PlaylistViewModel
      → ctor: 订阅 IPlaybackService.TrackEnded
    → DI 构造 MainViewModel(playerVm, playlistVm)
  → new MainWindow(vm, persistence).Show()
    → ctor: _persistence.LoadAsync().GetAwaiter().GetResult()  ← 读窗口尺寸
            (Phase 2 高度迁移逻辑保留)
```

**关键约束：**
- `IPlaybackService` 必须由 UI 线程构造（既有约束）
- `PlayerViewModel` 须先于 `PlaylistViewModel` 构造 → DI 容器按构造器参数顺序自然满足
- `IPlaybackService` 是 Singleton，首次解析发生在第一个 VM 构造期间，之后 NAudio 才会有第一次 LoadAsync。无事件丢失风险

---

## 4. 错误处理与不变量

### 4.1 必须保留的既有不变量

| 不变量 | 现位置 → 新位置 | 保留方式 |
|--------|-----------------|----------|
| `_playToken` 哨兵防 PlayTrackAtAsync 重入 | MainVM → PlaylistVM | 字段 + try/await 后校验逻辑整体搬迁 |
| `_isInitializing` 防构造期写盘 | MainVM → PlayerVM | 仅 PlayerVM 持有（只有它写 settings） |
| `Stop` vs `Unload` 语义 | MainVM 调 Unload → PlaylistVM 调 Unload | `UnloadCurrentTrack` 整体搬到 PlaylistVM；其内部 `_playToken++` 仍保留 |
| 拖动 Seek 时抑制位置回写 (`IsSeeking`) | MainVM → PlayerVM | 字段 + `HandlePositionChanged` 守卫整体搬迁 |
| `DurationChanged` 须先于 `TrackChanged` | `NAudioPlaybackService` 内部不变 | PlayerVM 单一订阅者，服务保证顺序 |
| 静音保留音量 (`_volumeBeforeMute`) | MainVM → PlayerVM | 私有字段 + `ToggleMute` 搬迁 |
| Phase 1 高度迁移 (`< 500 → 650`) | `MainWindow.xaml.cs` 不变 | 不动 |

### 4.2 新引入的不变量

| 不变量 | 位置 | 失守后果 |
|--------|------|----------|
| **`ITrackMetadataReader.ReadAsync` 绝不抛** | `AtlMetadataReader` | PlaylistVM 的 `try/catch` 仍在作为兜底，但语义改为"reader 已处理，catch 是兜底"。若抛 → 异常穿透到 `RelayCommand`，沉默吞掉 |
| **`UpdateAsync` 内部读-改-写须在同一信号量获取期内** | `JsonSettingsPersistence` | 信号量提前释放会重现 §3 #3 race |
| **mutator 必须是纯函数（无 IO 副作用）** | 所有 `UpdateAsync` 调用方 | 副作用 mutator 在锁内执行 IO 会阻塞其它 update 调用 |
| **PlayerVM 与 PlaylistVM 不持有彼此引用** | 两 VM 构造器 | 引用一旦加入，单元测试无法独立 mock；架构约束失效 |
| **MainVM 仅暴露子 VM 属性 + `CleanupAsync`** | `MainViewModel.cs` | 失守即倒退为转发 Facade，违反"严格 Facade" |

### 4.3 错误路径

| 路径 | 拆分前责任方 | 拆分后责任方 | 行为 |
|------|--------------|--------------|------|
| E1. 文件损坏 → `PlayTrackAtAsync` 抛 | MainVM 自身 catch | PlaylistVM 自身 catch | 不变：跳下一首，连跳 3 次停 |
| E2. NAudio 设备中断 → `PlaybackError` | MainVM TODO 静默 | PlayerVM TODO 静默 | 不变（不在 Phase 3 范围） |
| E3. 元数据读取失败 | MainVM 内 try/catch → fallback | `AtlMetadataReader` 内 try/catch → fallback | 行为不变，责任搬迁 |
| E4. `settings.json` 损坏 / 不存在 | `LoadAsync` 失败 → 上层 catch → IOptions 默认 | 同上；**`UpdateAsync` 读失败时以 `new AppSettings()` 作为 mutator 输入，写入；不抛** | 与 LoadAsync 失败回落语义一致 |

### 4.4 不在本期处理的已知问题

- **债 #1**：`BitmapImage` 在 VM 中（拆分后变成"在 PlayerVM 中"）
- **`PlaylistView.RefreshCurrentIndicator`** 用 DataTemplate 列序定位 ▶ TextBlock —— 隐式契约保留
- **`PlaybackError` 静默** —— 仍 TODO
- **启动期同步 IO** (`GetAwaiter().GetResult()`) —— 保留

---

## 5. 文件清单与重构边界

### 5.1 新增文件

| 文件 | 行数估计 | 责任 |
|------|----------|------|
| `Services/ITrackMetadataReader.cs` | ~15 | 接口：`ReadAsync` + `CreateFallback` |
| `Services/AtlMetadataReader.cs` | ~60 | 实现：搬现 `MainViewModel` 的 `ReadTrackMetadataAsync` 和 `CreateFallbackTrack` |
| `ViewModels/PlayerViewModel.cs` | ~150 | 见 §2.1 |
| `ViewModels/PlaylistViewModel.cs` | ~350 | 见 §2.1 |

### 5.2 修改文件

| 文件 | 改动类型 | 关键点 |
|------|----------|--------|
| `Services/ISettingsPersistence.cs` | 接口替换 | 删 `SaveAsync(AppSettings)`，加 `UpdateAsync(Func<>)` |
| `Services/JsonSettingsPersistence.cs` | 实现重写 | 锁内读-改-写；读失败 → `new AppSettings()` 作输入；XML 注释更新 |
| `ViewModels/MainViewModel.cs` | **整体重写** → ~40 行 facade | 仅暴露 `Player` / `Playlist` 子 VM + `CleanupAsync` |
| `Extensions/ServiceCollectionExtensions.cs` | 加 3 行 | `ITrackMetadataReader` + `PlayerViewModel` + `PlaylistViewModel` |
| `App.xaml.cs` | 0 改动 | 仍解析 `MainViewModel`，DI 自动构造子 VM |
| `Views/MainWindow.xaml` | 加 DataContext 分发 | `<PlayerBar DataContext="{Binding Player}"/>` + `<PlaylistView DataContext="{Binding Playlist}"/>` |
| `Views/MainWindow.xaml.cs` | 几乎不变 | `_vm.CleanupAsync()` 保留；持久化调用改 `UpdateAsync` |
| `Views/Controls/PlayerBar.xaml` | 📂 按钮跨级绑定 | `Command="{Binding DataContext.Playlist.OpenAndPlayCommand, RelativeSource={RelativeSource AncestorType=Window}}"` |
| `Views/Controls/PlayerBar.xaml.cs` | 类型替换 | `as MainViewModel` → `as PlayerViewModel` |
| `Views/Controls/PlaylistView.xaml` | 0 改动 | 内部 `{Binding}` 路径不变 |
| `Views/Controls/PlaylistView.xaml.cs` | 类型替换 + 订阅源迁移 | `as MainViewModel` → `as PlaylistViewModel`；`PropertyChanged`（`CurrentIndex`）/ `Queue.CollectionChanged` / `ItemContainerGenerator.StatusChanged` 三处订阅的 sender 类型换为 `PlaylistViewModel`，事件名与逻辑均不变 |

### 5.3 删除文件

无。

### 5.4 边界规则

实现期严格遵守，违反需停下来评估：

- **不动 `Models/*`** — 不修改 `Track` / `PlayState` / `RepeatMode` 形态
- **不动 `Themes/*`** — 不动主题资源
- **不动 `appsettings.json`** — 配置默认值保持不变
- **不动 `NAudioPlaybackService` 的播放链路** — 仅可能调整事件订阅相关注释
- **不为子 VM 写单元测试** — Phase 3 不引入测试基础设施
- **不重命名既有公共类型** — `Track` / `PlayState` / `IPlaybackService` 等名字不变

---

## 6. 实施顺序建议

为减小回归窗口，建议按以下顺序提交（每项可独立编译运行）：

1. **`ITrackMetadataReader` 抽取**（仅服务层，VM 调用点同步切换）
2. **`ISettingsPersistence.UpdateAsync` 改造**（仅服务层 + 两处调用点改写）
3. **`PlayerViewModel` 拆出**（VM 中"transport 半边"搬走，MainVM 保留 facade 转发使其编译通过）
4. **`PlaylistViewModel` 拆出**（VM 中"队列半边"搬走，同上）
5. **MainViewModel 精简为 facade + `OpenAndPlayCommand` 归 PlaylistVM**
6. **View 端 DataContext 分发 + code-behind 类型替换**

每步后跑 `dotnet build` 并手动 smoke。完成后整体重跑 Phase 2 验收清单。

---

## 7. 参考

- 当前架构：`docs/PROJECT.md`
- 风险登记册：`docs/COUPLING.md`（特别是 §3 / §5 / §6）
- Phase 2 实现：`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`
- Phase 2 验收清单：上文 plan §Task 14
