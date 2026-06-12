# UmaPlayer Phase 4 — 队列持久化设计

> 创建日期：2026/06/12 · 对应分支：将开 `feature/phase4-queue-persistence`（自 master `7cbae85`）
> 阶段定位：**Phase 4 第一项**（COUPLING.md §6 启动检查清单 ☐ 第 5 项："队列持久化")
> 上一阶段：Phase 3（VM 拆分 + 技术债清算）已完成

---

## 0. TL;DR

让用户关闭 UmaPlayer 后，下次启动恢复内存队列（含 Shuffle / Repeat / 当前曲选中位置）。

- **新文件**：`%LocalAppData%\UmaPlayer\queue.json`，与 `settings.json` 平行
- **新接口**：`IQueuePersistence` + `JsonQueuePersistence`（独立 `SemaphoreSlim`，与 `ISettingsPersistence` 平行）
- **范围**：仅恢复"队列 + 当前曲位置 + Shuffle + Repeat"，**不**恢复 PlayState、Position、AlbumArt、自动播放
- **写盘时机**：仅 `MainWindow.Window_Closing` 一次（与 settings 写盘对称）
- **加载时机**：`PlaylistViewModel` 构造函数同步段（与 PlayerVM 读音量、MainWindow 读窗口尺寸纪律一致）
- **首屏体验**：列表恢复，▶ 标记画在上次当前曲，PlayerBar 显示 "No track loaded"，按 ▶ 触发懒加载播放

---

## 1. 目标与非目标

### 1.1 目标

1. 关闭后队列条目不丢
2. 当前曲选中位置（`CurrentIndex`）恢复
3. `ShuffleEnabled` / `RepeatMode` 恢复
4. 文件丢失（用户外部移动 / 删除）静默过滤；空队列与首次启动行为一致
5. 文件损坏（手工破坏 queue.json）不让应用崩溃；fallback 为空队列
6. 启动期开销 ≤ 10 ms（与 `settings.json` 同步读盘相同量级）
7. 不引入新的隐式契约违规、不破坏 COUPLING.md §5 现有契约

### 1.2 非目标（Out-of-Scope）

- ❌ 恢复 PlayState（启动后不自动出声）
- ❌ 恢复 Position（断点续播 —— 后续阶段考虑）
- ❌ 队列内拖拽重排序、外部文件拖入入队
- ❌ 多命名播放列表（仍是 Phase 4+ 后续项）
- ❌ M3U / PLS 等格式互操作
- ❌ 偿还债 #1（`BitmapImage` 类型泄漏）
- ❌ 修复"PlayTrackAtAsync 期间 RemoveTrack 未自增 `_playToken`"的极窄竞态（独立小修，可搭车）
- ❌ 引入 xUnit 测试项目（保持 manual acceptance pass 节奏）
- ❌ 关闭流程改 deferral 让 `await` 等完整完成

---

## 2. 架构总览

```
                        ┌─────────────────────────────────┐
                        │ %LocalAppData%\UmaPlayer\        │
                        │   ├── settings.json   (已存在)    │
                        │   └── queue.json     ⬅ Phase 4   │
                        └────────────┬────────────────────┘
                                     │
            ┌────────────────────────┼─────────────────────────┐
            ▼                        ▼                         ▼
   ┌────────────────┐    ┌──────────────────────┐    ┌──────────────────────┐
   │ ISettings      │    │ IQueuePersistence    │    │ ITrackMetadataReader │
   │ Persistence    │    │   ⬅ NEW              │    │ (已存在)              │
   │ (已存在)       │    │ LoadAsync()          │    └──────────────────────┘
   └────────────────┘    │ SaveAsync(snapshot)  │
                         └──────────┬───────────┘
                                    │ 注入
                                    ▼
                         ┌──────────────────────┐
                         │ PlaylistViewModel    │
                         │ ────────────────     │
                         │ + LoadFromDisk()     │
                         │   (ctor 内同步调用)   │
                         │ + SnapshotState()    │
                         │ + PlayCurrentCommand │
                         └──────────────────────┘
                                    ▲
                                    │ MainWindow.Window_Closing 调
                                    │   _queuePersistence.SaveAsync(_vm.Playlist.SnapshotState())
                                    │
                         ┌──────────┴───────────┐
                         │ MainWindow           │
                         └──────────────────────┘
```

