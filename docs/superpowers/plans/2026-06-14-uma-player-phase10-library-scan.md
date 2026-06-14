# Phase 10: 文件夹绑定歌单（库扫描）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 UmaPlayer 添加文件夹绑定歌单——用户指定文件夹，递归扫描音频文件读取元数据，创建自动同步的歌单。

**Architecture:** 扩展现有 `Playlist` record 加 `SourceFolder: string?`；新建 `ILibraryScannerService`（扫描+Diff）和 `ILibraryCache`（元数据持久化缓存）；`PlaylistsViewModel` 新增 ImportFolder/Rescan/Refresh 命令；UI 通过 PlaylistView 工具栏按钮 + sidebar 📂 标识呈现。

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit, NSubstitute, System.Text.Json

---

### Task 1: Models — Playlist SourceFolder + QueueState v3

**Files:**
- Modify: `Models/Playlist.cs`
- Modify: `Models/QueueState.cs`
- Modify: `ViewModels/PlaylistsViewModel.cs:101-107` (AddPlaylist Playlist 构造)
- Modify: `ViewModels/PlaylistsViewModel.cs:128-136` (RemovePlaylist defaultSeed 构造)
- Modify: `Services/JsonPlaylistService.cs:107-121` (Seed 构造)
- Modify: `Services/JsonPlaylistService.cs:137-144` (MigrateV1ToV2 构造)

- [ ] **Step 1: Add SourceFolder to Playlist record**

```csharp
// Models/Playlist.cs — 替换整个文件
namespace UmaPlayer.Models;

/// <summary>
/// 一个命名歌单(Phase 6)。Id 是创建时生成的 GUID, 主键, 不可变;
/// Name 仅展示, 可重复可重命名。ShuffleEnabled / RepeatMode / CurrentIndex
/// 下沉到歌单级别 —— 各歌单独立, 不再共享顶层状态。
/// SourceFolder (Phase 10): 非 null 表示文件夹绑定歌单, Items 由扫描结果填充。
/// </summary>
public sealed record Playlist(
    string Id,
    string Name,
    IReadOnlyList<string> Items,
    int CurrentIndex,
    bool ShuffleEnabled,
    RepeatMode RepeatMode,
    string? SourceFolder = null);
```

- [ ] **Step 2: Update QueueState to schema v3**

```csharp
// Models/QueueState.cs — 替换整个文件
namespace UmaPlayer.Models;

/// <summary>
/// v3 队列持久化快照(Phase 10)。v2→v3: Playlist 新增 SourceFolder 字段。
/// 历史: v1 仅承载单一队列, v2 加多命名歌单, v3 加文件夹绑定。
/// SourceFolder 缺失时 System.Text.Json 反序列化为 null, 无需显式迁移。
/// SchemaVersion 永远写 3; ViewedPlaylistId 不持久化。
/// </summary>
public sealed record QueueState
{
    public int SchemaVersion { get; init; } = 3;
    public IReadOnlyList<Playlist> Playlists { get; init; } = Array.Empty<Playlist>();
    public string CurrentPlaylistId { get; init; } = string.Empty;
}
```

- [ ] **Step 3: Update JsonPlaylistService for v3**

在 `Services/JsonPlaylistService.cs` 中：
1. 把 `CurrentSchemaVersion` 从 `2` 改为 `3`
2. `LoadAsync` 中 version==2 的分支也接受（v2→v3 无需迁移，SourceFolder 反序列化为 null）
3. Seed 方法的 Playlist 构造加 `SourceFolder: null`（可选，已有默认值）

```csharp
// Services/JsonPlaylistService.cs — 修改常量
private const int CurrentSchemaVersion = 3;
```

```csharp
// Services/JsonPlaylistService.cs — LoadAsync 中 version 判断改为接受 2 和 3
if (version is 2 or 3)
{
    QueueState? loaded;
    try { loaded = JsonSerializer.Deserialize<QueueState>(text, JsonOptions); }
    catch { return Seed(); }

    if (loaded is null || loaded.Playlists.Count == 0)
        return Seed();

    var match = loaded.Playlists.Any(p => p.Id == loaded.CurrentPlaylistId);
    return match ? loaded : loaded with { CurrentPlaylistId = loaded.Playlists[0].Id };
}
```

- [ ] **Step 4: Update all Playlist construction sites**

PlaylistsViewModel.cs 中有两处 `new Playlist(...)` 构造（AddPlaylist 和 RemovePlaylist 的 defaultSeed）。它们已经用了命名参数，`SourceFolder` 有默认值 `null`，所以**不需要修改**——编译器会自动应用默认值。

同样，JsonPlaylistService.cs 的 Seed() 和 MigrateV1ToV2 中的构造也不需要改。

- [ ] **Step 5: Build and verify compilation**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded, 0 errors

- [ ] **Step 6: Run existing tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj -v minimal`
Expected: All 54 tests pass (no regressions)

- [ ] **Step 7: Commit**

```bash
git add Models/Playlist.cs Models/QueueState.cs Services/JsonPlaylistService.cs
git commit -m "feat(models): add Playlist.SourceFolder + QueueState v3 (Phase 10)"
```

---

### Task 2: Models — LibraryCacheEntry + LibraryDiff

**Files:**
- Create: `Models/LibraryCacheEntry.cs`
- Create: `Models/LibraryDiff.cs`

- [ ] **Step 1: Create LibraryCacheEntry record**

```csharp
// Models/LibraryCacheEntry.cs
namespace UmaPlayer.Models;

/// <summary>
/// 文件夹绑定歌单的元数据缓存条目(Phase 10)。
/// 不含 AlbumArt 字节——按需加载, 保持缓存文件体积小。
/// 用于 library-cache.json 持久化。
/// </summary>
public sealed record LibraryCacheEntry(
    string FilePath,
    string Title,
    string? Artist,
    string? Album,
    string? Genre,
    int? Year,
    TimeSpan Duration,
    int? SampleRate);
```

- [ ] **Step 2: Create LibraryDiff record**

```csharp
// Models/LibraryDiff.cs
namespace UmaPlayer.Models;

/// <summary>
/// 文件夹扫描增量同步结果(Phase 10)。
/// AddedPaths: 新增文件的绝对路径(需读元数据)。
/// RemovedPaths: 已从磁盘删除的文件路径(需从 Queue 移除)。
/// Unchanged: 未变化的缓存条目(可直接转 Track, 无需重新读元数据)。
/// </summary>
public sealed record LibraryDiff(
    IReadOnlyList<string> AddedPaths,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<LibraryCacheEntry> Unchanged);
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Models/LibraryCacheEntry.cs Models/LibraryDiff.cs
git commit -m "feat(models): add LibraryCacheEntry + LibraryDiff records (Phase 10)"
```

---

### Task 3: Service — ILibraryScannerService + LibraryScannerService

**Files:**
- Create: `Services/ILibraryScannerService.cs`
- Create: `Services/LibraryScannerService.cs`
- Create: `Tests/Services/LibraryScannerServiceTests.cs`

- [ ] **Step 1: Write ScanFolder tests**

```csharp
// Tests/Services/LibraryScannerServiceTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using UmaPlayer.Models;
using UmaPlayer.Services;
using Xunit;

namespace UmaPlayer.Tests.Services;

