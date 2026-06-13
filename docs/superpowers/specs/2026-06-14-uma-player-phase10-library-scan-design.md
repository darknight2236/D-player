# Phase 10 设计规格：文件夹绑定歌单（库扫描）

> 日期：2026/06/14 · 分支：`master` · 前置：Phase 9（sidebar 歌单拖拽重排）

---

## 1. 目标

为 UmaPlayer 添加音乐库扫描能力：用户指定文件夹，递曲扫描音频文件，读取元数据，创建文件夹绑定歌单。歌单启动时后台自动同步文件增删，用户也可手动刷新。

**核心价值**：将 UmaPlayer 从"手动逐文件入队"升级为"文件夹即歌单"的音乐库管理模式。

---

## 2. 设计决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| UI 入口 | 智能播放列表形式出现在 sidebar | 复用现有歌单 UI，零新增面板 |
| 数据模型 | 扩展现有 `Playlist` record 加 `SourceFolder: string?` | 最小改动，null=普通歌单，非null=文件夹绑定 |
| 同步策略 | 启动后台扫描 + 手动刷新 | 不阻塞 UI，歌单可即时使用旧数据 |
| 创建方式 | PlaylistView 工具栏「导入文件夹」按钮 | 与 sidebar ➕（新建空歌单）职责分离 |
| 显示组织 | 平铺列表，复用现有 PlaylistView | 零 UI 结构改动 |
| 元数据缓存 | 持久化到 `library-cache.json` | 启动即可显示标题/艺术家/专辑，无需重新扫描 |

---

## 3. 数据模型变更

### 3.1 Playlist record 扩展

```csharp
public sealed record Playlist
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = "默认歌单";
    public string? SourceFolder { get; init; }  // ← 新增
    public IReadOnlyList<Track> Items { get; init; } = Array.Empty<Track>();
    public int CurrentIndex { get; init; } = -1;
    public bool ShuffleEnabled { get; init; }
    public RepeatMode RepeatMode { get; init; }
}
```

- `SourceFolder` 为 `null`：普通歌单，行为与现有完全一致
- `SourceFolder` 非 `null`：文件夹绑定歌单，`Items` 由扫描结果填充

### 3.2 QueueState schema v3

```csharp
public sealed record QueueState
{
    public int SchemaVersion { get; init; } = 3;  // 2→3
    public IReadOnlyList<Playlist> Playlists { get; init; } = Array.Empty<Playlist>();
    public string? CurrentPlaylistId { get; init; }
}
```

v2→v3 迁移：`SourceFolder` 字段缺失时 `System.Text.Json` 反序列化为 `null`，无需显式迁移逻辑。

### 3.3 LibraryCacheEntry（新增）

```csharp
public sealed record LibraryCacheEntry
{
    public string FilePath { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string Genre { get; init; } = "";
    public int Year { get; init; }
    public TimeSpan Duration { get; init; }
    public int SampleRate { get; init; }
}
```

不含封面字节数组（按需加载），体积小。用于 `library-cache.json` 持久化。

---

## 4. 新服务：ILibraryScannerService

```csharp
public interface ILibraryScannerService
{
    /// <summary>
    /// 递归扫描文件夹，返回所有音频文件路径（白名单后缀过滤）。
    /// </summary>
    IReadOnlyList<string> ScanFolder(string folderPath);

    /// <summary>
    /// 批量读取元数据。对每个路径调用 ITrackMetadataReader.ReadAsync，失败静默跳过。
    /// </summary>
    Task<IReadOnlyList<Track>> ReadMetadataBatchAsync(IReadOnlyList<string> paths);

    /// <summary>
    /// 增量同步：比较磁盘文件与缓存，返回 (added, removed, unchanged) 三组。
    /// </summary>
    LibraryDiff ComputeDiff(IReadOnlyList<string> currentFiles, IReadOnlyList<LibraryCacheEntry> cached);
}

public sealed record LibraryDiff
{
    public IReadOnlyList<string> AddedPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RemovedPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<LibraryCacheEntry> Unchanged { get; init; } = Array.Empty<LibraryCacheEntry>();
}
```

