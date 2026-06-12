# UmaPlayer Phase 4 — 队列持久化 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让用户关闭 UmaPlayer 后，下次启动恢复内存队列（条目 + 当前曲位置 + Shuffle + Repeat），不恢复 PlayState 与 Position。

**Architecture:** 新增独立文件 `%LocalAppData%\UmaPlayer\queue.json` 与独立接口 `IQueuePersistence`，与现有 `ISettingsPersistence` 平行。仅存路径 + 队列态；启动时 `PlaylistViewModel` 在构造函数同步段读盘并填占位 Track；关闭时 `MainWindow.Window_Closing` 调 `SnapshotState() → SaveAsync()`。启动后不预加载当前曲；用户按 ▶ 时通过 PlayerBar 的 DataTrigger 切换到 `PlaylistVM.PlayCurrentCommand`，复用现有 `PlayTrackAtAsync` 路径懒加载。

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm 8.x, Microsoft.Extensions.DependencyInjection 10.x, `System.Text.Json` (BCL)。验证方式：构建 + manual acceptance pass（与 Phase 1/2/3 工程节奏一致；无 xUnit 项目）。

**Spec 引用：** [`docs/superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md`](../specs/2026-06-12-uma-player-phase4-queue-persistence-design.md)

---

## 文件结构

| 文件 | 操作 | 责任 |
|------|------|------|
| `Models/QueueState.cs` | 新增 | 队列持久化的不可变快照 record |
| `Services/IQueuePersistence.cs` | 新增 | 队列持久化抽象（`LoadAsync` / `SaveAsync`） |
| `Services/JsonQueuePersistence.cs` | 新增 | JSON 文件实现（`SemaphoreSlim` 互斥、catch-all fallback） |
| `Extensions/ServiceCollectionExtensions.cs` | 改 | 注册 `IQueuePersistence` Singleton |
| `ViewModels/PlaylistViewModel.cs` | 改 | 注入 + `LoadFromDisk` + `SnapshotState` + `PlayCurrentCommand` + `MapCurrentIndexAfterFilter` |
| `Views/Controls/PlayerBar.xaml` | 改 | ▶ 按钮 DataTrigger（`CurrentTrack==null` → `PlayCurrentCommand`） |
| `Views/MainWindow.xaml.cs` | 改 | 注入 `IQueuePersistence` + Closing 写盘 |
| `App.xaml.cs` | 改 | DI 解析 `IQueuePersistence` 传 MainWindow |
| `docs/PROJECT.md` | 改 | Phase 4 状态 + 新模块描述 |
| `docs/COUPLING.md` | 改 | 勾选 §6 检查清单第 5 项 + 新增隐式契约 |

工作分支：`feature/phase4-queue-persistence`（已存在，spec 提交于 `d1765db`）

---

## 验证方式说明

- 本项目 **没有 xUnit / 自动化测试项目**（详见 spec §1.2）。每个任务用"构建成功 + 必要时手工触发场景"作为通过条件。
- 最终一次性 manual acceptance pass 在 Task 9 集中跑（覆盖 spec §7.7 全部 7 个场景），并对应一个独立 commit（与 Phase 2/3 节奏一致：`c3b12ce test: Phase 3 manual acceptance pass`）。
- 构建命令：`dotnet build UmaPlayer.sln -c Debug`，期望 `Build succeeded. 0 Warning(s) 0 Error(s)`。
- 启动命令：`dotnet run --project UmaPlayer.csproj`。

---

## Task 1：QueueState record

**Files:**
- Create: `Models/QueueState.cs`

- [ ] **Step 1: 创建 `Models/QueueState.cs`**

```csharp
namespace UmaPlayer.Models;

/// <summary>
/// 队列持久化的不可变快照（Phase 4）。仅包含路径与队列态——不携带 Track 元数据或封面。
///
/// 启动时 PlaylistViewModel 同步读盘 → 把 Items 填到 ObservableCollection&lt;Track&gt;
/// 时为每条创建占位 Track（与 OpenAndPlay 的"占位 → 完整元数据"流程相同）。
///
/// 使用 record 不可变 + with 表达式风格，与 AppSettings 一致。
/// </summary>
public sealed record QueueState
{
    /// <summary>Schema 版本号；加载时不匹配则 fallback 空队列。当前版本：1。</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>队列中每首曲的文件路径，按队列顺序排列。</summary>
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();

    /// <summary>当前播放/选中索引；空队列或未选时为 -1。</summary>
    public int CurrentIndex { get; init; } = -1;

    /// <summary>是否启用 Shuffle 模式。</summary>
    public bool ShuffleEnabled { get; init; }

    /// <summary>循环模式（Off / List / One）。</summary>
    public RepeatMode RepeatMode { get; init; } = RepeatMode.Off;
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add Models/QueueState.cs
git commit -m "feat(models): add QueueState record (Phase 4 schema)"
```

---

## Task 2：IQueuePersistence 接口

**Files:**
- Create: `Services/IQueuePersistence.cs`

- [ ] **Step 1: 创建 `Services/IQueuePersistence.cs`**

```csharp
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 队列持久化抽象（Phase 4）。文件路径：%LocalAppData%\UmaPlayer\queue.json。
///
/// 与 ISettingsPersistence 的设计基线相同：
///   - 实现层用 SemaphoreSlim 串行化读写
///   - 单实例 Singleton；DI 容器懒构造
///   - 启动时若文件不存在/损坏，LoadAsync 返回 default(QueueState)；写盘失败抛
///
/// 与 ISettingsPersistence 的差异：
///   - 这里不需要 read-modify-write —— 队列状态由 PlaylistViewModel 整体快照后写入，
///     仅有一个写者（MainWindow.Window_Closing），不存在合并竞态。
///   - 因此用 SaveAsync(QueueState snapshot) 比 UpdateAsync(Func&lt;&gt;) 更直接。
/// </summary>
public interface IQueuePersistence
{
    /// <summary>
    /// 读取磁盘上的队列快照。
    /// 文件不存在 / 损坏 / SchemaVersion 不匹配 时静默返回空 QueueState（绝不抛）。
    /// </summary>
    Task<QueueState> LoadAsync();

    /// <summary>
    /// 把快照写入 queue.json。
    /// 写盘失败会抛（IO/权限），由 MainWindow.Window_Closing 顶层 catch 静默处理。
    /// </summary>
    Task SaveAsync(QueueState snapshot);
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add Services/IQueuePersistence.cs
git commit -m "feat(services): add IQueuePersistence interface"
```

---

## Task 3：JsonQueuePersistence 实现

**Files:**
- Create: `Services/JsonQueuePersistence.cs`

