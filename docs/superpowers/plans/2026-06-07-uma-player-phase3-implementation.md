# UmaPlayer Phase 3 技术债清算 — 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不引入任何用户可见功能的前提下，清算 Phase 1/2 累积的 4 项技术债：拆分 643 行的 `MainViewModel`、抽出 `ITrackMetadataReader`、把 settings 持久化改为锁内 read-modify-write、消除 View ↔ VM 硬转型。

**Architecture:** 严格 Facade 模式。`MainViewModel` 缩成 ~40 行只暴露 `Player`/`Playlist` 两个子 VM 和 `CleanupAsync`；两个子 VM 互不持引用，通过 `IPlaybackService` 共享状态变更（PlayerVM 订阅状态事件，PlaylistVM 订阅 `TrackEnded`）。`OpenAndPlayCommand` 归 PlaylistVM，PlayerBar 用 `RelativeSource AncestorType=Window` 跨级绑定调用。`ISettingsPersistence.SaveAsync` 替换为 `UpdateAsync(Func<AppSettings, AppSettings>)`，所有 read-modify-write 进入 `SemaphoreSlim` 临界区。

**Tech Stack:** .NET 10 / WPF, CommunityToolkit.Mvvm 8.x（源生成器：`[ObservableProperty]`、`[RelayCommand]`、`partial void On{Prop}Changed`）, Microsoft.Extensions.DependencyInjection, NAudio 2.2.x, z440.atl.core。Phase 3 **不引入**测试基础设施（xUnit/Moq），验证方式为 `dotnet build` + 手动 smoke + 重跑 Phase 2 验收清单。

**Verification approach:** 由于本仓库目前无自动化测试，Phase 3 同样不引入。每个任务的"测试"步骤为：(1) `dotnet build` 零 error/零 warning；(2) 在需要时按步骤进行手动 smoke。最终 Task 14 重跑 Phase 2 验收清单。

**Branch:** 在 `feature/phase3-tech-debt` 分支上工作（Task 1 创建）。

---

## File Structure

**新增文件（4 个）：**
- `Services/ITrackMetadataReader.cs` (~15 行) — 元数据读取接口：`ReadAsync` + `CreateFallback`
- `Services/AtlMetadataReader.cs` (~60 行) — ATL 实现，包揽 try/catch 与 fallback，绝不抛
- `ViewModels/PlayerViewModel.cs` (~150 行) — Transport 状态 + 命令 + 设置写入
- `ViewModels/PlaylistViewModel.cs` (~350 行) — 队列 + Shuffle/Repeat + 推进算法 + `OpenAndPlay`

**修改文件（8 个）：**
- `Services/ISettingsPersistence.cs` — `SaveAsync` → `UpdateAsync(Func<>)`
- `Services/JsonSettingsPersistence.cs` — 实现重写，锁内 read-modify-write
- `ViewModels/MainViewModel.cs` — **整体重写**为 ~40 行 Facade
- `Extensions/ServiceCollectionExtensions.cs` — 加 3 行（`ITrackMetadataReader` + 两个子 VM）
- `Views/MainWindow.xaml` — `<PlayerBar DataContext="{Binding Player}"/>` + `<PlaylistView DataContext="{Binding Playlist}"/>`
- `Views/MainWindow.xaml.cs` — `SaveAsync` 调用改 `UpdateAsync`
- `Views/Controls/PlayerBar.xaml` — 📂 按钮跨级绑定
- `Views/Controls/PlayerBar.xaml.cs` — `as MainViewModel` → `as PlayerViewModel`
- `Views/Controls/PlaylistView.xaml.cs` — `as MainViewModel` → `as PlaylistViewModel`

**不动文件：** `Models/*`、`Themes/*`、`appsettings.json`、`App.xaml.cs`、`NAudioPlaybackService.cs`、`Win32FileDialogService.cs`、`Configuration/AppSettings.cs`、所有 `Converters/*`、`PlaylistView.xaml`、`PlayerBar.xaml` 中 📂 以外的绑定。

---

## Task 1: 创建工作分支

**Files:**
- 无文件改动

- [ ] **Step 1: 确认起点干净**

Run: `git status -sb`
Expected: 只看到 `## master` 和 untracked 项（`.idea/`、`.superpowers/`），无 modified/staged 文件。

- [ ] **Step 2: 拉取 / 确认与远端同步**

Run: `git log --oneline -1`
Expected: 顶部是 `485c137 docs: add Phase 3 tech-debt cleanup design spec`

- [ ] **Step 3: 创建并切换到 feature 分支**

```bash
git checkout -b feature/phase3-tech-debt
```

Expected: `Switched to a new branch 'feature/phase3-tech-debt'`

- [ ] **Step 4: 验证分支已切换**

Run: `git branch --show-current`
Expected: `feature/phase3-tech-debt`

---

## Task 2: 抽出 `ITrackMetadataReader` 接口

**Files:**
- Create: `Services/ITrackMetadataReader.cs`

- [ ] **Step 1: 创建接口文件**

Write the following to `D:\CodingProjects\UmaPlayer\Services\ITrackMetadataReader.cs`:

```csharp
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 音频元数据读取抽象 —— 把"从文件读 Title/Artist/封面"的关注点从 ViewModel 解耦出来。
///
/// 设计契约：
///   - ReadAsync 永远不抛：文件损坏、读不出有效音频 → 返回 CreateFallback 的结果
///   - CreateFallback 同步、无 IO：仅 FilePath + Title（来自文件名），其他字段为 null/空
///     用于队列入队时立刻占位，等用户实际播放时再调 ReadAsync 回填完整元数据
/// </summary>
public interface ITrackMetadataReader
{
    /// <summary>异步读元数据。读失败返回 fallback Track（绝不抛）。</summary>
    Task<Track> ReadAsync(string filePath);

    /// <summary>同步 fallback：仅文件名作为 Title，其它字段为空。用于入队时占位。</summary>
    Track CreateFallback(string filePath);
}
```

- [ ] **Step 2: 编译确认接口语法正确（尚无实现，会因 DI 缺注册不报错——本步只验语法）**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)。
（接口暂无实现引用，编译器不会报错）

- [ ] **Step 3: 提交**

```bash
git add Services/ITrackMetadataReader.cs
git commit -m "feat(services): add ITrackMetadataReader interface"
```

---

## Task 3: 实现 `AtlMetadataReader`

**Files:**
- Create: `Services/AtlMetadataReader.cs`

- [ ] **Step 1: 创建实现文件**

Write the following to `D:\CodingProjects\UmaPlayer\Services\AtlMetadataReader.cs`:

```csharp
using System.IO;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 基于 z440.atl.core 的元数据读取实现。
///
/// 责任搬迁：从 MainViewModel 的 ReadTrackMetadataAsync / CreateFallbackTrack 整体迁来。
/// 行为不变；唯一差异是 ReadAsync 现在保证不抛（之前 catch 在调用方 PlayTrackAtAsync 兜底）。
/// </summary>
public sealed class AtlMetadataReader : ITrackMetadataReader
{
    /// <summary>
    /// 通过 z440.atl.core 读取音频标签（ID3、Vorbis Comment、APE 等）。
    /// 文件损坏或读不出有效音频时回落到仅含文件名的 fallback Track。
    /// 在后台线程执行，避免大文件首次解析卡 UI。
    /// </summary>
    public Task<Track> ReadAsync(string filePath)
    {
        return Task.Run<Track>(() =>
        {
            try
            {
                var atlTrack = new ATL.Track(filePath);

                // DurationMs == 0 通常意味着没有解析到有效音频数据 → 走 fallback
                if (atlTrack.DurationMs <= 0)
                    return CreateFallback(filePath);

                var title = !string.IsNullOrWhiteSpace(atlTrack.Title)
                    ? atlTrack.Title
                    : Path.GetFileNameWithoutExtension(filePath);

                // 只取第一张内嵌封面（多数情况下只有一张）
                var albumArt = atlTrack.EmbeddedPictures.Count > 0
                    ? atlTrack.EmbeddedPictures[0].PictureData
                    : null;

                return new Track(
                    FilePath: filePath,
                    Title: title,
                    Artist: atlTrack.Artist,
                    Album: atlTrack.Album,
                    Genre: atlTrack.Genre,
                    Year: atlTrack.Year > 0 ? atlTrack.Year : null,
                    SampleRate: atlTrack.SampleRate > 0 ? (int?)atlTrack.SampleRate : null,
                    AlbumArt: albumArt,
                    Duration: TimeSpan.Zero); // Duration 由播放服务加载完成后回填
            }
            catch
            {
                return CreateFallback(filePath);
            }
        });
    }

    /// <summary>退化版 Track：仅含文件路径与文件名作为标题。同步、无 IO。</summary>
    public Track CreateFallback(string filePath)
    {
        return new Track(
            FilePath: filePath,
            Title: Path.GetFileNameWithoutExtension(filePath),
            Artist: null,
            Album: null,
            Genre: null,
            Year: null,
            SampleRate: null,
            AlbumArt: null,
            Duration: TimeSpan.Zero);
    }
}
```