**核心决定回顾：**

- 新增独立文件 `queue.json`（与 `settings.json` 平行），独立接口 `IQueuePersistence`
- 仅存路径 + 队列状态，不存元数据/封面
- 启动同步加载（VM 构造期）；关闭异步写盘（`Window_Closing`）
- 启动后**不预加载**当前曲；按 ▶ 时 PlaylistVM 的 `PlayCurrentCommand` 接管首播
- 文件丢失静默跳过；schema 损坏 fallback 空队列

---

## 3. 数据模型与文件 schema

### 3.1 `Models/QueueState.cs`（新文件，`record`）

```csharp
namespace UmaPlayer.Models;

/// <summary>
/// 队列持久化的不可变快照。仅包含路径与队列态——不携带 Track 元数据或封面。
/// 启动时 PlaylistViewModel 同步读盘 → 把 Items 填到 ObservableCollection&lt;Track&gt;
/// 时为每条创建占位 Track（与 OpenAndPlay 的"占位 → 完整元数据"流程相同）。
/// </summary>
public sealed record QueueState
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    public int CurrentIndex { get; init; } = -1;
    public bool ShuffleEnabled { get; init; }
    public RepeatMode RepeatMode { get; init; } = RepeatMode.Off;
}
```

### 3.2 `queue.json` 文件格式

```json
{
  "SchemaVersion": 1,
  "Items": [
    "C:\\Music\\Album\\01 Track.mp3",
    "C:\\Music\\Album\\02 Track.mp3",
    "D:\\Other\\song.flac"
  ],
  "CurrentIndex": 1,
  "ShuffleEnabled": false,
  "RepeatMode": "List"
}
```

- 用 `JsonSerializer` 默认设置 + `WriteIndented = true`（与 `JsonSettingsPersistence` 一致）
- `RepeatMode` 走 `JsonStringEnumConverter`（人类可读、跨版本稳）
- 路径保留原始字符串，不规范化

### 3.3 不变量

| 不变量 | 谁守护 |
|--------|--------|
| `Items` 非空时 `CurrentIndex ∈ [0, Items.Count)`；为空时 `-1` | 写侧：`PlaylistViewModel` 在运行时保持 `Queue.Count` 与 `CurrentIndex` 同步，`SnapshotState` 直接快照即满足；读侧：`LoadFromDisk` 在 `MapCurrentIndexAfterFilter` 中做边界处理（过滤后空队列→`-1`，单点丢失→向后/向前滑） |
| `SchemaVersion == 1` | `LoadAsync` 加载时不匹配 → 返回空 `QueueState` |
| 路径保留写入时的字符串（不强制规范化） | 写时直接来源于 `Track.FilePath`；读时 `File.Exists` 对 `/` 和 `\\` 都识别 |
| `Queue[CurrentIndex]` 启动后是占位 Track（`Title==null`、`AlbumArt==null`），由 `PlayTrackAtAsync` 在用户首次播时升级为完整元数据 | `PlaylistViewModel.LoadFromDisk` |

### 3.4 文件位置与生命周期

- **路径**：`Path.Combine(Environment.GetFolderPath(LocalApplicationData), "UmaPlayer", "queue.json")`
- **首次启动**：文件不存在 → `LoadAsync` 返回 `new QueueState()` → 队列空 → UI 与 Phase 3 行为一致
- **写盘失败**：`SaveAsync` 抛出，由 `MainWindow.Window_Closing` 的 `try/catch` 静默吞
- **读盘失败**（损坏 / SchemaVersion 不匹配 / 反序列化抛）：`LoadAsync` 内 catch → 返回 `new QueueState()`，**旧文件保留不动**（让用户可手工恢复，与 settings 行为一致）

---

## 4. 服务层

### 4.1 `Services/IQueuePersistence.cs`（新文件）