- [ ] **Step 1: 创建 `Services/JsonQueuePersistence.cs`**

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// queue.json 的 JSON 文件实现（Phase 4）。
///
/// 设计要点：
///   - 与 JsonSettingsPersistence 共用相同 ctor 模式（拼路径 + Directory.CreateDirectory）
///   - 自带 SemaphoreSlim(1,1)，与 settings 持久化的锁互不影响（独立文件、独立 Singleton）
///   - LoadAsync 用 catch-all 静默 fallback；SaveAsync 不吞异常（顶层决定如何处理）
///   - RepeatMode 用 JsonStringEnumConverter 写入字符串值，跨版本稳定且人类可读
///   - 不写 .bak / temp-then-rename：极端情况下队列丢失 ≈ 用户重新拖一遍文件，
///     不值得加复杂度（与 settings.json 行为对称）
/// </summary>
public sealed class JsonQueuePersistence : IQueuePersistence
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,                             // 人类可读，方便手动调试
        Converters = { new JsonStringEnumConverter() },   // RepeatMode 字符串化
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonQueuePersistence()
    {
        // 与 JsonSettingsPersistence 同目录：%LocalAppData%\UmaPlayer
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "UmaPlayer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "queue.json");
    }

    /// <summary>
    /// 读取磁盘队列快照。catch-all 静默 fallback —— 任何异常逃出都会让 PlaylistViewModel
    /// 构造函数抛 → 应用启动崩溃。隐式契约：本方法绝不抛。
    /// </summary>
    public async Task<QueueState> LoadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return new QueueState();

            await using var stream = File.OpenRead(_path);
            var loaded = await JsonSerializer
                .DeserializeAsync<QueueState>(stream, JsonOptions)
                .ConfigureAwait(false);

            // null（空文件）/ 版本不匹配 → 视为不可用
            if (loaded is null || loaded.SchemaVersion != CurrentSchemaVersion)
                return new QueueState();

            return loaded;
        }
        catch
        {
            // JSON 损坏 / IO 失败 → 静默 fallback；旧文件不删，保留供用户排查
            return new QueueState();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 序列化并覆盖写入 queue.json。失败抛出，由调用方决定如何处理。
    /// </summary>
    public async Task SaveAsync(QueueState snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(_path);
            await JsonSerializer
                .SerializeAsync(stream, snapshot, JsonOptions)
                .ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add Services/JsonQueuePersistence.cs
git commit -m "feat(services): add JsonQueuePersistence (queue.json read/write with semaphore)"
```

---

## Task 4：DI 注册

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: 在 `AddUmaPlayerServices` 中追加 `IQueuePersistence` 注册**

定位 `Extensions/ServiceCollectionExtensions.cs:25` 附近这一行：

```csharp
        services.AddSingleton<ISettingsPersistence, JsonSettingsPersistence>();
```

在它之后**追加一行**：

```csharp
        services.AddSingleton<IQueuePersistence, JsonQueuePersistence>();
```

最终该段应为：

```csharp
        // 业务服务（Singleton —— 持有音频设备/文件句柄等长期资源）
        services.AddSingleton<IPlaybackService, NAudioPlaybackService>();
        services.AddSingleton<IFileDialogService, Win32FileDialogService>();
        services.AddSingleton<ISettingsPersistence, JsonSettingsPersistence>();
        services.AddSingleton<IQueuePersistence, JsonQueuePersistence>();
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(di): register IQueuePersistence singleton"
```

---

## Task 5：PlaylistViewModel 注入 + LoadFromDisk

> 本任务**只**做：注入字段、`LoadFromDisk` 方法（含 `MapCurrentIndexAfterFilter`）、构造函数末尾调用 `LoadFromDisk()`。不动 `SnapshotState` / `PlayCurrentCommand`（Task 6）。

**Files:**
- Modify: `ViewModels/PlaylistViewModel.cs`

- [ ] **Step 1: 在 `using` 区追加 `System.IO`**

定位 `ViewModels/PlaylistViewModel.cs:1-7` 顶部 using 区：

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmaPlayer.Models;
using UmaPlayer.Services;
```

替换为（**新增 `System.IO`**）：

```csharp
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmaPlayer.Models;
using UmaPlayer.Services;
```

- [ ] **Step 2: 在私有字段区追加 `_queuePersistence`**

定位 `ViewModels/PlaylistViewModel.cs:23-25` 的字段声明：

```csharp
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ITrackMetadataReader _metadataReader;
```

替换为（**追加一行**）：

```csharp
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ITrackMetadataReader _metadataReader;
    private readonly IQueuePersistence _queuePersistence;
```

- [ ] **Step 3: 修改构造函数签名 + 在末尾调用 `LoadFromDisk()`**

定位 `ViewModels/PlaylistViewModel.cs:70-88` 的构造函数：

```csharp
    public PlaylistViewModel(
        IPlaybackService player,
        IFileDialogService fileDialog,
        ITrackMetadataReader metadataReader)
    {
        _player = player;
        _fileDialog = fileDialog;
        _metadataReader = metadataReader;

        _player.TrackEnded += HandleTrackEnded;

        // 队列变化时强制刷新 Next/Prev 命令可用性
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCurrentTrack));
            NextTrackCommand.NotifyCanExecuteChanged();
            PrevTrackCommand.NotifyCanExecuteChanged();
        };
    }
```

替换为（新增参数 + 末尾调 `LoadFromDisk()`）：

```csharp
    public PlaylistViewModel(
        IPlaybackService player,
        IFileDialogService fileDialog,
        ITrackMetadataReader metadataReader,
        IQueuePersistence queuePersistence)
    {
        _player = player;
        _fileDialog = fileDialog;
        _metadataReader = metadataReader;
        _queuePersistence = queuePersistence;

        _player.TrackEnded += HandleTrackEnded;

        // 队列变化时强制刷新 Next/Prev 命令可用性
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCurrentTrack));
            NextTrackCommand.NotifyCanExecuteChanged();
            PrevTrackCommand.NotifyCanExecuteChanged();
        };

        // Phase 4：构造期同步读盘恢复队列。queue.json ≤ 10 KB 量级；
        // 与 PlayerViewModel.Initialize 读音量、MainWindow 读窗口尺寸的纪律一致。
        LoadFromDisk();
    }
```

- [ ] **Step 4: 在文件末尾（`Cleanup()` 之后、最末尾大括号 `}` 之前）追加 LoadFromDisk + MapCurrentIndexAfterFilter**

定位 `ViewModels/PlaylistViewModel.cs:380-383`：

```csharp
    /// <summary>由 MainViewModel.CleanupAsync 调用 —— 解绑 TrackEnded 订阅。</summary>
    public void Cleanup()
    {
        _player.TrackEnded -= HandleTrackEnded;
    }
}
```

替换为（在 `Cleanup()` 之后、最末 `}` 之前插入两个新方法）：

```csharp
    /// <summary>由 MainViewModel.CleanupAsync 调用 —— 解绑 TrackEnded 订阅。</summary>
    public void Cleanup()
    {
        _player.TrackEnded -= HandleTrackEnded;
    }

    // —— Phase 4：队列持久化 ——

    /// <summary>
    /// 启动期同步读盘恢复队列。隐式契约：本方法是同步段，不可 await（DI 容器构造 VM
    /// 时若死锁 UI sync ctx 会让窗口永不显示）。JsonQueuePersistence 内部已用
    /// ConfigureAwait(false)，UI 线程同步等待 worker pool 任务回调时不会死锁。
    ///
    /// 文件丢失（用户外部移动/删除）静默跳过；schema 损坏/IO 异常 → 视为首次启动。
    /// </summary>
    private void LoadFromDisk()
    {
        QueueState state;
        try
        {
            state = _queuePersistence.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // 极端 IO 故障 → 当作首次启动
            return;
        }

        // 文件存在性过滤
        var survivingPaths = state.Items.Where(File.Exists).ToList();
        if (survivingPaths.Count == 0)
        {
            // 全丢/原本就空 → 仅恢复 Shuffle/Repeat
            ShuffleEnabled = state.ShuffleEnabled;
            RepeatMode = state.RepeatMode;
            return;
        }

        int newCurrentIndex = MapCurrentIndexAfterFilter(state.Items, survivingPaths, state.CurrentIndex);

        foreach (var path in survivingPaths)
        {
            // 占位 Track —— 与 OpenAndPlay 流程一致；用户首次播放时由 PlayTrackAtAsync 升级为完整元数据
            Queue.Add(_metadataReader.CreateFallback(path));
        }

        CurrentIndex = newCurrentIndex;
        ShuffleEnabled = state.ShuffleEnabled;
        RepeatMode = state.RepeatMode;
    }

    /// <summary>
    /// 把过滤前的 CurrentIndex 映射到过滤后的索引。
    ///
    /// 算法：
    ///   - 若原索引仍在 surviving 中 → 直接返回它在 surviving 中的位置
    ///   - 若原索引项丢失 → 向后找原数组中第一个仍存在的项；找不到则向前回退
    ///   - 若 surviving 为空（外层已提前 return 处理）/ 原索引无效 → 返回 -1
    ///
    /// 设计取舍（spec §5.1）：选择"向后滑"而非"重置到 0"——
    /// 用户的"当前曲"语义上是听到了一半，跳到后面延续聆听比回到列表顶部更接近预期。
    /// 静态纯函数：无副作用，便于人工推理。
    /// </summary>
    private static int MapCurrentIndexAfterFilter(
        IReadOnlyList<string> originalItems,
        IReadOnlyList<string> survivingPaths,
        int originalIndex)
    {
        if (survivingPaths.Count == 0) return -1;
        if (originalIndex < 0 || originalIndex >= originalItems.Count) return 0;

        // Case 1: 原项仍存在 —— 直接定位
        var originalPath = originalItems[originalIndex];
        if (File.Exists(originalPath))
        {
            var idx = survivingPaths.IndexOf(originalPath);
            if (idx >= 0) return idx;
        }

        // Case 2: 原项丢失 —— 向后滑：找原数组中 originalIndex 之后第一个仍存在的项
        for (int i = originalIndex + 1; i < originalItems.Count; i++)
        {
            var idx = survivingPaths.IndexOf(originalItems[i]);
            if (idx >= 0) return idx;
        }

        // Case 3: 后方无幸存 —— 向前回退
        for (int i = originalIndex - 1; i >= 0; i--)
        {
            var idx = survivingPaths.IndexOf(originalItems[i]);
            if (idx >= 0) return idx;
        }

        // 理论不可达（surviving 非空意味着 originalItems 中至少一个 File.Exists）
        return 0;
    }
}
```

- [ ] **Step 5: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: 失败 —— DI 容器尚未提供 `IQueuePersistence`（在 Task 4 已注册），本任务**应当**通过编译；若失败则因为 `App.xaml.cs` / `MainWindow` 依然只传旧的构造参数集。复检编译错误：

- 若编译错误是 `'PlaylistViewModel' does not contain a constructor that takes 3 arguments`，说明这是预期的——但 Phase 3 的 DI 链条只通过 DI 解析 `PlaylistViewModel`，不会有 callsite 显式调它的旧构造函数。Grep 确认：

Run: `git grep -n "new PlaylistViewModel" -- "*.cs" "*.xaml"`
Expected: 无输出（DI 容器是唯一调用方）

- 故构建应**成功**。如果仍失败，复读错误提示并修正未触及的 callsite（不在预期范围内）。

最终：
Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add ViewModels/PlaylistViewModel.cs
git commit -m "feat(vm): inject IQueuePersistence into PlaylistViewModel + LoadFromDisk on ctor"
```

---

## Task 6：PlaylistViewModel SnapshotState + PlayCurrentCommand

**Files:**
- Modify: `ViewModels/PlaylistViewModel.cs`

- [ ] **Step 1: 追加 SnapshotState 与 PlayCurrentCommand**

定位 `ViewModels/PlaylistViewModel.cs` 末尾的 `MapCurrentIndexAfterFilter` 方法（Task 5 添加的）。在它之后、最末 `}` 之前**追加**：

```csharp
    /// <summary>
    /// 把当前 VM 状态打包成不可变快照。由 MainWindow.Window_Closing 调用。
    ///
    /// 隐式契约：本方法**仅读、无副作用**（COUPLING.md §5）。
    /// 若未来加副作用，Window_Closing 在 Cleanup 之后调它会让人意外。
    ///
    /// Track[i] 即便是占位（Title==文件名、AlbumArt==null），FilePath 也已被
    /// CreateFallback 填好，故未播放过的曲目也能被正确持久化。
    /// </summary>
    public QueueState SnapshotState() => new()
    {
        SchemaVersion = 1,
        Items = Queue.Select(t => t.FilePath).ToArray(),
        CurrentIndex = CurrentIndex,
        ShuffleEnabled = ShuffleEnabled,
        RepeatMode = RepeatMode,
    };

    /// <summary>
    /// 启动后用户首次按 ▶ 走的命令：加载并播放 CurrentIndex 指向的曲目。
    /// PlayerBar 的 ▶ 按钮通过 DataTrigger 在 PlayerVM.CurrentTrack==null 时
    /// 跨级绑定到本命令；TrackChanged 触发后回退到 PlayerVM.PlayPauseCommand。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasCurrentTrack))]
    private async Task PlayCurrent()
    {
        if (CurrentIndex < 0 || CurrentIndex >= Queue.Count) return;
        await PlayTrackAtAsync(CurrentIndex);
    }
```

- [ ] **Step 2: 在 `Queue.CollectionChanged` 订阅中加入 PlayCurrentCommand 的 Notify**

定位 Task 5 已修改的构造函数中的这段：

```csharp
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCurrentTrack));
            NextTrackCommand.NotifyCanExecuteChanged();
            PrevTrackCommand.NotifyCanExecuteChanged();
        };
