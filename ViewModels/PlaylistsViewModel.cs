using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UmaPlayer.Models;
using UmaPlayer.Services;

namespace UmaPlayer.ViewModels;

/// <summary>
/// Phase 6 多歌单容器。维护命名歌单集合、"正在查看"指针(UI 选中, 不持久化)、"正在播放"指针
/// (CurrentPlaylistId, 持久化, 决定双击播放是否要跨歌单切音频)。聚合每个 PlaylistViewModel 的
/// PropertyChanged 触发统一 StateChanged 事件, 给 MainViewModel debounce save 用。
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

    internal PlaylistsViewModel(Func<Playlist, PlaylistViewModel> factory)
        : this(factory, new NullFileDialogService(), new NullLibraryScannerService(), new NullLibraryCache()) { }

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
        // 关键: PlaylistViewModel 在 ctor 里订阅 IPlaybackService.TrackEnded;
        // 容器换出(Hydrate 清空 / RemovePlaylist) 时如果不解绑, 被换出的 VM
        // 会在每次曲目结束时继续触发 PlayTrackAtAsync, 抢主播放器 —— Phase 6
        // spec deferred 项, 此处一次性修掉。Cleanup 是同步, 仅解绑 TrackEnded。
        vm.Cleanup();
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

    // —— Phase 10: 文件夹绑定歌单命令 ——

    /// <summary>选文件夹 → 扫描 → 创建文件夹绑定歌单 → 缓存 → StateChanged。</summary>
    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var folderPath = _fileDialog.OpenFolder();
        if (folderPath is null) return;

        var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName)) folderName = folderPath;

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
        vm.Queue.Clear();
        foreach (var track in tracks)
            vm.Queue.Add(track);

        HookPlaylistVm(vm);
        Playlists.Add(vm);
        ViewedPlaylist = vm;

        var entries = tracks.Select(t => new LibraryCacheEntry(
            FilePath: t.FilePath, Title: t.Title, Artist: t.Artist,
            Album: t.Album, Genre: t.Genre, Year: t.Year,
            Duration: t.Duration, SampleRate: t.SampleRate)).ToList();

        try { await _cache.SaveAsync(Path.GetFullPath(folderPath), entries).ConfigureAwait(false); }
        catch (IOException) { /* cache write failure doesn't block */ }
    }

    /// <summary>启动后台扫描所有文件夹绑定歌单。由 MainViewModel.InitializeAsync fire-and-forget。</summary>
    internal async Task RescanFolderBoundPlaylistsAsync()
    {
        foreach (var vm in Playlists)
        {
            if (vm.SourceFolder is not { } sourceFolder)
                continue;
            await RescanSinglePlaylistAsync(vm, sourceFolder).ConfigureAwait(false);
        }
    }

    /// <summary>手动刷新单个文件夹绑定歌单。</summary>
    [RelayCommand]
    private async Task RefreshPlaylistAsync(PlaylistViewModel? playlist)
    {
        if (playlist?.SourceFolder is not { } sourceFolder)
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

            IReadOnlyList<Track> newTracks = Array.Empty<Track>();
            if (diff.AddedPaths.Count > 0)
                newTracks = await _scanner.ReadMetadataBatchAsync(diff.AddedPaths).ConfigureAwait(false);

            var updatedEntries = new List<LibraryCacheEntry>();
            updatedEntries.AddRange(diff.Unchanged);
            foreach (var track in newTracks)
                updatedEntries.Add(new LibraryCacheEntry(
                    FilePath: track.FilePath, Title: track.Title, Artist: track.Artist,
                    Album: track.Album, Genre: track.Genre, Year: track.Year,
                    Duration: track.Duration, SampleRate: track.SampleRate));

            // Remove deleted files (reverse order to keep indices stable)
            var removedIndices = diff.RemovedPaths
                .Select(p => FindTrackIndexByPath(vm.Queue, p))
                .Where(i => i >= 0)
                .OrderByDescending(i => i)
                .ToList();

            foreach (var idx in removedIndices)
            {
                vm.Queue.RemoveAt(idx);
                if (idx == vm.CurrentIndex)
                    vm.CurrentIndex = -1;
                else if (idx < vm.CurrentIndex)
                    vm.CurrentIndex--;
            }

            foreach (var track in newTracks)
                vm.Queue.Add(track);

            try { await _cache.SaveAsync(normalized, updatedEntries).ConfigureAwait(false); }
            catch (IOException) { /* cache write failure doesn't block */ }
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

    private static int FindTrackIndexByPath(ObservableCollection<Track> queue, string filePath)
    {
        for (int i = 0; i < queue.Count; i++)
        {
            if (string.Equals(queue[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    // —— Null implementations for backward-compatible constructor ——

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
}
