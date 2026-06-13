# Phase 6 — Named Playlists + Debt #1 + xUnit Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add multi-named-playlist support to UmaPlayer with v1→v2 schema migration, deliver a `byte[]→BitmapImage` IValueConverter (debt #1 partial repayment), and stand up an xUnit test project skeleton.

**Architecture:** Three independent sub-projects executed in dependency order — (A) xUnit skeleton first as a safety net, (B) multi-playlist refactor as the trunk (`Playlist` record, `IPlaylistService`, `PlaylistsViewModel` container, left sidebar UI, "viewed vs current" dual state), (C) `BytesToBitmapImageConverter` last as a self-contained delivery. Each sub-task commits independently; build stays green at every commit.

**Tech Stack:** C# 12 / .NET 10, WPF, CommunityToolkit.Mvvm 8.x source generators, System.Text.Json, NAudio (unchanged), xUnit + Coverlet (new), Microsoft.Extensions.DependencyInjection.

**Spec:** `docs/superpowers/specs/2026-06-13-uma-player-phase6-named-playlists-design.md`

---

## File Structure (locked decisions)

Files this plan creates or modifies, with single-responsibility blurbs:

### Sub-project A — xUnit skeleton
- `Tests/UmaPlayer.Tests.csproj` — Test project, references `UmaPlayer.csproj`, net10.0-windows + UseWPF.
- `Tests/Smoke/SmokeTests.cs` — One test verifying Track record value equality.
- `UmaPlayer.sln` — modify to include the new test project.
- `.gitignore` — append `Tests/bin/`, `Tests/obj/`, `coverage.cobertura.xml`.

### Sub-project B — multi-playlist
- `Models/Playlist.cs` (new) — `record Playlist(Id, Name, Items, CurrentIndex, ShuffleEnabled, RepeatMode)`.
- `Models/QueueState.cs` (rewrite) — v2 with `Playlists` and `CurrentPlaylistId`.
- `Services/IPlaylistService.cs` (new) — replaces `IQueuePersistence`, same Load/Save shape.
- `Services/JsonPlaylistService.cs` (new) — implements v1→v2 migration; replaces `JsonQueuePersistence`.
- `Services/IQueuePersistence.cs` (delete) — superseded.
- `Services/JsonQueuePersistence.cs` (delete) — superseded.
- `ViewModels/PlaylistsViewModel.cs` (new) — container ObservableObject; manages list + Add/Remove/Rename + double-click play takeover.
- `ViewModels/PlaylistViewModel.cs` (modify) — add `Id`, `Name`, `IsActivePlaylist`, `ToRecord()`; remove `IQueuePersistence`, `LoadFromDisk`, `SnapshotState`.
- `ViewModels/MainViewModel.cs` (modify) — replace `Player`/`Playlist` facade with `Player`/`Playlists`; centralize debounced Save.
- `Extensions/ServiceCollectionExtensions.cs` (modify) — DI: drop `IQueuePersistence`, register `IPlaylistService`/`PlaylistsViewModel`/`Func<Playlist,PlaylistViewModel>`.
- `App.xaml.cs` (modify) — drop `IQueuePersistence` resolution; expose `App.GetService<T>()` static.
- `Views/MainWindow.xaml` (modify) — Row 1 split into 2 columns: sidebar + queue.
- `Views/MainWindow.xaml.cs` (modify) — drop `IQueuePersistence` ctor arg + queue.json save in Closing (Save now lives in MainVM debounce).
- `Views/Controls/PlaylistsSidebarView.xaml` (new) — sidebar UserControl.
- `Views/Controls/PlaylistsSidebarView.xaml.cs` (new) — code-behind for Remove/Rename/IsActive marker.
- `Views/Controls/PlaylistView.xaml.cs` (modify) — double-click play routes through `PlaylistsVM.HandleDoubleClickPlay`; refresh ▶ guards on `IsActivePlaylist`.
- `Views/Dialogs/PromptDialog.xaml` (new) — shared single-input dialog.
- `Views/Dialogs/PromptDialog.xaml.cs` (new) — code-behind + `PromptDialog.Show(...)` static helper.

### Sub-project C — debt #1 (Converter)
- `Converters/BytesToBitmapImageConverter.cs` (new) — IValueConverter wrapping `byte[]`→Frozen BitmapImage.
- `App.xaml` (modify) — register `BytesToBitmapImage` as application resource.

### Documentation
- `docs/PROJECT.md` (modify) — Phase 5 → Phase 6 header, dir tree, design decisions, walkthroughs.
- `docs/COUPLING.md` (modify) — debt #1 status to "partial", new contracts, "Phase 7 startup checklist".

---

## Sub-project A — xUnit Skeleton (~2h)

### Task A1: Create test project file

**Files:**
- Create: `Tests/UmaPlayer.Tests.csproj`

- [ ] **Step 1: Create the csproj**

Write `Tests/UmaPlayer.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\UmaPlayer.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Run `dotnet restore` to fetch packages**

Run: `dotnet restore Tests/UmaPlayer.Tests.csproj`
Expected: `Restored ... Tests\UmaPlayer.Tests.csproj` with no errors.

### Task A2: Add test project to solution

**Files:**
- Modify: `UmaPlayer.sln`

- [ ] **Step 1: Add test project via dotnet CLI**

Run: `dotnet sln UmaPlayer.sln add Tests/UmaPlayer.Tests.csproj`
Expected: `Project ... added to the solution.`

- [ ] **Step 2: Verify both projects build from solution**

Run: `dotnet build UmaPlayer.sln`
Expected: `Build succeeded.` with both `UmaPlayer` and `UmaPlayer.Tests` listed.

### Task A3: Add smoke test verifying Track value equality

**Files:**
- Create: `Tests/Smoke/SmokeTests.cs`

- [ ] **Step 1: Write the failing test**

Write `Tests/Smoke/SmokeTests.cs`:

```csharp
using System;
using UmaPlayer.Models;
using Xunit;

namespace UmaPlayer.Tests.Smoke;

public class SmokeTests
{
    [Fact]
    public void Track_Record_StructuralEquality()
    {
        var a = new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero);
        var b = new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero);
        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj`
Expected: `Passed: 1, Failed: 0, Skipped: 0`. The test passes immediately because Track is already a `record` (Phase 1) — this is a smoke test, not red→green TDD. Failure here means the test project plumbing is broken, not the model.

- [ ] **Step 3: Run with coverage to confirm Coverlet works**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --collect:"XPlat Code Coverage"`
Expected: Output mentions `Attachments:` followed by a path ending in `coverage.cobertura.xml`.

### Task A4: Update .gitignore

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Append test artifacts**

Append to `.gitignore`:

```
Tests/bin/
Tests/obj/
coverage.cobertura.xml
TestResults/
```

- [ ] **Step 2: Verify ignore works**

Run: `git status Tests/`
Expected: only `Tests/Smoke/SmokeTests.cs` and `Tests/UmaPlayer.Tests.csproj` listed; no `bin/` or `obj/` entries.

### Task A5: Commit sub-project A

- [ ] **Step 1: Stage and commit**

Run:
```bash
git add Tests/ UmaPlayer.sln .gitignore
git commit -m "test: add xUnit skeleton with smoke test (Phase 6 sub-project A)"
```
Expected: commit succeeds with 4-5 files changed.

---
## Sub-project B — Multi-Playlist (~9h)

> **Order matters.** B1→B14 in sequence. Each commit leaves the build green where stated. The migration goes Models → Service → VMs → Views, with the seam between old and new world cleanly cut at task B7.

### Task B1: Add `Models/Playlist.cs`

**Files:**
- Create: `Models/Playlist.cs`

- [ ] **Step 1: Create the record**

Write `Models/Playlist.cs`:

```csharp
namespace UmaPlayer.Models;

/// <summary>
/// 一个命名歌单(Phase 6)。Id 是创建时生成的 GUID, 主键, 不可变;
/// Name 仅展示, 可重复可重命名。ShuffleEnabled / RepeatMode / CurrentIndex
/// 下沉到歌单级别 —— 各歌单独立, 不再共享顶层状态。
/// </summary>
public sealed record Playlist(
    string Id,
    string Name,
    IReadOnlyList<string> Items,
    int CurrentIndex,
    bool ShuffleEnabled,
    RepeatMode RepeatMode);
```