```

替换为（**新增 PlayCurrentCommand.NotifyCanExecuteChanged**）：

```csharp
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCurrentTrack));
            NextTrackCommand.NotifyCanExecuteChanged();
            PrevTrackCommand.NotifyCanExecuteChanged();
            PlayCurrentCommand.NotifyCanExecuteChanged();
        };
```

- [ ] **Step 3: 让 `OnCurrentIndexChanged` 也刷新 PlayCurrentCommand**

定位 `_currentIndex` 字段的 `[ObservableProperty]` 声明（约 `:31-33`）：

```csharp
    /// <summary>当前播放曲在 Queue 中的索引；-1 表示未选/队列空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTrack))]
    private int _currentIndex = -1;
```

`HasCurrentTrack` 已经在 `Queue.CollectionChanged` lambda 里通知刷新；`CurrentIndex` 变化时也需要刷新 PlayCurrent / Next / Prev。CommunityToolkit.Mvvm 的 `[ObservableProperty]` 配合 `[NotifyCanExecuteChangedFor]` 可声明式做到。替换为：

```csharp
    /// <summary>当前播放曲在 Queue 中的索引；-1 表示未选/队列空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTrack))]
    [NotifyCanExecuteChangedFor(nameof(PlayCurrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextTrackCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrevTrackCommand))]
    private int _currentIndex = -1;
```

> 这同时把 Phase 3 既存的 Next/Prev 通知由 lambda 改为声明式，行为等价；放进同一改动让 PlayCurrentCommand 与现有命令风格一致。

- [ ] **Step 4: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add ViewModels/PlaylistViewModel.cs
git commit -m "feat(vm): add PlaylistViewModel.SnapshotState() + PlayCurrentCommand"
```

---

## Task 7：MainWindow + App 注入 IQueuePersistence + Closing 写盘

**Files:**
- Modify: `Views/MainWindow.xaml.cs`
- Modify: `App.xaml.cs`

- [ ] **Step 1: MainWindow 增加 `_queuePersistence` 字段与构造参数**

定位 `Views/MainWindow.xaml.cs:18-26` 的字段与构造函数头：

```csharp
    private readonly MainViewModel _vm;
    private readonly ISettingsPersistence _persistence;

    public MainWindow(MainViewModel vm, ISettingsPersistence persistence)
    {
        InitializeComponent();
        _vm = vm;
        _persistence = persistence;
        DataContext = _vm;
```

替换为（新增字段 + 新增构造参数 + 赋值）：

```csharp
    private readonly MainViewModel _vm;
    private readonly ISettingsPersistence _persistence;
    private readonly IQueuePersistence _queuePersistence;

    public MainWindow(
        MainViewModel vm,
        ISettingsPersistence persistence,
        IQueuePersistence queuePersistence)
    {
        InitializeComponent();
        _vm = vm;
        _persistence = persistence;
        _queuePersistence = queuePersistence;
        DataContext = _vm;
```

- [ ] **Step 2: 在 Window_Closing 末尾追加队列写盘**

定位 `Views/MainWindow.xaml.cs:50-72` 的 `Window_Closing`：

```csharp
    /// <summary>关闭时：合并最新窗口几何到设置文件，再让 VM 清理播放器资源。</summary>
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // 提前在 UI 线程读出 WPF DependencyProperty —— UpdateAsync 内部
        // ConfigureAwait(false) 后 mutator 会在 thread-pool 上执行，
        // 那里读 Left/Top/Width/ActualHeight 会抛 InvalidOperationException
        var left = Left;
        var top = Top;
        var width = Width;
        var height = ActualHeight;

        try
        {
            // 锁内 read-modify-write：仅改窗口几何，DefaultVolume 等其他字段保留磁盘最新值
            await _persistence.UpdateAsync(s => s with
            {
                WindowLeft = left, WindowTop = top,
                WindowWidth = width, WindowHeight = height
            });
        }
        catch { /* 关闭流程不打扰用户 */ }

        await _vm.CleanupAsync();
    }
```

替换为（在 `_vm.CleanupAsync()` 之后追加 queue.json 写盘）：

```csharp
    /// <summary>关闭时：合并最新窗口几何到设置文件，再让 VM 清理播放器资源，最后写队列快照。</summary>
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // 提前在 UI 线程读出 WPF DependencyProperty —— UpdateAsync 内部
        // ConfigureAwait(false) 后 mutator 会在 thread-pool 上执行，
        // 那里读 Left/Top/Width/ActualHeight 会抛 InvalidOperationException
        var left = Left;
        var top = Top;
        var width = Width;
        var height = ActualHeight;

        try
        {
            // 锁内 read-modify-write：仅改窗口几何，DefaultVolume 等其他字段保留磁盘最新值
            await _persistence.UpdateAsync(s => s with
            {
                WindowLeft = left, WindowTop = top,
                WindowWidth = width, WindowHeight = height
            });
        }
        catch { /* 关闭流程不打扰用户 */ }

        await _vm.CleanupAsync();

        // Phase 4：保存队列快照到 queue.json。
        // 必须在 CleanupAsync 之后调 SnapshotState 也 OK ——
        // Cleanup 仅解绑 TrackEnded，不修改 Queue/CurrentIndex/Shuffle/Repeat。
        try
        {
            var snapshot = _vm.Playlist.SnapshotState();    // UI 线程纯读
            await _queuePersistence.SaveAsync(snapshot);
        }
        catch { /* 写盘失败 = 用户下次启动队列丢失，与 settings 写盘失败行为对称 */ }
    }
```