- [ ] **Step 2: 编译**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)。

- [ ] **Step 3: 提交**

```bash
git add Services/AtlMetadataReader.cs
git commit -m "feat(services): add AtlMetadataReader (ATL-based metadata reader)"
```

---

## Task 4: DI 注册 `ITrackMetadataReader`

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: 在 Stub 服务行之后、ViewModel 行之前插入注册**

Edit `D:\CodingProjects\UmaPlayer\Extensions\ServiceCollectionExtensions.cs`:

替换：
```csharp
        // 输出工厂（Transient —— 每次调用都新建一个 IWavePlayer，
        // 由 IPlaybackService 负责释放生命周期）
        services.AddTransient<IAudioOutputFactory, StubAudioOutputFactory>();

        // ViewModel（Transient —— 主窗口持有实例，关闭即释放）
        services.AddTransient<MainViewModel>();
```

为：
```csharp
        // 输出工厂（Transient —— 每次调用都新建一个 IWavePlayer，
        // 由 IPlaybackService 负责释放生命周期）
        services.AddTransient<IAudioOutputFactory, StubAudioOutputFactory>();

        // 元数据读取（Singleton —— 无状态、纯函数式接口）
        services.AddSingleton<ITrackMetadataReader, AtlMetadataReader>();

        // ViewModel（Transient —— 主窗口持有实例，关闭即释放）
        services.AddTransient<MainViewModel>();
```

- [ ] **Step 2: 编译**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)。

- [ ] **Step 3: 提交**

```bash
git add Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(di): register ITrackMetadataReader"
```

---

## Task 5: `MainViewModel` 改用 `ITrackMetadataReader`（最小切换，先不拆 VM）

**目的：** 把元数据读取从 VM 私有方法切到接口调用，让 Task 9（拆 PlaylistViewModel）能直接搬接口字段。本任务结束后 VM 仍 ~640 行，但 `new ATL.Track` 已不在 VM 中。

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: 注入 `ITrackMetadataReader` 到构造器**

Edit `D:\CodingProjects\UmaPlayer\ViewModels\MainViewModel.cs`:

替换字段块（约第 30-34 行）：
```csharp
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ISettingsPersistence _persistence;
    private readonly IOptions<AppSettings> _options;
    private AppSettings _settings;
```

为：
```csharp
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ISettingsPersistence _persistence;
    private readonly IOptions<AppSettings> _options;
    private readonly ITrackMetadataReader _metadataReader;
    private AppSettings _settings;
```

替换构造器签名与赋值（约第 393-403 行）：
```csharp
    public MainViewModel(
        IPlaybackService player,
        IFileDialogService fileDialog,
        ISettingsPersistence persistence,
        IOptions<AppSettings> options)
    {
        _player = player;
        _fileDialog = fileDialog;
        _persistence = persistence;
        _options = options;
        _settings = options.Value;
```

为：
```csharp
    public MainViewModel(
        IPlaybackService player,
        IFileDialogService fileDialog,
        ISettingsPersistence persistence,
        IOptions<AppSettings> options,
        ITrackMetadataReader metadataReader)
    {
        _player = player;
        _fileDialog = fileDialog;
        _persistence = persistence;
        _options = options;
        _metadataReader = metadataReader;
        _settings = options.Value;
```

- [ ] **Step 2: 把队列内 `CreateFallbackTrack(path)` 调用切到接口**

在 `AddToQueue`（约第 252 行）中：
```csharp
            Queue.Add(CreateFallbackTrack(path));
```
改为：
```csharp
            Queue.Add(_metadataReader.CreateFallback(path));
```

在 `OpenFilesAsync`（约第 525 行）中：
```csharp
            Queue.Add(CreateFallbackTrack(path));
```
改为：
```csharp
            Queue.Add(_metadataReader.CreateFallback(path));
```

- [ ] **Step 3: 把 `PlayTrackAtAsync` 的元数据读切到接口**

在 `PlayTrackAtAsync`（约第 214 行）中：
```csharp
            var meta = await ReadTrackMetadataAsync(Queue[index].FilePath);
```
改为：
```csharp
            var meta = await _metadataReader.ReadAsync(Queue[index].FilePath);
```

- [ ] **Step 4: 删除 VM 中已无人调用的两个 static 方法**

删除整个 `ReadTrackMetadataAsync` 方法（约第 537-574 行）和整个 `CreateFallbackTrack` 方法（约第 577-589 行）。

删除后这两块代码应消失：
```csharp
    private static Task<Track> ReadTrackMetadataAsync(string filePath) { ... }
    private static Track CreateFallbackTrack(string filePath) { ... }
```

- [ ] **Step 5: 删除现已无用的 `using System.IO;`（如已无 IO 用途）和 `using System.Linq;` 仍在用则保留**

检查文件开头 `using` —— 删除 `ReadTrackMetadataAsync` 和 `CreateFallbackTrack` 后，`System.IO` 仍被 `CreateAlbumArtImage` 中的 `MemoryStream` 使用，**保留**。

无需改动 using 块。

- [ ] **Step 6: 编译**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)。

- [ ] **Step 7: 手动 smoke**

启动应用：
```bash
dotnet run
```

操作清单（按顺序）：
1. 点击 📂 → 选 1 个 MP3 → 应自动开始播放，封面/标题/艺术家显示正确
2. 关闭应用

Expected: 元数据读取行为与重构前完全一致。

- [ ] **Step 8: 提交**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "refactor(vm): switch MainViewModel to ITrackMetadataReader

Remove inline ATL.Track usage from VM; metadata read now goes through
injected ITrackMetadataReader. VM file size unchanged; preparing for
later PlaylistViewModel split which will own the reader field."
```

---

## Task 6: 改造 `ISettingsPersistence` 接口

**目的：** 用 `UpdateAsync(Func<AppSettings, AppSettings>)` 替换 `SaveAsync`，把"读-改-写"封进实现层的临界区。本任务包含接口、实现、和**两处调用点**的同步迁移，必须一次性完成才能编译通过。

**Files:**
- Modify: `Services/ISettingsPersistence.cs`
- Modify: `Services/JsonSettingsPersistence.cs`
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `Views/MainWindow.xaml.cs`

- [ ] **Step 1: 改写接口定义**

Edit `D:\CodingProjects\UmaPlayer\Services\ISettingsPersistence.cs` —— 替换整个文件为：

```csharp
using UmaPlayer.Configuration;

namespace UmaPlayer.Services;

/// <summary>
/// 应用配置的运行时持久化抽象。
///
/// 与 IOptions&lt;AppSettings&gt; 的分工：
///   - IOptions：启动只读快照（来自 appsettings.json，跟随程序分发）
///   - 本接口  ：运行时可变状态（窗口尺寸、音量等），写入用户数据目录
///
/// 并发模型：实现层用 SemaphoreSlim 把"读盘 → mutator → 写盘"封进同一临界区，
/// 调用方不再持有 AppSettings 的内存副本（避免"VM 写音量、Window 写尺寸时
/// VM 的旧副本把 Window 的尺寸覆盖回去"这类 race）。
/// </summary>
public interface ISettingsPersistence
{
    /// <summary>读取磁盘上的当前设置。文件不存在则返回 record 默认值。</summary>
    Task<AppSettings> LoadAsync();