- [ ] **Step 2: Verify build**

Run: `dotnet build UmaPlayer.csproj`
Expected: `Build succeeded.` (no consumers yet, just adds the type).

- [ ] **Step 3: Commit**

```bash
git add Models/Playlist.cs
git commit -m "feat(models): add Playlist record (Phase 6 B1)"
```

### Task B2: Rewrite `Models/QueueState.cs` to v2

**Files:**
- Modify: `Models/QueueState.cs`

- [ ] **Step 1: Replace the file with v2 schema**

Replace entire `Models/QueueState.cs` with:

```csharp
namespace UmaPlayer.Models;

/// <summary>
/// v2 队列持久化快照(Phase 6)。包含所有命名歌单 + "正在播放"指针。
/// 历史: v1 仅承载单一队列, 由 JsonPlaylistService.LoadAsync 一次性迁移到 v2。
/// SchemaVersion 永远写 2; ViewedPlaylistId 不持久化。
/// </summary>
public sealed record QueueState
{
    public int SchemaVersion { get; init; } = 2;
    public IReadOnlyList<Playlist> Playlists { get; init; } = Array.Empty<Playlist>();
    public string CurrentPlaylistId { get; init; } = string.Empty;
}
```

- [ ] **Step 2: Verify build fails (consumers reference old fields)**

Run: `dotnet build UmaPlayer.csproj`
Expected: errors like `'QueueState' does not contain a definition for 'Items'` in `JsonQueuePersistence.cs` and `PlaylistViewModel.cs`. **This is intentional** — old persistence and VM read paths get removed/replaced in B3-B7. Build goes green again at B12. Do **NOT** commit.

### Task B3: Add `Services/IPlaylistService.cs`

**Files:**
- Create: `Services/IPlaylistService.cs`

- [ ] **Step 1: Create the interface**

Write `Services/IPlaylistService.cs`:

```csharp
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 多歌单持久化抽象(Phase 6, 替换 IQueuePersistence)。
/// 文件: %LocalAppData%\UmaPlayer\queue.json (沿用文件名)。
/// 契约: LoadAsync 绝不抛(失败回种子), SaveAsync IO 失败抛 IOException。
/// LoadAsync 内一次性迁移 v1 → v2; 迁移阶段 SaveAsync 失败吞掉, 内存仍是 v2, 下次启动重迁(幂等)。
/// </summary>
public interface IPlaylistService
{
    Task<QueueState> LoadAsync();
    Task SaveAsync(QueueState snapshot);
}
```

- [ ] **Step 2: No build yet (still red from B2)**

Continue to B4.

### Task B4: Add `Services/JsonPlaylistService.cs` with v1→v2 migration

**Files:**
- Create: `Services/JsonPlaylistService.cs`

- [ ] **Step 1: Create the implementation**

Write `Services/JsonPlaylistService.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// queue.json 的 JSON 实现(Phase 6, 替换 JsonQueuePersistence)。
/// LoadAsync 内含 v1→v2 一次性迁移: 检测顶层 SchemaVersion, ==1 则把 Items/CurrentIndex/
/// Shuffle/Repeat 包成单条名为"默认歌单"的 v2 Playlist; 立即 SaveAsync 覆盖文件。
/// 迁移阶段 SaveAsync 失败 → 吞掉, 内存里仍是 v2 表示, 下次启动重迁(幂等)。
/// </summary>
public sealed class JsonPlaylistService : IPlaylistService
{
    private const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonPlaylistService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "UmaPlayer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "queue.json");
    }

    public async Task<QueueState> LoadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return Seed();

            string text;
            await using (var stream = File.OpenRead(_path))
            using (var reader = new StreamReader(stream))
                text = await reader.ReadToEndAsync().ConfigureAwait(false);

            int version;
            try
            {
                using var doc = JsonDocument.Parse(text);
                version = doc.RootElement.TryGetProperty("SchemaVersion", out var v) ? v.GetInt32() : 1;
            }
            catch
            {
                return Seed();
            }

            if (version == 1)
                return await MigrateV1ToV2Async(text).ConfigureAwait(false);

            if (version == 2)
            {
                QueueState? loaded;
                try { loaded = JsonSerializer.Deserialize<QueueState>(text, JsonOptions); }
                catch { return Seed(); }

                if (loaded is null || loaded.Playlists.Count == 0)
                    return Seed();

                var match = loaded.Playlists.Any(p => p.Id == loaded.CurrentPlaylistId);
                return match ? loaded : loaded with { CurrentPlaylistId = loaded.Playlists[0].Id };
            }

            return Seed();
        }
        catch
        {
            return Seed();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(QueueState snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static QueueState Seed()
    {
        var defaultPlaylist = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: "默认歌单",
            Items: Array.Empty<string>(),
            CurrentIndex: -1,
            ShuffleEnabled: false,
            RepeatMode: RepeatMode.Off);
        return new QueueState
        {
            Playlists = new[] { defaultPlaylist },
            CurrentPlaylistId = defaultPlaylist.Id,
        };
    }

    private async Task<QueueState> MigrateV1ToV2Async(string v1Text)
    {
        QueueStateV1? v1;
        try { v1 = JsonSerializer.Deserialize<QueueStateV1>(v1Text, JsonOptions); }
        catch { return Seed(); }
        if (v1 is null) return Seed();

        var defaultPlaylist = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: "默认歌单",
            Items: v1.Items ?? Array.Empty<string>(),
            CurrentIndex: v1.CurrentIndex,
            ShuffleEnabled: v1.ShuffleEnabled,
            RepeatMode: v1.RepeatMode);
        var v2 = new QueueState
        {
            Playlists = new[] { defaultPlaylist },
            CurrentPlaylistId = defaultPlaylist.Id,
        };

        try
        {
            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, v2, JsonOptions).ConfigureAwait(false);
        }
        catch { /* swallow — 内存仍是 v2, 下次启动重迁 */ }

        return v2;
    }

    private sealed record QueueStateV1
    {
        public int SchemaVersion { get; init; } = 1;
        public IReadOnlyList<string>? Items { get; init; }
        public int CurrentIndex { get; init; } = -1;
        public bool ShuffleEnabled { get; init; }
        public RepeatMode RepeatMode { get; init; } = RepeatMode.Off;
    }
}
```

- [ ] **Step 2: Build still red, continue to B5**

### Task B5: Delete `IQueuePersistence` and `JsonQueuePersistence`

**Files:**
- Delete: `Services/IQueuePersistence.cs`
- Delete: `Services/JsonQueuePersistence.cs`

- [ ] **Step 1: Remove old persistence files**

Run: `git rm Services/IQueuePersistence.cs Services/JsonQueuePersistence.cs`
Expected: both files removed; listed as deleted in `git status`.

- [ ] **Step 2: Build still red on consumers, continue to B6**

### Task B6: Update DI registration

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Replace registrations**

In `Extensions/ServiceCollectionExtensions.cs`, **delete** the line:
```csharp
        services.AddSingleton<IQueuePersistence, JsonQueuePersistence>();
```

**Replace** the three ViewModel lines (`PlayerViewModel`, `PlaylistViewModel`, `MainViewModel`) with:

```csharp
        // Phase 6: 多歌单持久化 (替换 IQueuePersistence)
        services.AddSingleton<IPlaylistService, JsonPlaylistService>();

        // ViewModel
        services.AddTransient<PlayerViewModel>();
        // PlaylistViewModel 由 PlaylistsViewModel 通过工厂创建; 工厂封装依赖, seed 是动态参数。
        services.AddTransient<Func<Models.Playlist, PlaylistViewModel>>(sp => seed =>
            new PlaylistViewModel(
                seed,
                sp.GetRequiredService<IPlaybackService>(),
                sp.GetRequiredService<IFileDialogService>(),
                sp.GetRequiredService<ITrackMetadataReader>()));
        services.AddSingleton<PlaylistsViewModel>();
        services.AddTransient<MainViewModel>();
```