- [ ] **Step 3: App.xaml.cs 解析 IQueuePersistence 并传入 MainWindow**

定位 `App.xaml.cs:36-41`：

```csharp
        // 注意：MainWindow 需要 persistence 用于恢复/保存窗口位置，
        // 因此这里显式解析后通过构造函数传入（而非让 DI 解析窗口）
        var vm = _services.GetRequiredService<MainViewModel>();
        var persistence = _services.GetRequiredService<ISettingsPersistence>();
        var mainWindow = new Views.MainWindow(vm, persistence);
        mainWindow.Show();
```

替换为（**新增 queuePersistence 解析与注入**）：

```csharp
        // 注意：MainWindow 需要 persistence 用于恢复/保存窗口位置，
        // 因此这里显式解析后通过构造函数传入（而非让 DI 解析窗口）。
        // queuePersistence（Phase 4）同理 —— 仅 Window_Closing 写盘需要它。
        var vm = _services.GetRequiredService<MainViewModel>();
        var persistence = _services.GetRequiredService<ISettingsPersistence>();
        var queuePersistence = _services.GetRequiredService<IQueuePersistence>();
        var mainWindow = new Views.MainWindow(vm, persistence, queuePersistence);
        mainWindow.Show();
```

- [ ] **Step 4: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add Views/MainWindow.xaml.cs App.xaml.cs
git commit -m "feat(view): wire MainWindow.Window_Closing to write queue.json"
```

---

## Task 8：PlayerBar ▶ 按钮 DataTrigger

**Files:**
- Modify: `Views/Controls/PlayerBar.xaml`

- [ ] **Step 1: 把 ▶/⏸ 按钮的 Command 从静态绑定改为 Style + DataTrigger**

定位 `Views/Controls/PlayerBar.xaml:83-86`：

```xml
                <Button Command="{Binding PlayPauseCommand}" Width="48" Height="48" Margin="8,0,0,0">
                    <TextBlock Text="{Binding PlayState, Converter={StaticResource PlayStateToIcon}}"
                               FontSize="20" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
```

替换为（用 Style + DataTrigger 决定 Command）：

```xml
                <Button Width="48" Height="48" Margin="8,0,0,0">
                    <Button.Style>
                        <Style TargetType="Button">
                            <!-- 默认：transport 已 Loaded —— 走 PlayerVM 切换播放/暂停 -->
                            <Setter Property="Command" Value="{Binding PlayPauseCommand}"/>
                            <Style.Triggers>
                                <!--
                                    Phase 4：启动后 transport 空闲（CurrentTrack=null）但队列已恢复有当前曲，
                                    跨级到 PlaylistVM.PlayCurrentCommand 触发首次加载 + 播放。
                                    用户首次按 ▶ → PlayCurrentCommand → PlayTrackAtAsync → LoadAsync 触发
                                    TrackChanged(track) → CurrentTrack 变非 null → DataTrigger 失活 → 回到 PlayPauseCommand。
                                    清空队列后 IPlaybackService.Unload() 会触发 TrackChanged(null)，本 trigger 再次激活。
                                -->
                                <DataTrigger Binding="{Binding CurrentTrack}" Value="{x:Null}">
                                    <Setter Property="Command"
                                            Value="{Binding DataContext.Playlist.PlayCurrentCommand,
                                                    RelativeSource={RelativeSource AncestorType=Window}}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                    <TextBlock Text="{Binding PlayState, Converter={StaticResource PlayStateToIcon}}"
                               FontSize="20" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: 启动一次烟雾测试（确认按钮在空队列时禁用、在曲目加载后可用）**

Run: `dotnet run --project UmaPlayer.csproj`

期望：
- 应用正常启动，无异常对话框
- 主窗口显示 PlayerBar；▶ 按钮**禁用**（队列空 → `PlayCurrentCommand.CanExecute=false`）
- 点 📂 选一个文件 → 入队 + 播放 → ▶ 变为 ⏸（已加载，回到 `PlayPauseCommand`）
- 关闭窗口；下一步 commit 之后 Task 9 会做完整 acceptance

- [ ] **Step 4: Commit**

```bash
git add Views/Controls/PlayerBar.xaml
git commit -m "feat(view): PlayerBar ▶ button DataTrigger for null CurrentTrack → PlayCurrent"
```

---

## Task 9：Manual acceptance pass

**Files:** 无代码改动（仅人工验证 + commit 一个空标记）

- [ ] **Step 1: 准备一个干净的 queue.json 起点**

为了让"首次启动"场景可重复，先关闭应用，并删除旧 queue.json（如果存在）：

Run: `rm -f "$LOCALAPPDATA/UmaPlayer/queue.json"`（Git Bash）或在资源管理器中删除 `%LOCALAPPDATA%\UmaPlayer\queue.json`

- [ ] **Step 2: 场景 1 —— 基础队列恢复**

操作：
1. `dotnet run --project UmaPlayer.csproj` 启动
2. 点 [+ 添加] 选 5 首音频文件入队
3. 双击第 3 首 → 应当开始播放，▶ 标记落在第 3 行
4. 关闭窗口

期望（关闭后）：
- `%LOCALAPPDATA%\UmaPlayer\queue.json` 已生成
- 用文本编辑器打开应可读 JSON：`SchemaVersion=1`，`Items` 含 5 条路径（按顺序），`CurrentIndex=2`，`ShuffleEnabled=false`，`RepeatMode="Off"`

操作：
5. 再次 `dotnet run --project UmaPlayer.csproj`

期望（重启后）：
- 列表恢复 5 首
- ▶ 标记画在第 3 首
- PlayerBar 显示 "No track loaded"，位置 0:00 / 0:00
- ▶ 按钮**可用**（PlayCurrentCommand.CanExecute = HasCurrentTrack = true）

- [ ] **Step 3: 场景 2 —— 启动后按 ▶ 触发 PlayCurrentCommand**

接着场景 1 的状态：
1. 在重启后的窗口按 ▶
2. 应当读元数据并开始播放第 3 首
3. PlayerBar 显示标题/封面/Duration；▶ 变为 ⏸（trigger 失活回到 PlayPauseCommand）
4. 再点 ⏸ → 暂停（沿用 PlayerVM.PlayPauseCommand 路径）
5. 再点 ▶ → 继续播放

期望：与 Phase 3 一次双击播放后的行为完全一致；切换时无闪烁/异常。

- [ ] **Step 4: 场景 3 —— Shuffle/Repeat 状态恢复**

操作：
1. 在当前窗口点 🔀 启用 Shuffle，点 ⇄ 把循环切到 🔂 (RepeatOne)
2. 关闭窗口
3. 重启

期望：
- 队列项不变；Shuffle 按钮高亮、Repeat 显示 🔂
- queue.json 中 `"ShuffleEnabled": true, "RepeatMode": "One"`

- [ ] **Step 5: 场景 4 —— 单文件丢失静默跳过**

操作：
1. 关闭窗口（确保 queue.json 含 5 条）
2. 在资源管理器中**删除/移动**这 5 条中的第 2 首音频文件
3. 重启

期望：
- 列表显示 4 首（第 2 首被静默剔除）
- ▶ 标记仍指向"原第 3 首"对应的曲目（向后/向前滑算法）—— 在 4 项列表中位置可能是第 2 行
- 应用未弹错误对话框，未崩溃

- [ ] **Step 6: 场景 5 —— queue.json 损坏 fallback**

操作：
1. 关闭窗口
2. 用文本编辑器打开 `%LOCALAPPDATA%\UmaPlayer\queue.json`，把内容替换为 `X`（任意非 JSON 文本）保存
3. 重启

期望：
- 应用正常启动，列表为空
- Shuffle/Repeat 回到默认（关 / Off）
- 旧 queue.json 文件**仍存在不动**（用户可手工诊断）—— 用 ls 验证：`ls -la "$LOCALAPPDATA/UmaPlayer/queue.json"`
- 关闭窗口后 queue.json 被覆盖为合法的空队列 JSON

- [ ] **Step 7: 场景 6 —— 空队列状态保留**

操作：
1. 启动（接续场景 5 的空状态）
2. 启用 Shuffle、切到 🔁 (RepeatList)
3. 关闭

期望：
- queue.json 中 `Items` 为空数组、`CurrentIndex=-1`、`ShuffleEnabled=true`、`RepeatMode="List"`

