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
    private readonly ILibraryScannerService _scanner;
    private readonly ILibraryCache _cache;
    private readonly ITrackMetadataReader _metadataReader;
    private readonly PlaylistsViewModel _playlists;
    private readonly SynchronizationContext _syncContext;

    private readonly Dictionary<string, Track> _tracksByPath =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LibraryCacheEntry> _cachedEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _coverGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();

    private IReadOnlyList<string> _roots = Array.Empty<string>();
    private int _scanGeneration;
    private CancellationTokenSource? _scanCts;
    private bool _albumOpenedFromArtist;

    public ObservableCollection<LibraryArtist> Artists { get; } = new();
    public ObservableCollection<LibraryAlbum> Albums { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArtistsPage))]
    [NotifyPropertyChangedFor(nameof(IsAlbumsPage))]
    [NotifyPropertyChangedFor(nameof(IsArtistDetailPage))]
    [NotifyPropertyChangedFor(nameof(IsAlbumDetailPage))]
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
    public bool CanGoBack => Page is LibraryPage.ArtistDetail or LibraryPage.AlbumDetail;
    public bool IsEmpty => Artists.Count == 0;
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
        ILibraryScannerService scanner,
        ILibraryCache cache,
        ITrackMetadataReader metadataReader,
        PlaylistsViewModel playlists)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _playlists = playlists ?? throw new ArgumentNullException(nameof(playlists));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public async Task InitializeAsync()
    {
        // 启动回填也是一“代”：并发的扫描/刷新要么让位，要么让这次回填被丢弃，
        // 避免晚到的启动快照覆盖掉更新的结果。
        var generation = Interlocked.Increment(ref _scanGeneration);
        var roots = await LoadRootsAsync().ConfigureAwait(false);
        var loaded = await LoadCachedTracksAsync(roots).ConfigureAwait(false);
        if (generation != Volatile.Read(ref _scanGeneration)) return;

        _roots = roots;
        _syncContext.Send(_ =>
        {
            HasLibraryRoots = roots.Count > 0;
            ApplyLoaded(loaded);
        }, null);
    }

    /// <summary>后台逐根扫描；由 MainViewModel fire-and-forget。</summary>
    public async Task ScanInBackgroundAsync()
    {
        var generation = Interlocked.Increment(ref _scanGeneration);
        var roots = _roots;
        if (roots.Count == 0)
        {
            roots = await LoadRootsAsync().ConfigureAwait(false);
            // 等设置期间可能已有更新的扫描/刷新接管：不覆盖 _roots，也不点亮扫描状态。
            if (generation != Volatile.Read(ref _scanGeneration)) return;

            _roots = roots;
            _syncContext.Send(_ => HasLibraryRoots = roots.Count > 0, null);
        }

        if (roots.Count == 0) return;

        _syncContext.Send(_ => IsScanning = true, null);
        try
        {
            await ScanRootsAsync(roots, generation, _lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cleanup 已取消生命周期令牌。
        }
        finally
        {
            if (generation == Volatile.Read(ref _scanGeneration))
                _syncContext.Send(_ => IsScanning = false, null);
        }
    }

    /// <summary>重新读设置、重载缓存并扫描；手动刷新用。</summary>
    public Task RefreshAsync() => RefreshCoreAsync(loadedRoots: null);

    /// <summary>
    /// 设置保存后调用：只有库根集合变化时才重扫。
    /// 频谱/音量等设置保存不应触发整库扫描与封面重建。
    /// </summary>
    public async Task RefreshIfRootsChangedAsync()
    {
        var roots = await LoadRootsAsync().ConfigureAwait(false);
        if (RootsEqual(roots, _roots)) return;
        await RefreshCoreAsync(roots).ConfigureAwait(false);
    }

    private async Task RefreshCoreAsync(IReadOnlyList<string>? loadedRoots)
    {
        var generation = Interlocked.Increment(ref _scanGeneration);
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        // 与生命周期令牌联动：Cleanup 一次即可取消本轮刷新扫描。
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _scanCts = cts;

        try
        {
            var roots = loadedRoots ?? await LoadRootsAsync().ConfigureAwait(false);
            var loaded = await LoadCachedTracksAsync(roots).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _scanGeneration)) return;

            _roots = roots;
            _syncContext.Send(_ =>
            {
                HasLibraryRoots = roots.Count > 0;
                ApplyLoaded(loaded);
                IsScanning = roots.Count > 0;
            }, null);

            if (roots.Count == 0) return;
            await ScanRootsAsync(roots, generation, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 新一轮 Refresh 已接管，或 Cleanup 已取消生命周期令牌。
        }
        finally
        {
            if (generation == Volatile.Read(ref _scanGeneration))
                _syncContext.Send(_ => IsScanning = false, null);
        }
    }

    private static bool RootsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count) return false;
        var rightSet = new HashSet<string>(right, StringComparer.OrdinalIgnoreCase);
        return left.All(rightSet.Contains);
    }

    public void Cleanup()
    {
        Interlocked.Increment(ref _scanGeneration);
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        // 生命周期令牌永不 Dispose：Cancel 可重复调用，也不会给联动令牌引出
        // ObjectDisposedException（这里从不 Register 回调）。
        _lifetimeCts.Cancel();
    }

    public void ShowArtists()
    {
        SelectedArtist = null;
        SelectedAlbum = null;
        Page = LibraryPage.Artists;
    }

    public void ShowAlbums()
    {
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
        album.CoverAttempted = true;
        var generation = Volatile.Read(ref _scanGeneration);

        await _coverGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (album.Tracks.Count == 0) return;
            var track = await _metadataReader.ReadAsync(album.Tracks[0].FilePath)
                .ConfigureAwait(false);
            if (generation != Volatile.Read(ref _scanGeneration)) return;
            if (track.AlbumArt is { Length: > 0 } art)
                _syncContext.Send(_ => album.CoverArt = art, null);
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

    private async Task<IReadOnlyList<string>> LoadRootsAsync()
    {
        AppSettings settings;
        try { settings = await _settings.LoadAsync().ConfigureAwait(false); }
        catch { return Array.Empty<string>(); }

        return LibraryPathNormalizer.NormalizeMany(settings.LibraryFolders);
    }

    private async Task<LoadedLibrary> LoadCachedTracksAsync(
        IReadOnlyList<string> roots)
    {
        var tracks = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, LibraryCacheEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var cached = await _cache.LoadAsync(root).ConfigureAwait(false)
                ?? Array.Empty<LibraryCacheEntry>();
            foreach (var entry in cached)
            {
                string path;
                try { path = Path.GetFullPath(entry.FilePath); }
                catch { continue; }

                entries[path] = entry;
                tracks[path] = TrackFromEntry(entry);
            }
        }

        return new LoadedLibrary(tracks, entries);
    }

    private async Task ScanRootsAsync(
        IReadOnlyList<string> roots, int generation, CancellationToken ct)
    {
        var unavailable = 0;

        foreach (var root in roots)
        {
            if (generation != Volatile.Read(ref _scanGeneration)) return;
            ct.ThrowIfCancellationRequested();

            try
            {
                // 根目录不可枚举时必须跳过：不能把“暂时不可用”当成“所有文件已删除”，
                // 否则会用空结果覆盖掉这个根的缓存。Directory.Exists 区分不了
                // “存在但无法枚举”和“存在但为空”，所以状态来自扫描服务。
                var scan = _scanner.ScanFolderWithStatus(root);
                if (scan.IsUnavailable)
                {
                    unavailable++;
                    _syncContext.Send(_ => UnavailableRootCount = unavailable, null);
                    continue;
                }

                var cached = await _cache.LoadAsync(root).ConfigureAwait(false);
                var diff = _scanner.ComputeDiff(scan.Files, cached);

                var newTracks = await _scanner.ReadMetadataBatchAsync(diff.AddedPaths)
                    .ConfigureAwait(false) ?? Array.Empty<Track>();
                if (generation != Volatile.Read(ref _scanGeneration)) return;

                var added = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
                foreach (var track in newTracks)
                {
                    var path = Path.GetFullPath(track.FilePath);
                    added[path] = track with { AlbumArt = null };
                }
                foreach (var path in diff.AddedPaths)
                {
                    var full = Path.GetFullPath(path);
                    if (!added.ContainsKey(full))
                        added[full] = _metadataReader.CreateFallback(full);
                }

                var entries = new List<LibraryCacheEntry>(diff.Unchanged);
                foreach (var (path, track) in added)
                {
                    entries.RemoveAll(e => string.Equals(
                        Path.GetFullPath(e.FilePath), path, StringComparison.OrdinalIgnoreCase));
                    entries.Add(EntryFromTrack(track));
                }

                if (generation != Volatile.Read(ref _scanGeneration)) return;

                _syncContext.Send(_ =>
                {
                    foreach (var removed in diff.RemovedPaths)
                    {
                        var full = Path.GetFullPath(removed);
                        _tracksByPath.Remove(full);
                        _cachedEntries.Remove(full);
                    }
                    foreach (var entry in diff.Unchanged)
                    {
                        var full = Path.GetFullPath(entry.FilePath);
                        _cachedEntries[full] = entry;
                        _tracksByPath[full] = TrackFromEntry(entry);
                    }
                    foreach (var (path, track) in added)
                    {
                        _tracksByPath[path] = track;
                        _cachedEntries[path] = EntryFromTrack(track);
                    }
                    RebuildGroups();
                    StatusText = BuildStatus();
                }, null);

                try
                {
                    await _cache.SaveAsync(root, entries).ConfigureAwait(false);
                }
                catch
                {
                    // 缓存写失败不丢弃本轮内存结果；下次扫描再试。
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                unavailable++;
                _syncContext.Send(_ => UnavailableRootCount = unavailable, null);
            }
        }

        _syncContext.Send(_ =>
        {
            UnavailableRootCount = unavailable;
            StatusText = _roots.Count == 0 ? string.Empty : BuildStatus();
        }, null);
    }

    private void ApplyLoaded(LoadedLibrary loaded)
    {
        _tracksByPath.Clear();
        foreach (var (path, track) in loaded.Tracks)
            _tracksByPath[path] = track;

        _cachedEntries.Clear();
        foreach (var (path, entry) in loaded.Entries)
            _cachedEntries[path] = entry;

        UnavailableRootCount = 0;
        RebuildGroups();
        StatusText = _roots.Count == 0 ? string.Empty : BuildStatus();
    }

    private void RebuildGroups()
    {
        var result = LibraryGrouping.Build(_tracksByPath.Values);

        var selectedArtistName = SelectedArtist?.Name;
        var selectedAlbumTitle = SelectedAlbum?.Title;
        var selectedAlbumArtist = SelectedAlbum?.Artist;

        Artists.Clear();
        foreach (var artist in result.Artists) Artists.Add(artist);
        Albums.Clear();
        foreach (var album in result.Albums) Albums.Add(album);

        if (selectedArtistName is not null)
        {
            SelectedArtist = result.Artists.FirstOrDefault(a => string.Equals(
                a.Name, selectedArtistName, StringComparison.CurrentCultureIgnoreCase));
            if (SelectedArtist is null && Page == LibraryPage.ArtistDetail)
                Page = LibraryPage.Artists;
        }

        if (selectedAlbumTitle is not null)
        {
            SelectedAlbum = result.Albums.FirstOrDefault(a =>
                string.Equals(a.Title, selectedAlbumTitle, StringComparison.CurrentCultureIgnoreCase) &&
                string.Equals(a.Artist, selectedAlbumArtist, StringComparison.CurrentCultureIgnoreCase));
            if (SelectedAlbum is null && Page == LibraryPage.AlbumDetail)
                Page = LibraryPage.Albums;
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    private string BuildStatus()
    {
        var text = $"{Artists.Count} 位艺术家 · {Albums.Count} 张专辑 · {_tracksByPath.Count} 首";
        return UnavailableRootCount > 0
            ? text + $" · {UnavailableRootCount} 个文件夹不可用"
            : text;
    }

    partial void OnIsScanningChanged(bool value)
        => OnPropertyChanged(nameof(EmptyStateText));

    partial void OnHasLibraryRootsChanged(bool value)
        => OnPropertyChanged(nameof(EmptyStateText));

    private static Track TrackFromEntry(LibraryCacheEntry entry) =>
        new(entry.FilePath, entry.Title, entry.Artist, entry.Album, entry.Genre,
            entry.Year, entry.SampleRate, null, entry.Duration, entry.TrackNumber);

    private static LibraryCacheEntry EntryFromTrack(Track track) =>
        new(track.FilePath, track.Title, track.Artist, track.Album, track.Genre,
            track.Year, track.Duration, track.SampleRate, track.TrackNumber);

    private sealed record LoadedLibrary(
        Dictionary<string, Track> Tracks,
        Dictionary<string, LibraryCacheEntry> Entries);
}