- [ ] **Step 2: Build still red, continue to B7**

### Task B7: Rewrite `ViewModels/PlaylistViewModel.cs` to Phase 6 model

**Files:**
- Modify: `ViewModels/PlaylistViewModel.cs`

- [ ] **Step 1: Replace constructor signature and add Phase 6 surface**

In `ViewModels/PlaylistViewModel.cs`:

**Delete** the field and ctor parameter `IQueuePersistence _queuePersistence`. Phase 6 移除 VM 自己存盘 —— 由 `MainViewModel` 集中 debounce save。

**Replace** the constructor with:

```csharp
public PlaylistViewModel(
    Models.Playlist seed,
    IPlaybackService player,
    IFileDialogService fileDialog,
    ITrackMetadataReader metadataReader)
{
    ArgumentNullException.ThrowIfNull(seed);
    _player = player ?? throw new ArgumentNullException(nameof(player));
    _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
    _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));

    Id = seed.Id;
    Name = seed.Name;
    _shuffleEnabled = seed.ShuffleEnabled;
    _repeatMode = seed.RepeatMode;

    // Port LoadFromDisk: 过滤不存在的文件, 重映射 CurrentIndex
    var existing = (seed.Items ?? Array.Empty<string>())
        .Where(File.Exists)
        .ToList();
    foreach (var path in existing)
    {
        Tracks.Add(new Track(
            FilePath: path,
            Title: Path.GetFileNameWithoutExtension(path),
            Artist: null, Album: null, AlbumArtist: null,
            Genre: null, Year: null, Cover: null,
            Duration: TimeSpan.Zero));
    }
    CurrentIndex = MapCurrentIndexAfterFilter(seed.Items ?? Array.Empty<string>(), existing, seed.CurrentIndex);

    Tracks.CollectionChanged += OnTracksCollectionChanged;
}
```

**Add** these public members near the top of the class (after fields, before commands):

```csharp
public string Id { get; }

[ObservableProperty]
private string _name = string.Empty;

[ObservableProperty]
private bool _isActivePlaylist;

/// <summary>
/// 把当前 VM 状态打包成持久化用 Playlist record。MainViewModel 在 BuildSnapshot 时调用。
/// </summary>
public Models.Playlist ToRecord() => new(
    Id: Id,
    Name: Name,
    Items: Tracks.Select(t => t.FilePath).ToArray(),
    CurrentIndex: CurrentIndex,
    ShuffleEnabled: ShuffleEnabled,
    RepeatMode: RepeatMode);
```

- [ ] **Step 2: Delete obsolete persistence methods**

In `ViewModels/PlaylistViewModel.cs`:

**Delete entirely** the following methods (they are superseded by `ToRecord()` + MainVM debounce save):
- `public async Task LoadFromDisk()` — body已经迁进 ctor。
- `public QueueState SnapshotState()` — 由 `ToRecord()` 替代。

`MapCurrentIndexAfterFilter(...)` 静态助手保留不动 —— ctor 还在用。

- [ ] **Step 3: Remove `IQueuePersistence` references**

Search `ViewModels/PlaylistViewModel.cs` for `IQueuePersistence` and `_queuePersistence` —— 应已经全部跟 ctor 一起删干净。剩余的 `using UmaPlayer.Services;` 可能仍需要 (ITrackMetadataReader 等), 别动。

- [ ] **Step 4: Verify build is now closer to green**

Run: `dotnet build UmaPlayer.csproj`
Expected: 仍然红, 但错误从 `QueueState` 字段缺失转移到 `MainViewModel` / `MainWindow.xaml.cs` / `App.xaml.cs` 仍引用旧 `Playlist` facade、旧 `IQueuePersistence`。下一步 B8-B11 把这些消化掉。


### Task B8: Add `ViewModels/PlaylistsViewModel.cs`

**Files:**
- Create: `ViewModels/PlaylistsViewModel.cs`

- [ ] **Step 1: Create the container ViewModel**

Write `ViewModels/PlaylistsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmaPlayer.Models;

namespace UmaPlayer.ViewModels;

/// <summary>
/// Phase 6 多歌单容器。维护命名歌单集合、"正在查看"指针(UI 选中, 不持久化)、"正在播放"指针
/// (CurrentPlaylistId, 持久化, 决定双击播放是否要跨歌单切音频)。聚合每个 PlaylistViewModel 的
/// PropertyChanged 触发统一 StateChanged 事件, 给 MainViewModel debounce save 用。
/// </summary>
public sealed partial class PlaylistsViewModel : ObservableObject
{
    private readonly Func<Playlist, PlaylistViewModel> _factory;

    public ObservableCollection<PlaylistViewModel> Playlists { get; } = new();

    [ObservableProperty]
    private PlaylistViewModel? _viewedPlaylist;

    /// <summary>
    /// 当前正在播放的歌单 Id; 持久化字段。空字符串表示首启动或边缘态(BuildSnapshot 容忍)。
    /// </summary>
    [ObservableProperty]
    private string _currentPlaylistId = string.Empty;

    /// <summary>
    /// 任意歌单内部状态(Tracks、CurrentIndex、Shuffle、Repeat、Name)、容器结构(增删歌单)、
    /// 或 CurrentPlaylistId 改变时触发。MainViewModel 订阅此事件做 debounce save。
    /// </summary>
    public event EventHandler? StateChanged;

    public PlaylistsViewModel(Func<Playlist, PlaylistViewModel> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Playlists.CollectionChanged += OnPlaylistsCollectionChanged;
    }

    /// <summary>
    /// MainViewModel 启动时调用; 用持久化快照初始化容器。重复调用先清空。
    /// </summary>
    public void Hydrate(QueueState snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var vm in Playlists)
            UnhookPlaylistVm(vm);
        Playlists.Clear();

        foreach (var p in snapshot.Playlists)
        {
            var vm = _factory(p);
            HookPlaylistVm(vm);
            Playlists.Add(vm);
        }

        // CurrentPlaylistId: 取持久化值, 但若指向已不存在的歌单则修正到第一个。
        var matchId = Playlists.Any(p => p.Id == snapshot.CurrentPlaylistId)
            ? snapshot.CurrentPlaylistId
            : (Playlists.Count > 0 ? Playlists[0].Id : string.Empty);
        CurrentPlaylistId = matchId;

        // ViewedPlaylist 不持久化, 默认对齐 CurrentPlaylistId。
        ViewedPlaylist = Playlists.FirstOrDefault(p => p.Id == matchId);

        RecomputeIsActiveFlags();
    }

    /// <summary>
    /// 把容器当前状态打包成持久化快照。MainViewModel debounce save 用。
    /// </summary>
    public QueueState BuildSnapshot() => new()
    {
        Playlists = Playlists.Select(vm => vm.ToRecord()).ToArray(),
        CurrentPlaylistId = CurrentPlaylistId,
    };

    /// <summary>
    /// PlaylistView 双击播放回调入口。如果双击的不是当前正在播放的歌单, 切 CurrentPlaylistId;
    /// 然后让目标 VM 跑现有的 PlayTrackAtCommand。CommunityToolkit IAsyncRelayCommand
    /// 暴露 ExecuteAsync(object?), 调用方可以 await。
    /// </summary>
    public async Task HandleDoubleClickPlay(PlaylistViewModel target, int index)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.Id != CurrentPlaylistId)
            CurrentPlaylistId = target.Id;  // 触发 RecomputeIsActiveFlags + StateChanged

        if (index >= 0 && index < target.Tracks.Count)
            await target.PlayTrackAtCommand.ExecuteAsync(index).ConfigureAwait(true);
    }

    [RelayCommand]
    private void AddPlaylist(string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "新歌单" : name.Trim();
        var seed = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: trimmed,
            Items: Array.Empty<string>(),
            CurrentIndex: -1,
            ShuffleEnabled: false,
            RepeatMode: RepeatMode.Off);
        var vm = _factory(seed);
        HookPlaylistVm(vm);
        Playlists.Add(vm);
        ViewedPlaylist = vm;
    }

    [RelayCommand]
    private void RemovePlaylist(PlaylistViewModel? target)
    {
        if (target is null) return;

        var index = Playlists.IndexOf(target);
        if (index < 0) return;

        UnhookPlaylistVm(target);
        Playlists.RemoveAt(index);

        // 如果删的是当前播放歌单, 让 MainViewModel 在它的 OnCurrentPlaylistIdChanged 处理音频停 + 加载新目标。
        // 这里只关心容器结构与指针: 若整个集合空了则自动重建空"默认歌单", 保证不存在 0 歌单状态。
        if (Playlists.Count == 0)
        {
            var defaultSeed = new Playlist(
                Id: Guid.NewGuid().ToString(),
                Name: "默认歌单",
                Items: Array.Empty<string>(),
                CurrentIndex: -1,
                ShuffleEnabled: false,
                RepeatMode: RepeatMode.Off);
            var rebuilt = _factory(defaultSeed);
            HookPlaylistVm(rebuilt);
            Playlists.Add(rebuilt);
            CurrentPlaylistId = rebuilt.Id;
            ViewedPlaylist = rebuilt;
            return;
        }

        // 修指针: 若被删项是当前播放/查看, 落到相邻项(优先后一个, 没有则前一个)。
        if (target.Id == CurrentPlaylistId)
        {
            var fallbackIndex = Math.Min(index, Playlists.Count - 1);
            CurrentPlaylistId = Playlists[fallbackIndex].Id;
        }
        if (ReferenceEquals(target, ViewedPlaylist))
        {
            var fallbackIndex = Math.Min(index, Playlists.Count - 1);
            ViewedPlaylist = Playlists[fallbackIndex];
        }
    }

    [RelayCommand]
    private void RenamePlaylist((PlaylistViewModel? Target, string? NewName) args)
    {
        if (args.Target is null) return;
        var trimmed = string.IsNullOrWhiteSpace(args.NewName) ? args.Target.Name : args.NewName.Trim();
        if (trimmed == args.Target.Name) return;
        args.Target.Name = trimmed;  // OnPlaylistVmPropertyChanged 已挂 -> StateChanged
    }

    partial void OnCurrentPlaylistIdChanged(string value)
    {
        RecomputeIsActiveFlags();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecomputeIsActiveFlags()
    {
        foreach (var vm in Playlists)
            vm.IsActivePlaylist = vm.Id == CurrentPlaylistId;
    }

    private void HookPlaylistVm(PlaylistViewModel vm)
    {
        vm.PropertyChanged += OnPlaylistVmPropertyChanged;
        vm.Tracks.CollectionChanged += OnPlaylistTracksChanged;
    }

    private void UnhookPlaylistVm(PlaylistViewModel vm)
    {
        vm.PropertyChanged -= OnPlaylistVmPropertyChanged;
        vm.Tracks.CollectionChanged -= OnPlaylistTracksChanged;
    }

    private void OnPlaylistVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // IsActivePlaylist 是容器自己设的, 别把它当用户改动 echo 回 save。
        if (e.PropertyName == nameof(PlaylistViewModel.IsActivePlaylist)) return;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaylistTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => StateChanged?.Invoke(this, EventArgs.Empty);

    private void OnPlaylistsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => StateChanged?.Invoke(this, EventArgs.Empty);
}
```