操作：
4. 重启

期望：
- 列表仍为空
- Shuffle 按钮高亮、Repeat 显示 🔁

- [ ] **Step 8: 场景 7 —— 关闭时 in-flight 播放**

操作：
1. 用 [+ 添加] 入队 3 首
2. 双击第 1 首开始播放
3. 立刻按 ⏭（触发 PlayTrackAtAsync 第 2 首，期间 await ReadAsync）
4. **在 ⏭ 触发后立即**（< 1 秒，趁元数据读取未完成）按 X 关闭窗口

期望：
- 应用正常关闭，无异常对话框
- queue.json 保留 3 首；CurrentIndex 反映关闭瞬间的状态（可能是 0 或 1，取决于哪一步先完成）
- 重启后队列正确（3 首），不卡死

> 由于 `_playToken` 哨兵 + `Cleanup()` 已解绑 TrackEnded，关闭时 in-flight `PlayTrackAtAsync` 即便 await 完成回到 `_player.LoadAsync`，最坏情况下被新创建的 `WasapiOut` 在 `Window.OnExit` 时 Dispose；不影响 queue.json 的写入（SnapshotState 是同步纯读）。

- [ ] **Step 9: 兼容性回归 —— 与 Phase 3 的 settings.json 共存**

操作：
1. 关闭应用
2. 用文本编辑器打开 `%LOCALAPPDATA%\UmaPlayer\settings.json`
3. 检查 `DefaultVolume` / `WindowLeft` / `WindowTop` / `WindowWidth` / `WindowHeight` 字段都在
4. 重启 → 调整音量到 0.3、移动窗口到屏幕左上、关闭
5. 检查 settings.json 已更新

期望：settings.json 字段未被新增 queue 字段污染；queue.json 的写入与 settings.json 互不影响。

- [ ] **Step 10: 构建零警告确认**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 11: Commit acceptance 标记**

```bash
git commit --allow-empty -m "test: Phase 4 manual acceptance pass

scenarios verified:
1) basic queue restore (5 tracks + CurrentIndex=2)
2) start-up ▶ triggers PlayCurrentCommand path
3) Shuffle/Repeat state persists
4) single-file missing → silent skip + index remap
5) corrupted queue.json → fallback empty, old file preserved
6) empty queue + Shuffle/Repeat persist
7) closing during in-flight PlayTrackAtAsync → no crash, queue intact
9) settings.json untouched by queue persistence (Phase 3 compat)"
```

---

## Task 10：文档更新

**Files:**
- Modify: `docs/PROJECT.md`
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: PROJECT.md 顶部状态块**

定位 `docs/PROJECT.md:1-7`：

```markdown
# UmaPlayer 项目文档

> 一个轻量级、本地优先的 Windows 音乐播放器（WPF + .NET 10 + NAudio）。
>
> 文档日期：2026/06/08 · 对应分支：`master` · 当前阶段：**Phase 3 完成**（VM 拆分 + 技术债清算）

---
```

替换为：

```markdown
# UmaPlayer 项目文档

> 一个轻量级、本地优先的 Windows 音乐播放器（WPF + .NET 10 + NAudio）。
>
> 文档日期：2026/06/12 · 对应分支：`master` · 当前阶段：**Phase 4-1 完成**（队列持久化）

---
```

- [ ] **Step 2: PROJECT.md §1 项目简介**

定位 `docs/PROJECT.md:11`（"## 1. 项目简介" 之后的段落）。

把：

```markdown
**UmaPlayer** 是一款面向 Windows 桌面的本地音乐播放器，灵感来源于 foobar2000 / Winamp。Phase 1 实现单曲播放骨架，Phase 2 加入内存播放队列（多选入队、自动推进、随机/循环模式）。Phase 3 重构 ViewModel 层（按职责拆分 + 抽象元数据读取 + 修正持久化合并纪律），偿还 4 项技术债。可视化、库扫描、多命名播放列表、队列持久化等放在 Phase 4+。
```

替换为：

```markdown
**UmaPlayer** 是一款面向 Windows 桌面的本地音乐播放器，灵感来源于 foobar2000 / Winamp。Phase 1 实现单曲播放骨架，Phase 2 加入内存播放队列（多选入队、自动推进、随机/循环模式）。Phase 3 重构 ViewModel 层（按职责拆分 + 抽象元数据读取 + 修正持久化合并纪律），偿还 4 项技术债。Phase 4-1 加入队列持久化（关闭即存、重启即恢复，路径 + Shuffle/Repeat + 当前曲位置）。可视化、库扫描、多命名播放列表、拖拽等放在 Phase 4+ 后续项。
```

并在 §1.1 表格末尾追加一行：

定位 `docs/PROJECT.md:25`：

```markdown
| 播放列表 | 内存队列：多选入队、单项删除、清空、上/下一首、自然播完自动推进、随机/3 态循环（Off/List/One） |
```

之后追加：

```markdown
| 队列持久化 | 关闭时保存到 `%LocalAppData%\UmaPlayer\queue.json`：路径 + 当前曲位置 + Shuffle + Repeat；启动时恢复（不预加载、不自动播）|
```

- [ ] **Step 3: PROJECT.md §1.2 后续增量**

定位 §1.2 第一行（PROJECT.md `:29-30` 附近）：

```markdown
- 队列持久化（关闭即丢；Phase 4 计划项）
- 多个命名播放列表（创建 / 保存 / 加载 / 切换）—— 当前仅支持单个内存队列
```

替换为（删除"队列持久化"那一项，因为已实现）：

```markdown
- 多个命名播放列表（创建 / 保存 / 加载 / 切换）—— 当前仅支持单个内存队列
```

- [ ] **Step 4: PROJECT.md §3 目录结构追加新文件**

定位 `Models/` 子树（`docs/PROJECT.md:68-72`）：

```markdown
├── Models/
│   ├── Track.cs                 # 不可变 record：音轨信息（含封面字节数组）
│   ├── PlayState.cs             # enum: Stopped / Playing / Paused
│   ├── RepeatMode.cs            # enum: Off / List / One  (Phase 2)
│   └── AudioDeviceInfo.cs       # 预留：设备信息
```

替换为（**追加 QueueState.cs**）：

```markdown
├── Models/
│   ├── Track.cs                 # 不可变 record：音轨信息（含封面字节数组）
│   ├── PlayState.cs             # enum: Stopped / Playing / Paused
│   ├── RepeatMode.cs            # enum: Off / List / One  (Phase 2)
│   ├── QueueState.cs            # 不可变 record：队列持久化快照 (Phase 4)
│   └── AudioDeviceInfo.cs       # 预留：设备信息
```

定位 `Services/` 子树。在 `IAudioOutputFactory` 之前（`docs/PROJECT.md:84-86` 附近）：

```markdown
│   ├── ITrackMetadataReader.cs # 元数据读取抽象 (Phase 3)
│   ├── AtlMetadataReader.cs    # 基于 z440.atl.core 的实现 (Phase 3)
│   ├── IAudioDeviceManager.cs   # 预留：设备枚举/切换
```

替换为（**插入 IQueuePersistence + JsonQueuePersistence**）：

```markdown
│   ├── ITrackMetadataReader.cs # 元数据读取抽象 (Phase 3)
│   ├── AtlMetadataReader.cs    # 基于 z440.atl.core 的实现 (Phase 3)
│   ├── IQueuePersistence.cs    # 队列持久化抽象 (Phase 4)
│   ├── JsonQueuePersistence.cs # JSON 文件实现 (Phase 4)
│   ├── IAudioDeviceManager.cs   # 预留：设备枚举/切换
```

- [ ] **Step 5: PROJECT.md §4.2 服务生命周期表追加 IQueuePersistence**

定位 `docs/PROJECT.md:177-178`（`ISettingsPersistence` 那一行附近）：

```markdown
| `ISettingsPersistence` | Singleton | 内部 `SemaphoreSlim` 并发互斥 |
| `ITrackMetadataReader` | Singleton (Phase 3) | 无状态，封装 z440.atl.core；`ReadAsync` 不抛 |
```

替换为（**在两行之间插入**）：

```markdown
| `ISettingsPersistence` | Singleton | 内部 `SemaphoreSlim` 并发互斥 |
| `IQueuePersistence` | **Singleton** (Phase 4) | 内部 `SemaphoreSlim`；与 settings 持久化独立锁、独立文件 |
| `ITrackMetadataReader` | Singleton (Phase 3) | 无状态，封装 z440.atl.core；`ReadAsync` 不抛 |
```