```csharp
namespace UmaPlayer.Services;

/// <summary>
/// 队列持久化（%LocalAppData%\UmaPlayer\queue.json）。
/// 与 ISettingsPersistence 的设计基线相同：
///   - 同步生命周期由 SemaphoreSlim 互斥，单实例 Singleton
///   - 启动时若文件不存在/损坏，返回 default(QueueState)；写盘失败抛
///
/// 与 ISettingsPersistence 的差异：
///   - 这里不需要 read-modify-write（队列状态由 PlaylistVM 整体快照后写入），
///     所以 SaveAsync(QueueState snapshot) 比 UpdateAsync(Func&lt;&gt;) 更直接。
/// </summary>
public interface IQueuePersistence
{
    Task<QueueState> LoadAsync();
    Task SaveAsync(QueueState snapshot);
}
```

> **为什么不用 `UpdateAsync(Func<>)` 模式？** `ISettingsPersistence` 改 `UpdateAsync` 是为了解决"窗口尺寸 + 音量两个写者"的合并竞态（COUPLING.md 债 #3）。`queue.json` **只有一个写者**（`MainWindow.Window_Closing` 同一个 callsite），不存在合并问题。`SaveAsync(snapshot)` 是合适的更小语义。

### 4.2 `Services/JsonQueuePersistence.cs`（新文件，~80 行）

```csharp
public sealed class JsonQueuePersistence : IQueuePersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private const int CurrentSchemaVersion = 1;

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonQueuePersistence()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UmaPlayer");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "queue.json");
    }

    public async Task<QueueState> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath)) return new QueueState();

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<QueueState>(stream, JsonOptions)
                              .ConfigureAwait(false);
            if (loaded is null || loaded.SchemaVersion != CurrentSchemaVersion)
                return new QueueState();

            return loaded;
        }
        catch
        {
            // JSON 损坏 / IO 失败 → 静默 fallback；旧文件不删，保留供用户排查
            return new QueueState();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(QueueState snapshot)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions)
                                .ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
```

**关键决策：**

- 与 `JsonSettingsPersistence` 共享相同 ctor 模式（拼路径 + `Directory.CreateDirectory`）和 `SemaphoreSlim` 锁纪律
- `LoadAsync` 用 catch-all 静默 fallback；`SaveAsync` **不**吞异常（让 `Window_Closing` 顶层决定如何处理）—— 与 settings 一致
- 不写 `.bak` / temp-then-rename 等"写入半截损坏"防护——队列丢一次（极端情况）≈ 用户重新拖一遍文件，不值得加复杂度

### 4.3 DI 注册

`Extensions/ServiceCollectionExtensions.cs` 中追加：

```csharp
services.AddSingleton<IQueuePersistence, JsonQueuePersistence>();
```

注册位置紧跟在 `ISettingsPersistence` 之后。

### 4.4 不引入的东西（YAGNI 显式记录）

| 不做 | 理由 |
|------|------|
| `LoadAsync` 不去 `File.Exists` 队列里每一项 | 那是 **PlaylistVM** 的职责（loader 回填时 sanity check） |
| 不加 file-watcher 监视 queue.json 外部修改 | 没有任何工具会在 UmaPlayer 运行时改这个文件 |
| 不做增量写盘 / 内存 buffer | 写盘只在 Closing 时一次；`File.Create` 直接覆盖 |

---

## 5. `PlaylistViewModel` 改造

### 5.1 构造函数 —— 注入 + 同步加载

```csharp
public PlaylistViewModel(
    IPlaybackService playbackService,
    IFileDialogService fileDialog,
    ITrackMetadataReader metadataReader,
    IQueuePersistence queuePersistence)   // ⬅ NEW
{
    _player = playbackService;
    _fileDialog = fileDialog;
    _metadataReader = metadataReader;
    _queuePersistence = queuePersistence;

    _player.TrackEnded += HandleTrackEnded;
    Queue.CollectionChanged += OnQueueCollectionChanged;

    // 与 PlayerViewModel.Initialize 读音量、MainWindow 读窗口尺寸的纪律一致：
    // queue.json ≤ 10 KB 量级；同步读盘开销 < 5 ms，启动可接受
    LoadFromDisk();
}

private void LoadFromDisk()
{
    QueueState state;
    try
    {
        state = _queuePersistence.LoadAsync().GetAwaiter().GetResult();
    }
    catch
    {
        return; // 极端 IO 故障 → 当作首次启动
    }

    var survivingPaths = state.Items.Where(File.Exists).ToList();
    if (survivingPaths.Count == 0)
    {
        ShuffleEnabled = state.ShuffleEnabled;
        RepeatMode = state.RepeatMode;
        return;  // 全丢/原本就空 → 仅恢复 Shuffle/Repeat
    }

    int newCurrentIndex = MapCurrentIndexAfterFilter(state.Items, survivingPaths, state.CurrentIndex);

    foreach (var path in survivingPaths)
        Queue.Add(_metadataReader.CreateFallback(path));

    CurrentIndex = newCurrentIndex;
    ShuffleEnabled = state.ShuffleEnabled;
    RepeatMode = state.RepeatMode;
}
```