public class LibraryScannerServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly ITrackMetadataReader _metadataReader = Substitute.For<ITrackMetadataReader>();
    private readonly LibraryScannerService _sut;

    public LibraryScannerServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "UmaPlayerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _sut = new LibraryScannerService(_metadataReader);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void ScanFolder_ReturnsAudioFiles()
    {
        File.WriteAllText(Path.Combine(_testDir, "song.mp3"), "");
        File.WriteAllText(Path.Combine(_testDir, "song.flac"), "");
        File.WriteAllText(Path.Combine(_testDir, "readme.txt"), "");

        var result = _sut.ScanFolder(_testDir);

        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.True(
            p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ScanFolder_ScansSubdirectories()
    {
        var subDir = Path.Combine(_testDir, "Album");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "track.mp3"), "");

        var result = _sut.ScanFolder(_testDir);

        Assert.Single(result);
        Assert.Contains("track.mp3", result[0]);
    }

    [Fact]
    public void ScanFolder_EmptyDirectory_ReturnsEmpty()
    {
        var result = _sut.ScanFolder(_testDir);
        Assert.Empty(result);
    }

    [Fact]
    public void ScanFolder_NonexistentDirectory_ReturnsEmpty()
    {
        var result = _sut.ScanFolder(Path.Combine(_testDir, "nonexistent"));
        Assert.Empty(result);
    }

    [Fact]
    public void ScanFolder_IsCaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_testDir, "song.MP3"), "");
        File.WriteAllText(Path.Combine(_testDir, "song.Flac"), "");

        var result = _sut.ScanFolder(_testDir);

        Assert.Equal(2, result.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --filter "FullyQualifiedName~LibraryScannerServiceTests" -v minimal`
Expected: FAIL — `LibraryScannerService` does not exist

- [ ] **Step 3: Create ILibraryScannerService interface**

```csharp
// Services/ILibraryScannerService.cs
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 音乐库扫描抽象(Phase 10)。递归扫描文件夹、批量读元数据、增量 Diff。
/// </summary>
public interface ILibraryScannerService
{
    /// <summary>
    /// 递归扫描文件夹, 返回所有音频文件的绝对路径(白名单后缀过滤)。
    /// 目录不存在或无权限时返回空集合(绝不抛)。
    /// </summary>
    IReadOnlyList<string> ScanFolder(string folderPath);

    /// <summary>
    /// 批量读取元数据。对每个路径调用 ITrackMetadataReader.ReadAsync, 失败静默跳过。
    /// </summary>
    Task<IReadOnlyList<Track>> ReadMetadataBatchAsync(IReadOnlyList<string> paths);

    /// <summary>
    /// 增量同步: 比较磁盘文件与缓存, 返回 (added, removed, unchanged) 三组。
    /// 路径比较用 Path.GetFullPath 标准化 + OrdinalIgnoreCase。
    /// </summary>
    LibraryDiff ComputeDiff(IReadOnlyList<string> currentFiles, IReadOnlyList<LibraryCacheEntry> cached);
}
```

- [ ] **Step 4: Implement LibraryScannerService**

```csharp
// Services/LibraryScannerService.cs
using UmaPlayer.Models;
using UmaPlayer.Views.Controls;

namespace UmaPlayer.Services;

/// <summary>
/// ILibraryScannerService 的实现(Phase 10)。
/// ScanFolder 复用 DragDropExtensions.AudioExtensions 白名单;
/// ComputeDiff 用 File.GetLastWriteTimeUtc 比较修改时间。
/// </summary>
public sealed class LibraryScannerService : ILibraryScannerService
{
    private readonly ITrackMetadataReader _metadataReader;

    public LibraryScannerService(ITrackMetadataReader metadataReader)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
    }

    public IReadOnlyList<string> ScanFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return Array.Empty<string>();

        try
        {
            var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories);
            return DragDropExtensions.FilterAudioPaths(files);
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyList<Track>> ReadMetadataBatchAsync(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0)
            return Array.Empty<Track>();

        var tracks = new List<Track>(paths.Count);
        foreach (var path in paths)
        {
            try
            {
                var track = await _metadataReader.ReadAsync(path).ConfigureAwait(false);
                if (track is not null)
                    tracks.Add(track);
            }
            catch
            {
                // 单文件失败静默跳过, 与 PlayTrackAtAsync 策略一致
            }
        }
        return tracks;
    }

    public LibraryDiff ComputeDiff(IReadOnlyList<string> currentFiles, IReadOnlyList<LibraryCacheEntry> cached)
    {
        if (currentFiles is null || currentFiles.Count == 0)
        {
            return new LibraryDiff(
                AddedPaths: Array.Empty<string>(),
                RemovedPaths: cached?.Select(e => e.FilePath).ToArray() ?? Array.Empty<string>(),
                Unchanged: Array.Empty<LibraryCacheEntry>());
        }

        if (cached is null || cached.Count == 0)
        {
            return new LibraryDiff(
                AddedPaths: currentFiles.ToArray(),
                RemovedPaths: Array.Empty<string>(),
                Unchanged: Array.Empty<LibraryCacheEntry>());
        }

        // 标准化当前文件路径并建立修改时间字典
        var currentNormalized = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in currentFiles)
        {
            var normalized = Path.GetFullPath(path);
            try
            {
                currentNormalized[normalized] = File.GetLastWriteTimeUtc(normalized);
            }
            catch
            {
                // 文件不可访问, 跳过
            }
        }

        // 建立缓存字典(路径已标准化)
        var cachedDict = new Dictionary<string, LibraryCacheEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in cached)
        {
            cachedDict[entry.FilePath] = entry;
        }

        var added = new List<string>();
        var removed = new List<string>();
        var unchanged = new List<LibraryCacheEntry>();

        // 找新增 + 未变化
        foreach (var (filePath, writeTime) in currentNormalized)
        {
            if (cachedDict.TryGetValue(filePath, out var entry))
            {
                // 比较修改时间: 若缓存中没有 Duration/SampleRate 信息(旧缓存), 视为需更新
                if (entry.Duration == TimeSpan.Zero && entry.SampleRate is null or 0)
                {
                    added.Add(filePath);
                }
                else
                {
                    unchanged.Add(entry);
                }
                cachedDict.Remove(filePath); // 标记已处理
            }
            else
            {
                added.Add(filePath);
            }
        }

        // 剩余的缓存条目 = 已从磁盘删除
        foreach (var entry in cachedDict.Values)
        {
            removed.Add(entry.FilePath);
        }

        return new LibraryDiff(AddedPaths: added, RemovedPaths: removed, Unchanged: unchanged);
    }
}
```

- [ ] **Step 5: Run ScanFolder tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --filter "FullyQualifiedName~LibraryScannerServiceTests" -v minimal`
Expected: All 5 ScanFolder tests pass

- [ ] **Step 6: Write ComputeDiff tests**

追加到 `Tests/Services/LibraryScannerServiceTests.cs`：