- [ ] **Step 6: PROJECT.md §4.3 关键设计决策追加第 10 条**

定位 §4.3 末尾（PROJECT.md `:201-203` 附近，第 9 条之后）。在第 9 条 "持久化 read-modify-write 原子化" 块结束后追加：

```markdown
10. **队列持久化（Phase 4）**：新增 `IQueuePersistence` + `JsonQueuePersistence`，平行于 `ISettingsPersistence`，文件 `%LocalAppData%\UmaPlayer\queue.json`。仅存路径 + `CurrentIndex` + `Shuffle` + `RepeatMode`（不存元数据/封面），启动时 `PlaylistViewModel` 在 ctor 同步段读盘并填占位 `Track`，关闭时 `MainWindow.Window_Closing` 调 `Playlist.SnapshotState() → SaveAsync()`。启动后**不预加载**当前曲；`PlayerBar` 的 ▶ 按钮通过 XAML `DataTrigger` 在 `PlayerVM.CurrentTrack==null` 时跨级绑定到新增的 `Playlist.PlayCurrentCommand`，懒加载 + 播放。文件丢失静默跳过；schema 损坏 fallback 空队列。`SchemaVersion=1` 预留未来破坏式升级位。
```

- [ ] **Step 7: PROJECT.md §5.4b 列出新方法/命令**

定位 `docs/PROJECT.md:266-282`（PlaylistViewModel 描述块）。在 `[RelayCommand]` 列表那一行（`:272`）：

```markdown
`[RelayCommand]`：`AddToQueue / RemoveTrack(int) / ClearQueue / PlayTrackAt(int) / NextTrack / PrevTrack / ToggleShuffle / CycleRepeat / OpenAndPlay`。
```

替换为（追加 PlayCurrent）：

```markdown
`[RelayCommand]`：`AddToQueue / RemoveTrack(int) / ClearQueue / PlayTrackAt(int) / NextTrack / PrevTrack / ToggleShuffle / CycleRepeat / OpenAndPlay / PlayCurrent` (Phase 4)。
```

并在该节"关键私有方法"列表（`:276-280`）末尾追加两条：

定位：

```markdown
**关键私有方法**（与旧 MainViewModel 等价）：
- `PlayTrackAtAsync(int, int skipCount=0)`：抢占 `_playToken` → 读元数据 → `Queue[i] = meta` → `LoadAsync` → `Play`；每个 `await` 后校验 token，被顶替则静默退出；失败连续 3 次自动停止
- `CalculateNextIndex(int? failedIndex)`：纯算法，按 (Shuffle × RepeatMode) 4 种组合返回下一索引；Shuffle 用 `_shuffleHistory` 排除已播
- `HandleTrackEnded()`：RepeatOne 重播当前，否则走 `CalculateNextIndex`
- `UnloadCurrentTrack()`：`_playToken++` 顶替 in-flight → `_player.Unload()`（NAudio 服务自动广播 TrackChanged(null) 让 PlayerVM 清屏）
```

替换为（**追加 LoadFromDisk + MapCurrentIndexAfterFilter + SnapshotState**）：

```markdown
**关键私有方法**（与旧 MainViewModel 等价）：
- `PlayTrackAtAsync(int, int skipCount=0)`：抢占 `_playToken` → 读元数据 → `Queue[i] = meta` → `LoadAsync` → `Play`；每个 `await` 后校验 token，被顶替则静默退出；失败连续 3 次自动停止
- `CalculateNextIndex(int? failedIndex)`：纯算法，按 (Shuffle × RepeatMode) 4 种组合返回下一索引；Shuffle 用 `_shuffleHistory` 排除已播
- `HandleTrackEnded()`：RepeatOne 重播当前，否则走 `CalculateNextIndex`
- `UnloadCurrentTrack()`：`_playToken++` 顶替 in-flight → `_player.Unload()`（NAudio 服务自动广播 TrackChanged(null) 让 PlayerVM 清屏）
- **`LoadFromDisk()` (Phase 4)**：构造期同步段调 `IQueuePersistence.LoadAsync().GetAwaiter().GetResult()` → 过滤 `File.Exists` → 通过 `MapCurrentIndexAfterFilter` 重映射 `CurrentIndex` → 逐项 `Queue.Add(metadataReader.CreateFallback(path))` → 写 Shuffle/Repeat
- **`MapCurrentIndexAfterFilter` (Phase 4)**：纯静态函数；原索引仍存在 → 直返；丢失则向后滑找下一个幸存项，无则向前回退；surviving 全空 → -1（外层已提前 return）
- **`SnapshotState()` 公开 (Phase 4)**：仅读、无副作用；由 `MainWindow.Window_Closing` 调用打包 `QueueState`
```

- [ ] **Step 8: PROJECT.md §6.2 运行时持久化追加 queue.json**

定位 `docs/PROJECT.md:322-328`：

```markdown
### 6.2 运行时持久化：`%LocalAppData%\UmaPlayer\settings.json`

由 `JsonSettingsPersistence` 读写，包含与 `AppSettings` 相同的字段；首次启动文件不存在时使用 record 默认值。

**当前被持久化的字段**：`DefaultVolume`、`WindowLeft/Top/Width/Height`。
**已建模但未启用**：`OutputMode`、`PreferredDeviceId`、`LastPlayedPath`。
```

替换为（追加 queue.json 段落）：

```markdown
### 6.2 运行时持久化：`%LocalAppData%\UmaPlayer\settings.json`

由 `JsonSettingsPersistence` 读写，包含与 `AppSettings` 相同的字段；首次启动文件不存在时使用 record 默认值。

**当前被持久化的字段**：`DefaultVolume`、`WindowLeft/Top/Width/Height`。
**已建模但未启用**：`OutputMode`、`PreferredDeviceId`、`LastPlayedPath`。

### 6.3 队列持久化：`%LocalAppData%\UmaPlayer\queue.json` (Phase 4)

由 `JsonQueuePersistence` 读写。文件 schema：

```json
{
  "SchemaVersion": 1,
  "Items": ["C:\\Music\\Album\\01.mp3", "..."],
  "CurrentIndex": 1,
  "ShuffleEnabled": false,
  "RepeatMode": "List"
}
```

**仅持久化路径**（不含元数据/封面）。启动时 PlaylistVM 同步读盘并填占位 Track；用户首次按 ▶ / 双击列表项时由 `PlayTrackAtAsync` 升级为完整 Track。文件丢失静默跳过；JSON 损坏 / `SchemaVersion` 不匹配 fallback 空队列且不删旧文件。
```

- [ ] **Step 9: PROJECT.md §10 历史与参考追加 Phase 4 设计/计划**

定位 `docs/PROJECT.md:415-424`（设计稿与实现计划列表）。

把：

```markdown
- 设计稿：
  - [`docs/superpowers/specs/2026-04-23-uma-player-design.md`](./superpowers/specs/2026-04-23-uma-player-design.md) — Phase 1 整体设计
  - [`docs/superpowers/specs/2026-06-06-uma-player-playlist-design.md`](./superpowers/specs/2026-06-06-uma-player-playlist-design.md) — Phase 2 播放列表设计
  - [`docs/superpowers/specs/2026-06-07-uma-player-phase3-design.md`](./superpowers/specs/2026-06-07-uma-player-phase3-design.md) — Phase 3 VM 拆分 + 技术债清算设计
- 实现计划：
  - [`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — Phase 1
  - [`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`](./superpowers/plans/2026-06-06-uma-player-playlist-implementation.md) — Phase 2
  - [`docs/superpowers/plans/2026-06-07-uma-player-phase3-implementation.md`](./superpowers/plans/2026-06-07-uma-player-phase3-implementation.md) — Phase 3