**`MapCurrentIndexAfterFilter` 算法（私有静态、纯函数）：**

```
原 Items   = [A, B, C, D, E]
filtered  = [A, C, E]  (B 和 D 丢失)
原 CurrentIndex = 3 (D)
                ↓
  D 丢失 → 找原数组中 D 之后第一个仍存在的项（E）→ 返回 filtered.IndexOf(E) = 2
  若 D 后无幸存项，回退找 D 之前最后一个幸存项（C）→ 返回 1
  若 filtered 全空 → -1（已在外层提前 return 处理）
```

> 选择 "向后滑" 而非 "重置到 0"：用户的"当前曲"语义上是听到了一半，跳到后面延续聆听比回到列表顶部更接近预期。

### 5.2 新 RelayCommand：`PlayCurrentCommand`

```csharp
[RelayCommand(CanExecute = nameof(CanPlayCurrent))]
private async Task PlayCurrentAsync()
{
    if (CurrentIndex < 0 || CurrentIndex >= Queue.Count) return;
    await PlayTrackAtAsync(CurrentIndex);
}

private bool CanPlayCurrent() => HasCurrentTrack;
```

并在 `OnCurrentIndexChanged` / `Queue.CollectionChanged` 已有的 `NotifyCanExecuteChanged` 列表中加入 `PlayCurrentCommand`。

### 5.3 新公开方法：`SnapshotState()`

```csharp
/// <summary>
/// 由 MainWindow.Window_Closing 调用 —— 把当前队列状态打包成不可变快照。
/// 纯读，无副作用；UI 线程或 worker 线程都能调（实际由 UI 线程调）。
/// </summary>
public QueueState SnapshotState() => new()
{
    SchemaVersion = 1,
    Items = Queue.Select(t => t.FilePath).ToArray(),
    CurrentIndex = CurrentIndex,
    ShuffleEnabled = ShuffleEnabled,
    RepeatMode = RepeatMode,
};
```

> `Queue` 中即便是占位 Track，`FilePath` 也已被填好（`CreateFallback(path)` 设置了它），所以即便用户从未播放某曲，它的路径也能被正确持久化。

### 5.4 `PlayerBar.xaml` —— ▶ 按钮双重绑定

当前 `PlayerBar` 的 ▶/⏸ 按钮绑到 `PlayPauseCommand`。改为根据 transport 状态选择目标命令：

```xml
<Button x:Name="PlayPauseButton" Style="{StaticResource TransportButtonStyle}">
    <Button.Style>
        <Style TargetType="Button" BasedOn="{StaticResource TransportButtonStyle}">
            <!-- 默认：transport 已 Loaded —— 走 PlayerVM 切换播放/暂停 -->
            <Setter Property="Command" Value="{Binding PlayPauseCommand}" />
            <Style.Triggers>
                <!-- 启动后 transport 空闲（CurrentTrack=null）但队列有当前曲 —— 跨级走 PlaylistVM 启动播放 -->
                <DataTrigger Binding="{Binding CurrentTrack}" Value="{x:Null}">
                    <Setter Property="Command"
                        Value="{Binding DataContext.Playlist.PlayCurrentCommand,
                                RelativeSource={RelativeSource AncestorType=Window}}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
    <ContentControl Content="{Binding PlayState, Converter={StaticResource PlayStateToIcon}}"/>
</Button>
```

**为什么用 DataTrigger 而不是 ICommand 委托：**

