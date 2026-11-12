using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.Services.PlaylistFiles;

namespace DPlayer.ViewModels;

/// <summary>
/// Phase 6 多歌单容器。维护命名歌单集合、"正在查看"指针(UI 选中, 不持久化)、"正在播放"指针
/// (CurrentPlaylistId, 持久化, 决定双击播放是否要跨歌单切音频)。聚合每个 PlaylistViewModel 的
/// PropertyChanged 触发统一 StateChanged 事件, 给 MainViewModel debounce save 用。
/// </summary>
public sealed partial class PlaylistsViewModel : ObservableObject
{
    private readonly Func<Playlist, PlaylistViewModel> _factory;
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ILibraryScannerService _scanner;
    private readonly ILibraryCache _cache;
    private readonly ITrackMetadataReader _metadataReader;
    private readonly IPlaylistFileService _playlistFiles;
    private readonly ILibraryRootScanner _rootScanner;
    private readonly SynchronizationContext _syncContext;

    private readonly CancellationTokenSource _folderLifetimeCts = new();
    private readonly object _folderGate = new();
    private readonly HashSet<Task> _folderTasks = new();
    private readonly OneShotCleanup _cleanup = new();
    private bool _acceptingFolderTasks = true;

    private PlaylistViewModel? _libraryPlayback;

    /// <summary>库播放队列的保留 Id；不在可见歌单集合中。</summary>
    public const string LibraryPlaybackId = "__library-playback__";

    public ObservableCollection<PlaylistViewModel> Playlists { get; } = new();

    [ObservableProperty]
    private PlaylistViewModel? _viewedPlaylist;

    /// <summary>
    /// 当前正在播放的歌单 Id; 持久化字段。空字符串表示首启动或边缘态(BuildSnapshot 容忍)。
    /// </summary>
    [ObservableProperty]
    private string _currentPlaylistId = string.Empty;

    /// <summary>库播放队列；未从库中播放过时为 null。</summary>
    public PlaylistViewModel? LibraryPlayback => _libraryPlayback;

    /// <summary>库播放队列是否正在出声（侧栏 ▶ 标记用）。</summary>
    public bool IsLibraryPlaybackActive =>
        _libraryPlayback is not null && CurrentPlaylistId == _libraryPlayback.Id;

    /// <summary>
    /// 当前正在播放的歌单 VM；由 <see cref="CurrentPlaylistId"/> 解析，找不到为 null。
    /// 传输按钮（上一首/下一首）绑定这里，**不绑** ViewedPlaylist ——
    /// 切换查看不打断播放，只有双击才跨歌单切换音频（Spotify 双指针模型）。
    /// 库播放队列当前时返回它（它不在可见歌单集合里）。
    /// </summary>
    public PlaylistViewModel? CurrentPlaylist =>
        _libraryPlayback?.Id == CurrentPlaylistId
            ? _libraryPlayback
            : Playlists.FirstOrDefault(p => p.Id == CurrentPlaylistId);

    // —— 全局播放模式（所有歌单共享） ——