```

替换为（追加 Phase 4 设计 / 计划）：

```markdown
- 设计稿：
  - [`docs/superpowers/specs/2026-04-23-uma-player-design.md`](./superpowers/specs/2026-04-23-uma-player-design.md) — Phase 1 整体设计
  - [`docs/superpowers/specs/2026-06-06-uma-player-playlist-design.md`](./superpowers/specs/2026-06-06-uma-player-playlist-design.md) — Phase 2 播放列表设计
  - [`docs/superpowers/specs/2026-06-07-uma-player-phase3-design.md`](./superpowers/specs/2026-06-07-uma-player-phase3-design.md) — Phase 3 VM 拆分 + 技术债清算设计
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md`](./superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md) — Phase 4-1 队列持久化设计
- 实现计划：
  - [`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — Phase 1
  - [`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`](./superpowers/plans/2026-06-06-uma-player-playlist-implementation.md) — Phase 2
  - [`docs/superpowers/plans/2026-06-07-uma-player-phase3-implementation.md`](./superpowers/plans/2026-06-07-uma-player-phase3-implementation.md) — Phase 3
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md) — Phase 4-1
```

并在 §10 最后的"主要里程碑提交"列表末尾，Phase 3 块之后追加：

定位文件最末尾（PROJECT.md `:449` 附近）："`70bd36a` merge: Phase 3 tech-debt cleanup" 之后**追加**：

```markdown
  - **Phase 4-1**（feature/phase4-queue-persistence → master, hash TBD by merge commit）
    - `d1765db` docs: add Phase 4 queue persistence design spec
    - `<hash>` feat(models): add QueueState record (Phase 4 schema)
    - `<hash>` feat(services): add IQueuePersistence interface
    - `<hash>` feat(services): add JsonQueuePersistence (queue.json read/write with semaphore)
    - `<hash>` feat(di): register IQueuePersistence singleton
    - `<hash>` feat(vm): inject IQueuePersistence into PlaylistViewModel + LoadFromDisk on ctor
    - `<hash>` feat(vm): add PlaylistViewModel.SnapshotState() + PlayCurrentCommand
    - `<hash>` feat(view): wire MainWindow.Window_Closing to write queue.json
    - `<hash>` feat(view): PlayerBar ▶ button DataTrigger for null CurrentTrack → PlayCurrent
    - `<hash>` test: Phase 4 manual acceptance pass
    - `<hash>` docs: update PROJECT.md and COUPLING.md to Phase 4 state
```

> 实际 hash 在每个 commit 之后用 `git log --oneline -15` 获取并填入。

- [ ] **Step 10: COUPLING.md 顶部状态块**

定位 `docs/COUPLING.md:1-5`：

```markdown
# UmaPlayer 耦合分析与重构备忘

> 创建日期：2026/06/06 · 更新日期：2026/06/08 · 对应分支：`master` · 对应阶段：**Phase 3 完成**（VM 拆分 + 技术债清算）
>
> **本文档的用途：** 不是行动清单，是**风险登记册**。Phase 2 偿还债 #2；Phase 3 偿还债 #3/#4 + 完成 VM 拆分 + View 去硬转型；剩余债与后续工作详见 §6。
```

替换为：

```markdown
# UmaPlayer 耦合分析与重构备忘

> 创建日期：2026/06/06 · 更新日期：2026/06/12 · 对应分支：`master` · 对应阶段：**Phase 4-1 完成**（队列持久化）
>
> **本文档的用途：** 不是行动清单，是**风险登记册**。Phase 2 偿还债 #2；Phase 3 偿还债 #3/#4 + 完成 VM 拆分 + View 去硬转型；Phase 4-1 加入队列持久化。剩余债与后续工作详见 §6。
```

- [ ] **Step 11: COUPLING.md TL;DR 表格**

定位 `docs/COUPLING.md:11-17`：

```markdown
| 维度 | 评级 | 备注 |
|------|------|------|
| 整体耦合度 | **低** | Phase 3 后 MainViewModel 仅 44 行（Strict Facade）；架构按 transport/queue 双 VM 分离 |
| 是否需要立即重构 | ✅ 无 | Phase 3 完成所有结构性改造；下一波是功能增量（队列持久化等） |
| Phase 4 是否会变痛 | ⚠️ **队列持久化要新加 `queue.json`** | settings.json 已隔离窗口/音量，queue 独立文件减小写盘压力 |
| 已识别"待还的债" | 1 项剩余（#1 BitmapImage；#2/#3/#4 ✅ 已偿） | 见 §3 |
| 已识别"过度抽象" | 2 项 | 见 §4 |
```

替换为：

```markdown
| 维度 | 评级 | 备注 |
|------|------|------|
| 整体耦合度 | **低** | Phase 3 后 MainViewModel 仅 44 行（Strict Facade）；架构按 transport/queue 双 VM 分离 |
| 是否需要立即重构 | ✅ 无 | Phase 4-1 沿用既有持久化纪律，未引入结构性变更 |
| Phase 4 后续是否会变痛 | ⚠️ **多命名播放列表 / 拖拽** | 队列持久化已落地；下一波 UI 拓展时需要重新规划 PlaylistView 容器 |
| 已识别"待还的债" | 1 项剩余（#1 BitmapImage；#2/#3/#4 ✅ 已偿） | 见 §3 |
| 已识别"过度抽象" | 2 项 | 见 §4 |
```

- [ ] **Step 12: COUPLING.md §1 当前架构为什么是健康的 追加一行**

定位 `docs/COUPLING.md:23-29`：

```markdown
✅ DI 容器集中注册（`ServiceCollectionExtensions.AddUmaPlayerServices`），无 Service Locator 反模式
✅ 依赖方向正确：`View → VM → Service → Model`，Service 从不反向引用 VM/UI
✅ 所有跨边界依赖**都走接口**：`IPlaybackService` / `IFileDialogService` / `ISettingsPersistence`
✅ 无 `static` 单例、无全局可变状态
✅ Layer 边界清晰（`Models/` / `Services/` / `ViewModels/` / `Views/` 物理隔离）
✅ Phase 2 新增（`PlaylistView` / Phase 2 命令 / 推进算法）全部沿用既有模式，未引入新抽象层
✅ Phase 3 拆分：MainViewModel 收敛为 Strict Facade（44 行）；PlayerVM/PlaylistVM 互不持引用，仅共享 IPlaybackService Singleton；View 跨域命令用 RelativeSource AncestorType=Window 跨级绑定
```

替换为（追加 Phase 4 行）：

```markdown
✅ DI 容器集中注册（`ServiceCollectionExtensions.AddUmaPlayerServices`），无 Service Locator 反模式
✅ 依赖方向正确：`View → VM → Service → Model`，Service 从不反向引用 VM/UI
✅ 所有跨边界依赖**都走接口**：`IPlaybackService` / `IFileDialogService` / `ISettingsPersistence` / `IQueuePersistence`
✅ 无 `static` 单例、无全局可变状态
✅ Layer 边界清晰（`Models/` / `Services/` / `ViewModels/` / `Views/` 物理隔离）
✅ Phase 2 新增（`PlaylistView` / Phase 2 命令 / 推进算法）全部沿用既有模式，未引入新抽象层
✅ Phase 3 拆分：MainViewModel 收敛为 Strict Facade（44 行）；PlayerVM/PlaylistVM 互不持引用，仅共享 IPlaybackService Singleton；View 跨域命令用 RelativeSource AncestorType=Window 跨级绑定
✅ Phase 4-1 队列持久化复用 settings 持久化纪律：独立文件、独立 SemaphoreSlim、catch-all fallback；PlaylistVM ctor 同步段读盘 + Window_Closing 异步写盘单点写者；未引入新抽象层
```

- [ ] **Step 13: COUPLING.md §2 依赖图追加 IQueuePersistence**

定位 `docs/COUPLING.md:42-43`：

```markdown
| `MainViewModel` (Facade) | `PlayerViewModel`, `PlaylistViewModel`, `IPlaybackService` | — |
| `PlayerViewModel` | `IPlaybackService`, `ISettingsPersistence`, `IOptions<AppSettings>` | ⚠️ `BitmapImage`（WPF，债 #1） |
| `PlaylistViewModel` | `IPlaybackService`, `IFileDialogService`, `ITrackMetadataReader` | `Track`、`RepeatMode` |
| `MainWindow` | `MainViewModel`, `ISettingsPersistence` | `Window`, `SystemParameters` |
```

替换为（**给 PlaylistViewModel 加 `IQueuePersistence`、给 MainWindow 加 `IQueuePersistence`、新加 `JsonQueuePersistence` 行**）：

```markdown
| `MainViewModel` (Facade) | `PlayerViewModel`, `PlaylistViewModel`, `IPlaybackService` | — |
| `PlayerViewModel` | `IPlaybackService`, `ISettingsPersistence`, `IOptions<AppSettings>` | ⚠️ `BitmapImage`（WPF，债 #1） |
| `PlaylistViewModel` | `IPlaybackService`, `IFileDialogService`, `ITrackMetadataReader`, `IQueuePersistence` (Phase 4) | `Track`、`RepeatMode`、`QueueState` |
| `MainWindow` | `MainViewModel`, `ISettingsPersistence`, `IQueuePersistence` (Phase 4) | `Window`, `SystemParameters` |
```

并定位 `docs/COUPLING.md:51`：

```markdown
| `AtlMetadataReader` | `ITrackMetadataReader` | `ATL.Track`（封装隔离） |
```

之后追加：

```markdown
| `JsonQueuePersistence` | `IQueuePersistence` (Phase 4) | `File`, `JsonSerializer`, `Environment.SpecialFolder` |
```

- [ ] **Step 14: COUPLING.md §5 隐式契约追加 Phase 4 项**

定位 `docs/COUPLING.md:184-189`（Phase 3 新增契约块结尾，最后那一项 `IPlaybackService.TrackChanged` 携带 `Track?`）。在 Phase 3 块结束后**追加**：

```markdown
| **Phase 4 新增** | | |
| `JsonQueuePersistence.LoadAsync` 必须 catch-all 静默 fallback | 注释 + 类 XML 注释 | 任何 IO/JSON 异常逃出会让 PlaylistVM ctor 抛 → MainViewModel 解析失败 → 应用启动崩溃 |
| `PlaylistViewModel.LoadFromDisk` 是构造同步段，不可 await | 注释 | DI 容器构造 VM 时若死锁 UI sync ctx 会让窗口永不显示；用 `.ConfigureAwait(false)` + `GetAwaiter().GetResult()` 模式（依赖 JsonQueuePersistence 内部 ConfigureAwait(false)） |
| `PlaylistViewModel.SnapshotState` 仅读不副作用 | 注释 | 若未来加副作用，`Window_Closing` 在 CleanupAsync 之后调它会让人意外（且 Cleanup 已解绑 TrackEnded） |
| `MainWindow.Window_Closing` 中 queue 写盘紧跟 settings 写盘后串行执行 | 注释 | 顺序不影响正确性，但便于诊断"哪步失败"；并发也意味着写盘失败诊断变难 |
| PlayerBar ▶ 按钮在 `PlayerVM.CurrentTrack==null` 时跨级绑到 `Playlist.PlayCurrentCommand` | XAML DataTrigger 注释 | TrackChanged(null) 触发后 trigger 重新激活；任何让 `CurrentTrack` 短暂为 null 的逻辑都会让按钮闪一下命令变化（Phase 4-1 行为可接受） |
```

- [ ] **Step 15: COUPLING.md §6 启动检查清单勾选第 5 项**

定位 `docs/COUPLING.md:204-208`：

```markdown
4. ✅ **解决 settings 合并纪律**（Phase 3 完成，commit `fadae44`）—— `UpdateAsync(Func<>)` 把读-改-写封进锁内
5. ☐ **队列持久化** —— `%LocalAppData%\UmaPlayer\queue.json`；考虑与 settings.json 分离以减小写盘压力
6. ☐ **多命名播放列表（L3）** —— 真正的"播放列表管理"；UI 侧需引入 Tab 或侧栏
7. ☐ **拖拽支持** —— 外部文件拖入入队 + 队列内拖拽重排序
```

替换为（**勾选 5；保留 6/7**）：

```markdown
4. ✅ **解决 settings 合并纪律**（Phase 3 完成，commit `fadae44`）—— `UpdateAsync(Func<>)` 把读-改-写封进锁内
5. ✅ **队列持久化**（Phase 4-1 完成）—— `%LocalAppData%\UmaPlayer\queue.json`；独立 `IQueuePersistence` + `SemaphoreSlim`，关闭一次性写、启动同步读
6. ☐ **多命名播放列表（L3）** —— 真正的"播放列表管理"；UI 侧需引入 Tab 或侧栏
7. ☐ **拖拽支持** —— 外部文件拖入入队 + 队列内拖拽重排序
```

并把第二行 "Phase 3 实际工作量：" 之后的"Phase 4+ 候选范围预估" 段（`docs/COUPLING.md:209-211`）：

```markdown
**Phase 3 实际工作量：** 14 commits + 3 个搭车修复 ≈ 一个工作日（subagent-driven，单 session 完成）