- 完全 XAML 化、无 code-behind 改动
- `CurrentTrack` 是 `PlayerViewModel` 的现成属性（已绑定），切换瞬时
- `HasCurrentTrack` 由 `PlaylistVM` 已暴露，`PlayCurrentCommand.CanExecute` 自然把按钮在空队列时禁掉

**触发器切回的时机：** 用户按 ▶（启动后首次）→ `PlayCurrentCommand` → `PlayTrackAtAsync` → `IPlaybackService.LoadAsync` 触发 `TrackChanged(track)` → `PlayerVM.CurrentTrack` 变非 null → DataTrigger 失活 → 按钮回到 `PlayPauseCommand` 绑定。后续暂停/播放循环走 PlayerVM 路径，与 Phase 3 行为完全一致。当用户按 `[清空队列]` 按钮 / 删除当前曲 → `IPlaybackService.Unload()` → `TrackChanged(null)` → DataTrigger 再次激活，按钮回到"启动后首次"语义。

> PlayerBar 的 DataContext 是 `PlayerViewModel`（PROJECT.md §5.5），因此 trigger 的 `Binding` 默认就是 PlayerVM 的 `CurrentTrack`；跨级访问 `Playlist.PlayCurrentCommand` 用 `RelativeSource AncestorType=Window`，与 PlayerBar 中现有的 `Playlist.PrevTrackCommand` / `NextTrackCommand` 模式一致。

### 5.5 不变的事

| 现有 | 仍然如此 |
|------|---------|
| 跟 `PlayerViewModel` 互不持引用 | ✅ 仅新加 `IQueuePersistence` 依赖 |
| `_playToken` 重入哨兵 | ✅ 启动加载是构造函数同步段，不与异步 PlayTrackAtAsync 交叉 |
| `Cleanup()` 同步解绑 TrackEnded | ✅ 队列写盘不在 VM 内做（在 Window_Closing 做） |
| DI 注册顺序：PlayerVM 先于 PlaylistVM | ✅ 不变 |

---

## 6. `MainWindow.Window_Closing` + 启动流程

### 6.1 `Views/MainWindow.xaml.cs` —— 关闭时写盘

```csharp
private async void Window_Closing(object? sender, CancelEventArgs e)
{
    // —— 现有：捕获窗口 DP 到局部变量 ——
    var left   = Left;
    var top    = Top;
    var width  = Width;
    var height = ActualHeight;

    try
    {
        await _persistence.UpdateAsync(s => s with
        {
            WindowLeft = left, WindowTop = top,
            WindowWidth = width, WindowHeight = height,
        });
    }
    catch { /* 静默 */ }

    await _vm.CleanupAsync();

    // —— NEW：快照队列后写盘 ——
    // 必须在 CleanupAsync 之后调 SnapshotState 也 OK ——
    // Cleanup 仅解绑 TrackEnded，不修改 Queue/CurrentIndex/Shuffle/Repeat
    try
    {
        var snapshot = _vm.Playlist.SnapshotState();
        await _queuePersistence.SaveAsync(snapshot);
    }
    catch { /* 静默 */ }
}
```

**MainWindow ctor 增加注入**：

```csharp
public MainWindow(
    MainViewModel viewModel,
    ISettingsPersistence persistence,
    IQueuePersistence queuePersistence)   // ⬅ NEW
```

### 6.2 `App.xaml.cs` —— DI 解析顺序

```csharp
var queuePersistence = _serviceProvider.GetRequiredService<IQueuePersistence>();
var window = new MainWindow(vm, persistence, queuePersistence);
```

> ⚠️ DI 容器在 `vm = GetRequiredService<MainViewModel>()` 时构造 `MainViewModel → PlayerViewModel + PlaylistViewModel`。`PlaylistViewModel` ctor 内部调 `IQueuePersistence.LoadAsync().GetAwaiter().GetResult()` —— 这意味着 `IQueuePersistence` 必须先 `JsonQueuePersistence` 完成构造（`Directory.CreateDirectory` 等）才能被 PlaylistVM 使用。Singleton 注册天然保证了这一点：DI 容器按需懒构造。

### 6.3 启动流程顺序图