```csharp
    [Fact]
    public void ComputeDiff_EmptyCurrentAndCache_ReturnsEmpty()
    {
        var diff = _sut.ComputeDiff(Array.Empty<string>(), Array.Empty<LibraryCacheEntry>());
        Assert.Empty(diff.AddedPaths);
        Assert.Empty(diff.RemovedPaths);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_EmptyCurrentNonEmptyCache_ReturnsAllRemoved()
    {
        var cached = new List<LibraryCacheEntry>
        {
            new("C:\\a.mp3", "A", null, null, null, null, TimeSpan.FromMinutes(3), 44100)
        };

        var diff = _sut.ComputeDiff(Array.Empty<string>(), cached);

        Assert.Empty(diff.AddedPaths);
        Assert.Single(diff.RemovedPaths);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_NonEmptyCurrentEmptyCache_ReturnsAllAdded()
    {
        var current = new List<string> { "C:\\a.mp3", "C:\\b.mp3" };

        var diff = _sut.ComputeDiff(current, Array.Empty<LibraryCacheEntry>());

        Assert.Equal(2, diff.AddedPaths.Count);
        Assert.Empty(diff.RemovedPaths);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_MatchingPaths_ReturnsUnchanged()
    {
        var testFile = Path.Combine(_testDir, "song.mp3");
        File.WriteAllText(testFile, "test");
        var current = new List<string> { testFile };
        var cached = new List<LibraryCacheEntry>
        {
            new(Path.GetFullPath(testFile), "Song", null, null, null, null, TimeSpan.FromMinutes(3), 44100)
        };

        var diff = _sut.ComputeDiff(current, cached);

        Assert.Empty(diff.AddedPaths);
        Assert.Empty(diff.RemovedPaths);
        Assert.Single(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_FileDeletedFromDisk_ReturnsRemoved()
    {
        var current = new List<string> { "C:\\exists.mp3" };
        var cached = new List<LibraryCacheEntry>
        {
            new("C:\\exists.mp3", "A", null, null, null, null, TimeSpan.FromMinutes(3), 44100),
            new("C:\\deleted.mp3", "B", null, null, null, null, TimeSpan.FromMinutes(2), 44100)
        };

        var diff = _sut.ComputeDiff(current, cached);

        Assert.Empty(diff.AddedPaths);
        Assert.Single(diff.RemovedPaths);
        Assert.Equal("C:\\deleted.mp3", diff.RemovedPaths[0]);
        Assert.Single(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_CaseInsensitivePathMatching()
    {
        var current = new List<string> { "C:\\Music\\Song.MP3" };
        var cached = new List<LibraryCacheEntry>
        {
            new("c:\\music\\song.mp3", "Song", null, null, null, null, TimeSpan.FromMinutes(3), 44100)
        };

        var diff = _sut.ComputeDiff(current, cached);

        Assert.Empty(diff.AddedPaths);
        Assert.Empty(diff.RemovedPaths);
        Assert.Single(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_CachedWithZeroDuration_TreatedAsAdded()
    {
        var testFile = Path.Combine(_testDir, "song.mp3");
        File.WriteAllText(testFile, "test");
        var current = new List<string> { testFile };
        var cached = new List<LibraryCacheEntry>
        {
            new(Path.GetFullPath(testFile), "Song", null, null, null, null, TimeSpan.Zero, 0)
        };

        var diff = _sut.ComputeDiff(current, cached);

        Assert.Single(diff.AddedPaths);
        Assert.Empty(diff.RemovedPaths);
        Assert.Empty(diff.Unchanged);
    }
```