> **注意:** 现有 `PlaylistViewModel` 在 Phase 5 已暴露 `[RelayCommand] private async Task PlayTrackAt(int index)` (源生成器导出 `PlayTrackAtCommand` 实现 `IAsyncRelayCommand`)。本 task 直接复用; 不要新加方法。

- [ ] **Step 2: Verify build**

Run: `dotnet build UmaPlayer.csproj`
Expected: 仍然红 —— `MainViewModel` / `App.xaml.cs` / `MainWindow.xaml.cs` 还没改。继续 B9。


### Task B9: Rewrite `ViewModels/MainViewModel.cs` — Phase 6 facade + debounce save

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: Replace entire file**

Replace `ViewModels/MainViewModel.cs` content with:

```csharp
using UmaPlayer.Models;
using UmaPlayer.Services;

namespace UmaPlayer.ViewModels;

/// <summary>
/// Phase 6 Facade: Player + Playlists 容器 + 窗口生命周期清理。
/// 集中持有 debounce save 的 CancellationTokenSource ——
/// 任何子 VM 状态变化(PlaylistsViewModel.StateChanged) 触发 500ms 后写盘;
/// 在 500ms 内连发的变更只产出一次 SaveAsync。
///
/// 边界约束(COUPLING.md, Phase 6 新不变量):
///   - 不再暴露单个 PlaylistViewModel; 只暴露 Player + Playlists + CleanupAsync + InitializeAsync
///   - debounce 状态(_saveCts)只在本类持有, 子 VM 不感知存盘
///   - CleanupAsync 关闭前 flush: 取消挂起的 timer, 立即同步 SaveAsync, 再 Dispose Player
/// </summary>
public sealed class MainViewModel : IAsyncDisposable
{
    private const int DebounceMs = 500;

    private readonly IPlaybackService _player;
    private readonly IPlaylistService _playlistService;
    private CancellationTokenSource? _saveCts;
    private bool _hydrated;

    public PlayerViewModel Player { get; }
    public PlaylistsViewModel Playlists { get; }

    public MainViewModel(
        PlayerViewModel player,
        PlaylistsViewModel playlists,
        IPlaybackService playbackService,
        IPlaylistService playlistService)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Playlists = playlists ?? throw new ArgumentNullException(nameof(playlists));
        _player = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _playlistService = playlistService ?? throw new ArgumentNullException(nameof(playlistService));
    }

    /// <summary>
    /// MainWindow Loaded 时调用一次。读 queue.json (含 v1→v2 迁移), Hydrate 容器, 然后开始监听 StateChanged。
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_hydrated) return;
        var snapshot = await _playlistService.LoadAsync().ConfigureAwait(true);
        Playlists.Hydrate(snapshot);
        Playlists.StateChanged += OnPlaylistsStateChanged;
        _hydrated = true;
    }

    private void OnPlaylistsStateChanged(object? sender, EventArgs e) => ScheduleSave();

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _saveCts = cts;
        _ = SaveAfterDelayAsync(cts.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken ct)
    {
        try { await Task.Delay(DebounceMs, ct).ConfigureAwait(false); }
        catch (TaskCanceledException) { return; }

        try
        {
            var snapshot = Playlists.BuildSnapshot();
            await _playlistService.SaveAsync(snapshot).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // 吞掉 —— Phase 6 容忍 IO 失败, 用户下次操作会再次触发 debounce save。
        }
    }

    /// <summary>
    /// 窗口关闭路径: 取消挂起 debounce, 立即同步 flush, 解绑事件, 释放音频。
    /// MainWindow.Window_Closing 用 cancel-and-close 模式包住此调用 (memory: wpf-async-void-closing-dispatcher-race)。
    /// </summary>
    public async Task CleanupAsync()
    {
        Playlists.StateChanged -= OnPlaylistsStateChanged;
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        _saveCts = null;

        if (_hydrated)
        {
            try
            {
                var snapshot = Playlists.BuildSnapshot();
                await _playlistService.SaveAsync(snapshot).ConfigureAwait(true);
            }
            catch (IOException) { /* 吞掉, 关闭路径不阻塞 */ }
        }

        await Player.CleanupAsync().ConfigureAwait(true);
        _player.Dispose();
    }

    public async ValueTask DisposeAsync() => await CleanupAsync().ConfigureAwait(false);
}
```

> **重要:** 旧版本里有 `Playlist.Cleanup()` 解绑 TrackEnded —— 在 Phase 6 这条解绑由 PlaylistsViewModel 容器在 Hydrate / RemovePlaylist / 析构 时统一处理(`UnhookPlaylistVm`), 所以这里不再需要单独调用。如果发现 PlaylistViewModel 自己还在订阅 `IPlaybackService.TrackEnded`, 需要在 PlaylistViewModel 析构/容器换出时 unhook —— 实操中查清后再补。