### 4.1 实现：LibraryScannerService

| 方法 | 实现 |
|------|------|
| `ScanFolder` | `Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)` + `DragDropExtensions.AudioExtensions` 白名单过滤（复用同一数据源） |
| `ReadMetadataBatchAsync` | 遍历路径逐个调 `ITrackMetadataReader.ReadAsync`，失败返回 null → 过滤掉 |
| `ComputeDiff` | 用 `File.GetLastWriteTimeUtc` 比较修改时间；路径用 `Path.GetFullPath` + `OrdinalIgnoreCase` 规范化 |

**依赖**：`ITrackMetadataReader`（已有的 AtlMetadataReader）

**生命周期**：Singleton（无状态）

---

## 5. 缓存层：JsonLibraryCache

```csharp
public interface ILibraryCache
{
    Task<IReadOnlyList<LibraryCacheEntry>> LoadAsync(string folderPath);
    Task SaveAsync(string folderPath, IReadOnlyList<LibraryCacheEntry> entries);
}
```

### 5.1 缓存文件结构

路径：`%LocalAppData%\UmaPlayer\library-cache.json`

```json
{
  "C:\\Music": [
    { "FilePath": "C:\\Music\\song1.mp3", "Title": "...", "Artist": "...", ... },
    { "FilePath": "C:\\Music\\song2.flac", "Title": "...", "Artist": "...", ... }
  ],
  "D:\\Albums": [
    { "FilePath": "D:\\Albums\\track1.mp3", "Title": "...", "Artist": "...", ... }
  ]
}
```

所有文件夹绑定歌单共用一个缓存文件。键为标准化后的 `SourceFolder` 路径。

### 5.2 实现纪律

- `SemaphoreSlim(1,1)` 保护读写（与 `JsonSettingsPersistence` / `JsonPlaylistService` 模式一致）
- `LoadAsync` 绝不抛（catch-all fallback 到空列表，与 `IPlaylistService.LoadAsync` 隐式契约一致）
- `SaveAsync` 失败抛出
- `FilePath` 用 `Path.GetFullPath` 标准化后存储
- `WriteIndented = true`，`JsonStringEnumConverter`

### 5.3 生命周期

Singleton，DI 注册。

---

## 6. ViewModel 变更

### 6.1 PlaylistsViewModel（主要改动）

新增依赖注入：`ILibraryScannerService`、`ILibraryCache`

新增命令：

```csharp
[RelayCommand]
private async Task ImportFolderAsync();  // 选文件夹 → 扫描 → 创建文件夹绑定歌单

internal async Task RescanFolderBoundPlaylistsAsync();  // 启动后台扫描

[RelayCommand]
private async Task RefreshPlaylistAsync(PlaylistViewModel playlist);  // 手动刷新单个歌单
```

新增属性：

```csharp
[ObservableProperty]
private bool _isGlobalScanning;  // 全局扫描状态（用于 sidebar 指示）
```

#### ImportFolderAsync 流程

```
1. IFileDialogService.OpenFolder() → folderPath (null 则取消)
2. ScanFolder(folderPath) → paths
3. ReadMetadataBatchAsync(paths) → tracks
4. new Playlist { Name=folderName, SourceFolder=folderPath, Items=tracks }
5. Playlists.Add(playlist)
6. ViewedPlaylist = 对应的 PlaylistVM
7. libraryCache.SaveAsync(folderPath, entries)
8. StateChanged → debounce save
```

#### RescanFolderBoundPlaylistsAsync 流程