- [ ] **Step 7: Run ComputeDiff tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --filter "FullyQualifiedName~LibraryScannerServiceTests" -v minimal`
Expected: All tests pass

- [ ] **Step 8: Write ReadMetadataBatchAsync tests**

追加到测试文件：

```csharp
    [Fact]
    public async Task ReadMetadataBatchAsync_EmptyPaths_ReturnsEmpty()
    {
        var result = await _sut.ReadMetadataBatchAsync(Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadMetadataBatchAsync_NullPaths_ReturnsEmpty()
    {
        var result = await _sut.ReadMetadataBatchAsync(null!);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadMetadataBatchAsync_ReadsAllPaths()
    {
        var paths = new List<string> { "a.mp3", "b.flac" };
        _metadataReader.ReadAsync("a.mp3").Returns(new Track("a.mp3", "A", null, null, null, null, null, null, TimeSpan.Zero));
        _metadataReader.ReadAsync("b.flac").Returns(new Track("b.flac", "B", null, null, null, null, null, null, TimeSpan.Zero));

        var result = await _sut.ReadMetadataBatchAsync(paths);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReadMetadataBatchAsync_SkipsFailedReads()
    {
        var paths = new List<string> { "good.mp3", "bad.mp3" };
        _metadataReader.ReadAsync("good.mp3").Returns(new Track("good.mp3", "Good", null, null, null, null, null, null, TimeSpan.Zero));
        _metadataReader.ReadAsync("bad.mp3").Returns(Task.FromException<Track>(new IOException("corrupt")));

        var result = await _sut.ReadMetadataBatchAsync(paths);

        Assert.Single(result);
        Assert.Equal("good.mp3", result[0].FilePath);
    }
```

- [ ] **Step 9: Run all LibraryScannerService tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --filter "FullyQualifiedName~LibraryScannerServiceTests" -v minimal`
Expected: All tests pass

- [ ] **Step 10: Commit**

```bash
git add Services/ILibraryScannerService.cs Services/LibraryScannerService.cs Tests/Services/LibraryScannerServiceTests.cs
git commit -m "feat(services): add ILibraryScannerService + LibraryScannerService (Phase 10)"
```

---

### Task 4: Service — ILibraryCache + JsonLibraryCache

**Files:**
- Create: `Services/ILibraryCache.cs`
- Create: `Services/JsonLibraryCache.cs`
- Create: `Tests/Services/JsonLibraryCacheTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// Tests/Services/JsonLibraryCacheTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UmaPlayer.Models;
using UmaPlayer.Services;
using Xunit;

namespace UmaPlayer.Tests.Services;

public class JsonLibraryCacheTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _originalLocalAppData;

    public JsonLibraryCacheTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "UmaPlayerCacheTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        // 重定向 LocalApplicationData 到临时目录
        _originalLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private JsonLibraryCache CreateSut()
    {
        // 直接注入路径以避免依赖系统目录
        return new JsonLibraryCache(_testDir);
    }

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsEmpty()
    {
        var sut = CreateSut();
        var result = await sut.LoadAsync("C:\\Music");
        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var sut = CreateSut();
        var entries = new List<LibraryCacheEntry>
        {
            new("C:\\Music\\a.mp3", "Song A", "Artist 1", "Album X", "Rock", 2020, TimeSpan.FromMinutes(3), 44100),
            new("C:\\Music\\b.flac", "Song B", "Artist 2", "Album Y", null, null, TimeSpan.FromMinutes(4), 96000),
        };

        await sut.SaveAsync("C:\\Music", entries);
        var loaded = await sut.LoadAsync("C:\\Music");

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Song A", loaded[0].Title);
        Assert.Equal("Artist 2", loaded[1].Artist);
    }

    [Fact]
    public async Task SaveAsync_MultipleFolders_Independently()
    {
        var sut = CreateSut();
        var entriesA = new List<LibraryCacheEntry>
        {
            new("C:\\A\\a.mp3", "A", null, null, null, null, TimeSpan.Zero, null),
        };
        var entriesB = new List<LibraryCacheEntry>
        {
            new("D:\\B\\b.mp3", "B", null, null, null, null, TimeSpan.Zero, null),
        };

        await sut.SaveAsync("C:\\A", entriesA);
        await sut.SaveAsync("D:\\B", entriesB);

        var loadedA = await sut.LoadAsync("C:\\A");
        var loadedB = await sut.LoadAsync("D:\\B");

        Assert.Single(loadedA);
        Assert.Equal("A", loadedA[0].Title);
        Assert.Single(loadedB);
        Assert.Equal("B", loadedB[0].Title);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmpty()
    {
        var sut = CreateSut();
        // 写入损坏的 JSON
        var cachePath = Path.Combine(_testDir, "library-cache.json");
        await File.WriteAllTextAsync(cachePath, "{invalid json");

        var result = await sut.LoadAsync("C:\\Music");
        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveAsync_OverwritesPreviousEntries()
    {
        var sut = CreateSut();
        var entries1 = new List<LibraryCacheEntry>
        {
            new("C:\\a.mp3", "A", null, null, null, null, TimeSpan.Zero, null),
        };
        var entries2 = new List<LibraryCacheEntry>
        {
            new("C:\\b.mp3", "B", null, null, null, null, TimeSpan.Zero, null),
            new("C:\\c.mp3", "C", null, null, null, null, TimeSpan.Zero, null),
        };

        await sut.SaveAsync("C:\\Music", entries1);
        await sut.SaveAsync("C:\\Music", entries2);
        var loaded = await sut.LoadAsync("C:\\Music");

        Assert.Equal(2, loaded.Count);
        Assert.Equal("B", loaded[0].Title);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --filter "FullyQualifiedName~JsonLibraryCacheTests" -v minimal`
Expected: FAIL — `JsonLibraryCache` does not exist

- [ ] **Step 3: Create ILibraryCache interface**

```csharp
// Services/ILibraryCache.cs
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 文件夹绑定歌单的元数据缓存抽象(Phase 10)。
/// 所有文件夹绑定歌单共用一个缓存文件, 按 SourceFolder 路径分组。
/// 契约: LoadAsync 绝不抛(失败回空列表), SaveAsync IO 失败抛 IOException。
/// </summary>
public interface ILibraryCache
{
    Task<IReadOnlyList<LibraryCacheEntry>> LoadAsync(string folderPath);
    Task SaveAsync(string folderPath, IReadOnlyList<LibraryCacheEntry> entries);
}
```

- [ ] **Step 4: Implement JsonLibraryCache**

```csharp
// Services/JsonLibraryCache.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// library-cache.json 的 JSON 实现(Phase 10)。
/// 结构: Dictionary&lt;string, IReadOnlyList&lt;LibraryCacheEntry&gt;&gt;
/// 键 = 标准化后的 SourceFolder 路径。
/// SemaphoreSlim(1,1) 保护读写, 与 JsonSettingsPersistence 模式一致。
/// </summary>
public sealed class JsonLibraryCache : ILibraryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonLibraryCache() : this(null) { }

    /// <summary>测试用构造: 可指定目录(避免依赖系统 LocalApplicationData)。</summary>
    internal JsonLibraryCache(string? overrideDir)
    {
        var dir = overrideDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UmaPlayer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "library-cache.json");
    }

    public async Task<IReadOnlyList<LibraryCacheEntry>> LoadAsync(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        var normalizedKey = Path.GetFullPath(folderPath);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return Array.Empty<LibraryCacheEntry>();

            string text;
            await using (var stream = File.OpenRead(_path))
            using (var reader = new StreamReader(stream))
                text = await reader.ReadToEndAsync().ConfigureAwait(false);

            Dictionary<string, List<LibraryCacheEntry>>? dict;
            try { dict = JsonSerializer.Deserialize<Dictionary<string, List<LibraryCacheEntry>>>(text, JsonOptions); }
            catch { return Array.Empty<LibraryCacheEntry>(); }

            if (dict is null || !dict.TryGetValue(normalizedKey, out var entries))
                return Array.Empty<LibraryCacheEntry>();

            return entries;
        }
        catch
        {
            return Array.Empty<LibraryCacheEntry>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string folderPath, IReadOnlyList<LibraryCacheEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        ArgumentNullException.ThrowIfNull(entries);
        var normalizedKey = Path.GetFullPath(folderPath);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            Dictionary<string, List<LibraryCacheEntry>> dict;

            if (File.Exists(_path))
            {
                string text;
                await using (var stream = File.OpenRead(_path))
                using (var reader = new StreamReader(stream))
                    text = await reader.ReadToEndAsync().ConfigureAwait(false);

                try { dict = JsonSerializer.Deserialize<Dictionary<string, List<LibraryCacheEntry>>>(text, JsonOptions) ?? new(); }
                catch { dict = new(); }
            }
            else
            {
                dict = new();
            }

            dict[normalizedKey] = entries.ToList();

            // 原子写: 先写 .tmp 再 Move (与 JsonPlaylistService 同纪律)
            var tmp = _path + ".tmp";
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, dict, JsonOptions).ConfigureAwait(false);
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

- [ ] **Step 5: Run all JsonLibraryCache tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --filter "FullyQualifiedName~JsonLibraryCacheTests" -v minimal`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add Services/ILibraryCache.cs Services/JsonLibraryCache.cs Tests/Services/JsonLibraryCacheTests.cs
git commit -m "feat(services): add ILibraryCache + JsonLibraryCache (Phase 10)"
```

---

### Task 5: Service — IFileDialogService.OpenFolder

**Files:**
- Modify: `Services/IFileDialogService.cs`
- Modify: `Services/Win32FileDialogService.cs`

- [ ] **Step 1: Add OpenFolder to IFileDialogService**

```csharp
// Services/IFileDialogService.cs — 替换整个文件
namespace UmaPlayer.Services;

/// <summary>
/// 文件/文件夹选择对话框抽象，便于单元测试以 Mock 替换。
///
/// [STA Thread Required] —— Win32 OpenFileDialog 必须在 STA 线程调用。
/// 当前由 VM 的 RelayCommand 在 UI 线程触发，符合要求；
/// 若从后台线程调用会抛 InvalidOperationException。
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// 弹出文件选择对话框。
    /// </summary>
    /// <param name="filter">WPF 格式过滤器，如 "Audio Files|*.mp3;*.wav"。</param>
    /// <param name="multiselect">是否允许多选；默认 false 保持原行为。</param>
    /// <returns>用户选中的文件路径列表；取消则返回空集合。</returns>
    IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false);

    /// <summary>
    /// 弹出文件夹选择对话框(Phase 10)。
    /// </summary>
    /// <returns>用户选中的文件夹路径；取消则返回 null。</returns>
    string? OpenFolder();
}
```

- [ ] **Step 2: Implement OpenFolder in Win32FileDialogService**

```csharp
// Services/Win32FileDialogService.cs — 替换整个文件
using Microsoft.Win32;

namespace UmaPlayer.Services;

/// <summary>
/// 基于 Microsoft.Win32 的文件/文件夹对话框实现。
/// 默认单选；调用方可通过 multiselect=true 启用多选（用于播放列表入队）。
/// OpenFolder 使用 VistaFolderBrowser (WPF 内置)。
/// </summary>
public sealed class Win32FileDialogService : IFileDialogService
{
    public IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Multiselect = multiselect
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames.ToList().AsReadOnly()
            : Array.Empty<string>();
    }

    public string? OpenFolder()
    {
        // WPF 没有原生 FolderBrowserDialog; 使用 OpenFolderDialog (.NET 8+ 内置)
        var dialog = new OpenFolderDialog
        {
            Title = "选择音乐文件夹"
        };

        return dialog.ShowDialog() == true
            ? dialog.FolderName
            : null;
    }
}
```

> **注意**: `OpenFolderDialog` 是 .NET 8+ WPF 内置的。如果目标框架不支持，需改用 `System.Windows.Forms.FolderBrowserDialog`（需引用 `Microsoft.Windows.Compatibility` NuGet 包）。当前项目用 `net10.0-windows`，应直接可用。

- [ ] **Step 3: Build and verify**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 4: Run existing tests (确保 mock 接口兼容)**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj -v minimal`
Expected: All tests pass（NSubstitute 自动 mock 新方法，无需改测试）

- [ ] **Step 5: Commit**

```bash
git add Services/IFileDialogService.cs Services/Win32FileDialogService.cs
git commit -m "feat(services): add IFileDialogService.OpenFolder (Phase 10)"
```

---

### Task 6: DI — Register new services

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Add registrations**

```csharp
// Extensions/ServiceCollectionExtensions.cs — 在 ITrackMetadataReader 注册之后、ViewModel 之前插入
```

在 `services.AddSingleton<ITrackMetadataReader, AtlMetadataReader>();` 之后添加：

```csharp
        // Phase 10: 库扫描 + 元数据缓存
        services.AddSingleton<ILibraryScannerService, LibraryScannerService>();
        services.AddSingleton<ILibraryCache, JsonLibraryCache>();
```

完整文件：

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmaPlayer.Configuration;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Extensions;

/// <summary>
/// DI 容器注册中心 —— 集中维护服务的生命周期与实现绑定，
/// 让 App.OnStartup 只需一行 `services.AddUmaPlayerServices(configuration)`。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUmaPlayerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 配置 —— 仅绑定 "Player" 子节，避免与其他节冲突
        services.Configure<AppSettings>(configuration.GetSection("Player"));

        // 业务服务（Singleton —— 持有音频设备/文件句柄等长期资源）
        services.AddSingleton<IPlaybackService, NAudioPlaybackService>();
        services.AddSingleton<IFileDialogService, Win32FileDialogService>();
        services.AddSingleton<ISettingsPersistence, JsonSettingsPersistence>();

        // Phase 6: 多歌单持久化 (替换 IQueuePersistence)
        services.AddSingleton<IPlaylistService, JsonPlaylistService>();

        // 预留服务 —— 注册 Stub 以便未来替换不需要改 DI
        services.AddSingleton<IAudioDeviceManager, StubAudioDeviceManager>();

        // 输出工厂（Transient —— 每次调用都新建一个 IWavePlayer，
        // 由 IPlaybackService 负责释放生命周期）
        services.AddTransient<IAudioOutputFactory, StubAudioOutputFactory>();

        // 元数据读取（Singleton —— 无状态、纯函数式接口）
        services.AddSingleton<ITrackMetadataReader, AtlMetadataReader>();

        // Phase 10: 库扫描 + 元数据缓存
        services.AddSingleton<ILibraryScannerService, LibraryScannerService>();
        services.AddSingleton<ILibraryCache, JsonLibraryCache>();

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

        return services;
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(di): register ILibraryScannerService + ILibraryCache (Phase 10)"
```

---

### Task 7: ViewModel — PlaylistViewModel IsScanning/HasScanError

**Files:**
- Modify: `ViewModels/PlaylistViewModel.cs`
- Create: `Tests/ViewModels/PlaylistViewModelLibraryTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// Tests/ViewModels/PlaylistViewModelLibraryTests.cs
using System;
using NSubstitute;
using UmaPlayer.Models;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;
using Xunit;

namespace UmaPlayer.Tests.ViewModels;

public class PlaylistViewModelLibraryTests
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly IFileDialogService _fileDialog = Substitute.For<IFileDialogService>();
    private readonly ITrackMetadataReader _metadataReader = Substitute.For<ITrackMetadataReader>();

    private PlaylistViewModel CreateVm(string? sourceFolder = null)
    {
        var seed = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: "Test",
            Items: Array.Empty<string>(),
            CurrentIndex: -1,
            ShuffleEnabled: false,
            RepeatMode: RepeatMode.Off,
            SourceFolder: sourceFolder);
        return new PlaylistViewModel(seed, _player, _fileDialog, _metadataReader);
    }

    [Fact]
    public void IsScanning_DefaultsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public void HasScanError_DefaultsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.HasScanError);
    }

    [Fact]
    public void IsScanning_CanBeSetAndNotifies()
    {
        var vm = CreateVm();
        var notified = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsScanning)) notified = true; };

        vm.IsScanning = true;

        Assert.True(vm.IsScanning);
        Assert.True(notified);
    }

    [Fact]
    public void HasScanError_CanBeSetAndNotifies()
    {
        var vm = CreateVm();
        var notified = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.HasScanError)) notified = true; };

        vm.HasScanError = true;

        Assert.True(vm.HasScanError);
        Assert.True(notified);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --filter "FullyQualifiedName~PlaylistViewModelLibraryTests" -v minimal`
Expected: FAIL — `IsScanning` / `HasScanError` not found

- [ ] **Step 3: Add properties to PlaylistViewModel**

在 `ViewModels/PlaylistViewModel.cs` 中：

1. 在 `IsActivePlaylist` 属性之后添加新属性：

```csharp
    /// <summary>该歌单是否正在后台扫描(Phase 10)。由 PlaylistsViewModel 设置。</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>最近一次扫描是否失败(Phase 10)。由 PlaylistsViewModel 设置。</summary>
    [ObservableProperty]
    private bool _hasScanError;