- [ ] **Step 2: Build still red**

Run: `dotnet build UmaPlayer.csproj`
Expected: 错误现在集中在 `App.xaml.cs` (旧 IQueuePersistence 解析) 和 `MainWindow.xaml.cs` (旧 ctor 签名)。继续 B10。


### Task B10: Update `App.xaml.cs` — drop IQueuePersistence, expose static GetService<T>

**Files:**
- Modify: `App.xaml.cs`

- [ ] **Step 1: Replace OnStartup body and add static accessor**

Replace `App.xaml.cs` content with:

```csharp
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmaPlayer.Extensions;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer;

/// <summary>
/// 应用入口(替代默认 StartupUri 启动方式, 便于注入 DI 容器)。
///
/// Phase 6 新增 GetService&lt;T&gt; 静态入口 —— 给 View 层 code-behind 在事件
/// 处理(双击播放等)中按需取 PlaylistsViewModel, 避免 PlaylistView 与
/// PlaylistsViewModel 之间硬绑 DataContext 通道。
/// </summary>
public partial class App : Application
{
    private static ServiceProvider? _services;

    /// <summary>
    /// 取一个 DI 单例/瞬态。仅供 View 层 code-behind 在事件处理中使用 ——
    /// VM 之间永远走构造函数注入, 不要调用本方法。
    /// </summary>
    public static T GetService<T>() where T : notnull
    {
        if (_services is null)
            throw new InvalidOperationException("ServiceProvider not initialized; called before App.OnStartup.");
        return _services.GetRequiredService<T>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddUmaPlayerServices(configuration);
        _services = services.BuildServiceProvider();

        // 注意:MainWindow 需要 settings persistence 用于恢复/保存窗口位置,
        // 因此这里显式解析后通过构造函数传入(而非让 DI 解析窗口)。
        // queue 持久化已下沉到 MainViewModel.InitializeAsync/CleanupAsync, 不再在此显式拉取。
        var vm = _services.GetRequiredService<MainViewModel>();
        var persistence = _services.GetRequiredService<ISettingsPersistence>();
        var mainWindow = new Views.MainWindow(vm, persistence);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 触发 Singleton 服务的 Dispose(NAudioPlaybackService 借此释放音频设备)
        (_services as IDisposable)?.Dispose();
        _services = null;
        base.OnExit(e);
    }
}
```

- [ ] **Step 2: Build still red on MainWindow ctor mismatch, continue to B11**

### Task B11: Update `Views/MainWindow.xaml.cs` — drop IQueuePersistence, move queue save into VM

**Files:**
- Modify: `Views/MainWindow.xaml.cs`

- [ ] **Step 1: Remove `IQueuePersistence` field + ctor parameter**

In `Views/MainWindow.xaml.cs`:

**Delete** these lines:
```csharp
private readonly IQueuePersistence _queuePersistence;
```
and
```csharp
        IQueuePersistence queuePersistence,
```
(third constructor parameter)
and
```csharp
        _queuePersistence = queuePersistence;
```

**New constructor signature:**
```csharp
public MainWindow(MainViewModel vm, ISettingsPersistence persistence)
```

- [ ] **Step 2: Add Loaded handler that calls InitializeAsync**

In `Views/MainWindow.xaml.cs` at end of constructor (after `WindowStartupLocation = WindowStartupLocation.CenterScreen;` catch block), add:

```csharp
        Loaded += MainWindow_Loaded;
```

Then add the handler method:

```csharp
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 异步水化多歌单容器(读 queue.json + 可能的 v1→v2 迁移)。
        // 失败 → JsonPlaylistService 内部已回 seed; UI 仍能用。
        try
        {
            await _vm.InitializeAsync();
        }
        catch
        {
            // 一道额外护栏: 服务保证不抛, 这里只防御未来回归。
        }
    }
```

- [ ] **Step 3: Strip queue.json save out of `Window_Closing`**

In `Views/MainWindow.xaml.cs`, **delete** these lines from `Window_Closing`:

```csharp
        // Phase 4：保存队列快照到 queue.json。
        // 必须在 CleanupAsync 之后调 SnapshotState 也 OK ——
        // Cleanup 仅解绑 TrackEnded，不修改 Queue/CurrentIndex/Shuffle/Repeat。
        try
        {
            var snapshot = _vm.Playlist.SnapshotState();    // UI 线程纯读
            await _queuePersistence.SaveAsync(snapshot);
        }
        catch { /* 写盘失败 = 用户下次启动队列丢失，与 settings 写盘失败行为对称 */ }
```

`MainViewModel.CleanupAsync` 内已经 flush 一次 SaveAsync, 不再在 View 层写盘。

- [ ] **Step 4: Update the doc comment on `Window_Closing`**

`Window_Closing` 顶部的 XML doc 提到 "Phase 4 加入 queue.json 写盘后 await 链变深"。Phase 6 把 queue 写盘下沉进 VM, 但 cancel-and-close 模式仍有效(settings UpdateAsync + CleanupAsync 加起来仍是 2 个 await)。把段落最后一句改为:

```csharp
    /// 取消、做完异步工作再 Close()。Phase 6 把 queue.json 写盘下沉到 MainViewModel.CleanupAsync,
    /// 此处 await 链变成 settings UpdateAsync + CleanupAsync 两段, cancel-and-close 模式继续保护。
```

- [ ] **Step 5: Verify build is green**

Run: `dotnet build UmaPlayer.csproj`
Expected: `Build succeeded.` —— 此时所有源文件应当兼容 v2 schema。**这是 Phase 6 第一次 build 转绿。**

- [ ] **Step 6: Commit a green checkpoint**

```bash
git add Models/Playlist.cs Models/QueueState.cs \
        Services/IPlaylistService.cs Services/JsonPlaylistService.cs \
        Services/IQueuePersistence.cs Services/JsonQueuePersistence.cs \
        ViewModels/PlaylistViewModel.cs ViewModels/PlaylistsViewModel.cs ViewModels/MainViewModel.cs \
        Extensions/ServiceCollectionExtensions.cs \
        App.xaml.cs Views/MainWindow.xaml.cs
git commit -m "feat(playlists): Phase 6 multi-playlist core — schema v2 + container VM (B1-B11)"
```

> **注意:** 此时 UI 还是 Phase 5 单歌单视图 (MainWindow.xaml 一栏), 但底层已经是 v2 多歌单数据。运行程序应当: 启动正常、自动迁移 v1→v2、播放/Shuffle/Repeat 全部沿用旧行为(因为只有一个"默认歌单")。


### Task B12: Add `Views/Dialogs/PromptDialog.xaml` + code-behind

**Files:**
- Create: `Views/Dialogs/PromptDialog.xaml`
- Create: `Views/Dialogs/PromptDialog.xaml.cs`

- [ ] **Step 1: Create the dialog XAML**

Write `Views/Dialogs/PromptDialog.xaml`:

```xml
<Window x:Class="UmaPlayer.Views.Dialogs.PromptDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="输入"
        Width="360" Height="160"
        WindowStartupLocation="CenterOwner"
        WindowStyle="ToolWindow"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Background="{StaticResource BackgroundPrimary}"
        Foreground="{StaticResource ForegroundPrimary}">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock x:Name="LabelText"
                   Grid.Row="0"
                   Margin="0,0,0,8"
                   Text="名称"/>

        <TextBox x:Name="InputBox"
                 Grid.Row="1"
                 Padding="6,4"
                 KeyDown="InputBox_KeyDown"/>

        <StackPanel Grid.Row="3"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Margin="0,12,0,0">
            <Button Content="取消"
                    Width="80" Height="28"
                    Margin="0,0,8,0"
                    IsCancel="True"/>
            <Button Content="确定"
                    Width="80" Height="28"
                    IsDefault="True"
                    Click="Ok_Click"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind with static `Show` helper**

Write `Views/Dialogs/PromptDialog.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;

namespace UmaPlayer.Views.Dialogs;