```
对每个 SourceFolder != null 的 PlaylistVM（串行，避免并发磁盘 IO）:
  1. vm.IsScanning = true
  2. libraryCache.LoadAsync(sourceFolder) → cached
  3. ScanFolder(sourceFolder) → currentFiles
  4. ComputeDiff(currentFiles, cached) → diff
  5. IF diff 为空 → 跳过 6-7
  6. ReadMetadataBatchAsync(diff.AddedPaths) → newTracks
  7. 合并: Unchanged→Track + newTracks, 过滤 removed
  8. 更新 vm.Queue（增量增删）：
     - 先移除 diff.RemovedPaths 对应的 Track（逐个 Remove，复用现有 RemoveTrack 逻辑保护 CurrentIndex）
     - 再 Add diff.AddedPaths 对应的新 Track 到末尾
     - 不 Clear —— 保护正在播放的曲目和 CurrentIndex
  9. libraryCache.SaveAsync(合并后的 entries)
  10. vm.IsScanning = false
  11. StateChanged
```

**关键**：后台扫描期间歌单可正常使用旧数据。扫描完成后静默更新。

#### RefreshPlaylistAsync 流程

与 RescanFolderBoundPlaylistsAsync 相同，但只处理单个歌单。

### 6.2 PlaylistViewModel — 新增属性

```csharp
[ObservableProperty]
private bool _isScanning;  // 该歌单是否正在扫描

[ObservableProperty]
private bool _hasScanError;  // 最近一次扫描是否失败
```

`SourceFolder` 的语义由 PlaylistsViewModel 管理，PlaylistVM 本身只暴露状态标志。

### 6.3 IFileDialogService 扩展

```csharp
public interface IFileDialogService
{
    IReadOnlyList<string> OpenFiles(string filter);  // 已有
    string? OpenFolder();  // ← 新增
}
```

具体实现方式在 plan 阶段确定（WPF 无原生 FolderBrowserDialog，可用 `Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog` 或 `System.Windows.Forms.FolderBrowserDialog`）。

### 6.4 MainViewModel — 小改动

```csharp
public async Task InitializeAsync()
{
    // 现有: Hydrate playlists from disk
    // 新增:
    _ = _playlists.RescanFolderBoundPlaylistsAsync();  // fire-and-forget
}
```

---

## 7. View 变更

### 7.1 PlaylistView 工具栏

现有：`[+ 添加] [清空]                    🔀  [⇄/🔁/📂]`

新增：`[+ 添加] [📂 导入文件夹] [清空]     🔄  🔀  [⇄/🔁/📂]`

- 「📂 导入文件夹」绑到 `PlaylistsViewModel.ImportFolderCommand`
- 跨级绑定：`{Binding DataContext.Playlists.ImportFolderCommand, RelativeSource={RelativeSource AncestorType=Window}}`

### 7.2 Sidebar 标识

文件夹绑定歌单显示 📂 前缀：

```
  ▶ 默认歌单
    我的收藏
  📂 Music Library
```

- `DataTemplate` 中用 `DataTrigger` 绑 `SourceFolder` 非 null 时显示 📂

### 7.3 刷新按钮

PlaylistView 工具栏右侧区域新增 🔄 按钮：

- 仅当 `ViewedPlaylist.SourceFolder != null` 时显示（`DataTrigger`）
- 绑到 `PlaylistsViewModel.RefreshPlaylistCommand`
- 参数为当前 `ViewedPlaylist`

### 7.4 扫描状态指示

后台扫描中，sidebar 歌单名后显示 🔄：

```
  📂 Music Library 🔄   ← 扫描中
  📂 Music Library       ← 扫描完成
```

- 绑到 `PlaylistViewModel.IsScanning`
- `DataTrigger` 控制 🔄 TextBlock 的 `Visibility`

---

## 8. 数据流

### 8.1 首次导入文件夹

```
用户点击 [📂 导入文件夹]
  → PlaylistsViewModel.ImportFolderAsync
  → OpenFolder() → "D:\Music"
  → ScanFolder → paths
  → ReadMetadataBatchAsync → tracks
  → new Playlist { Name="Music Library", SourceFolder="D:\Music", Items=tracks }
  → Playlists.Add → sidebar 显示
  → ViewedPlaylist 切换 → PlaylistView 显示曲目
  → libraryCache.SaveAsync → 写缓存
  → StateChanged → debounce → queue.json 持久化
```