    /// <summary>
    /// 原子读-改-写：实现内读最新磁盘版本 → 应用 mutator → 写回，
    /// 全程在同一 SemaphoreSlim 获取期内完成。
    ///
    /// 契约：
    ///   - mutator 必须是纯函数（无 IO 副作用）。它在锁内执行，副作用会阻塞其他更新
    ///   - 读盘失败（损坏 / 不存在 / 权限）时，把 new AppSettings() 作为 mutator 输入
    ///     ——与 LoadAsync 的失败回落语义一致。写盘失败仍会抛出
    /// </summary>
    Task UpdateAsync(Func<AppSettings, AppSettings> mutator);
}
```

- [ ] **Step 2: 改写 JSON 实现**

Edit `D:\CodingProjects\UmaPlayer\Services\JsonSettingsPersistence.cs` —— 替换整个文件为：

```csharp
using System.IO;
using System.Text.Json;
using UmaPlayer.Configuration;

namespace UmaPlayer.Services;

/// <summary>
/// 将 AppSettings 序列化到 %LocalAppData%\UmaPlayer\settings.json。
///
/// 并发控制：用 SemaphoreSlim(1,1) 把整个"读盘 → mutator → 写盘"封进临界区，
/// 调用方只需提供 mutator (s => s with { Field = newValue })，
/// 字段合并由实现保证。
/// </summary>
public sealed class JsonSettingsPersistence : ISettingsPersistence
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonSettingsPersistence()
    {
        // 使用 LocalApplicationData 而非 ApplicationData：本机配置不漫游，避免多机互覆盖
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "UmaPlayer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    /// <summary>文件不存在则返回 record 默认值（首次启动场景）。</summary>
    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_path))
            return new AppSettings();

        await _lock.WaitAsync();
        try
        {
            return await ReadFromDiskNoLockAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 锁内 read-modify-write。读盘失败 → 把 new AppSettings() 喂给 mutator
    /// （与 LoadAsync 失败回落语义一致），写盘失败照样抛出（关闭流程调用方自行 catch）。
    /// </summary>
    public async Task UpdateAsync(Func<AppSettings, AppSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        await _lock.WaitAsync();
        try
        {
            AppSettings current;
            try
            {
                current = await ReadFromDiskNoLockAsync().ConfigureAwait(false);
            }
            catch
            {
                // 读失败（文件损坏 / 权限）→ 以默认值为起点，让 mutator 仍可应用变更
                current = new AppSettings();
            }

            var next = mutator(current);

            var json = JsonSerializer.Serialize(next, new JsonSerializerOptions
            {
                WriteIndented = true // 人类可读，方便手动调试
            });
            await File.WriteAllTextAsync(_path, json).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>不获取锁的读 —— 调用方负责持锁。文件不存在返回默认值。</summary>
    private async Task<AppSettings> ReadFromDiskNoLockAsync()
    {
        if (!File.Exists(_path))
            return new AppSettings();

        var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}
```

- [ ] **Step 3: 改写 `MainViewModel.OnVolumeChanged` 与 `CleanupAsync`**

Edit `D:\CodingProjects\UmaPlayer\ViewModels\MainViewModel.cs`:

替换 `OnVolumeChanged`（约第 615-626 行）：
```csharp
    partial void OnVolumeChanged(float value)
    {
        _player.Volume = value;
        if (_isInitializing) return; // 跳过初始化期间的写盘

        // 静音时直接拖动滑块到非零 → 自动解除静音状态
        if (IsMuted && value > 0f)
            IsMuted = false;

        _settings = _settings with { DefaultVolume = value };
        _ = _persistence.SaveAsync(_settings); // fire-and-forget；下次写覆盖前者
    }
```

为：
```csharp
    partial void OnVolumeChanged(float value)
    {
        _player.Volume = value;
        if (_isInitializing) return; // 跳过初始化期间的写盘

        // 静音时直接拖动滑块到非零 → 自动解除静音状态
        if (IsMuted && value > 0f)
            IsMuted = false;

        // 锁内 read-modify-write：DefaultVolume 改这个，其他字段（含 Window* 几何）保留磁盘最新值
        _ = _persistence.UpdateAsync(s => s with { DefaultVolume = value });
    }
```

替换 `CleanupAsync`（约第 632-642 行）：
```csharp
    public async Task CleanupAsync()
    {
        _player.PositionChanged -= HandlePositionChanged;
        _player.StateChanged -= HandleStateChanged;
        _player.DurationChanged -= HandleDurationChanged;
        _player.TrackChanged -= HandleTrackChanged;
        _player.PlaybackError -= HandlePlaybackError;
        _player.TrackEnded -= HandleTrackEnded;
        _player.Dispose();
        await _persistence.SaveAsync(_settings);
    }
```

为：
```csharp
    public async Task CleanupAsync()
    {
        _player.PositionChanged -= HandlePositionChanged;
        _player.StateChanged -= HandleStateChanged;
        _player.DurationChanged -= HandleDurationChanged;
        _player.TrackChanged -= HandleTrackChanged;
        _player.PlaybackError -= HandlePlaybackError;
        _player.TrackEnded -= HandleTrackEnded;
        _player.Dispose();
        // 兜底写一次音量（OnVolumeChanged 已 fire-and-forget；此处确保最后一次拖动被持久化）
        await _persistence.UpdateAsync(s => s with { DefaultVolume = Volume });
    }
```

- [ ] **Step 4: 改写 `MainWindow.Window_Closing`**

Edit `D:\CodingProjects\UmaPlayer\Views\MainWindow.xaml.cs`:

替换 `Window_Closing` 方法体（约第 50-66 行）：
```csharp
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            // 先读再写，保留 VM 写入的其他字段（如 DefaultVolume）
            var settings = await _persistence.LoadAsync();
            settings = settings with
            {
                WindowLeft = Left, WindowTop = Top,
                WindowWidth = Width, WindowHeight = ActualHeight
            };
            await _persistence.SaveAsync(settings);
        }
        catch { /* 关闭流程不打扰用户 */ }

        await _vm.CleanupAsync();
    }
```

为：
```csharp
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            // 锁内 read-modify-write：仅改窗口几何，DefaultVolume 等其他字段保留磁盘最新值
            await _persistence.UpdateAsync(s => s with
            {
                WindowLeft = Left, WindowTop = Top,
                WindowWidth = Width, WindowHeight = ActualHeight
            });
        }
        catch { /* 关闭流程不打扰用户 */ }

        await _vm.CleanupAsync();
    }
```

- [ ] **Step 5: 编译**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)。

如果出现 `SaveAsync` 仍被引用的错误：仓库内 `grep -r "SaveAsync"` 应为 0 结果。

- [ ] **Step 6: 验证 grep**

Run: `grep -r "SaveAsync" Services/ ViewModels/ Views/`
Expected: 无输出（0 行）。

- [ ] **Step 7: 手动 smoke（验证合并纪律）**

启动应用：
```bash
dotnet run
```

操作清单（**关键**：验证音量与窗口尺寸互不覆盖）：
1. 启动后拖动音量滑块到 0.3 → 关闭应用
2. 重新启动 → 音量应为 0.3
3. 调整窗口大小到 ~1000x800 → 拖动音量到 0.7 → 关闭
4. 重启 → **音量 0.7 且窗口大小 ~1000x800 都保留**（这是 race 修复点）

Expected: 步骤 4 两个字段都生效。

- [ ] **Step 8: 提交**

```bash
git add Services/ISettingsPersistence.cs Services/JsonSettingsPersistence.cs \
        ViewModels/MainViewModel.cs Views/MainWindow.xaml.cs
git commit -m "refactor(settings): replace SaveAsync with UpdateAsync(Func<>)

Read-modify-write is now atomic inside the persistence layer's SemaphoreSlim.
Callers no longer hold an AppSettings snapshot. Closes coupling debt #3
(settings.json double-writer race)."
```

---

## Task 7: 创建 `PlayerViewModel`（仅 Transport 半）

**目的：** 把 `MainViewModel` 中"transport + 当前曲信息 + 音量持久化"的部分搬到独立的 `PlayerViewModel`。本任务结束后 `MainViewModel` 仍保留这些代码（双份），下一任务才删除——这是为了保证每步可编译运行。

**Files:**
- Create: `ViewModels/PlayerViewModel.cs`

- [ ] **Step 1: 创建 PlayerViewModel 文件**

Write the following to `D:\CodingProjects\UmaPlayer\ViewModels\PlayerViewModel.cs`:

```csharp
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using UmaPlayer.Configuration;
using UmaPlayer.Models;
using UmaPlayer.Services;