/// <summary>
/// Phase 6 共享单输入框对话框。AddPlaylist / RenamePlaylist 都用它。
/// 静态 Show(...) 返回 (Confirmed, Text)。Confirmed=false → 用户取消, Text 不可信。
/// </summary>
public partial class PromptDialog : Window
{
    public PromptDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    /// <summary>
    /// 模态显示。owner 用于居中; title/label/defaultValue 三段都可空。
    /// 返回 (确认?, 输入文本)。
    /// </summary>
    public static (bool Confirmed, string Text) Show(
        Window? owner,
        string title,
        string label,
        string defaultValue = "")
    {
        var dlg = new PromptDialog
        {
            Owner = owner,
            Title = title,
        };
        dlg.LabelText.Text = label;
        dlg.InputBox.Text = defaultValue ?? string.Empty;

        var ok = dlg.ShowDialog() == true;
        return (ok, dlg.InputBox.Text);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter 由 IsDefault Button 自动处理; Esc 由 IsCancel 处理。本方法预留扩展点(占位)。
    }
}
```

- [ ] **Step 3: Build still green**

Run: `dotnet build UmaPlayer.csproj`
Expected: `Build succeeded.` (新增的 dialog 还没有 caller, 不会破坏现有引用)。

### Task B13: Add `Views/Controls/PlaylistsSidebarView.xaml` + code-behind

**Files:**
- Create: `Views/Controls/PlaylistsSidebarView.xaml`
- Create: `Views/Controls/PlaylistsSidebarView.xaml.cs`

- [ ] **Step 1: Create the sidebar XAML**

Write `Views/Controls/PlaylistsSidebarView.xaml`:

```xml
<UserControl x:Class="UmaPlayer.Views.Controls.PlaylistsSidebarView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:UmaPlayer.ViewModels"
             d:DataContext="{d:DesignInstance Type=vm:PlaylistsViewModel}"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             Background="{StaticResource BackgroundSecondary}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- 顶部 + / − 工具栏 -->
        <StackPanel Grid.Row="0"
                    Orientation="Horizontal"
                    Margin="6">
            <Button x:Name="AddBtn"
                    Width="28" Height="28"
                    Content="+"
                    ToolTip="新建歌单"
                    Click="AddBtn_Click"/>
            <Button x:Name="RemoveBtn"
                    Width="28" Height="28"
                    Margin="6,0,0,0"
                    Content="−"
                    ToolTip="删除选中歌单"
                    Click="RemoveBtn_Click"/>
        </StackPanel>

        <!-- 歌单列表 -->
        <ListBox x:Name="PlaylistList"
                 Grid.Row="1"
                 ItemsSource="{Binding Playlists}"
                 SelectedItem="{Binding ViewedPlaylist, Mode=TwoWay}"
                 BorderThickness="0"
                 Background="Transparent"
                 MouseDoubleClick="PlaylistList_MouseDoubleClick">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid Margin="0,2">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="14"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <!-- ▶ 标记由 code-behind 在 RefreshActiveMarker 中按 IsActivePlaylist 写入 -->
                        <TextBlock Grid.Column="0"
                                   FontSize="12"
                                   Foreground="{StaticResource AccentPrimary}"/>
                        <TextBlock Grid.Column="1"
                                   Text="{Binding Name}"
                                   TextTrimming="CharacterEllipsis"
                                   VerticalAlignment="Center"/>
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create the sidebar code-behind**

Write `Views/Controls/PlaylistsSidebarView.xaml.cs`:

```csharp
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UmaPlayer.ViewModels;
using UmaPlayer.Views.Dialogs;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// Phase 6 左侧歌单容器侧边栏。+ 新建、− 删选中、双击重命名;
/// "正在播放"行加 ▶ 前缀(模仿 PlaylistView.RefreshCurrentIndicator 的 ItemContainerGenerator 模式)。
/// </summary>
public partial class PlaylistsSidebarView : UserControl
{
    private PlaylistsViewModel? _vm;

    public PlaylistsSidebarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            PlaylistList.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                if (PlaylistList.ItemContainerGenerator.Status ==
                    System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    RefreshActiveMarker();
            };
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Playlists.CollectionChanged -= OnPlaylistsChanged;
        }

        _vm = e.NewValue as PlaylistsViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Playlists.CollectionChanged += OnPlaylistsChanged;
            RefreshActiveMarker();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistsViewModel.CurrentPlaylistId))
            RefreshActiveMarker();
    }

    private void OnPlaylistsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(RefreshActiveMarker),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 模仿 PlaylistView.RefreshCurrentIndicator: 找每个 container 第 0 个 TextBlock(▶ 列),
    /// 按目标 VM.IsActivePlaylist 写 "▶" 或 ""。
    /// </summary>
    private void RefreshActiveMarker()
    {
        if (_vm is null) return;
        PlaylistList.UpdateLayout();
        for (int i = 0; i < PlaylistList.Items.Count; i++)
        {
            if (PlaylistList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
                continue;
            if (PlaylistList.Items[i] is not PlaylistViewModel vm) continue;

            var marker = FindChildByOrder<TextBlock>(container, 0);
            if (marker is null) continue;
            marker.Text = vm.IsActivePlaylist ? "▶" : string.Empty;
        }
    }

    private static T? FindChildByOrder<T>(DependencyObject parent, int n) where T : DependencyObject
    {
        int count = 0;
        return Walk(parent);

        T? Walk(DependencyObject p)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(p); i++)
            {
                var c = VisualTreeHelper.GetChild(p, i);
                if (c is T match)
                {
                    if (count == n) return match;
                    count++;
                }
                var deeper = Walk(c);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }

    // —— 事件处理 ——

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var (ok, text) = PromptDialog.Show(Window.GetWindow(this), "新建歌单", "名称", "新歌单");
        if (!ok) return;
        _vm.AddPlaylistCommand.Execute(text);
    }

    private void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var target = _vm.ViewedPlaylist;
        if (target is null) return;

        // 简单 yes/no 确认 —— Phase 6 用 MessageBox.OK/Cancel
        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"确定删除歌单 \"{target.Name}\" 吗?",
            "删除歌单",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        _vm.RemovePlaylistCommand.Execute(target);
    }

    private void PlaylistList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is null) return;
        if (item.DataContext is not PlaylistViewModel target) return;

        var (ok, text) = PromptDialog.Show(Window.GetWindow(this), "重命名歌单", "新名称", target.Name);
        if (!ok) return;
        _vm.RenamePlaylistCommand.Execute((target, text));
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T match) return match;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
```

- [ ] **Step 3: Build still green (sidebar still not wired into MainWindow)**

Run: `dotnet build UmaPlayer.csproj`
Expected: `Build succeeded.`

### Task B14: Wire sidebar + viewed playlist into MainWindow + tweak PlaylistView double-click

**Files:**
- Modify: `Views/MainWindow.xaml`
- Modify: `Views/Controls/PlaylistView.xaml.cs`

- [ ] **Step 1: Update `Views/MainWindow.xaml` — Row 1 split into 2 columns**

Replace the `<Grid>` block in `Views/MainWindow.xaml` with:

```xml
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <controls:PlayerBar Grid.Row="0" DataContext="{Binding Player}"/>

        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160" MinWidth="120"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <controls:PlaylistsSidebarView Grid.Column="0"
                                            DataContext="{Binding Playlists}"/>
            <controls:PlaylistView Grid.Column="1"
                                    DataContext="{Binding Playlists.ViewedPlaylist}"/>
        </Grid>
    </Grid>
```

也把 XAML 文件顶部注释更新为 Phase 6 描述:

```xml
<!--
    主窗口布局 —— Phase 6 改为 PlayerBar + 2 列(Sidebar + 当前查看队列):
      Row 0 (Auto): PlayerBar
      Row 1 (*) : Grid 2 列
        Column 0 (160): PlaylistsSidebarView (新建/重命名/删除/▶ 标记)
        Column 1 (*) : PlaylistView, 绑定 Playlists.ViewedPlaylist (UI 选中, 与正在播放可不同)
    Closing 事件由 code-behind 处理: 保存窗口几何 + 触发 VM 清理(VM 内 flush queue.json)。
-->
```