```
App.OnStartup
  ├── 读 appsettings.json → ServiceCollection
  ├── AddUmaPlayerServices(...)
  ├── BuildServiceProvider
  │
  ├── persistence = GetRequiredService<ISettingsPersistence>()
  ├── queuePersistence = GetRequiredService<IQueuePersistence>()  ⬅ NEW
  │
  ├── vm = GetRequiredService<MainViewModel>()
  │     ├── PlayerViewModel ctor
  │     │   ├── 订阅 IPlaybackService 5 个事件
  │     │   └── Initialize: 同步读 settings.DefaultVolume
  │     ├── PlaylistViewModel ctor
  │     │   ├── 订阅 IPlaybackService.TrackEnded
  │     │   ├── 订阅 Queue.CollectionChanged
  │     │   └── LoadFromDisk():                                    ⬅ NEW
  │     │       ├── 同步 LoadAsync → QueueState
  │     │       ├── 过滤 File.Exists；MapCurrentIndexAfterFilter
  │     │       ├── 逐项 Queue.Add(_metadataReader.CreateFallback(path))
  │     │       └── 写 CurrentIndex / ShuffleEnabled / RepeatMode
  │     └── MainViewModel ctor (Strict Facade)
  │
  └── new MainWindow(vm, persistence, queuePersistence)
       ├── ctor: 同步读 settings.Window* → 写 Left/Top/Width/Height
       ├── EnsureVisible()
       └── Show()
```

**首屏 UI 状态：**

- PlayerBar：标题"No track loaded"（`CurrentTrack==null`），位置 0:00、时长 0:00
- PlaylistView：列出全部恢复曲；▶ 标记画在 `CurrentIndex` 那一行；Shuffle/Repeat 图标恢复
- 用户按 ▶ → DataTrigger 切换到 `Playlist.PlayCurrentCommand` → `PlayTrackAtAsync(CurrentIndex)` → 读元数据并播

### 6.4 关闭流程顺序图

```
用户点 X → Window_Closing 触发
  ├── 捕获 Left/Top/Width/ActualHeight 到局部变量（UI 线程必须）
  │
  ├── await _persistence.UpdateAsync(s => s with {...})        (settings.json)
  │
  ├── await _vm.CleanupAsync()
  │     ├── Playlist.Cleanup()         (同步：解绑 TrackEnded)
  │     ├── await Player.CleanupAsync() (异步：解绑 5 事件 + 写 settings.DefaultVolume)
  │     └── _player.Dispose()           (NAudio 资源)
  │
  └── snapshot = _vm.Playlist.SnapshotState()                  ⬅ NEW
      └── await _queuePersistence.SaveAsync(snapshot)          (queue.json，独立 SemaphoreSlim)
```

> **与 settings 写盘并行还是串行？** —— 串行（如上）。两者都很快（< 10 ms），不值得 `Task.WhenAll`；并发也意味着写盘失败的诊断变难。

### 6.5 `async void Window_Closing` 的 await 语义

WPF 不会等 `async void Window_Closing` 走完所有 await 就开始进程退出；现有 `App.OnExit` 已通过 `DisposePlayback` 幂等性救场。`queue.json` 写盘极快（≤ 10 ms 量级），实践上能在 dispatcher 关闭前完成；万一漏掉一次 = 用户重新拖一遍队列，可接受。

**不引入额外救场逻辑**（如 deferral 或 `e.Cancel=true; await; Application.Current.Shutdown()` 重做）：那是后续阶段的事，YAGNI。

---

## 7. 错误处理 / 兼容性 / 回归风险

### 7.1 错误处理矩阵