```

2. 在 `Id` 属性之后添加 `SourceFolder` 只读属性：

```csharp
    /// <summary>文件夹绑定歌单的源文件夹路径; null 表示普通歌单(Phase 10)。</summary>
    public string? SourceFolder { get; }
```

3. 在构造函数中，`Name = seed.Name;` 之后赋值：

```csharp
        SourceFolder = seed.SourceFolder;
```

4. 修改 `ToRecord()` 方法，添加 `SourceFolder` 参数：

```csharp
    public Models.Playlist ToRecord() => new(
        Id: Id,
        Name: Name,
        Items: Queue.Select(t => t.FilePath).ToArray(),
        CurrentIndex: CurrentIndex,
        ShuffleEnabled: ShuffleEnabled,
        RepeatMode: RepeatMode,
        SourceFolder: SourceFolder);
```

5. 添加派生属性（供 sidebar DataTemplate 使用）：

```csharp
    /// <summary>是否为文件夹绑定歌单(Phase 10)。sidebar DataTemplate 用。</summary>
    public bool HasSourceFolder => SourceFolder is not null;
```

- [ ] **Step 4: Run tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --filter "FullyQualifiedName~PlaylistViewModelLibraryTests" -v minimal`
Expected: All 4 tests pass

- [ ] **Step 5: Run all tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj -v minimal`
Expected: All tests pass (no regressions)

- [ ] **Step 6: Commit**

```bash
git add ViewModels/PlaylistViewModel.cs Tests/ViewModels/PlaylistViewModelLibraryTests.cs
git commit -m "feat(vm): add PlaylistViewModel.IsScanning/HasScanError (Phase 10)"
```

---

### Task 8: ViewModel — PlaylistsViewModel ImportFolder/Rescan/Refresh

**Files:**
- Modify: `ViewModels/PlaylistsViewModel.cs`
- Create: `Tests/ViewModels/PlaylistsViewModelLibraryTests.cs`

- [ ] **Step 1: Write ImportFolder tests**

```csharp
// Tests/ViewModels/PlaylistsViewModelLibraryTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using UmaPlayer.Models;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;
using Xunit;

namespace UmaPlayer.Tests.ViewModels;