**Phase 4+ 候选范围预估：** 队列持久化 ~3h；拖拽支持 ~4h；多命名播放列表 ~10h+。
```

替换为：

```markdown
**Phase 3 实际工作量：** 14 commits + 3 个搭车修复 ≈ 一个工作日（subagent-driven，单 session 完成）

**Phase 4-1 实际工作量：** 10 commits ≈ 半个工作日（无新抽象层、复用 settings 纪律）

**Phase 4+ 候选范围预估：** 拖拽支持 ~4h；多命名播放列表 ~10h+。
```

- [ ] **Step 16: 构建验证（仅文档变更，仅检查项目仍可构建）**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 17: Commit**

```bash
git add docs/PROJECT.md docs/COUPLING.md
git commit -m "docs: update PROJECT.md and COUPLING.md to Phase 4 state"
```

- [ ] **Step 18: 回填 Phase 4 commit hashes 到 PROJECT.md（可选搭车修）**

Run: `git log --oneline -15`

把 Step 9 中 `<hash>` 占位逐条替换为实际 7 位短 hash。然后：

```bash
git add docs/PROJECT.md
git commit -m "docs: backfill Phase 4 commit hashes in PROJECT.md"
```

> 若不想多一个 commit，可在 Step 17 之前先做这一步，把 PROJECT.md 一次写齐再提交。两种风格皆可，与 Phase 3 文档纪律一致。

---

## 完成后建议

- [ ] 运行 `git log --oneline master..HEAD` 检查分支：应有 11 个 commit（spec 1 + 实现 8 + manual acceptance 1 + docs 1，可能 +1 hash backfill）
- [ ] 切回 master fast-forward 合并：`git checkout master && git merge --ff-only feature/phase4-queue-persistence`，或开 PR
- [ ] 删除 feature 分支：`git branch -d feature/phase4-queue-persistence`

---

## Self-Review 结果

执行 plan 自审清单：

**1. Spec 覆盖：**

| Spec 章节 / 需求 | 实现位置 |
|------------------|----------|
| §3.1 `Models/QueueState.cs` 新文件 | Task 1 |
| §3.2 queue.json 文件格式（带 SchemaVersion / 字符串 RepeatMode） | Task 1 + Task 3 (`JsonStringEnumConverter`) |
| §4.1 `IQueuePersistence` 接口 | Task 2 |
| §4.2 `JsonQueuePersistence` 实现（`SemaphoreSlim` / catch-all / `WriteIndented`） | Task 3 |
| §4.3 DI 注册（紧跟 settings） | Task 4 |
| §5.1 PlaylistVM ctor 同步加载 + `LoadFromDisk` + `MapCurrentIndexAfterFilter` | Task 5 |
| §5.2 `PlayCurrentCommand` 可执行性绑定 | Task 6 |
| §5.3 `SnapshotState()` 公开方法 | Task 6 |
| §5.4 PlayerBar ▶ 按钮 DataTrigger | Task 8 |
| §6.1 MainWindow ctor + Closing 写盘顺序 | Task 7 |
| §6.2 App.xaml.cs DI 解析顺序 | Task 7 |
| §7.1 错误处理矩阵（首次启动 / JSON 损坏 / 单文件丢失等） | Task 3（catch-all）+ Task 5（File.Exists 过滤）+ Task 9 场景验证 |
| §7.5 新隐式契约写入 COUPLING.md | Task 10 Step 14 |
| §7.7 7 个 manual acceptance 场景 | Task 9 Step 2-8 |
| §8.2 提交分组（10 个 commit） | Task 1-10 各自 commit message 与 §8.2 完全一致 |

无 spec 需求遗漏。

**2. Placeholder 扫描：** 全文搜索 "TBD" / "TODO" / "fill in" —— Task 10 Step 9 中 PROJECT.md 的 commit hash 占位 `<hash>` 是**预期的**（在实际执行 Task 10 时由 `git log` 获取后回填，Step 18 显式说明）。其余无占位。

**3. 类型一致性：** 全文检查：
- `QueueState` 字段名（`SchemaVersion / Items / CurrentIndex / ShuffleEnabled / RepeatMode`）→ Task 1 / 3 / 5 / 6 / 9 完全一致
- `IQueuePersistence` 方法（`LoadAsync()` / `SaveAsync(QueueState)`）→ 全文一致
- `PlayCurrentCommand` 命名 → Task 6 / 8 / 10 全部使用同一名（CommunityToolkit.Mvvm 规则：方法 `PlayCurrent` → 生成 `PlayCurrentCommand`）
- `MapCurrentIndexAfterFilter` 签名（`IReadOnlyList<string>, IReadOnlyList<string>, int`）→ Task 5 内部一致
- `SnapshotState()` 返回 `QueueState` → Task 6 / 7 / 10 一致

无类型/命名漂移。