| 场景 | 触发位置 | 处理 | 可见性 |
|------|---------|------|--------|
| `queue.json` 不存在（首次启动） | `LoadAsync` | 返回空 `QueueState` | UI = 空队列（与 Phase 3 一致） |
| `queue.json` JSON 损坏 | `LoadAsync` 反序列化抛 | catch → 空 `QueueState`；**旧文件不删** | 用户重新拖列表；旧文件可手动诊断 |
| `SchemaVersion` 不匹配 | `LoadAsync` 校验 | 返回空 `QueueState` | 同上 |
| 单个文件不存在 | `LoadFromDisk` 内 `File.Exists` | 该项跳过 | 队列项数变少；CurrentIndex 重映射 |
| **所有**项都不存在 | `LoadFromDisk` 过滤后 `Count==0` | 仅恢复 Shuffle/Repeat；不写 Queue | UI = 空队列但 Shuffle/Repeat 保留 |
| 启动时持久化层 IO 异常 | `LoadFromDisk` 顶层 catch | return（视为首次启动） | 同首次启动 |
| `SaveAsync` 写盘失败 | `Window_Closing` 顶层 catch | 静默吞 | 用户下次启动队列丢失（与 settings 写盘失败行为对称） |
| 用户在 `PlayTrackAtAsync` 进行中关闭窗口 | Cleanup 后 SnapshotState | `_playToken` 已让 in-flight 回退；`Queue` 处于一致态 | 写入的是关闭瞬间的 Queue 状态 |

### 7.2 兼容性

**Phase 3 → Phase 4 数据兼容：**

- `settings.json` 完全不变，无任何字段调整
- 首次升级到 Phase 4 启动时 `queue.json` 不存在 → 视为空队列，**与 Phase 3 行为完全一致**

**未来 Phase 4+ 兼容：**

- `queue.json` 表示"当前正在播的内存队列"，不会和未来"用户保存的命名播放列表"概念冲突
- 多命名播放列表上线时，`queue.json` 仍可作为"上次正在播什么"的最后状态文件
- 真要破坏式升级 → `SchemaVersion` 撞号 → fallback 空队列（用户感知 = 一次性丢一次队列）

### 7.3 回归风险登记（COUPLING.md §5 隐式契约审视）

| 既有契约 | 本 Phase 是否动到？ | 风险评级 |
|----------|---------------------|---------|
| `NAudioPlaybackService` UI 线程构造 | ✗ | — |
| `PART_Track` 命名 | ✗ | — |
| `DurationChanged` 必须在 `TrackChanged` 之前触发 | ✗ | — |
| `_isInitializing` 防写盘 | ✗ | — |
| `Window_Closing` async void 不等 await | 多加一段 await | ⚠️ 低：写盘 ≤ 10 ms |
| `Stop()` vs `Unload()` 语义 | ✗ | — |
| `PlayTrackAtAsync` 必须自增 `_playToken` 后 await | ✗ | — |
| `RefreshCurrentIndicator` 用 DataTemplate 列序 | 启动后批量 `Queue.Add` | ⚠️ 低：首屏可能闪烁一帧 |
| `PlayerVM`/`PlaylistVM` 互不持引用 | ✗ | — |
| DI 注册顺序：PlayerVM 先于 PlaylistVM | ✗ | — |
| `UpdateAsync` mutator 内不可读 WPF DP | ✗ | — |
| `IPlaybackService.TrackChanged(null)` null-safe | ✗ | — |

### 7.4 与"债 #1（BitmapImage）" / "Phase 3 已知小窗口"的关系

- **债 #1**：本 Phase 完全不触碰 `AlbumArtImage`，债不变 / 不增加
- **删非当前曲 + in-flight 元数据极窄竞态**：本 Phase `LoadFromDisk` 是同步段（无 await，无 in-flight token），不在该窗口范围内

### 7.5 新增隐式契约（写入 COUPLING.md §5）

| 契约 | 位置 | 风险 |
|------|------|------|
| `JsonQueuePersistence.LoadAsync` 必须 catch-all 静默 fallback | 注释 | 任何 IO/JSON 异常逃出会让 `PlaylistVM` ctor 抛 → 应用启动崩溃 |
| `PlaylistVM.LoadFromDisk` 是同步段，不可 await | 注释 | DI 容器构造 VM 时若死锁 (UI sync ctx) 会让窗口永不显示；用 `.ConfigureAwait(false)` + `GetAwaiter().GetResult()` 模式 |
| `PlaylistVM.SnapshotState` 仅读不副作用 | 注释 | 若未来加副作用，`Window_Closing` 在 Cleanup 之后调它会让人意外 |
| `MainWindow.Window_Closing` 中 queue 写盘紧跟 settings 写盘后 | 注释 | 顺序不影响正确性，但便于诊断"哪步失败" |