public class PlaylistsViewModelLibraryTests
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly IFileDialogService _fileDialog = Substitute.For<IFileDialogService>();
    private readonly ITrackMetadataReader _metadataReader = Substitute.For<ITrackMetadataReader>();
    private readonly ILibraryScannerService _scanner = Substitute.For<ILibraryScannerService>();
    private readonly ILibraryCache _cache = Substitute.For<ILibraryCache>();

    private PlaylistViewModel CreatePlaylistVm(Playlist seed)
    {
        _metadataReader.CreateFallback(Arg.Any<string>())
            .Returns(ci => new Track(ci.ArgAt<string>(0), ci.ArgAt<string>(0), null, null, null, null, null, null, TimeSpan.Zero));
        return new PlaylistViewModel(seed, _player, _fileDialog, _metadataReader);
    }

    private PlaylistsViewModel CreateContainerVm()
    {
        return new PlaylistsViewModel(
            seed => CreatePlaylistVm(seed),
            _fileDialog,
            _scanner,
            _cache);
    }

    [Fact]
    public async Task ImportFolderAsync_NullFolder_DoesNothing()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });
        _fileDialog.OpenFolder().Returns((string?)null);

        await container.ImportFolderAsync();

        Assert.Single(container.Playlists); // 没有新增
    }

    [Fact]
    public async Task ImportFolderAsync_WithFolder_CreatesFolderBoundPlaylist()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });

        _fileDialog.OpenFolder().Returns("C:\\Music");
        _scanner.ScanFolder("C:\\Music").Returns(new List<string> { "C:\\Music\\a.mp3" });
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(new List<Track> { new("C:\\Music\\a.mp3", "Song", null, null, null, null, null, null, TimeSpan.Zero) });

        await container.ImportFolderAsync();

        Assert.Equal(2, container.Playlists.Count);
        var newPl = container.Playlists[1];
        Assert.Equal("Music", newPl.Name);
        Assert.Single(newPl.Queue);
        Assert.Equal("Song", newPl.Queue[0].Title);
    }

    [Fact]
    public async Task ImportFolderAsync_SetsViewedPlaylist()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });

        _fileDialog.OpenFolder().Returns("C:\\Music");
        _scanner.ScanFolder("C:\\Music").Returns(new List<string> { "C:\\Music\\a.mp3" });
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(new List<Track> { new("C:\\Music\\a.mp3", "Song", null, null, null, null, null, null, TimeSpan.Zero) });

        await container.ImportFolderAsync();

        Assert.Equal(container.Playlists[1], container.ViewedPlaylist);
    }

    [Fact]
    public async Task ImportFolderAsync_FiresStateChanged()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });

        _fileDialog.OpenFolder().Returns("C:\\Music");
        _scanner.ScanFolder("C:\\Music").Returns(new List<string> { "C:\\Music\\a.mp3" });
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(new List<Track> { new("C:\\Music\\a.mp3", "Song", null, null, null, null, null, null, TimeSpan.Zero) });

        var fired = false;
        container.StateChanged += (_, _) => fired = true;

        await container.ImportFolderAsync();

        Assert.True(fired);
    }

    [Fact]
    public async Task RescanFolderBoundPlaylistsAsync_UpdatesQueueOnNewFiles()
    {
        var container = CreateContainerVm();
        var snapshot = new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "Music", new[] { "C:\\Music\\old.mp3" }, -1, false, RepeatMode.Off, SourceFolder: "C:\\Music"),
            },
            CurrentPlaylistId = "id1"
        };
        _metadataReader.CreateFallback(Arg.Any<string>())
            .Returns(ci => new Track(ci.ArgAt<string>(0), System.IO.Path.GetFileName(ci.ArgAt<string>(0)), null, null, null, null, null, null, TimeSpan.Zero));
        container.Hydrate(snapshot);

        _cache.LoadAsync("C:\\Music").Returns(new List<LibraryCacheEntry>
        {
            new("C:\\Music\\old.mp3", "Old", null, null, null, null, TimeSpan.FromMinutes(3), 44100)
        });
        _scanner.ScanFolder("C:\\Music").Returns(new List<string> { "C:\\Music\\old.mp3", "C:\\Music\\new.mp3" });
        _scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(
                AddedPaths: new[] { "C:\\Music\\new.mp3" },
                RemovedPaths: Array.Empty<string>(),
                Unchanged: new List<LibraryCacheEntry>
                {
                    new("C:\\Music\\old.mp3", "Old", null, null, null, null, TimeSpan.FromMinutes(3), 44100)
                }));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(new List<Track> { new("C:\\Music\\new.mp3", "New", null, null, null, null, null, null, TimeSpan.Zero) });

        await container.RescanFolderBoundPlaylistsAsync();

        Assert.Equal(2, container.Playlists[0].Queue.Count);
        Assert.Contains(container.Playlists[0].Queue, t => t.Title == "New");
    }

    [Fact]
    public async Task RescanFolderBoundPlaylistsAsync_SkipsNormalPlaylists()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "Normal", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });

        await container.RescanFolderBoundPlaylistsAsync();

        // 没有 SourceFolder → scanner 不应被调用
        _scanner.DidNotReceive().ScanFolder(Arg.Any<string>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj --filter "FullyQualifiedName~PlaylistsViewModelLibraryTests" -v minimal`
Expected: FAIL — constructor / methods not found

- [ ] **Step 3: Modify PlaylistsViewModel constructor and add fields**

在 `ViewModels/PlaylistsViewModel.cs` 中：

1. 添加新依赖字段和构造函数参数
2. 添加 ImportFolderAsync / RescanFolderBoundPlaylistsAsync / RefreshPlaylistAsync 方法

```csharp
// ViewModels/PlaylistsViewModel.cs — 修改构造函数和添加新成员
```

替换整个文件为：

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
///
/// Phase 10: 新增 ImportFolder/Rescan/Refresh 命令, 支持文件夹绑定歌单。
/// </summary>
public sealed partial class PlaylistsViewModel : ObservableObject
{
    private readonly Func<Playlist, PlaylistViewModel> _factory;
    private readonly IFileDialogService _fileDialog;
    private readonly ILibraryScannerService _scanner;
    private readonly ILibraryCache _cache;

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

    public PlaylistsViewModel(
        Func<Playlist, PlaylistViewModel> factory,
        IFileDialogService fileDialog,
        ILibraryScannerService scanner,
        ILibraryCache cache)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        Playlists.CollectionChanged += OnPlaylistsCollectionChanged;
    }

    // Phase 10: 为向后兼容的无注入构造(测试用)
    internal PlaylistsViewModel(Func<Playlist, PlaylistViewModel> factory)
        : this(factory,
            new NullFileDialogService(),
            new NullLibraryScannerService(),
            new NullLibraryCache()) { }

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

        if (index >= 0 && index < target.Queue.Count)
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

    /// <summary>
    /// sidebar 拖拽重排：把 sourceIndex 处的歌单移到 targetIndex 之前。
    /// targetIndex == Playlists.Count 表示移到末尾。
    /// ViewedPlaylist / CurrentPlaylistId 跟随对象身份，不因位置变化而改变。
    /// </summary>
    [RelayCommand]
    private void MovePlaylist((int SourceIndex, int TargetIndex) args)
    {
        var (src, tgt) = args;
        if (src < 0 || src >= Playlists.Count) return;
        if (tgt < 0 || tgt > Playlists.Count) return;
        if (src == tgt || src == tgt - 1) return; // 拖到原位 = no-op

        var item = Playlists[src];
        Playlists.RemoveAt(src);

        // 删源后, 若 target 在源之后, 索引前移 1
        if (tgt > src) tgt--;
        Playlists.Insert(tgt, item);

        // ViewedPlaylist 跟随对象身份（ObservableCollection 移动同一引用）
        ViewedPlaylist = item;
    }

    // —— Phase 10: 文件夹绑定歌单命令 ——

    /// <summary>
    /// 选文件夹 → 扫描 → 创建文件夹绑定歌单 → 缓存 → StateChanged。
    /// </summary>
    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var folderPath = _fileDialog.OpenFolder();
        if (folderPath is null) return;

        var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName)) folderName = folderPath; // 根目录情况

        var paths = _scanner.ScanFolder(folderPath);
        var tracks = await _scanner.ReadMetadataBatchAsync(paths).ConfigureAwait(true);

        var seed = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: folderName,
            Items: tracks.Select(t => t.FilePath).ToArray(),
            CurrentIndex: -1,
            ShuffleEnabled: false,
            RepeatMode: RepeatMode.Off,
            SourceFolder: Path.GetFullPath(folderPath));

        var vm = _factory(seed);
        // 直接设置 Queue 内容(构造函数会用 CreateFallback 占位, 这里替换为完整元数据)
        vm.Queue.Clear();
        foreach (var track in tracks)
            vm.Queue.Add(track);

        HookPlaylistVm(vm);
        Playlists.Add(vm);
        ViewedPlaylist = vm;

        // 写缓存
        var entries = tracks.Select(t => new LibraryCacheEntry(
            FilePath: t.FilePath,
            Title: t.Title,
            Artist: t.Artist,
            Album: t.Album,
            Genre: t.Genre,
            Year: t.Year,
            Duration: t.Duration,
            SampleRate: t.SampleRate)).ToList();

        try { await _cache.SaveAsync(Path.GetFullPath(folderPath), entries).ConfigureAwait(false); }
        catch (IOException) { /* 缓存写盘失败不阻塞 */ }
    }

    /// <summary>
    /// 启动后台扫描所有文件夹绑定歌单。由 MainViewModel.InitializeAsync fire-and-forget 调用。
    /// </summary>
    internal async Task RescanFolderBoundPlaylistsAsync()
    {
        foreach (var vm in Playlists)
        {
            if (vm.ToRecord().SourceFolder is not { } sourceFolder)
                continue;

            await RescanSinglePlaylistAsync(vm, sourceFolder).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 手动刷新单个文件夹绑定歌单。
    /// </summary>
    [RelayCommand]
    private async Task RefreshPlaylistAsync(PlaylistViewModel? playlist)
    {
        if (playlist?.ToRecord().SourceFolder is not { } sourceFolder)
            return;

        await RescanSinglePlaylistAsync(playlist, sourceFolder).ConfigureAwait(true);
    }

    private async Task RescanSinglePlaylistAsync(PlaylistViewModel vm, string sourceFolder)
    {
        var normalized = Path.GetFullPath(sourceFolder);

        vm.IsScanning = true;
        vm.HasScanError = false;

        try
        {
            var cached = await _cache.LoadAsync(normalized).ConfigureAwait(false);
            var currentFiles = _scanner.ScanFolder(normalized);

            if (currentFiles.Count == 0 && cached.Count == 0)
                return;

            var diff = _scanner.ComputeDiff(currentFiles, cached);

            // 读新增文件的元数据
            IReadOnlyList<Track> newTracks = Array.Empty<Track>();
            if (diff.AddedPaths.Count > 0)
            {
                newTracks = await _scanner.ReadMetadataBatchAsync(diff.AddedPaths).ConfigureAwait(false);
            }

            // 构建更新后的缓存条目
            var updatedEntries = new List<LibraryCacheEntry>();

            // Unchanged: 直接保留
            updatedEntries.AddRange(diff.Unchanged);

            // 新增: 转为缓存条目
            foreach (var track in newTracks)
            {
                updatedEntries.Add(new LibraryCacheEntry(
                    FilePath: track.FilePath,
                    Title: track.Title,
                    Artist: track.Artist,
                    Album: track.Album,
                    Genre: track.Genre,
                    Year: track.Year,
                    Duration: track.Duration,
                    SampleRate: track.SampleRate));
            }

            // 更新 Queue: 先移除已删除的, 再添加新增的
            // 倒序移除以保持索引稳定(与 MoveTracks 的倒序删源同理)
            var removedIndices = diff.RemovedPaths
                .Select(p => FindTrackIndexByPath(vm.Queue, p))
                .Where(i => i >= 0)
                .OrderByDescending(i => i)
                .ToList();

            foreach (var idx in removedIndices)
            {
                vm.Queue.RemoveAt(idx);
                // 修正 CurrentIndex (与 PlaylistViewModel.RemoveTrack 同逻辑)
                if (idx == vm.CurrentIndex)
                {
                    vm.CurrentIndex = -1;
                }
                else if (idx < vm.CurrentIndex)
                {
                    vm.CurrentIndex--;
                }
            }

            foreach (var track in newTracks)
            {
                vm.Queue.Add(track);
            }

            // 写缓存
            try { await _cache.SaveAsync(normalized, updatedEntries).ConfigureAwait(false); }
            catch (IOException) { /* 缓存写盘失败不阻塞 */ }
        }
        catch (DirectoryNotFoundException)
        {
            vm.HasScanError = true;
        }
        catch (UnauthorizedAccessException)
        {
            vm.HasScanError = true;
        }
        finally
        {
            vm.IsScanning = false;
        }
    }

    /// <summary>按 FilePath 查找 Track 在 ObservableCollection 中的索引。</summary>
    private static int FindTrackIndexByPath(ObservableCollection<Track> queue, string filePath)
    {
        for (int i = 0; i < queue.Count; i++)
        {
            if (string.Equals(queue[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    // —— 内部 Null 实现(向后兼容无注入的测试构造函数) ——

    private sealed class NullFileDialogService : IFileDialogService
    {
        public IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false) => Array.Empty<string>();
        public string? OpenFolder() => null;
    }

    private sealed class NullLibraryScannerService : ILibraryScannerService
    {
        public IReadOnlyList<string> ScanFolder(string folderPath) => Array.Empty<string>();
        public Task<IReadOnlyList<Track>> ReadMetadataBatchAsync(IReadOnlyList<string> paths) => Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>());
        public LibraryDiff ComputeDiff(IReadOnlyList<string> currentFiles, IReadOnlyList<LibraryCacheEntry> cached) =>
            new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<LibraryCacheEntry>());
    }

    private sealed class NullLibraryCache : ILibraryCache
    {
        public Task<IReadOnlyList<LibraryCacheEntry>> LoadAsync(string folderPath) => Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(Array.Empty<LibraryCacheEntry>());
        public Task SaveAsync(string folderPath, IReadOnlyList<LibraryCacheEntry> entries) => Task.CompletedTask;
    }

    // —— 原有成员(不变) ——

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
        vm.Queue.CollectionChanged += OnPlaylistTracksChanged;
    }

    private void UnhookPlaylistVm(PlaylistViewModel vm)
    {
        vm.PropertyChanged -= OnPlaylistVmPropertyChanged;
        vm.Queue.CollectionChanged -= OnPlaylistTracksChanged;
        vm.Cleanup();
    }

    private void OnPlaylistVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistViewModel.IsActivePlaylist)) return;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaylistTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => StateChanged?.Invoke(this, EventArgs.Empty);

    private void OnPlaylistsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => StateChanged?.Invoke(this, EventArgs.Empty);
}
```

> **注意**: `RescanSinglePlaylistAsync` 中直接操作 `vm.Queue.RemoveAt(idx)` + 手动调整 `vm.CurrentIndex`，不通过 `RemoveTrackCommand`（后者会 `_playToken++` 且是 private）。后台扫描是同步批量操作，不需要顶替 playToken。

- [ ] **Step 4: Update existing PlaylistsViewModelTests**

现有测试使用 `new PlaylistsViewModel(seed => CreatePlaylistVm(seed.Id, seed.Name))` —— 这是无注入构造函数，已通过 `Null*` 实现保留兼容。无需修改现有测试。

- [ ] **Step 5: Run all tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj -v minimal`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add ViewModels/PlaylistsViewModel.cs Tests/ViewModels/PlaylistsViewModelLibraryTests.cs
git commit -m "feat(vm): add PlaylistsViewModel ImportFolder/Rescan/Refresh (Phase 10)"
```

---

### Task 9: ViewModel — MainViewModel InitializeAsync fire-and-forget

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add RescanFolderBoundPlaylistsAsync call**

在 `ViewModels/MainViewModel.cs` 的 `InitializeAsync` 方法中，`_hydrated = true;` 之前添加：

```csharp
        // Phase 10: 后台扫描文件夹绑定歌单(不阻塞 UI, 不阻塞 InitializeAsync 返回)
        _ = Playlists.RescanFolderBoundPlaylistsAsync();
```

完整 `InitializeAsync`:

```csharp
    public async Task InitializeAsync()
    {
        if (_hydrated) return;
        var snapshot = await _playlistService.LoadAsync().ConfigureAwait(true);
        Playlists.Hydrate(snapshot);
        Playlists.StateChanged += OnPlaylistsStateChanged;
        _hydrated = true;

        // Phase 10: 后台扫描文件夹绑定歌单(不阻塞 UI)
        _ = Playlists.RescanFolderBoundPlaylistsAsync();
    }
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 3: Run all tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj -v minimal`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "feat(vm): MainViewModel.InitializeAsync triggers background rescan (Phase 10)"
```

---

### Task 10: View — PlaylistView toolbar (ImportFolder + Refresh buttons)

**Files:**
- Modify: `Views/Controls/PlaylistView.xaml`

- [ ] **Step 1: Add ImportFolder button to toolbar**

在 `Views/Controls/PlaylistView.xaml` 中，工具栏左侧 StackPanel 内，在「清空」按钮之后添加「📂 导入文件夹」按钮：

```xml
            <!-- 左侧：添加 / 导入文件夹 / 清空 -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Left">
                <Button Command="{Binding AddToQueueCommand}" Padding="12,4">
                    <TextBlock Text="+ 添加" FontSize="12"/>
                </Button>
                <Button Padding="12,4" Margin="8,0,0,0"
                        Command="{Binding DataContext.Playlists.ImportFolderCommand,
                                 RelativeSource={RelativeSource AncestorType=Window}}">
                    <TextBlock Text="📂 导入文件夹" FontSize="12"/>
                </Button>
                <Button Command="{Binding ClearQueueCommand}" Padding="12,4" Margin="8,0,0,0">
                    <TextBlock Text="清空" FontSize="12"/>
                </Button>
            </StackPanel>