namespace UmaPlayer.ViewModels;

/// <summary>
/// 播放器 ViewModel —— 负责 transport 状态（播放/暂停/位置/音量）和当前曲信息展示。
///
/// 与 PlaylistViewModel 的边界：
///   - 本 VM 订阅 IPlaybackService 的 Position/Duration/State/Track/Error 事件
///   - PlaylistViewModel 单独订阅 TrackEnded 推进队列
///   - 两个 VM 互不持引用；通过 IPlaybackService 单例共享底层状态
///
/// 注：BitmapImage 在此 VM 中暂时保留（COUPLING.md 债 #1，Phase 4 单测前再还）。
/// </summary>
public partial class PlayerViewModel : ObservableObject
{
    private readonly IPlaybackService _player;
    private readonly ISettingsPersistence _persistence;
    private readonly IOptions<AppSettings> _options;

    // 标记构造期间：避免 OnVolumeChanged 在初始化时写磁盘
    private bool _isInitializing = true;

    /// <summary>拖动进度条时为 true —— 抑制 PositionChanged 回写，避免滑块被服务"拽回"。</summary>
    [ObservableProperty]
    private bool _isSeeking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionNormalized))]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private PlayState _playState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SampleRateText))]
    private Track? _currentTrack;

    [ObservableProperty]
    private BitmapImage? _albumArtImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIcon))]
    private float _volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIcon))]
    private bool _isMuted;

    /// <summary>静音前的音量快照，用于"取消静音"时恢复。</summary>
    private float _volumeBeforeMute;

    // —— 派生只读属性，供 XAML 绑定 ——

    public string VolumeIcon => IsMuted ? "\U0001F507" : "\U0001F50A"; // 🔇 / 🔊

    public string SampleRateText =>
        CurrentTrack?.SampleRate is { } sr ? $"{sr:N0} Hz" : "";

    /// <summary>进度条用归一化 [0,1] 值；Duration 为 0 时返回 0 防止除零。</summary>
    public double PositionNormalized =>
        Duration.TotalSeconds > 0 ? Position.TotalSeconds / Duration.TotalSeconds : 0;

    public PlayerViewModel(
        IPlaybackService player,
        ISettingsPersistence persistence,
        IOptions<AppSettings> options)
    {
        _player = player;
        _persistence = persistence;
        _options = options;

        // 订阅播放服务事件 —— 所有事件已由服务封送到 UI 线程，handler 可直接更新属性
        _player.PositionChanged += HandlePositionChanged;
        _player.StateChanged += HandleStateChanged;
        _player.DurationChanged += HandleDurationChanged;
        _player.TrackChanged += HandleTrackChanged;
        _player.PlaybackError += HandlePlaybackError;

        Initialize();
    }

    /// <summary>从持久化加载音量；失败回落到 appsettings.json 默认值。</summary>
    private void Initialize()
    {
        AppSettings settings;
        try
        {
            // 构造期同步阻塞读盘（小文件、毫秒级），避免 async 构造器复杂度
            settings = _persistence.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            settings = _options.Value;
        }
        Volume = settings.DefaultVolume;
        _isInitializing = false;
    }

    // —— 播放服务事件 handler ——

    private void HandlePositionChanged(TimeSpan position)
    {
        // 拖动时不更新 Position，否则用户拖到的位置会被服务每 33ms 覆盖回去
        if (!IsSeeking)
            Position = position;
    }

    private void HandleDurationChanged(TimeSpan duration)
        => Duration = duration;

    private void HandleTrackChanged(Track track)
    {
        CurrentTrack = track;
        AlbumArtImage = CreateAlbumArtImage(track.AlbumArt);
    }

    private void HandleStateChanged(PlayState state)
        => PlayState = state;

    private void HandlePlaybackError(string error)
    {
        // TODO: 首期暂不处理；后续可弹 toast 或写日志
    }

    // —— UI 命令 ——

    /// <summary>用户开始拖动进度条 thumb 时由 View code-behind 调用。</summary>
    [RelayCommand]
    private void SeekStarted() => IsSeeking = true;

    /// <summary>拖动完成 / 单击跳转 —— 把归一化位置 [0,1] 转回 TimeSpan 并通知播放服务。</summary>
    [RelayCommand]
    private void SeekCompleted(double normalized)
    {
        IsSeeking = false;
        var target = TimeSpan.FromSeconds(normalized * Duration.TotalSeconds);
        _player.Seek(target);
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (PlayState == PlayState.Playing)
            _player.Pause();
        else
            _player.Play();
    }

    [RelayCommand]
    private void Stop() => _player.Stop();

    /// <summary>切换静音：静音时记忆当前音量，恢复时还原。</summary>
    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            Volume = _volumeBeforeMute;
        }
        else
        {
            _volumeBeforeMute = Volume;
            IsMuted = true;
            Volume = 0f;
        }
    }

    /// <summary>
    /// 音量变化钩子（源生成器自动调用）：
    ///   1) 同步到播放服务  2) 拖滑块时若处于静音则自动取消静音  3) 锁内持久化到磁盘
    /// </summary>
    partial void OnVolumeChanged(float value)
    {
        _player.Volume = value;
        if (_isInitializing) return; // 跳过初始化期间的写盘

        // 静音时直接拖动滑块到非零 → 自动解除静音状态
        if (IsMuted && value > 0f)
            IsMuted = false;

        _ = _persistence.UpdateAsync(s => s with { DefaultVolume = value });
    }

    /// <summary>
    /// 由 MainViewModel.CleanupAsync 调用 —— 解绑事件并持久化最后一次音量。
    /// 注意：不在这里 Dispose IPlaybackService（PlaylistViewModel 还在用，
    /// Facade 层统一 Dispose）。
    /// </summary>
    public async Task CleanupAsync()
    {
        _player.PositionChanged -= HandlePositionChanged;
        _player.StateChanged -= HandleStateChanged;
        _player.DurationChanged -= HandleDurationChanged;
        _player.TrackChanged -= HandleTrackChanged;
        _player.PlaybackError -= HandlePlaybackError;
        await _persistence.UpdateAsync(s => s with { DefaultVolume = Volume });
    }

    /// <summary>
    /// 从字节数组创建可跨线程使用的 BitmapImage：
    ///   - DecodePixelWidth=200：解码时即缩放，省内存（封面渲染区只有 80px）
    ///   - Freeze()：冻结后可被任意线程读取，且 WPF 渲染更高效
    /// </summary>
    private static BitmapImage? CreateAlbumArtImage(byte[]? data)
    {
        if (data is not { Length: > 0 }) return null;

        var image = new BitmapImage();
        using var ms = new MemoryStream(data);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // 一次性把流读入内存，立即释放 MemoryStream
        image.StreamSource = ms;
        image.DecodePixelWidth = 200;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
```

- [ ] **Step 2: 编译（PlayerViewModel 应能独立编译）**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)。
（此时 MainViewModel 中的双份代码还在，但因为 DI 还没解析 PlayerViewModel，无运行时冲突。）

- [ ] **Step 3: 提交**

```bash
git add ViewModels/PlayerViewModel.cs
git commit -m "feat(vm): add PlayerViewModel (transport half of split)

Carved out the transport-side responsibilities from MainViewModel:
position/duration/state/volume/mute + their commands + playback service
event handlers. MainViewModel still holds duplicates; Task 9 will swap
to facade and remove them."
```

---

## Task 8: 创建 `PlaylistViewModel`（队列 + 推进 + OpenAndPlay）

**Files:**
- Create: `ViewModels/PlaylistViewModel.cs`

- [ ] **Step 1: 创建 PlaylistViewModel 文件**

Write the following to `D:\CodingProjects\UmaPlayer\ViewModels\PlaylistViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmaPlayer.Models;
using UmaPlayer.Services;

namespace UmaPlayer.ViewModels;

/// <summary>
/// 播放队列 ViewModel —— 负责队列状态、Shuffle/Repeat、自动推进算法、跨域的 OpenAndPlay。
///
/// 与 PlayerViewModel 的边界：
///   - 本 VM 订阅 IPlaybackService.TrackEnded 触发推进；不订阅 transport 事件
///   - 不持有 PlayerViewModel 引用；通过 IPlaybackService 调用 LoadAsync/Play/Unload/Stop
///
/// 跨域命令：OpenAndPlay（PlayerBar 的 📂 按钮）归本 VM——
/// 因为它本质是"批量入队 + 自动播首项"，前者属于队列域，后者只是结果。
/// PlayerBar 通过 RelativeSource AncestorType=Window 跨级访问。
/// </summary>
public partial class PlaylistViewModel : ObservableObject
{
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ITrackMetadataReader _metadataReader;

    /// <summary>当前播放队列。ObservableCollection 自动通知 UI 增删改。</summary>
    public ObservableCollection<Track> Queue { get; } = new();

    /// <summary>当前播放曲在 Queue 中的索引；-1 表示未选/队列空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTrack))]
    private int _currentIndex = -1;

    /// <summary>UI 列表选中项（与"当前播放曲"无关，仅供 Delete 键定位）。</summary>
    [ObservableProperty]
    private Track? _selectedTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShuffleBrushKey))]
    private bool _shuffleEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatActive))]
    private RepeatMode _repeatMode = RepeatMode.Off;

    /// <summary>随机模式下"已播过"的索引集合。切换 ShuffleEnabled 或清空队列时重置。</summary>
    private readonly HashSet<int> _shuffleHistory = new();

    /// <summary>用于 Shuffle 模式随机选曲；构造一次复用。</summary>
    private readonly Random _random = new();

    /// <summary>
    /// PlayTrackAtAsync 重入哨兵：每次入口自增；await 完成后若 token 不匹配，则丢弃本次结果。
    /// 防止用户连续 Next 时多个 LoadAsync 互相覆盖 NAudio 资源。
    /// </summary>
    private int _playToken;

    // —— 派生属性 ——

    /// <summary>循环按钮是否处于"激活"状态（List 或 One 都算）。</summary>
    public bool RepeatActive => RepeatMode != RepeatMode.Off;

    /// <summary>暴露给 XAML 的 Shuffle 高亮指示（直接绑 ShuffleEnabled 即可，留作语义清晰）。</summary>
    public bool ShuffleBrushKey => ShuffleEnabled;

    /// <summary>当前是否有正在播放的曲（用于 Next/Prev 按钮 CanExecute）。</summary>
    public bool HasCurrentTrack => CurrentIndex >= 0 && CurrentIndex < Queue.Count;

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

    /// <summary>
    /// 计算下一首曲目的索引。
    /// </summary>
    /// <param name="failedIndex">本轮已确认播放失败的索引，候选集合需排除它（防止无限循环）。</param>
    /// <returns>下一首索引；-1 表示无下一首（队列空或循环关闭已到底）。</returns>
    /// <remarks>
    /// 注意：RepeatOne 的"重播当前"逻辑不在本方法处理，由调用方
    /// (HandleTrackEnded) 直接返回 CurrentIndex。本方法只处理 Shuffle/顺序 × Repeat 组合。
    /// </remarks>
    private int CalculateNextIndex(int? failedIndex = null)
    {
        if (Queue.Count == 0) return -1;

        if (ShuffleEnabled)
        {
            // 候选 = 所有索引 - 已播过 - 失败过
            var candidates = Enumerable.Range(0, Queue.Count)
                .Where(i => !_shuffleHistory.Contains(i) && i != failedIndex)
                .ToList();

            if (candidates.Count == 0)
            {
                // 全部播过 → 视循环模式决定
                if (RepeatMode == RepeatMode.List)
                {
                    _shuffleHistory.Clear();
                    candidates = Enumerable.Range(0, Queue.Count)
                        .Where(i => i != failedIndex)
                        .ToList();
                    if (candidates.Count == 0) return -1;
                }
                else
                {
                    return -1; // RepeatOff/One 且 Shuffle 已耗尽 → 停
                }
            }

            return candidates[_random.Next(candidates.Count)];
        }
        else
        {
            // 顺序模式
            var next = CurrentIndex + 1;
            if (next < Queue.Count) return next;
            return RepeatMode == RepeatMode.List ? 0 : -1;
        }
    }

    /// <summary>
    /// 计算上一首索引。Shuffle 模式下不维护历史栈（MVP 简化）, 直接退到 0 或 Count-1。
    /// </summary>
    private int CalculatePrevIndex()
    {
        if (Queue.Count == 0) return -1;

        if (ShuffleEnabled)
        {
            // MVP: Shuffle 下 Prev 不回溯历史, 简单退到 0；后续可加历史栈
            return CurrentIndex > 0 ? CurrentIndex - 1 : 0;
        }
        else
        {
            var prev = CurrentIndex - 1;
            if (prev >= 0) return prev;
            return RepeatMode == RepeatMode.List ? Queue.Count - 1 : -1;
        }
    }

    /// <summary>
    /// 播放指定索引的曲目。失败时尝试跳过到下一首，最多连跳 3 次防无限循环。
    /// 使用 _playToken 哨兵防止重入：若 await 期间用户触发了新一轮播放，旧调用会静默退出。
    /// </summary>
    private async Task PlayTrackAtAsync(int index, int skipCount = 0)
    {
        if (index < 0 || index >= Queue.Count)
        {
            _player.Stop();
            CurrentIndex = -1;
            return;
        }

        if (skipCount >= 3)
        {
            // 连续 3 个文件失败 → 停止，避免无限错误循环
            _player.Stop();
            CurrentIndex = -1;
            return;
        }

        // 抢占 token：之后任何 await 若发现 token 已变，说明被新调用顶替，立即放弃
        int myToken = ++_playToken;

        CurrentIndex = index;
        _shuffleHistory.Add(index); // 不管是否 Shuffle 都登记，便于切换时无缝

        try
        {
            // 读元数据并回写到 Queue[index]（占位 Track → 完整 Track）
            var meta = await _metadataReader.ReadAsync(Queue[index].FilePath);
            if (myToken != _playToken) return; // 被顶替, 静默退出
            Queue[index] = meta; // ObservableCollection.set[i] 触发 Replace, UI 自动刷新

            await _player.LoadAsync(meta);
            if (myToken != _playToken) return; // 被顶替, 不再 Play
            _player.Play();
        }
        catch
        {
            if (myToken != _playToken) return; // 被顶替, 不再 fallback
            // 文件损坏 / 不存在 → 跳过到下一首（reader 已不会抛，此 catch 兜底 LoadAsync 异常）
            var failed = index;
            var next = CalculateNextIndex(failedIndex: failed);
            if (next == -1 || next == failed)
            {
                _player.Stop();
                CurrentIndex = -1;
                return;
            }
            await PlayTrackAtAsync(next, skipCount + 1);
        }
    }

    /// <summary>
    /// 停止播放并清空 PlayerBar 上与"当前曲"相关的所有 VM 状态。
    /// 必须用 _player.Unload() 而不是 Stop() —— 后者保留底层 reader, 用户再点 Play 会重播刚才那首。
    /// 同时令 _playToken 自增，使任何 in-flight 的 PlayTrackAtAsync 被顶替丢弃。
    ///
    /// 注：本方法只清"队列侧"的状态（CurrentIndex）；PlayerViewModel 的
    /// CurrentTrack/Position/Duration/AlbumArtImage 由 IPlaybackService.Unload()
    /// 引发的事件链路自动清零。
    /// </summary>
    private void UnloadCurrentTrack()
    {
        _playToken++; // 顶替任何 in-flight 的播放调用
        _player.Unload();
        CurrentIndex = -1;
    }

    // —— Phase 2 命令 ——

    /// <summary>文件对话框多选 → 入队（不读元数据，仅占位）。</summary>
    [RelayCommand]
    private void AddToQueue()
    {
        var files = _fileDialog.OpenFiles(
            "Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav",
            multiselect: true);
        if (files.Count == 0) return;

        foreach (var path in files)
        {
            // 轻量占位 Track：仅文件名作为 Title，其他字段为空
            Queue.Add(_metadataReader.CreateFallback(path));
        }
    }

    /// <summary>按索引移除单项；若是当前播放曲则停止播放并同步索引。</summary>
    [RelayCommand]
    private void RemoveTrack(int index)
    {
        if (index < 0 || index >= Queue.Count) return;

        bool isCurrent = (index == CurrentIndex);
        Queue.RemoveAt(index);

        // 修正 CurrentIndex
        if (isCurrent)
        {
            UnloadCurrentTrack();
        }
        else if (index < CurrentIndex)
        {
            CurrentIndex--; // 当前曲位置前的项被删，当前曲索引下移 1
        }

        // 修正 _shuffleHistory：
        // 1) 删除该索引本身  2) 大于该索引的全部 -1
        var rebuilt = new HashSet<int>();
        foreach (var i in _shuffleHistory)
        {
            if (i == index) continue;
            rebuilt.Add(i > index ? i - 1 : i);
        }
        _shuffleHistory.Clear();
        foreach (var i in rebuilt) _shuffleHistory.Add(i);
    }

    /// <summary>清空整个队列 → 停止播放，重置索引和历史。</summary>
    [RelayCommand]
    private void ClearQueue()
    {
        UnloadCurrentTrack();
        Queue.Clear();
        _shuffleHistory.Clear();
    }

    /// <summary>双击列表项 → 播放该索引曲目。重置 shuffleHistory（视为新会话）。</summary>
    [RelayCommand]
    private async Task PlayTrackAt(int index)
    {
        _shuffleHistory.Clear();
        await PlayTrackAtAsync(index);
    }

    /// <summary>下一首按钮（用户手动）。RepeatOne 下也跳走，不重播当前。</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentTrack))]
    private async Task NextTrack()
    {
        var next = CalculateNextIndex();
        if (next == -1) return;
        await PlayTrackAtAsync(next);
    }

    /// <summary>上一首按钮。</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentTrack))]
    private async Task PrevTrack()
    {
        var prev = CalculatePrevIndex();
        if (prev == -1) return;
        await PlayTrackAtAsync(prev);
    }

    /// <summary>切换 Shuffle 开关。同时清空已播过历史（避免状态语义混乱）。</summary>
    [RelayCommand]
    private void ToggleShuffle()
    {
        ShuffleEnabled = !ShuffleEnabled;
        _shuffleHistory.Clear();
        if (CurrentIndex >= 0) _shuffleHistory.Add(CurrentIndex); // 当前曲不应再被随机选中
    }

    /// <summary>循环模式三态循环：Off → List → One → Off。</summary>
    [RelayCommand]
    private void CycleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off  => RepeatMode.List,
            RepeatMode.List => RepeatMode.One,
            _               => RepeatMode.Off,
        };
    }

    /// <summary>
    /// PlayerBar 上的 📂 按钮：选文件 → 全部入队 → 从第一首新加入的开始播。
    /// 与 [+ 添加] 区别：本命令会立即触发播放。
    ///
    /// 跨域归属说明：本质是"批量入队 + 自动播首项"，前者属于队列域，后者只是结果。
    /// 故归本 VM；PlayerBar 通过 RelativeSource AncestorType=Window 跨级绑定调用。
    /// </summary>
    [RelayCommand]
    private async Task OpenAndPlay()
    {
        var files = _fileDialog.OpenFiles(
            "Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav",
            multiselect: true);
        if (files.Count == 0) return;

        int firstNewIndex = Queue.Count;
        foreach (var path in files)
        {
            Queue.Add(_metadataReader.CreateFallback(path));
        }

        _shuffleHistory.Clear();
        await PlayTrackAtAsync(firstNewIndex);
    }

    /// <summary>
    /// IPlaybackService.TrackEnded 订阅：根据循环/随机模式自动推进。
    /// </summary>
    private async void HandleTrackEnded()
    {
        // 单曲循环：仅在自动播完时重播当前
        if (RepeatMode == RepeatMode.One && CurrentIndex >= 0)
        {
            await PlayTrackAtAsync(CurrentIndex);
            return;
        }

        var next = CalculateNextIndex();
        if (next == -1)
        {
            // 列表播完且不循环 → 维持 Stopped, 当前索引保留以便用户重新点击 Play
            return;
        }
        await PlayTrackAtAsync(next);
    }

    /// <summary>由 MainViewModel.CleanupAsync 调用 —— 解绑 TrackEnded 订阅。</summary>
    public void Cleanup()
    {
        _player.TrackEnded -= HandleTrackEnded;
    }
}
```

- [ ] **Step 2: 编译**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)。
（PlaylistViewModel 暂未被任何东西引用，但应能独立编译。）

- [ ] **Step 3: 提交**

```bash
git add ViewModels/PlaylistViewModel.cs
git commit -m "feat(vm): add PlaylistViewModel (queue half of split)