### 7.6 性能 / 资源

- **启动加载**：queue.json ≤ 10 KB（粗估 1 万首路径）→ 同步反序列化 < 5 ms；`Queue.Add` × N（N 典型 < 200）尾部添加 O(1)，总耗时 < 5 ms；**首屏总开销 < 10 ms**
- **关闭写盘**：写 ≤ 10 KB JSON 文件 < 5 ms
- **运行时**：零开销 —— 队列变更不主动写盘
- **内存**：`QueueState` 仅含字符串数组 + 4 个标量

### 7.7 Manual acceptance 关键场景

1. 添加 5 首 → 双击第 3 首播 → 关闭 → 重启 → 列表 5 首；▶ 在第 3 首；标题"No track loaded"
2. 启动后按 ▶ → 第 3 首加载并播放（PlayCurrentCommand 路径）
3. 改 Shuffle = on, Repeat = One → 关 → 重启 → 状态保留
4. 加 3 首 → 关 → **手工删除磁盘上第 2 首文件** → 重启 → 列表 2 首
5. 关 → 手工破坏 queue.json（写"X"）→ 重启 → 空队列，应用不崩
6. 队列空时关 → 重启 → 队列空、Shuffle/Repeat 保留
7. 关闭瞬间正在 PlayTrackAtAsync 中（按 ⏭ 后立刻 X）→ 重启 → 队列正确（不卡死、不丢）

---

## 8. 交付物清单

### 8.1 文件级 diff 预览

| 文件 | 操作 | 估计行数 |
|------|------|---------|
| `Models/QueueState.cs` | 新增 | ~25 |
| `Services/IQueuePersistence.cs` | 新增 | ~20 |
| `Services/JsonQueuePersistence.cs` | 新增 | ~80 |
| `Extensions/ServiceCollectionExtensions.cs` | 改：注册 | +2 |
| `ViewModels/PlaylistViewModel.cs` | 改：注入 + LoadFromDisk + SnapshotState + PlayCurrentCommand + MapCurrentIndexAfterFilter | +60 |
| `Views/MainWindow.xaml.cs` | 改：注入 + Closing 写盘 | +12 |
| `Views/Controls/PlayerBar.xaml` | 改：▶ 按钮 DataTrigger | +10 |
| `App.xaml.cs` | 改：解析并传递 | +2 |
| `docs/PROJECT.md` | 改：Phase 4 状态 | +30 |
| `docs/COUPLING.md` | 改：检查清单第 5 项 + 隐式契约 | +20 |

**净增**：约 **240 行 C# / XAML** + 文档。

### 8.2 提交分组（plan 阶段会展开为单步骤）

```
feat(models): add QueueState record (Phase 4 schema)
feat(services): add IQueuePersistence interface
feat(services): add JsonQueuePersistence (queue.json read/write with semaphore)
feat(di): register IQueuePersistence singleton
feat(vm): inject IQueuePersistence into PlaylistViewModel + LoadFromDisk on ctor
feat(vm): add PlaylistViewModel.SnapshotState() + PlayCurrentCommand
feat(view): wire MainWindow.Window_Closing to write queue.json
feat(view): PlayerBar ▶ button DataTrigger for Stopped+HasCurrent → PlayCurrent
test: Phase 4 manual acceptance pass
docs: update PROJECT.md and COUPLING.md to Phase 4 state
```

### 8.3 验收门槛

- 全部 7 个 §7.7 manual scenario 通过
- 项目构建 0 warning、0 error
- 用 Phase 3 build 写下 settings.json 后切换到 Phase 4 build 启动 = 不崩、不影响 settings
- COUPLING.md / PROJECT.md 更新到 Phase 4 状态
- 干净 git history（feature 分支 → squash-merge / fast-forward 到 master）

---

## 9. 参考

- 项目导读：[`docs/PROJECT.md`](../../PROJECT.md)
- 耦合分析：[`docs/COUPLING.md`](../../COUPLING.md)
- Phase 3 设计：[`docs/superpowers/specs/2026-06-07-uma-player-phase3-design.md`](2026-06-07-uma-player-phase3-design.md)