- [ ] **Step 2: Update `Views/Controls/PlaylistView.xaml.cs` — route double-click through PlaylistsViewModel**

In `Views/Controls/PlaylistView.xaml.cs`:

**Replace** `QueueList_MouseDoubleClick` method body:

```csharp
    /// <summary>
    /// 双击列表项 → 通过 PlaylistsViewModel 路由播放 ——
    /// 若双击的歌单不是 CurrentPlaylistId, 容器先切 CurrentPlaylistId(▶ 标记跨歌单移动),
    /// 然后让目标 PlaylistViewModel 跑 PlayTrackAtCommand。空白区双击不触发。
    /// </summary>
    private void QueueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;

        int index = QueueList.ItemContainerGenerator.IndexFromContainer(item);
        if (index < 0) return;

        var container = App.GetService<PlaylistsViewModel>();
        // fire-and-forget: PlaylistsViewModel.HandleDoubleClickPlay 内部 await PlayTrackAtCommand.ExecuteAsync,
        // 此处与 Phase 5 PlayTrackAtCommand.Execute(index) 行为对称(fire-and-forget UI 事件)。
        _ = container.HandleDoubleClickPlay(_vm, index);
        e.Handled = true;
    }
```

**Add** `using UmaPlayer;` at top of the file (for `App.GetService<T>()`).

**Update** `RefreshCurrentIndicator` to gate ▶ marker on `IsActivePlaylist`:

Find this block:

```csharp
            bool isCurrent = (i == _vm.CurrentIndex);
            marker.Text = isCurrent ? "▶" : ""; // ▶
            title.Foreground = isCurrent
                ? (Brush)Application.Current.FindResource("AccentPrimary")
                : (Brush)Application.Current.FindResource("ForegroundPrimary");
```

Replace with:

```csharp
            // Phase 6: 只在 _vm 是当前正在播放的歌单时才显示 ▶/高亮 ——
            // 用户切到别的歌单查看时, 那个歌单的 CurrentIndex 仍然是它自己的本地光标,
            // 但 ▶ 不应在非播放歌单上点亮(否则视觉与音频脱钩)。
            bool isCurrent = _vm.IsActivePlaylist && (i == _vm.CurrentIndex);
            marker.Text = isCurrent ? "▶" : "";
            title.Foreground = isCurrent
                ? (Brush)Application.Current.FindResource("AccentPrimary")
                : (Brush)Application.Current.FindResource("ForegroundPrimary");
```

**Add** PropertyChanged subscription for `IsActivePlaylist` —— 用户切歌单后 ▶ 要刷一次。

In `OnVmPropertyChanged`, replace the body with:

```csharp
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistViewModel.CurrentIndex) ||
            e.PropertyName == nameof(PlaylistViewModel.IsActivePlaylist))
            RefreshCurrentIndicator();
    }
```

- [ ] **Step 3: Verify build + run**

Run: `dotnet build UmaPlayer.csproj`
Expected: `Build succeeded.`

Run: `dotnet run --project UmaPlayer.csproj`
Expected: 应用启动; 出现左侧侧边栏 (空 / 仅 "默认歌单") + 右侧 PlaylistView。点击 + 创建新歌单, 切换查看, 双击播放跨歌单切换 —— 全部应当工作。

- [ ] **Step 4: Commit Sub-project B 完整**

```bash
git add Views/Dialogs/PromptDialog.xaml Views/Dialogs/PromptDialog.xaml.cs \
        Views/Controls/PlaylistsSidebarView.xaml Views/Controls/PlaylistsSidebarView.xaml.cs \
        Views/Controls/PlaylistView.xaml.cs \
        Views/MainWindow.xaml
git commit -m "feat(views): Phase 6 sidebar + prompt dialog + dual-state PlaylistView (B12-B14)"
```


---

## Sub-project C — Debt #1 Converter (~1h)

> 偿债 #1 的最小版本: 仅交付 IValueConverter, PlayerViewModel 不动。带来的好处是给后续替换 `Cover: BitmapImage` → `byte[]` 备好转换器, 当前不切换数据流。

### Task C1: Add `Converters/BytesToBitmapImageConverter.cs`

**Files:**
- Create: `Converters/BytesToBitmapImageConverter.cs`

- [ ] **Step 1: Create the converter**

Write `Converters/BytesToBitmapImageConverter.cs`:

```csharp
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace UmaPlayer.Converters;

/// <summary>
/// Phase 6 偿债 #1(部分): byte[] → 已 Freeze 的 BitmapImage。
/// CacheOption.OnLoad 让 BitmapImage 在 EndInit 前完全读完字节流(否则 BitmapImage 会持有
/// MemoryStream 直到第一次绘制 — 跨线程或源被释放时崩)。Freeze 让结果可以跨线程绑定(WPF UI),
/// 也免掉 INotifyPropertyChanged 的开销。
/// 输入为 null / 空 byte[] / 解码失败 → 返回 null(WPF Image 控件会显示为空)。
/// 仅 OneWay; ConvertBack 抛 NotSupportedException。
/// </summary>
public sealed class BytesToBitmapImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
            return null;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("BytesToBitmapImageConverter is OneWay.");
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build UmaPlayer.csproj`
Expected: `Build succeeded.`

### Task C2: Register Converter in `App.xaml`

**Files:**
- Modify: `App.xaml`

- [ ] **Step 1: Add Converter resource**

In `App.xaml`, add `xmlns:conv` namespace and a Converter resource entry. Replace the file with:

```xml
<!--
    应用级资源 —— 主题字典在此合并到全局, 全应用任何 View 都可通过 StaticResource 引用。
    顺序敏感: Colors 先加载, Fonts/Controls 中可引用其中的画刷资源。

    Phase 6: 注册 BytesToBitmapImage(债务 #1 部分偿还) —— 准备好后续把
    Track.Cover 从 BitmapImage 切换到 byte[] 的转换器, 当前数据流未切换。
-->
<Application x:Class="UmaPlayer.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:UmaPlayer.Converters">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Colors.xaml"/>
                <ResourceDictionary Source="Themes/Fonts.xaml"/>
                <ResourceDictionary Source="Themes/Controls.xaml"/>
            </ResourceDictionary.MergedDictionaries>

            <conv:BytesToBitmapImageConverter x:Key="BytesToBitmapImage"/>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Verify build + commit Sub-project C**

Run: `dotnet build UmaPlayer.csproj`
Expected: `Build succeeded.`

Commit:

```bash
git add Converters/BytesToBitmapImageConverter.cs App.xaml
git commit -m "feat(converters): add BytesToBitmapImageConverter (Phase 6 sub-project C, debt #1 partial)"
```

---

## Documentation (~1h)

### Task D1: Update `docs/PROJECT.md`

**Files:**
- Modify: `docs/PROJECT.md`

- [ ] **Step 1: Bump phase header + status**

Search `docs/PROJECT.md` for "Phase 5"(头部状态行)。把当前 Phase 标记为 **Phase 6 — Named Playlists + Debt #1 + xUnit Skeleton**, 并把 Phase 5 移入 "已完成"段落。

- [ ] **Step 2: Update directory tree**

在 PROJECT.md 的目录树章节中, 加入新增文件:

```
Models/Playlist.cs                           (Phase 6)
Services/IPlaylistService.cs                 (Phase 6, 替换 IQueuePersistence)
Services/JsonPlaylistService.cs              (Phase 6, 替换 JsonQueuePersistence; 内置 v1→v2 迁移)
ViewModels/PlaylistsViewModel.cs             (Phase 6, 多歌单容器)
Views/Controls/PlaylistsSidebarView.xaml(.cs) (Phase 6, 左侧 sidebar)
Views/Dialogs/PromptDialog.xaml(.cs)         (Phase 6, 共享单输入对话框)
Converters/BytesToBitmapImageConverter.cs    (Phase 6, 债务 #1 部分偿还)
Tests/UmaPlayer.Tests.csproj                 (Phase 6, xUnit 骨架)
Tests/Smoke/SmokeTests.cs                    (Phase 6)
```

并标记删除:

```
Services/IQueuePersistence.cs        (Phase 6 移除, 由 IPlaylistService 替代)
Services/JsonQueuePersistence.cs     (Phase 6 移除, 由 JsonPlaylistService 替代)
```

- [ ] **Step 3: Add Phase 6 design decisions**

在 "设计决策"(Design Decisions)章节末尾追加:

```markdown
### Phase 6: 多命名歌单 + Spotify dual-state

- **GUID 主键, Name 仅展示**: 重命名/重复名都不破坏持久化; 切换持久化时音轨绑定不丢。
- **Viewed vs Current 双指针**: ViewedPlaylist(UI 选中, 不持久化) 与 CurrentPlaylistId(正在播放, 持久化) 解耦 —— 用户切查看不打断播放, 只有双击才跨歌单切换音频。
- **Schema v2 一次性自动迁移**: `JsonPlaylistService.LoadAsync` 检测 v1 包成单条 "默认歌单", 立即写盘;迁移失败则吞掉, 内存仍是 v2, 下次重迁(幂等)。
- **删除最后一个歌单自动重建**: 永远不存在 0 歌单状态; UI 不需要"空状态"分支。
- **Debounce save 集中在 MainViewModel**: 子 VM 不感知存盘; PlaylistsViewModel.StateChanged → MainVM 500ms debounce → SaveAsync。CleanupAsync 同步 flush 一次。
- **debt #1 部分偿还**: 仅交付 BytesToBitmapImageConverter, PlayerViewModel.CurrentCover 仍是 BitmapImage。完整切换 byte[] 数据流推迟到 Phase 7+。
```

- [ ] **Step 4: Add walkthroughs**

在 "Walkthroughs"章节追加 4 段:

```markdown
#### 启动 + v1→v2 迁移
App.OnStartup 构建 DI → MainWindow Show → MainWindow.Loaded → MainViewModel.InitializeAsync()
→ JsonPlaylistService.LoadAsync 读 queue.json: SchemaVersion==1 自动迁移成单条 "默认歌单"+ 立即覆盖写;
SchemaVersion==2 则正常反序列化。Hydrate 进 PlaylistsViewModel 后, ViewedPlaylist 默认对齐 CurrentPlaylistId。

#### 创建/重命名/删除歌单
PlaylistsSidebarView + 按钮 → PromptDialog.Show → AddPlaylistCommand/RenamePlaylistCommand → 容器结构变化触发
StateChanged → MainViewModel debounce 500ms 后 SaveAsync。删最后一个 → RemovePlaylistCommand 自动重建 "默认歌单"。

#### 切换查看(viewed) 不打断播放
sidebar 选中 → ViewedPlaylist 改 → MainWindow.xaml DataContext 切换 → PlaylistView 重绑数据;
PlaylistView.RefreshCurrentIndicator 检查 _vm.IsActivePlaylist —— 非当前播放歌单上不显示 ▶。
当前正在播放的 PlayerBar 仍指向 CurrentPlaylistId 对应的歌单, 不变。

#### 双击跨歌单播放
PlaylistView.QueueList_MouseDoubleClick → App.GetService<PlaylistsViewModel>() →
HandleDoubleClickPlay(target, index) → 若 target.Id != CurrentPlaylistId 先切 CurrentPlaylistId
(触发 RecomputeIsActiveFlags + StateChanged) → await target.PlayTrackAtCommand.ExecuteAsync(index)。
sidebar ▶ 标记跟随 CurrentPlaylistId 移动; PlaylistView ▶ 标记按 IsActivePlaylist 显隐。
```

- [ ] **Step 5: Verify build/lint of doc**

无需 build, 但快速预览确认 Markdown 缩进/标题层级一致。

### Task D2: Update `docs/COUPLING.md`

**Files:**
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: Update debt table**

把 debt #1 状态从 "Open" 改成 "Partial(Phase 6 added Converter; data-flow swap pending)"。

- [ ] **Step 2: Update contracts section**

在 "Contracts"章节:
- 删除 `IQueuePersistence` 条目
- 新增 `IPlaylistService`: "Load/Save 整个 v2 QueueState; LoadAsync 永不抛(失败回 seed); Save IO 失败抛 IOException; v1→v2 迁移在 LoadAsync 内一次性完成, 失败吞掉, 下次重迁(幂等)"
- 新增 `PlaylistsViewModel.StateChanged`: "任意子 VM 内部状态/容器结构/CurrentPlaylistId 变化触发; MainViewModel 订阅做 500ms debounce save; IsActivePlaylist 设值不触发(防 echo)"

- [ ] **Step 3: Add "Phase 7 startup checklist"**

在文档末尾追加:

```markdown
## Phase 7 startup checklist

- [ ] 全跑一遍 Phase 6 acceptance(spec §9)前再开新 phase
- [ ] 评估债务 #1 完整偿还: Track.Cover BitmapImage → byte[]?(替 PlayerViewModel.CurrentCover 为 byte[] + XAML 用 BytesToBitmapImage)
- [ ] 评估 PlaylistsViewModel 测试覆盖: HandleDoubleClickPlay / RemovePlaylist 边界 / StateChanged 节流回归
- [ ] 评估 sidebar 拖拽重排歌单顺序(目前只支持新建/重命名/删除, 不支持调序)
```

- [ ] **Step 4: Commit documentation**

```bash
git add docs/PROJECT.md docs/COUPLING.md
git commit -m "docs: update PROJECT.md and COUPLING.md for Phase 6"
```

---

## Self-Review

> 写完计划后,看一遍 spec, 检查覆盖、占位符、类型一致性。把发现的问题就地修, 不必再 review 一次。

- [ ] **Spec coverage**:
  - §3 Schema v2 → B2 + B4 迁移路径 ✓
  - §4 IPlaylistService → B3 (interface), B4 (impl), B5 (delete旧), B6 (DI) ✓
  - §5 PlaylistsViewModel 容器 → B8 ✓
  - §6 PlaylistViewModel 改 → B7 ✓
  - §7 Sidebar UI → B12 (PromptDialog) + B13 (sidebar) + B14 (wiring) ✓
  - §8 xUnit 骨架 → A1-A5 ✓
  - §9 Debt #1 Converter → C1-C2 ✓
  - §10 Acceptance / 操作清单 → 落到 D1 walkthroughs ✓
  - §11 Risks → 已经在 B4(JsonPlaylistService) 注释 + B11(cancel-and-close) 备注里覆盖 ✓
  - §12 Docs → D1, D2 ✓

- [ ] **Placeholder scan**: 全文 grep `TBD|TODO|fill in|TBD|implement later`. 期望: 0 命中。

- [ ] **Type consistency**:
  - `Playlist` record 字段顺序 (Id, Name, Items, CurrentIndex, ShuffleEnabled, RepeatMode) — B1 定义, B4 / B7 / B8 / B14 一致 ✓
  - `IPlaylistService.LoadAsync()` / `SaveAsync(QueueState)` 签名 — B3 / B4 / B6 / B9 一致 ✓
  - `PlaylistsViewModel.HandleDoubleClickPlay(PlaylistViewModel, int)` — B8 / B14 一致 ✓
  - `App.GetService<T>()` — B10 暴露, B14 调用 ✓
  - `PlaylistView.RefreshCurrentIndicator` ▶ 标记由 B14 加 IsActivePlaylist guard, 与 B7 加的 ObservableProperty 名 `IsActivePlaylist` 一致 ✓
  - `MainViewModel(PlayerViewModel, PlaylistsViewModel, IPlaybackService, IPlaylistService)` 构造 — B6 DI 注册需要全部 4 项 ✓

- [ ] **Build green points**: A5 (commit A), B6 之后 build 仍红 (B2 起的 schema 错误未 close), B11 Step 5 (Phase 6 第一次绿), B14 Step 3 (UI 完整后绿), C2 Step 2 (绿)。

> 自审完成。

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-06-13-uma-player-phase6-named-playlists.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — 我每个 task 派一个 fresh subagent, 中间做两段 review (spec compliance + code quality), 快速迭代。
**2. Inline Execution** — 在当前会话里按 batch 跑, 中途 checkpoint 给你看进展。

**Which approach?**