Carved out the queue-side responsibilities from MainViewModel:
Queue/CurrentIndex/Shuffle/Repeat + advancement algorithm + all queue
commands + OpenAndPlay (cross-domain, owned here per spec §2.1).
MainViewModel still holds duplicates; Task 9 will swap to facade."
```

---

## Task 9: 把 `MainViewModel` 改写为 Facade，注册子 VM

**目的：** 删掉 `MainViewModel` 中所有 transport / 队列 / 命令代码，留下 ~40 行的 Facade。同时在 DI 中注册两个子 VM。本任务结束后 View 仍硬转型到 `MainViewModel`，会编译失败 —— Task 10、11 修复 View。**这是最大的一步，必须一次性完成。**

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: 先在 DI 中注册两个子 VM**

Edit `D:\CodingProjects\UmaPlayer\Extensions\ServiceCollectionExtensions.cs`:

替换：
```csharp
        // ViewModel（Transient —— 主窗口持有实例，关闭即释放）
        services.AddTransient<MainViewModel>();
```

为：
```csharp
        // ViewModel（Transient —— 主窗口持有实例，关闭即释放）
        services.AddTransient<PlayerViewModel>();
        services.AddTransient<PlaylistViewModel>();
        services.AddTransient<MainViewModel>();
```

- [ ] **Step 2: 整体重写 MainViewModel.cs**

Replace the entire contents of `D:\CodingProjects\UmaPlayer\ViewModels\MainViewModel.cs` with:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using UmaPlayer.Services;

namespace UmaPlayer.ViewModels;

/// <summary>
/// 主窗口的 ViewModel —— 严格 Facade：仅暴露两个子 VM 和窗口关闭时的清理入口。
///
/// 拆分历史：
///   - Phase 1/2 期间本类直接承载所有 transport + 队列状态（峰值 643 行）
///   - Phase 3 拆为 PlayerViewModel + PlaylistViewModel；本类降为 ~40 行
///
/// 边界约束（COUPLING.md §4.2 新不变量）：
///   - 两个子 VM 互不持引用，仅共享 IPlaybackService Singleton
///   - 本类不暴露除 Player/Playlist/CleanupAsync 外的任何成员（违反则倒退为转发 Facade）
/// </summary>
public sealed class MainViewModel
{
    private readonly IPlaybackService _player;

    public PlayerViewModel Player { get; }
    public PlaylistViewModel Playlist { get; }

    public MainViewModel(
        PlayerViewModel player,
        PlaylistViewModel playlist,
        IPlaybackService playbackService)
    {
        Player = player;
        Playlist = playlist;
        _player = playbackService;
    }

    /// <summary>
    /// 窗口关闭时由 MainWindow.Window_Closing 调用 —— 解绑两个子 VM 的事件、
    /// 释放底层播放服务。子 VM 内部不 Dispose IPlaybackService（共享 Singleton），
    /// 由 Facade 在此统一 Dispose。
    /// </summary>
    public async Task CleanupAsync()
    {
        Playlist.Cleanup();      // 同步：仅解绑 TrackEnded
        await Player.CleanupAsync(); // 异步：解绑事件 + 写最后一次音量
        _player.Dispose();
    }
}
```

