using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;

namespace DPlayer.ViewModels;

/// <summary>
/// 多根目录音乐库：启动先读缓存建分组，后台逐根扫描并增量更新；封面按专辑惰性读取。
/// 所有 ObservableCollection 更新都回到构造时捕获的 UI 上下文。
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly ISettingsPersistence _settings;
    private readonly ILibraryCache _cache;
    private readonly ITrackMetadataReader _metadataReader;
    private readonly PlaylistsViewModel _playlists;
    private readonly ILibraryWatcher _watcher;
    private readonly SynchronizationContext _syncContext;

    private readonly Dictionary<string, Track> _tracksByPath =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LibraryCacheEntry> _cachedEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _coverGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _stateGate = new();
    private readonly object _cleanupGate = new();
    private readonly HashSet<Task> _coverTasks = new();

    private IReadOnlyList<string> _roots = Array.Empty<string>();
    private long _libraryVersion;
    private bool _albumOpenedFromArtist;
    private Task? _cleanupTask;
    private volatile bool _cleaned;

    public ObservableCollection<LibraryArtist> Artists { get; } = new();
    public ObservableCollection<LibraryAlbum> Albums { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArtistsPage))]
    [NotifyPropertyChangedFor(nameof(IsAlbumsPage))]
    [NotifyPropertyChangedFor(nameof(IsArtistDetailPage))]
    [NotifyPropertyChangedFor(nameof(IsAlbumDetailPage))]
    [NotifyPropertyChangedFor(nameof(IsSearchPage))]
    [NotifyPropertyChangedFor(nameof(ShowLibraryEmptyState))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private LibraryPage _page = LibraryPage.Artists;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private LibraryArtist? _selectedArtist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    private LibraryAlbum? _selectedAlbum;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _unavailableRootCount;

    [ObservableProperty]
    private bool _hasLibraryRoots;

    public bool IsArtistsPage => Page == LibraryPage.Artists;
    public bool IsAlbumsPage => Page == LibraryPage.Albums;
    public bool IsArtistDetailPage => Page == LibraryPage.ArtistDetail;
    public bool IsAlbumDetailPage => Page == LibraryPage.AlbumDetail;
    public bool IsSearchPage => Page == LibraryPage.Search;
    public bool CanGoBack => Page is LibraryPage.ArtistDetail
        or LibraryPage.AlbumDetail or LibraryPage.Search;
    public bool IsEmpty => Artists.Count == 0;
    public bool ShowLibraryEmptyState => IsEmpty && !IsSearchPage;
    public string EmptyStateText =>
        !HasLibraryRoots ? "还没有音乐库文件夹"
        : IsScanning ? "正在扫描音乐库…"
        : "未找到音频文件";

    public string HeaderTitle => Page switch
    {
        LibraryPage.Artists => "艺术家",
        LibraryPage.Albums => "专辑",
        LibraryPage.ArtistDetail => SelectedArtist?.Name ?? "艺术家",
        LibraryPage.AlbumDetail => SelectedAlbum?.Title ?? "专辑",
        LibraryPage.Search => "搜索",
        _ => string.Empty,
    };

    /// <summary>
    /// 所有根目录缓存条目的快照（路径 → 条目），供启动时回填库播放队列。
    /// 每次访问都复制一份，调用方拿不到（也无法写穿）内部字典；
    /// 底层字典只在 UI 上下文更新，因此该属性必须在 UI 线程读取。
    /// </summary>
    public IReadOnlyDictionary<string, LibraryCacheEntry> CachedEntriesByPath
        => new Dictionary<string, LibraryCacheEntry>(_cachedEntries, StringComparer.OrdinalIgnoreCase);

    public LibraryViewModel(
        ISettingsPersistence settings,
        ILibraryRootScanner rootScanner,
        ILibraryCache cache,
        ITrackMetadataReader metadataReader,
        PlaylistsViewModel playlists,
        ILibraryWatcher watcher,
        TimeProvider clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _rootScanner = rootScanner ?? throw new ArgumentNullException(nameof(rootScanner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _playlists = playlists ?? throw new ArgumentNullException(nameof(playlists));
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _scheduler = new LibraryRefreshScheduler(
            _clock, RunRefreshBatchAsync, ReportRefreshError);
    }

    public Task InitializeAsync()
    {
        lock (_cleanupGate)
        {
            if (_cleaned) return Task.CompletedTask;
            return _initializeTask = InitializeCoreAsync();
        }
    }

    /// <summary>后台逐根扫描；由 MainViewModel fire-and-forget。不启动 watcher。</summary>
    public Task ScanInBackgroundAsync()
    {
        lock (_cleanupGate)
        {
            if (_cleaned) return Task.CompletedTask;
            return _scanTask = ScanInBackgroundCoreAsync();
        }
    }

    /// <summary>重新读设置、重载缓存并扫描；手动刷新用。</summary>
    public Task RefreshAsync() => RefreshFromSettingsAsync(forceEvenIfUnchanged: true);

    /// <summary>
    /// 设置保存后调用：只有库根集合变化时才重扫。
    /// 频谱/音量等设置保存不应触发整库扫描与封面重建。
    /// </summary>
    public async Task RefreshIfRootsChangedAsync()
        => await RefreshFromSettingsAsync(forceEvenIfUnchanged: false).ConfigureAwait(false);

    public Task CleanupAsync()
    {
        TaskCompletionSource completion;
        lock (_cleanupGate)
        {
            if (_cleanupTask is not null) return _cleanupTask;
            _cleaned = true;
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _cleanupTask = completion.Task;
        }
        _ = CompleteCleanupAsync(completion);
        return completion.Task;
    }

    public void ShowArtists()
    {
        ClearSearchForNavigation();
        SelectedArtist = null;
        SelectedAlbum = null;
        Page = LibraryPage.Artists;
    }

    public void ShowAlbums()
    {
        ClearSearchForNavigation();
        SelectedArtist = null;
        SelectedAlbum = null;
        Page = LibraryPage.Albums;
    }

    public void OpenArtist(LibraryArtist artist)
    {
        SelectedArtist = artist;
        SelectedAlbum = null;
        Page = LibraryPage.ArtistDetail;
    }

    public void OpenAlbum(LibraryAlbum album, bool fromArtist)
    {
        _albumOpenedFromArtist = fromArtist;
        SelectedAlbum = album;
        Page = LibraryPage.AlbumDetail;
        _ = EnsureCoverAsync(album);
    }

    public void Back()
    {
        if (Page == LibraryPage.Search)
        {
            ClearSearch();
            return;
        }

        if (Page == LibraryPage.AlbumDetail && _albumOpenedFromArtist)
        {
            SelectedAlbum = null;
            Page = LibraryPage.ArtistDetail;
        }
        else if (Page == LibraryPage.AlbumDetail)
        {
            SelectedAlbum = null;
            Page = LibraryPage.Albums;
        }
        else if (Page == LibraryPage.ArtistDetail)
        {
            SelectedArtist = null;
            Page = LibraryPage.Artists;
        }
    }

    public async Task PlayAlbumAsync(LibraryAlbum album)
    {
        if (album.Tracks.Count == 0) return;
        await _playlists.PlayLibraryTracksAsync(album.Tracks, 0, album.Title)
            .ConfigureAwait(true);
    }

    public async Task PlayArtistAsync(LibraryArtist artist)
    {
        if (artist.Tracks.Count == 0) return;
        await _playlists.PlayLibraryTracksAsync(artist.Tracks, 0, artist.Name)
            .ConfigureAwait(true);
    }

    public async Task PlayAlbumTrackAsync(LibraryAlbum album, int index)
    {
        if (album.Tracks.Count == 0) return;
        await _playlists.PlayLibraryTracksAsync(album.Tracks, index, album.Title)
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private Task PlayAlbum(LibraryAlbum? album)
        => album is null ? Task.CompletedTask : PlayAlbumAsync(album);

    [RelayCommand]
    private Task PlayArtist(LibraryArtist? artist)
        => artist is null ? Task.CompletedTask : PlayArtistAsync(artist);

    public async Task EnsureCoverAsync(LibraryAlbum album)
    {
        if (album.CoverArt is not null || album.CoverAttempted) return;

        Task task;
        lock (_cleanupGate)
        {
            if (_cleaned) return;
            task = EnsureCoverCoreAsync(album);
            _coverTasks.Add(task);
        }
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            lock (_cleanupGate)
                _coverTasks.Remove(task);
        }
    }

    private async Task EnsureCoverCoreAsync(LibraryAlbum album)
    {
        album.CoverAttempted = true;
        var version = Volatile.Read(ref _libraryVersion);

        await _coverGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (album.Tracks.Count == 0) return;
            var track = await _metadataReader.ReadAsync(album.Tracks[0].FilePath)
                .ConfigureAwait(false);
            if (_cleaned || version != Volatile.Read(ref _libraryVersion)) return;
            if (track.AlbumArt is { Length: > 0 } art)
            {
                _syncContext.Send(_ =>
                {
                    if (_cleaned || !Albums.Contains(album)) return;
                    album.CoverArt = art;
                }, null);
            }
        }
        catch
        {
            // 封面失败保持占位图。
        }
        finally
        {
            _coverGate.Release();
        }
    }

    private void PublishGrouping(LibraryGroupingResult result, bool preserveSelection)
    {
        var previousAlbums = new Dictionary<LibraryAlbumKey, LibraryAlbum>(
            LibraryIdentity.AlbumComparer);
        foreach (var album in Albums)
            previousAlbums[LibraryIdentity.AlbumOf(album)] = album;

        string? selectedArtistName = null;
        LibraryAlbumKey? selectedAlbumKey = null;
        if (preserveSelection)
        {
            selectedArtistName = SelectedArtist?.Name;
            if (SelectedAlbum is not null)
                selectedAlbumKey = LibraryIdentity.AlbumOf(SelectedAlbum);
        }

        foreach (var album in result.Albums)
        {
            if (previousAlbums.TryGetValue(LibraryIdentity.AlbumOf(album), out var old) &&
                SameAlbumTracks(old.Tracks, album.Tracks))
            {
                album.CoverArt = old.CoverArt;
                album.CoverAttempted = old.CoverAttempted && old.CoverArt is not null;
            }
        }

        Artists.Clear();
        foreach (var artist in result.Artists)
            Artists.Add(artist);
        Albums.Clear();
        foreach (var album in result.Albums)
            Albums.Add(album);

        if (preserveSelection)
        {
            if (selectedArtistName is not null)
            {
                SelectedArtist = result.Artists.FirstOrDefault(artist => string.Equals(
                    artist.Name, selectedArtistName,
                    StringComparison.CurrentCultureIgnoreCase));
                if (SelectedArtist is null && Page == LibraryPage.ArtistDetail)
                    Page = LibraryPage.Artists;
            }

            if (selectedAlbumKey is { } key)
            {
                SelectedAlbum = result.Albums.FirstOrDefault(album =>
                    LibraryIdentity.AlbumComparer.Equals(
                        LibraryIdentity.AlbumOf(album), key));
                if (SelectedAlbum is null && Page == LibraryPage.AlbumDetail)
                    Page = LibraryPage.Albums;
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    private static bool SameAlbumTracks(
        IReadOnlyList<Track> left, IReadOnlyList<Track> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            var current = left[i];
            var updated = right[i];
            if (!string.Equals(current.FilePath, updated.FilePath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.Title, updated.Title, StringComparison.Ordinal) ||
                !string.Equals(current.Artist, updated.Artist, StringComparison.Ordinal) ||
                !string.Equals(current.Album, updated.Album, StringComparison.Ordinal) ||
                current.Duration != updated.Duration ||
                current.SampleRate != updated.SampleRate ||
                current.TrackNumber != updated.TrackNumber)
                return false;
        }
        return true;
    }

    private string BuildStatus()
    {
        int watchUnavailable;
        int cacheWriteFailed;
        int refreshErrors;
        lock (_stateGate)
        {
            watchUnavailable = _watchUnavailableRoots.Count;
            cacheWriteFailed = _cacheWriteFailedRoots.Count;
            refreshErrors = _refreshErrorCount;
        }

        var text = $"{Artists.Count} 位艺术家 · {Albums.Count} 张专辑 · {_tracksByPath.Count} 首";
        if (UnavailableRootCount > 0)
            text += $" · {UnavailableRootCount} 个文件夹不可用";
        if (watchUnavailable > 0)
            text += " · 部分文件夹未能自动监控，请手动刷新";
        if (cacheWriteFailed > 0)
            text += $" · {cacheWriteFailed} 个缓存未保存";
        if (refreshErrors > 0)
            text += " · 刷新失败";
        return text;
    }

    partial void OnIsScanningChanged(bool value)
        => OnPropertyChanged(nameof(EmptyStateText));

    partial void OnHasLibraryRootsChanged(bool value)
        => OnPropertyChanged(nameof(EmptyStateText));

    private static Track TrackFromEntry(LibraryCacheEntry entry) =>
        new(entry.FilePath, entry.Title, entry.Artist, entry.Album, entry.Genre,
            entry.Year, entry.SampleRate, null, entry.Duration, entry.TrackNumber);
}