### 8.2 启动后台同步

```
App.OnStartup → MainViewModel.InitializeAsync
  → Hydrate playlists (SourceFolder 已在 record 中)
  → RescanFolderBoundPlaylistsAsync() [fire-and-forget]
    → 对每个文件夹绑定歌单:
      → IsScanning=true → sidebar 🔄
      → Load缓存 + Scan磁盘 + Diff
      → 有变化: 读新元数据 → 合并 → 更新 Queue → 写缓存
      → IsScanning=false → sidebar 🔄 消失
```

### 8.3 手动刷新

```
用户点击 [🔄] → RefreshPlaylistAsync(currentPlaylistVM)
  → 同 8.2 的单歌单扫描流程
```

### 8.4 播放流程

文件夹绑定歌单的播放与普通歌单**完全相同**。双击 → HandleDoubleClickPlay → PlayTrackAtAsync → Load + Play。

---

## 9. 错误处理

| 场景 | 处理 |
|------|------|
| 文件夹不存在/被删除 | catch DirectoryNotFoundException → HasScanError=true，保留旧数据 |
| 单文件读元数据失败 | ITrackMetadataReader 已有 try-catch → 返回 null → 跳过 |
| 缓存文件损坏 | LoadAsync catch-all 返回空列表 → 等同首次全量扫描 |
| 文件夹绑定歌单被删除 | 正常 RemovePlaylist，缓存惰性不清理 |
| 同一文件夹导入两次 | 允许，各自独立 GUID，共享缓存条目 |
| 扫描发现当前播放曲目已从磁盘删除 | 从 Queue 中移除该 Track，复用现有 RemoveTrack 逻辑（CurrentIndex 自动调整或触发下一首） |

---

## 10. 路径规范化规则

所有涉及文件路径比较的场景统一遵循：

- 存储：`Path.GetFullPath(path)` 标准化
- 比较：`string.Equals(a, b, StringComparison.OrdinalIgnoreCase)`（Windows 不区分大小写）
- 缓存键：标准化后的 SourceFolder 路径

---

## 11. 不做的事 🚫

- ❌ 不做文件系统监听（FileSystemWatcher）—— 启动扫描 + 手动刷新足够，避免复杂性
- ❌ 不做分组折叠显示（按艺术家/专辑）—— 平铺列表复用现有 UI
- ❌ 不做扫描进度条—— 后台异步，用户无感知
- ❌ 不做去重—— 允许同一文件出现在多个歌单
- ❌ 不做智能规则编辑器（"所有摇滚歌曲"）—— 仅文件夹绑定，YAGNI

---

## 12. 依赖关系

```
新增文件:
  Services/ILibraryScannerService.cs      (接口)
  Services/LibraryScannerService.cs       (实现)
  Services/ILibraryCache.cs               (接口)
  Services/JsonLibraryCache.cs            (实现)
  Models/LibraryCacheEntry.cs             (record)
  Models/LibraryDiff.cs                   (record)

修改文件:
  Models/Playlist.cs                      (加 SourceFolder 字段)
  Models/QueueState.cs                    (SchemaVersion 2→3)
  Services/IFileDialogService.cs          (加 OpenFolder)
  Services/Win32FileDialogService.cs      (实现 OpenFolder)
  ViewModels/PlaylistsViewModel.cs        (加 ImportFolder/Rescan/Refresh)
  ViewModels/PlaylistViewModel.cs         (加 IsScanning/HasScanError)
  ViewModels/MainViewModel.cs             (InitializeAsync 加 fire-and-forget)
  Views/Controls/PlaylistView.xaml        (工具栏加按钮)
  Views/Controls/PlaylistView.xaml.cs     (跨级绑定)
  Views/Controls/PlaylistsSidebarView.xaml (📂 标识 + 🔄 扫描指示)
  Extensions/ServiceCollectionExtensions.cs (注册新服务)
```