    [ObservableProperty]
    private bool _shuffleEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatActive))]
    private RepeatMode _repeatMode = RepeatMode.Off;

    /// <summary>循环按钮是否处于"激活"状态（List 或 One 都算）。</summary>
    public bool RepeatActive => RepeatMode != RepeatMode.Off;

    /// <summary>
    /// 任意歌单内部状态(Tracks、CurrentIndex、Shuffle、Repeat、Name)、容器结构(增删歌单)、
    /// 或 CurrentPlaylistId 改变时触发。MainViewModel 订阅此事件做 debounce save。
    /// </summary>
    public event EventHandler? StateChanged;

    public PlaylistsViewModel(
        Func<Playlist, PlaylistViewModel> factory,
        IPlaybackService player,
        IFileDialogService fileDialog,
        ILibraryScannerService scanner,
        ILibraryCache cache,
        ITrackMetadataReader metadataReader,
        IPlaylistFileService playlistFiles,
        ILibraryRootScanner rootScanner)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _playlistFiles = playlistFiles ?? throw new ArgumentNullException(nameof(playlistFiles));
        _rootScanner = rootScanner ?? throw new ArgumentNullException(nameof(rootScanner));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        Playlists.CollectionChanged += OnPlaylistsCollectionChanged;
    }

    internal PlaylistsViewModel(
        Func<Playlist, PlaylistViewModel> factory, ILibraryRootScanner rootScanner)
        : this(factory, new NullPlaybackService(), new NullFileDialogService(), new NullLibraryScannerService(),
               new NullLibraryCache(), new NullMetadataReader(), new NullPlaylistFileService(), rootScanner) { }

    public Task CleanupAsync()
        => _cleanup.RunAsync(CompleteCleanupAsync, onFirst: () =>
        {
            lock (_folderGate) _acceptingFolderTasks = false;
        });

    private async Task CompleteCleanupAsync()
    {
        try
        {
            _folderLifetimeCts.Cancel();
            Task[] pending;
            lock (_folderGate)
                pending = _folderTasks.ToArray();

            foreach (var task in pending)
            {
                try { await task.ConfigureAwait(false); }
                catch { /* folder work is abandoned during shutdown */ }
            }
        }
        finally
        {
            _folderLifetimeCts.Dispose();
        }
    }

    private Task TrackFolderTaskAsync(Func<CancellationToken, Task> action)
    {
        TaskCompletionSource completion;
        lock (_folderGate)
        {
            if (!_acceptingFolderTasks)
                return Task.CompletedTask;
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _folderTasks.Add(completion.Task);
        }
        _ = RunFolderTaskAsync(action, completion);
        return completion.Task;
    }

    private async Task RunFolderTaskAsync(
        Func<CancellationToken, Task> action, TaskCompletionSource completion)
    {
        try
        {
            await action(_folderLifetimeCts.Token).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            lock (_folderGate)
                _folderTasks.Remove(completion.Task);
        }
    }

    /// <summary>
    /// MainViewModel 启动时调用; 用持久化快照初始化容器。重复调用先清空。
    /// 对文件夹绑定歌单, 同步加载 library-cache.json 回填元数据(缓存文件极小, 阻塞 &lt; 几 ms)。
    /// </summary>
    public void Hydrate(QueueState snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_libraryPlayback is not null)
        {
            UnhookPlaylistVm(_libraryPlayback);
            _libraryPlayback = null;
        }

        foreach (var vm in Playlists)
            UnhookPlaylistVm(vm);
        Playlists.Clear();

        foreach (var p in snapshot.Playlists)
        {
            var vm = _factory(p);

            if (p.SourceFolder is { } sourceFolder)
                ApplyCachedMetadataSync(vm, sourceFolder);   // 文件夹歌单: 缓存回填
            else
                LoadMetadataForNormalPlaylistSync(vm);        // 普通歌单: 读文件元数据

            HookPlaylistVm(vm);
            Playlists.Add(vm);
        }

        // 库播放队列：不在可见歌单集合里，也不读标签（启动时由库缓存回填）。
        if (snapshot.LibraryPlayback is { } libraryPlayback)
        {
            var libraryVm = _factory(libraryPlayback);
            HookPlaylistVm(libraryVm);
            _libraryPlayback = libraryVm;
        }

        // CurrentPlaylistId: 取持久化值, 但若指向已不存在的歌单则修正到第一个。
        var matchId =
            _libraryPlayback?.Id == snapshot.CurrentPlaylistId ||
            Playlists.Any(p => p.Id == snapshot.CurrentPlaylistId)
                ? snapshot.CurrentPlaylistId
                : (Playlists.Count > 0 ? Playlists[0].Id : string.Empty);
        CurrentPlaylistId = matchId;

        // ViewedPlaylist 永远只指向可见歌单；库队列当前时回落到第一个可见歌单。
        ViewedPlaylist = Playlists.FirstOrDefault(p => p.Id == matchId) ?? Playlists.FirstOrDefault();

        // 全局 Shuffle/Repeat
        ShuffleEnabled = snapshot.ShuffleEnabled;
        RepeatMode = snapshot.RepeatMode;

        RecomputeIsActiveFlags();
    }

    /// <summary>
    /// 按文件路径在已水化的歌单里找曲目，并返回它所属的歌单。
    /// 查找顺序（设计 §5.2）：当前播放上下文 → 可见歌单 → 库播放队列。
    /// 当前上下文优先保证库会话续播不会被含同一文件的可见歌单抢走。
    /// 供启动断点续播就位；找不到返回 null。
    /// </summary>
    public (PlaylistViewModel Playlist, Track Track)? FindPlaylistTrackByPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        static Track? FindIn(PlaylistViewModel? vm, string path) => vm?.Queue.FirstOrDefault(
            t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));

        // CurrentPlaylist 可能是隐藏的库播放队列，也可能是可见歌单。
        var current = CurrentPlaylist;
        if (current is not null && FindIn(current, filePath) is { } currentTrack)
            return (current, currentTrack);

        foreach (var vm in Playlists)
        {
            if (ReferenceEquals(vm, current)) continue;   // 已查过，避免重复
            if (FindIn(vm, filePath) is { } track)
                return (vm, track);
        }

        if (_libraryPlayback is not null &&
            !ReferenceEquals(_libraryPlayback, current) &&
            FindIn(_libraryPlayback, filePath) is { } libraryTrack)
        {
            return (_libraryPlayback, libraryTrack);
        }
        return null;
    }

    /// <summary>
    /// 从库中播放：懒创建专用队列，替换内容并启动播放。
    /// 不修改 ViewedPlaylist —— 用户仍停留在库视图。
    /// </summary>
    public async Task PlayLibraryTracksAsync(
        IReadOnlyList<Track> tracks, int startIndex, string contextName)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0) return;

        if (_libraryPlayback is null)
        {
            var seed = new Playlist(
                LibraryPlaybackId, "音乐库播放", Array.Empty<string>(), -1, false, RepeatMode.Off);
            _libraryPlayback = _factory(seed);
            HookPlaylistVm(_libraryPlayback);
        }

        _libraryPlayback.Name = string.IsNullOrWhiteSpace(contextName)
            ? "音乐库播放"
            : contextName;

        _libraryPlayback.Queue.Clear();
        foreach (var track in tracks)
            _libraryPlayback.Queue.Add(track);

        var index = Math.Clamp(startIndex, 0, tracks.Count - 1);
        await _libraryPlayback.PlayTrackAtCommand.ExecuteAsync(index).ConfigureAwait(true);
        OnPropertyChanged(nameof(IsLibraryPlaybackActive));
    }

    /// <summary>启动时用库缓存回填库播放队列，避免逐首读标签。</summary>
    public void ApplyCachedMetadataToLibraryPlayback(
        IReadOnlyDictionary<string, LibraryCacheEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (_libraryPlayback is null || entries.Count == 0) return;

        for (int i = 0; i < _libraryPlayback.Queue.Count; i++)
        {
            var old = _libraryPlayback.Queue[i];
            if (!entries.TryGetValue(old.FilePath, out var entry)) continue;
            _libraryPlayback.Queue[i] = new Track(
                entry.FilePath, entry.Title, entry.Artist, entry.Album, entry.Genre,
                entry.Year, entry.SampleRate, null, entry.Duration, entry.TrackNumber);
        }
    }

    /// <summary>
    /// 把容器当前状态打包成持久化快照。MainViewModel debounce save 用。
    /// </summary>
    public QueueState BuildSnapshot() => new()
    {
        Playlists = Playlists.Select(vm => vm.ToRecord()).ToArray(),
        CurrentPlaylistId = CurrentPlaylistId,
        ShuffleEnabled = ShuffleEnabled,
        RepeatMode = RepeatMode,
        LibraryPlayback = _libraryPlayback?.ToRecord(),
    };

    /// <summary>
    /// 双击播放的唯一入口，**两壳共用**（WPF: Views/Controls/PlaylistView.xaml.cs；
    /// WinUI: D-player.WinUI/MainWindow.xaml.cs）—— 不是 PlaylistView 专属回调，
    /// 给单个壳加并行入口会让同一个手势的语义分叉（COUPLING §5 / PROJECT §4.3 第 30 条）。
    /// 如果双击的不是当前正在播放的歌单, 切 CurrentPlaylistId;
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

    /// <summary>
    /// 把曲目按顺序追加到普通歌单。文件夹歌单由目录驱动，拒绝手动写入。
    /// 不改变 ViewedPlaylist / CurrentPlaylistId，也不触发播放。
    /// </summary>
    public bool AppendTracks(PlaylistViewModel target, IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tracks);

        if (tracks.Count == 0) return false;
        if (target.HasSourceFolder) return false;
        if (!Playlists.Contains(target)) return false;

        foreach (var track in tracks)
            target.Queue.Add(track);
        return true;
    }

    /// <summary>
    /// 新建普通歌单并追加曲目；不改变 ViewedPlaylist —— 库视图下侧栏不能出现高亮歌单。
    /// </summary>
    public PlaylistViewModel? CreatePlaylistWithTracks(
        string? name, IReadOnlyList<Track> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (tracks.Count == 0) return null;

        var vm = CreatePlaylistCore(name);
        foreach (var track in tracks)
            vm.Queue.Add(track);
        return vm;
    }

    [RelayCommand]
    private void AddPlaylist(string? name)
    {
        var vm = CreatePlaylistCore(name);
        ViewedPlaylist = vm;
    }

    private PlaylistViewModel CreatePlaylistCore(string? name)
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
        return vm;
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
            if (!IsLibraryPlaybackActive)
                CurrentPlaylistId = rebuilt.Id;
            ViewedPlaylist = rebuilt;
            return;
        }

        // 修指针: 若被删项是当前播放歌单, 停止播放并清空指针。
        if (target.Id == CurrentPlaylistId)
        {
            _player.Stop();
            _player.Unload();
            CurrentPlaylistId = string.Empty;
        }
        // 若被删项是当前查看歌单, 切到相邻项。
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

    /// <summary>切换 Shuffle 开关。同时清空当前歌单的已播过历史。</summary>
    [RelayCommand]
    private void ToggleShuffle()
    {
        ShuffleEnabled = !ShuffleEnabled;
        if (ViewedPlaylist is { } vp)
        {
            vp.ClearShuffleHistory();
        }
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

    partial void OnCurrentPlaylistIdChanged(string value)
    {
        RecomputeIsActiveFlags();
        OnPropertyChanged(nameof(CurrentPlaylist));
        OnPropertyChanged(nameof(IsLibraryPlaybackActive));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecomputeIsActiveFlags()
    {
        foreach (var vm in Playlists)
            vm.IsActivePlaylist = vm.Id == CurrentPlaylistId;
        if (_libraryPlayback is not null)
            _libraryPlayback.IsActivePlaylist = _libraryPlayback.Id == CurrentPlaylistId;
    }

    private void HookPlaylistVm(PlaylistViewModel vm)
    {
        vm.Container = this;
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
    {
        // 集合换出/换入可能让 CurrentPlaylistId 解析到另一个 VM（或 null），
        // WPF 的传输按钮绑定依赖这条通知重新求值。
        OnPropertyChanged(nameof(CurrentPlaylist));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // —— Phase 10: 文件夹绑定歌单命令 ——

    /// <summary>选文件夹 → 根扫描 → 创建文件夹绑定歌单。缓存由根扫描事务保存。</summary>
    [RelayCommand]
    private Task ImportFolderAsync() => TrackFolderTaskAsync(ImportFolderCoreAsync);

    private async Task ImportFolderCoreAsync(CancellationToken ct)
    {
        var folderPath = await _fileDialog.OpenFolderAsync().ConfigureAwait(true);
        if (folderPath is null) return;

        var normalized = LibraryPathNormalizer.Normalize(folderPath);
        if (normalized is null) return;

        using var lease = new LibraryScanLease(ct);
        var snapshot = await _rootScanner.ScanAsync(normalized, lease).ConfigureAwait(true);
        if (snapshot.IsUnavailable) return;

        var tracks = snapshot.Entries.Select(TrackFromEntry).ToArray();
        var folderName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(folderName)) folderName = normalized;

        var seed = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: folderName,
            Items: tracks.Select(t => t.FilePath).ToArray(),
            CurrentIndex: -1,
            ShuffleEnabled: false,
            RepeatMode: RepeatMode.Off,
            SourceFolder: normalized);

        var vm = _factory(seed);
        vm.Queue.Clear();
        foreach (var track in tracks)
            vm.Queue.Add(track);

        HookPlaylistVm(vm);
        Playlists.Add(vm);
        ViewedPlaylist = vm;
    }

    // —— Phase 18: 播放列表文件导入（容器级 = 新建歌单） ——

    /// <summary>
    /// 导入 .m3u/.m3u8/.pls → **新建**一个普通歌单并设为当前查看项（与 ImportFolderAsync 同层级语义）。
    ///
    /// presetPath 非空 = 拖拽入口；null = 走文件对话框。返回 null 表示用户取消。
    /// SourceFolder 刻意留 null：导入的歌单不是文件夹绑定歌单，不写 library cache，
    /// 重启后由 Hydrate 走 LoadMetadataForNormalPlaylistSync 读文件元数据。
    /// </summary>
    public async Task<PlaylistImportReport?> ImportPlaylistFileAsync(string? presetPath = null)
    {
        var path = presetPath ?? (await _fileDialog.OpenFilesAsync(PlaylistFileFormats.OpenFilter)).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return null;

        var sourceFile = Path.GetFileName(path);
        // ConfigureAwait(true)：下面要动 ObservableCollection（WPF 要求 UI 线程）
        var result = await _playlistFiles.ImportAsync(path).ConfigureAwait(true);

        if (result.AcceptedPaths.Count == 0)
        {
            return new PlaylistImportReport(sourceFile, null, false, 0,
                result.SkippedMissing, result.SkippedUnsupported, result.TotalEntries);
        }

        var tracks = await _scanner.ReadMetadataBatchAsync(result.AcceptedPaths).ConfigureAwait(true);
        if (tracks.Count == 0)
        {
            // 条目都存在但元数据一条都没读出来 —— 不建空歌单，报告为"没导入"
            return new PlaylistImportReport(sourceFile, null, false, 0,
                result.SkippedMissing, result.SkippedUnsupported, result.TotalEntries);
        }

        var seed = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: result.SuggestedName,
            Items: result.AcceptedPaths,
            CurrentIndex: -1,
            ShuffleEnabled: false,
            RepeatMode: RepeatMode.Off,
            SourceFolder: null);

        var vm = _factory(seed);
        // 必须 Clear：PlaylistViewModel 构造器会用 File.Exists 过滤 seed.Items 并预填占位 Track，
        // 这里要用带真实元数据的 Track 顶掉它们（与 ImportFolderAsync 同一手法）。
        vm.Queue.Clear();
        foreach (var track in tracks)
            vm.Queue.Add(track);

        HookPlaylistVm(vm);
        Playlists.Add(vm);        // CollectionChanged → StateChanged → MainViewModel debounce save
        ViewedPlaylist = vm;

        return new PlaylistImportReport(sourceFile, result.SuggestedName, true, tracks.Count,
            result.SkippedMissing, result.SkippedUnsupported, result.TotalEntries);
    }

    /// <summary>
    /// 同步加载缓存元数据并替换占位 Track。由 Hydrate 在构造 PlaylistVM 后立即调用。
    /// cache.LoadAsync 内部有 SemaphoreSlim, 用 .GetAwaiter().GetResult() 同步等待;
    /// 缓存文件极小(几 KB), 阻塞 UI 线程 &lt; 几 ms。
    /// </summary>
    private void ApplyCachedMetadataSync(PlaylistViewModel vm, string sourceFolder)
    {
        try
        {
            var normalized = Path.GetFullPath(sourceFolder);
            var cached = _cache.LoadAsync(normalized).GetAwaiter().GetResult();
            if (cached.Count == 0) return;

            var cacheDict = new Dictionary<string, LibraryCacheEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in cached)
                cacheDict[entry.FilePath] = entry;

            for (int i = 0; i < vm.Queue.Count; i++)
            {
                if (cacheDict.TryGetValue(vm.Queue[i].FilePath, out var entry))
                {
                    var trackNumber = entry.TrackNumber;
                    // 旧缓存无 TrackNumber 时回读文件补全
                    if (trackNumber is null)
                    {
                        try
                        {
                            var full = _metadataReader.ReadAsync(entry.FilePath).GetAwaiter().GetResult();
                            trackNumber = full.TrackNumber;
                        }
                        catch { /* 回读失败保持 null */ }
                    }
                    vm.Queue[i] = new Track(
                        FilePath: entry.FilePath,
                        Title: entry.Title,
                        Artist: entry.Artist,
                        Album: entry.Album,
                        Genre: entry.Genre,
                        Year: entry.Year,
                        SampleRate: entry.SampleRate,
                        AlbumArt: null,
                        Duration: entry.Duration,
                        TrackNumber: trackNumber);
                }
            }
        }
        catch
        {
            // 缓存加载失败静默跳过, 队列保持占位 Track
        }
    }

    /// <summary>
    /// 同步读取普通歌单的元数据。由 Hydrate 在构造 PlaylistVM 后调用。
    /// 对每个占位 Track 调用 ITrackMetadataReader.ReadAsync(.GetAwaiter().GetResult());
    /// 失败的文件静默跳过(保持占位 Track)。
    /// </summary>
    private void LoadMetadataForNormalPlaylistSync(PlaylistViewModel vm)
    {
        for (int i = 0; i < vm.Queue.Count; i++)
        {
            try
            {
                var track = _metadataReader.ReadAsync(vm.Queue[i].FilePath).GetAwaiter().GetResult();
                if (track is not null)
                    vm.Queue[i] = track;
            }
            catch
            {
                // 文件损坏/不存在, 保持占位 Track
            }
        }
    }

    /// <summary>启动后台扫描所有文件夹绑定歌单。由 MainViewModel.InitializeAsync fire-and-forget。</summary>
    internal Task RescanFolderBoundPlaylistsAsync()
        => TrackFolderTaskAsync(async ct =>
        {
            foreach (var vm in Playlists)
            {
                if (vm.SourceFolder is not { } sourceFolder)
                    continue;
                ct.ThrowIfCancellationRequested();
                await RescanSinglePlaylistAsync(vm, sourceFolder, ct).ConfigureAwait(false);
            }
        });

    /// <summary>手动刷新单个文件夹绑定歌单。</summary>
    [RelayCommand]
    private Task RefreshPlaylistAsync(PlaylistViewModel? playlist)
    {
        if (playlist?.SourceFolder is not { } sourceFolder)
            return Task.CompletedTask;
        return TrackFolderTaskAsync(
            ct => RescanSinglePlaylistAsync(playlist, sourceFolder, ct));
    }

    private async Task RescanSinglePlaylistAsync(
        PlaylistViewModel vm, string sourceFolder, CancellationToken ct)
    {
        var normalized = Path.GetFullPath(sourceFolder);
        _syncContext.Send(_ => { vm.IsScanning = true; vm.HasScanError = false; }, null);

        try
        {
            ct.ThrowIfCancellationRequested();
            using var lease = new LibraryScanLease(ct);
            var snapshot = await _rootScanner.ScanAsync(normalized, lease).ConfigureAwait(false);

            if (snapshot.IsUnavailable)
            {
                _syncContext.Send(_ => vm.HasScanError = true, null);
                return;
            }

            ct.ThrowIfCancellationRequested();
            _syncContext.Send(_ => ApplySnapshot(vm, snapshot, ct), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 容器关闭时放弃本轮刷新，不再改动队列。
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                _syncContext.Send(_ => vm.IsScanning = false, null);
        }
    }

    private static void ApplySnapshot(
        PlaylistViewModel vm, LibraryRootSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var byPath = snapshot.Entries.ToDictionary(
            entry => Path.GetFullPath(entry.FilePath), StringComparer.OrdinalIgnoreCase);

        for (var i = vm.Queue.Count - 1; i >= 0; i--)
        {
            if (byPath.ContainsKey(Path.GetFullPath(vm.Queue[i].FilePath))) continue;

            vm.Queue.RemoveAt(i);
            if (i == vm.CurrentIndex)
                vm.CurrentIndex = -1;
            else if (i < vm.CurrentIndex)
                vm.CurrentIndex--;
        }

        for (var i = 0; i < vm.Queue.Count; i++)
        {
            var path = Path.GetFullPath(vm.Queue[i].FilePath);
            if (!byPath.TryGetValue(path, out var entry)) continue;

            var old = vm.Queue[i];
            var updated = TrackFromEntry(entry) with { AlbumArt = old.AlbumArt };
            if (updated != old)
                vm.Queue[i] = updated;
        }

        var present = new HashSet<string>(
            vm.Queue.Select(track => Path.GetFullPath(track.FilePath)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Entries)
        {
            var path = Path.GetFullPath(entry.FilePath);
            if (present.Add(path))
                vm.Queue.Add(TrackFromEntry(entry));
        }
    }

    private static Track TrackFromEntry(LibraryCacheEntry entry) => new(
        entry.FilePath, entry.Title, entry.Artist, entry.Album, entry.Genre,
        entry.Year, entry.SampleRate, null, entry.Duration, entry.TrackNumber);

    // —— Null implementations for backward-compatible constructor ——

    private sealed class NullFileDialogService : IFileDialogService
    {
        public Task<IReadOnlyList<string>> OpenFilesAsync(string filter, bool multiselect = false)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<string?> OpenFolderAsync() => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string filter, string defaultFileName, string defaultExtension)
            => Task.FromResult<string?>(null);
    }

    private sealed class NullLibraryScannerService : ILibraryScannerService
    {
        public IReadOnlyList<string> ScanFolder(
            string folderPath, CancellationToken ct = default) => Array.Empty<string>();
        public LibraryScanResult ScanFolderWithStatus(
            string folderPath, CancellationToken ct = default) =>
            new(Array.Empty<string>(), IsUnavailable: false);
        public Task<IReadOnlyList<Track>> ReadMetadataBatchAsync(
            IReadOnlyList<string> paths, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>());
        public LibraryDiff ComputeDiff(IReadOnlyList<string> currentFiles, IReadOnlyList<LibraryCacheEntry> cached) =>
            new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<LibraryCacheEntry>());
    }

    private sealed class NullLibraryCache : ILibraryCache
    {
        public Task<IReadOnlyList<LibraryCacheEntry>> LoadAsync(string folderPath) => Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(Array.Empty<LibraryCacheEntry>());
        public Task SaveAsync(string folderPath, IReadOnlyList<LibraryCacheEntry> entries) => Task.CompletedTask;
    }

    private sealed class NullMetadataReader : ITrackMetadataReader
    {
        public Task<Track> ReadAsync(string filePath) => Task.FromResult(new Track(filePath, System.IO.Path.GetFileName(filePath), null, null, null, null, null, null, TimeSpan.Zero, null));
        public Track CreateFallback(string filePath) => new(filePath, System.IO.Path.GetFileName(filePath), null, null, null, null, null, null, TimeSpan.Zero, null);
    }

    private sealed class NullPlaylistFileService : IPlaylistFileService
    {
        public Task<PlaylistImportResult> ImportAsync(string playlistFilePath) =>
            Task.FromResult(new PlaylistImportResult("导入的歌单", Array.Empty<string>(), 0, 0, 0));

        public Task ExportAsync(string destPath, IReadOnlyList<Track> tracks) => Task.CompletedTask;
    }

    private sealed class NullPlaybackService : IPlaybackService
    {
        public PlayState State => PlayState.Stopped;
        public Track? CurrentTrack => null;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public float Volume { get; set; }
        public Task LoadAsync(Track track) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void Unload() { }
        public void Seek(TimeSpan position) { }
        public event Action<PlayState>? StateChanged { add { } remove { } }
        public event Action<TimeSpan>? PositionChanged { add { } remove { } }
        public event Action<TimeSpan>? DurationChanged { add { } remove { } }
        public event Action<Track?>? TrackChanged { add { } remove { } }
        public event Action<string>? PlaybackError { add { } remove { } }
        public event Action? TrackEnded { add { } remove { } }
        public event Action<float[]>? SpectrumDataAvailable { add { } remove { } }
        public SpectrumConfig SpectrumConfig { get; set; } = new();
        public EqualizerConfig EqualizerConfig { get; set; } = new();
        public void Dispose() { }
    }
}