```

- [ ] **Step 2: Add Refresh button to right side toolbar**

在右侧 StackPanel（随机/循环按钮）之前，添加一个刷新按钮，仅在文件夹绑定歌单时显示：

```xml
            <!-- 右侧：刷新 / 随机 / 循环 -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <!-- 刷新按钮: 仅文件夹绑定歌单可见 -->
                <Button Width="32" Height="32"
                        Background="Transparent" BorderThickness="0"
                        ToolTip="刷新文件夹"
                        Command="{Binding DataContext.Playlists.RefreshPlaylistCommand,
                                 RelativeSource={RelativeSource AncestorType=Window}}"
                        CommandParameter="{Binding}">
                    <Button.Style>
                        <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
                            <Setter Property="Visibility" Value="Collapsed"/>
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding SourceFolder}" Value="">
                                    <Setter Property="Visibility" Value="Visible"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                    <TextBlock Text="🔄" FontSize="14"
                               FontFamily="Segoe UI Emoji"
                               HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Button>
                <Button Command="{Binding ToggleShuffleCommand}" Width="32" Height="32"
                        Background="Transparent" BorderThickness="0"
                        ToolTip="随机播放">
                    <!-- ... 原有 Shuffle 按钮内容 ... -->
```

> **注意**: `SourceFolder` 和 `HasSourceFolder` 属性已在 Task 7 中添加到 PlaylistViewModel。此处直接使用。

需要在 `UserControl.Resources` 中添加 `BooleanToVisibilityConverter`：

```xml
    <UserControl.Resources>
        <converters:RepeatModeToIconConverter x:Key="RepeatModeToIcon"/>
        <converters:BoolToAccentBrushConverter x:Key="BoolToAccentBrush"/>
        <BooleanToVisibilityConverter x:Key="BoolToVisibility"/>
    </UserControl.Resources>