- [ ] **Step 3: 编译（预期 View 层会报错）**

Run: `dotnet build`
Expected: **预期失败**。错误应集中在：
- `PlayerBar.xaml.cs`: `MainViewModel` 没有 `SeekStartedCommand`、`SeekCompletedCommand`
- `PlaylistView.xaml.cs`: `MainViewModel` 没有 `PropertyChanged`（已不是 `ObservableObject`）、`Queue`、`PlayTrackAtCommand`、`RemoveTrackCommand`、`CurrentIndex`

这是预期的；Task 10、11 修复。**本步骤无需提交**（先把 View 改好一起提交，避免引入"编译失败的提交"）。

---

## Task 10: 修复 `PlayerBar` —— 切到 `PlayerViewModel` + 跨级绑定 OpenAndPlay

**Files:**
- Modify: `Views/Controls/PlayerBar.xaml`
- Modify: `Views/Controls/PlayerBar.xaml.cs`
- Modify: `Views/MainWindow.xaml`

- [ ] **Step 1: 改 MainWindow.xaml 把 PlayerBar 的 DataContext 指向 Player 子 VM**

Edit `D:\CodingProjects\UmaPlayer\Views\MainWindow.xaml`:

替换：
```xml
        <controls:PlayerBar    Grid.Row="0" DataContext="{Binding}"/>
        <controls:PlaylistView Grid.Row="1" DataContext="{Binding}"/>
```

为：
```xml
        <controls:PlayerBar    Grid.Row="0" DataContext="{Binding Player}"/>
        <controls:PlaylistView Grid.Row="1" DataContext="{Binding Playlist}"/>
```

- [ ] **Step 2: 改 PlayerBar.xaml 的 📂 按钮为跨级绑定**

Edit `D:\CodingProjects\UmaPlayer\Views\Controls\PlayerBar.xaml`:

替换（约第 94-96 行）：
```xml
                <Button Command="{Binding OpenFilesCommand}" Width="48" Height="48" Margin="8,0,0,0">
                    <TextBlock Text="&#x1F4C2;" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
```

为：
```xml
                <Button Command="{Binding DataContext.Playlist.OpenAndPlayCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                        Width="48" Height="48" Margin="8,0,0,0">
                    <TextBlock Text="&#x1F4C2;" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
```

- [ ] **Step 3: 改 PlayerBar.xaml.cs 把硬转型从 `MainViewModel` 换成 `PlayerViewModel`**

Edit `D:\CodingProjects\UmaPlayer\Views\Controls\PlayerBar.xaml.cs`:

替换三处 `(DataContext as MainViewModel)`（在 `SeekBar_PreviewMouseLeftButtonDown`、`SeekBar_DragStarted`、`SeekBar_DragCompleted` 方法中）：

```csharp
        (DataContext as MainViewModel)?.SeekCompletedCommand.Execute(ratio);
```
为：
```csharp
        (DataContext as PlayerViewModel)?.SeekCompletedCommand.Execute(ratio);
```

```csharp
    private void SeekBar_DragStarted(object sender, DragStartedEventArgs e)
        => (DataContext as MainViewModel)?.SeekStartedCommand.Execute(null);
```
为：
```csharp
    private void SeekBar_DragStarted(object sender, DragStartedEventArgs e)
        => (DataContext as PlayerViewModel)?.SeekStartedCommand.Execute(null);
```

```csharp
    private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
        => (DataContext as MainViewModel)?.SeekCompletedCommand.Execute(SeekBar.Value);
```
为：
```csharp
    private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
        => (DataContext as PlayerViewModel)?.SeekCompletedCommand.Execute(SeekBar.Value);
```

可以用 `replace_all` 一次替换：
- old: `DataContext as MainViewModel`
- new: `DataContext as PlayerViewModel`

- [ ] **Step 4: 编译（PlayerBar 已修，PlaylistView 仍报错）**

Run: `dotnet build`
Expected: 错误集中在 `PlaylistView.xaml.cs`（`MainViewModel.PropertyChanged` / `Queue` / `PlayTrackAtCommand` / `RemoveTrackCommand` / `CurrentIndex` 找不到）。其他文件已干净。

---

## Task 11: 修复 `PlaylistView` —— 切到 `PlaylistViewModel`

**Files:**
- Modify: `Views/Controls/PlaylistView.xaml.cs`

- [ ] **Step 1: 把字段、订阅、转发都从 `MainViewModel` 换成 `PlaylistViewModel`**

Edit `D:\CodingProjects\UmaPlayer\Views\Controls\PlaylistView.xaml.cs`:

替换字段（约第 21 行）：
```csharp
    private MainViewModel? _vm;
```
为：
```csharp
    private PlaylistViewModel? _vm;
```

替换 `OnDataContextChanged` 中的转型（约第 47 行）：
```csharp
        _vm = e.NewValue as MainViewModel;
```
为：
```csharp
        _vm = e.NewValue as PlaylistViewModel;
```

替换 `OnVmPropertyChanged` 中的类型引用（约第 60 行）：
```csharp
        if (e.PropertyName == nameof(MainViewModel.CurrentIndex))
            RefreshCurrentIndicator();
```
为：
```csharp
        if (e.PropertyName == nameof(PlaylistViewModel.CurrentIndex))
            RefreshCurrentIndicator();
```

`OnQueueChanged`、`RefreshCurrentIndicator`、`QueueList_MouseDoubleClick`、`QueueList_KeyDown`、`RemoveButton_Click` 内部用到的 `_vm.Queue`、`_vm.CurrentIndex`、`_vm.PlayTrackAtCommand`、`_vm.RemoveTrackCommand` 字段名都不变（`PlaylistViewModel` 上同名存在），无需改动。

- [ ] **Step 2: 编译**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)。

- [ ] **Step 3: 验证关键 grep**

Run: `grep -rn "as MainViewModel" Views/`
Expected: 无输出（0 行）。

Run: `grep -rn "MainViewModel\." ViewModels/ Views/ Extensions/`
Expected: 只剩 DI 注册和构造器类型引用，**绝无属性 / 命令访问**。

- [ ] **Step 4: 手动 smoke（完整回归 Phase 2 主路径）**

启动应用：
```bash
dotnet run
```

按顺序操作：
1. 点击 📂 → 选 2 个 MP3 → 第一首自动播放
2. 双击队列中第二首 → 应切换播放
3. 拖动音量滑块 → 音量变化
4. 拖动进度条 → 跳转
5. 点 ⏭ → 下一首
6. 点 ⏮ → 上一首
7. 切换 🔀 / 🔁 按钮 → 高亮切换正确
8. 关闭

Expected: 全部功能行为与 Phase 2 一致。

- [ ] **Step 5: 提交（覆盖 Task 9、10、11 的完整改动）**

```bash
git add ViewModels/MainViewModel.cs ViewModels/PlayerViewModel.cs ViewModels/PlaylistViewModel.cs \
        Extensions/ServiceCollectionExtensions.cs \
        Views/MainWindow.xaml Views/Controls/PlayerBar.xaml Views/Controls/PlayerBar.xaml.cs \
        Views/Controls/PlaylistView.xaml.cs
git commit -m "refactor(vm): split MainViewModel into Player/Playlist facade

- MainViewModel: now a strict facade (~40 lines), exposes Player/Playlist + CleanupAsync
- PlayerBar: DataContext={Binding Player}; 📂 button uses cross-level binding to Playlist.OpenAndPlayCommand
- PlaylistView: DataContext={Binding Playlist}; code-behind hard-casts to PlaylistViewModel
- DI: registers PlayerViewModel + PlaylistViewModel as Transient

Closes coupling debts #1 (VM bloat) and #2 (View hard-cast)."
```

---

## Task 12: 删除 `MainViewModel.cs` 旧代码遗留的死代码（无操作核对）

**目的：** Task 9 整体重写 `MainViewModel.cs` 时已完整替换文件内容；本任务用 grep 核对没有遗漏。

**Files:**
- 仅查询，不修改

- [ ] **Step 1: 验证 `new ATL.Track` 仅出现在 AtlMetadataReader**

Run: `grep -rn "new ATL.Track" .`
Expected: 仅匹配 `Services/AtlMetadataReader.cs` 中的一行。