```

刷新按钮 XAML（使用 `HasSourceFolder` + `BoolToVisibility`）：

```xml
                <Button Width="32" Height="32"
                        Background="Transparent" BorderThickness="0"
                        ToolTip="刷新文件夹"
                        Visibility="{Binding HasSourceFolder, Converter={StaticResource BoolToVisibility}}"
                        Command="{Binding DataContext.Playlists.RefreshPlaylistCommand,
                                 RelativeSource={RelativeSource AncestorType=Window}}"
                        CommandParameter="{Binding}">
                    <TextBlock Text="🔄" FontSize="14"
                               FontFamily="Segoe UI Emoji"
                               HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Button>
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add Views/Controls/PlaylistView.xaml ViewModels/PlaylistViewModel.cs
git commit -m "feat(view): PlaylistView toolbar adds ImportFolder + Refresh buttons (Phase 10)"
```

---

### Task 11: View — PlaylistsSidebarView (📂 icon + 🔄 scanning indicator)

**Files:**
- Modify: `Views/Controls/PlaylistsSidebarView.xaml`

- [ ] **Step 1: Add 📂 prefix and 🔄 scanning indicator**

在 `PlaylistsSidebarView.xaml` 的 DataTemplate 中，修改列布局为 3 列：▶ 标记 | 📂/🔄 前缀 | 歌单名。

```xml
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid Margin="0,2">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="14"/>
                            <ColumnDefinition Width="16"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <!-- ▶ 标记由 code-behind 在 RefreshActiveMarker 中按 IsActivePlaylist 写入 -->
                        <TextBlock Grid.Column="0"
                                   FontSize="12"
                                   Foreground="{StaticResource AccentPrimary}"/>
                        <!-- 📂 文件夹图标 / 🔄 扫描中 -->
                        <TextBlock Grid.Column="1"
                                   FontSize="11"
                                   VerticalAlignment="Center">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Text" Value=""/>
                                    <Style.Triggers>
                                        <!-- 扫描中: 显示 🔄 -->
                                        <DataTrigger Binding="{Binding IsScanning}" Value="True">
                                            <Setter Property="Text" Value="🔄"/>
                                        </DataTrigger>
                                        <!-- 文件夹绑定歌单且未在扫描: 显示 📂 -->
                                        <DataTrigger Binding="{Binding HasSourceFolder}" Value="True">
                                            <Setter Property="Text" Value="📂"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>
                        <TextBlock Grid.Column="2"
                                   Text="{Binding Name}"
                                   TextTrimming="CharacterEllipsis"
                                   VerticalAlignment="Center"/>
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
```

> **注意**: 多个 DataTrigger 的优先级——WPF 中多个 DataTrigger 按声明顺序应用，最后一个匹配的生效。所以把 `IsScanning=True` 放在 `HasSourceFolder=True` 之后，扫描中时 🔄 会覆盖 📂。但上面的写法 IsScanning 在前，HasSourceFolder 在后——当 IsScanning=True 且 HasSourceFolder=True 时，HasSourceFolder 会覆盖 IsScanning。需要调换顺序或合并。

**修正**: 把 `IsScanning` trigger 放在 `HasSourceFolder` trigger **之后**：

```xml
                            <Style.Triggers>
                                <!-- 文件夹绑定歌单: 显示 📂 -->
                                <DataTrigger Binding="{Binding HasSourceFolder}" Value="True">
                                    <Setter Property="Text" Value="📂"/>
                                </DataTrigger>
                                <!-- 扫描中(覆盖 📂): 显示 🔄 -->
                                <DataTrigger Binding="{Binding IsScanning}" Value="True">
                                    <Setter Property="Text" Value="🔄"/>
                                </DataTrigger>
                            </Style.Triggers>
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add Views/Controls/PlaylistsSidebarView.xaml
git commit -m "feat(view): sidebar shows 📂 for folder-bound playlists + 🔄 scanning (Phase 10)"
```

---

### Task 12: Update PROJECT.md and COUPLING.md

**Files:**
- Modify: `docs/PROJECT.md`
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: Update PROJECT.md**

在 §1.1 关键特性表格中添加 Phase 10 行：

```
| 文件夹绑定歌单 | 指定文件夹扫描 → 创建歌单; 启动后台自动同步增删; 手动刷新; 元数据缓存 (Phase 10) |
```

在 §1 项目简介末尾添加 Phase 10 描述。

在 §3 目录结构中添加新文件。

在 §4.1 架构图中添加 `ILibraryScannerService` 和 `ILibraryCache`。

在 §4.2 服务生命周期表中添加新服务。

在 §5.4b PlaylistViewModel 中添加 Phase 10 新成员说明。

在 §5.4 PlaylistsViewModel 中添加 Phase 10 新命令说明。

在 §6.3 中添加 library-cache.json 说明。

在 §10 历史中添加 Phase 10 commit。

- [ ] **Step 2: Update COUPLING.md**

在 §5 隐式契约表中添加 Phase 10 新契约。

在 §6 Phase 7 启动检查清单中更新 Phase 10 候选状态。

- [ ] **Step 3: Commit**

```bash
git add docs/PROJECT.md docs/COUPLING.md
git commit -m "docs: update PROJECT.md and COUPLING.md for Phase 10"
```

---

### Task 13: Full test suite + manual acceptance

- [ ] **Step 1: Run complete test suite**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj -v normal`
Expected: All tests pass (existing 54 + new ~25 = ~79 tests)

- [ ] **Step 2: Build and run the app**

Run: `dotnet run --project UmaPlayer.csproj`
Expected: App launches, sidebar shows existing playlists, PlaylistView toolbar has 「📂 导入文件夹」button

- [ ] **Step 3: Manual acceptance test**

1. 点击「📂 导入文件夹」→ 选择一个包含音频文件的文件夹
2. 验证：sidebar 出现新歌单（带 📂 前缀），PlaylistView 显示扫描到的曲目
3. 关闭并重启应用
4. 验证：文件夹绑定歌单仍存在，曲目从缓存加载
5. 验证：sidebar 🔄 指示短暂出现后消失（后台扫描完成）

- [ ] **Step 4: Final commit (if any fixes needed)**

```bash
git add -A
git commit -m "fix: address Phase 10 manual acceptance findings"
```