- [ ] **Step 2: 验证 `SaveAsync(` 已无任何调用**

Run: `grep -rn "SaveAsync(" Services/ ViewModels/ Views/`
Expected: 无输出（0 行）。

- [ ] **Step 3: 验证 `MainViewModel.cs` 文件行数 ≤ 50**

Run: `wc -l ViewModels/MainViewModel.cs`
Expected: 行数 ≤ 50（spec §1.3 成功标准）。

- [ ] **Step 4: 验证仓库内 `as MainViewModel` 为 0**

Run: `grep -rn "as MainViewModel" .`
Expected: 仅可能匹配 docs/（历史文档），不应有任何 `.cs` 文件命中。

如果以上四项有任何一项不符合预期，**回到对应任务排查**。如果都通过，本任务无提交。

---

## Task 13: 文档微更新

**目的：** 在 `docs/COUPLING.md` 顶部标注 Phase 3 已偿还的债，让下次 brainstorming 看到正确的"风险登记册"快照。

**Files:**
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: 在 TL;DR 表格中标记债 #1/#2/#3/#4 已偿**

Edit `D:\CodingProjects\UmaPlayer\docs\COUPLING.md`:

在 §3 的债务标题上加 ✅ 后缀：
- `### 债 #1 — VM 持有 BitmapImage（WPF 类型泄漏）` → 保持不变（Phase 3 没动这个）
- `### 债 #2 — IPlaybackService 缺"自然播完"信号 ✅ 已偿` → 已有，不动
- `### 债 #3 — settings.json 双写者无合并纪律` → 改为 `### 债 #3 — settings.json 双写者无合并纪律 ✅ 已偿（Phase 3）`
- `### 债 #4 — 元数据读取硬编码 ATL.Track` → 改为 `### 债 #4 — 元数据读取硬编码 ATL.Track ✅ 已偿（Phase 3）`

具体步骤：

替换：
```markdown
### 债 #3 — settings.json 双写者无合并纪律
```
为：
```markdown
### 债 #3 — settings.json 双写者无合并纪律 ✅ 已偿（Phase 3）
```

替换：
```markdown
### 债 #4 — 元数据读取硬编码 `ATL.Track`
```
为：
```markdown
### 债 #4 — 元数据读取硬编码 `ATL.Track` ✅ 已偿（Phase 3）
```

并在 §6 Phase 3 启动检查清单的标题下加一行注释（在 "> Phase 2..." 注释行后追加）：
```markdown
> **更新（Phase 3 完成）：** 项 1 (VM 拆分)、2 (View 去硬转型)、3 (ITrackMetadataReader)、4 (settings 合并纪律) 已完成。后续 Phase 4+ 仍待办：5、6、7。
```

- [ ] **Step 2: 提交**

```bash
git add docs/COUPLING.md
git commit -m "docs(coupling): mark debts #3 and #4 as paid (Phase 3)"
```

---

## Task 14: 完整回归 — 重跑 Phase 2 验收清单

**目的：** Phase 3 不改用户可见行为；通过 Phase 2 plan 的验收清单是"行为零回归"的判定线。

**Files:**
- 无文件改动；记录结果用

- [ ] **Step 1: 启动应用**

```bash
dotnet run
```

- [ ] **Step 2: 跑通完整验收清单**

下面 25 项按顺序操作，每项都应通过：

**Phase 1 基础（验证 transport 未回归）：**
1. ✅ 启动后窗口位置、大小、音量与上次关闭一致
2. ✅ 点 📂 选单个 MP3 → 自动开始播放
3. ✅ 标题 / 艺术家 / 专辑 / 采样率显示正确
4. ✅ 内嵌封面显示在 80x80 框中
5. ✅ ▶/⏸ 切换正常
6. ✅ ⏹ 停止
7. ✅ 拖进度条 thumb 跳转准确
8. ✅ 单击进度条空白处跳转
9. ✅ 拖音量滑块 → 实时变化
10. ✅ 🔇/🔊 按钮切换，记住静音前音量
11. ✅ 关窗 → 重启 → 音量保留

**Phase 2 队列（验证拆分未回归）：**
12. ✅ [+ 添加] 多选 5 个 MP3 → 全部入队
13. ✅ 双击第 3 首 → 播放该首；▶ 标记跳到第 3 行；标题红色高亮
14. ✅ 点 ⏭ → 第 4 首；标记同步
15. ✅ 点 ⏮ → 第 3 首
16. ✅ × 按钮删非当前曲 → 队列减少；当前播放不中断
17. ✅ × 按钮删当前曲 → 停止播放；▶ 消失
18. ✅ Delete 键删选中项 → 同 × 按钮
19. ✅ [清空] → 队列空；播放停止
20. ✅ 自然播完 → 自动播下一首
21. ✅ 末曲播完 + Repeat=Off → 停在末曲
22. ✅ 末曲播完 + Repeat=List → 回到第 0 首
23. ✅ 任意曲播完 + Repeat=One → 重播当前
24. ✅ Shuffle 模式 + 自然播完 → 跳到未播过的随机曲；全部播过后视 Repeat
25. ✅ 关闭窗口 → 重启 → 队列状态丢失（队列不持久化是 Phase 3 非目标）

**Phase 3 新验收点：**
26. ✅ 拖音量到 0.3 → 调整窗口大小 → 关闭 → 重启 → 音量 0.3 且窗口大小都保留（race 修复）

- [ ] **Step 3: 如全部通过，记录结果**

将本次验收结果以 commit 形式归档：

```bash
git commit --allow-empty -m "test: Phase 3 manual acceptance pass

All 25 Phase 2 cases + 1 Phase 3 race-fix case verified.
Behavior is byte-identical with Phase 2 from the user's perspective."
```

- [ ] **Step 4: 如有任何项失败 — 不可合并**

如果上述 26 项中任一失败：
1. 记录失败项与现象
2. 回到相关任务（多半是 Task 6、9、10、11 之一）排查
3. 修好后重跑整个 Step 2

---

## Task 15: 合并到 master

**Files:**
- 无文件改动；git 操作

- [ ] **Step 1: 确认分支状态干净**

Run: `git status -sb`
Expected: `## feature/phase3-tech-debt`，无 modified/staged 文件。

- [ ] **Step 2: 查看本期提交历史**

Run: `git log master..feature/phase3-tech-debt --oneline`
Expected: 应看到约 9 个提交（Task 2、3、4、5、6、7、8、Task 9-11 合并、Task 13、Task 14 共约 9-10 条）。

- [ ] **Step 3: 切到 master 并合并（保留合并 commit）**

```bash
git checkout master
git merge --no-ff feature/phase3-tech-debt -m "merge: Phase 3 tech-debt cleanup

Splits MainViewModel into PlayerViewModel + PlaylistViewModel (strict
facade); introduces ITrackMetadataReader; replaces ISettingsPersistence.
SaveAsync with atomic UpdateAsync(Func<>); removes all View<->VM hard
casts. No user-visible behavior change.

Pays coupling debts #1, #2, #3, #4 from docs/COUPLING.md."
```

Expected: `Merge made by the 'ort' strategy.`

- [ ] **Step 4: 最后构建验证**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s), 0 Warning(s)。

- [ ] **Step 5: 保留 feature 分支作为参考**

不删除 `feature/phase3-tech-debt` 分支（与 Phase 2 的约定一致 — "保留作为参考"）。

Run: `git branch`
Expected: 两个分支都在：
```
  feature/phase3-tech-debt
* master
```

---

## 验收成功标准（spec §1.3 摘录）

实现完成时以下全部为真：

**静态：**
- ✅ `wc -l ViewModels/MainViewModel.cs` ≤ 50
- ✅ `grep -rn "as MainViewModel" .` 无 `.cs` 命中
- ✅ `grep -rn "SaveAsync(" Services/ ViewModels/ Views/` 无输出
- ✅ `grep -rn "new ATL.Track" .` 仅命中 `Services/AtlMetadataReader.cs`

**构建：**
- ✅ `dotnet build` — 0 error / 0 warning

**行为：**
- ✅ Task 14 的 26 项手动 smoke 100% 通过
