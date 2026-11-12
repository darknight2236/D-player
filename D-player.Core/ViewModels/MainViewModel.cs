using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;

namespace DPlayer.ViewModels;

/// <summary>
/// Phase 6/24 Facade: Player + Playlists 容器 + Library 视图模型 + 窗口生命周期清理。
/// 集中持有 debounce save 的 CancellationTokenSource ——
/// 任何子 VM 状态变化(PlaylistsViewModel.StateChanged) 触发 500ms 后写盘;
/// 在 500ms 内连发的变更只产出一次 SaveAsync。
///
/// 边界约束(COUPLING.md, Phase 6 新不变量):
///   - 不再暴露单个 PlaylistViewModel; 只暴露 Player + Playlists + Library + CleanupAsync + InitializeAsync
///   - debounce 状态(_saveCts)只在本类持有, 子 VM 不感知存盘
///   - CleanupAsync 关闭前 flush: 取消挂起的 timer, 立即同步 SaveAsync, 再 Dispose Player
///   - Phase 24: IsLibraryViewActive 是壳级导航状态（播放列表 ↔ 音乐库），子 VM 不自持
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int DebounceMs = 500;

    private readonly IPlaybackService _player;
    private readonly IPlaylistService _playlistService;
    private readonly ISettingsPersistence _settings;
    private CancellationTokenSource? _saveCts;
    private Task? _saveTask;
    private volatile bool _hydrated;
    private readonly OneShotCleanup _cleanup = new();

    public PlayerViewModel Player { get; }
    public PlaylistsViewModel Playlists { get; }

    public LibraryViewModel Library { get; }

    [ObservableProperty]
    private bool _isLibraryViewActive;

    /// <summary>启动水化与导航恢复完成后才显示中间内容，避免先闪歌单视图。</summary>
    [ObservableProperty]
    private bool _isInitialized;

    public MainViewModel(
        PlayerViewModel player,
        PlaylistsViewModel playlists,
        LibraryViewModel library,
        IPlaybackService playbackService,
        IPlaylistService playlistService,
        ISettingsPersistence settings)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Playlists = playlists ?? throw new ArgumentNullException(nameof(playlists));
        Library = library ?? throw new ArgumentNullException(nameof(library));
        _player = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _playlistService = playlistService ?? throw new ArgumentNullException(nameof(playlistService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void ShowLibrary(LibraryPage page)
    {
        if (page == LibraryPage.Albums)
            Library.ShowAlbums();
        else
            Library.ShowArtists();
        IsLibraryViewActive = true;
    }

    public void ShowPlaylist()
    {
        Library.ClearSearch();
        IsLibraryViewActive = false;
    }

    partial void OnIsLibraryViewActiveChanged(bool value)
    {
        // 库视图没有“查看中的歌单”：启动恢复直接置位时也要清掉 Hydrate 选中的第一个歌单，
        // 否则库页与侧栏歌单会同时高亮。
        if (value)
            Playlists.ViewedPlaylist = null;
    }

    /// <summary>
    /// MainWindow Loaded 时调用一次。读 queue.json (含 v1→v2 迁移), Hydrate 容器, 开始监听 StateChanged,
    /// 读库缓存回填库播放队列, 然后做断点续播就位（只加载不出声）, 最后启动两路后台扫描。
    /// 库初始化失败只会留下空库, 不影响断点续播与后台扫描。
    /// 返回非 null = 恢复被跳过、需要向用户提示的原因。
    /// </summary>
    public async Task<string?> InitializeAsync()
    {
        try
        {
            return await InitializeCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            IsInitialized = true;
        }
    }

    private async Task<string?> InitializeCoreAsync()
    {
        if (_hydrated) return null;
        var snapshot = await _playlistService.LoadAsync().ConfigureAwait(true);
        Playlists.Hydrate(snapshot);
        Playlists.StateChanged += OnPlaylistsStateChanged;
        _hydrated = true;

        // 先读库缓存并回填隐藏的库播放队列，再恢复上次播放位置。
        // 库是可选能力：缓存/元数据失败不能连累断点续播与后台扫描，也不能把启动变成
        // 半初始化状态。_hydrated 保持在上方（若挪到末尾，重试会重跑 Hydrate 并重复订阅
        // StateChanged），所以这里只吞异常、继续往下走。
        try
        {
            await Library.InitializeAsync().ConfigureAwait(true);
            Playlists.ApplyCachedMetadataToLibraryPlayback(Library.CachedEntriesByPath);
        }
        catch
        {
            // 库初始化失败：保留空库，继续断点续播与两路后台扫描。
        }

        AppSettings? settings = null;
        try { settings = await _settings.LoadAsync().ConfigureAwait(true); }
        catch { /* 设置读不出来时跳过恢复，不打扰启动 */ }

        RestoreLibraryView(settings);
        var restoreWarning = await RestoreLastPlayedAsync(settings).ConfigureAwait(true);

        // Phase 10/24: 后台扫描文件夹歌单与音乐库，不阻塞 UI。
        _ = Playlists.RescanFolderBoundPlaylistsAsync();
        _ = Library.ScanInBackgroundAsync();

        return restoreWarning;
    }

    /// <summary>
    /// 断点续播：把上次播放的曲目与位置装进播放器（**不**自动出声，▶ 才出声）。
    /// 跳过恢复时返回给用户看的原因；正常恢复或本就无记录返回 null。
    /// </summary>
    private void RestoreLibraryView(AppSettings? settings)
    {
        if (settings is not { LibraryViewActive: true } || !Library.HasLibraryRoots)
            return;

        var page = settings.LibraryViewPage;
        if (page == nameof(LibraryPage.AlbumDetail) &&
            settings.LibraryViewAlbumTrackPath is { Length: > 0 } trackPath)
        {
            var album = Library.Albums.FirstOrDefault(a => a.Tracks.Any(t =>
                string.Equals(t.FilePath, trackPath, StringComparison.OrdinalIgnoreCase)));
            if (album is not null)
            {
                Library.OpenAlbum(album, fromArtist: false);
                IsLibraryViewActive = true;
                return;
            }
        }

        if (page == nameof(LibraryPage.ArtistDetail) &&
            settings.LibraryViewArtistName is { Length: > 0 } artistName)
        {
            var artist = Library.Artists.FirstOrDefault(a =>
                string.Equals(a.Name, artistName, StringComparison.CurrentCultureIgnoreCase));
            if (artist is not null)
            {
                Library.OpenArtist(artist);
                IsLibraryViewActive = true;
                return;
            }
        }

        if (page == nameof(LibraryPage.Albums) || page == nameof(LibraryPage.AlbumDetail))
        {
            Library.ShowAlbums();
            IsLibraryViewActive = true;
            return;
        }

        if (page == nameof(LibraryPage.Artists) || page == nameof(LibraryPage.ArtistDetail))
        {
            Library.ShowArtists();
            IsLibraryViewActive = true;
        }
    }

    private async Task<string?> RestoreLastPlayedAsync(AppSettings? settings)
    {
        if (settings is null) return null;

        var path = settings.LastPlayedPath;
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (!File.Exists(path))
            return $"上次播放的文件已不存在，未恢复播放位置：\n{path}";

        var found = Playlists.FindPlaylistTrackByPath(path);
        if (found is null)
            return $"上次播放的曲目已不在任何歌单中，未恢复播放位置：\n{path}";

        var (owner, track) = found.Value;
        // 归属同步：恢复的曲目属于哪个歌单，哪个歌单就是当前播放歌单。
        // 旧版本从 ViewedPlaylist 发起播放时不会同步指针，升级后首次启动可能不一致；
        // 不同步会让用户按 ▶ 出声后，TrackEnded 守卫推进错误的歌单。
        if (Playlists.CurrentPlaylistId != owner.Id)
            Playlists.CurrentPlaylistId = owner.Id;

        track = await owner.EnsureAlbumArtAsync(track).ConfigureAwait(true);
        await _player.LoadAsync(track).ConfigureAwait(true);

        var position = TimeSpan.FromSeconds(settings.LastPlayedPositionSeconds);
        if (position > TimeSpan.Zero)
            _player.Seek(position);

        // 立即回写一次：启动后若马上退出/被杀，不会把位置退回 0
        Player.SaveLastPlayedState();
        return null;
    }

    private void OnPlaylistsStateChanged(object? sender, EventArgs e) => ScheduleSave();

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _saveCts = cts;
        _saveTask = SaveAfterDelayAsync(cts.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken ct)
    {
        // ConfigureAwait(true): 续延仍在 UI 线程, 这样 BuildSnapshot 读 ObservableCollection
        // 不会与 UI 线程的拖拽/Add/Remove 撞车 (Code review C1)。
        try { await Task.Delay(DebounceMs, ct).ConfigureAwait(true); }
        catch (TaskCanceledException) { return; }

        QueueState snapshot;
        try { snapshot = Playlists.BuildSnapshot(); }
        catch { return; } // 集合在快照过程中被并发改动 → 放弃这一轮; 下个 StateChanged 会重排。

        try
        {
            // 实际 IO 走线程池, 不阻塞 UI。
            await _playlistService.SaveAsync(snapshot).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // 吞掉 —— Phase 6 容忍 IO 失败, 用户下次操作会再次触发 debounce save。
        }
    }

    /// <summary>
    /// 窗口关闭路径: 取消挂起 debounce, 等当前 in-flight save 跑完(避免被它的旧快照
    /// 覆盖最终写), 立即同步 flush, 解绑事件, 释放音频。MainWindow.Window_Closing
    /// 用 cancel-and-close 模式包住此调用 (memory: wpf-async-void-closing-dispatcher-race)。
    /// </summary>
    public Task CleanupAsync()
        => _cleanup.RunAsync(CleanupCoreAsync, continueOnCapturedContext: true);

    private async Task CleanupCoreAsync()
    {
        var libraryViewActive = IsLibraryViewActive;
        var navigation = Library.CaptureNavigation();

        // 先封闭库与歌单入口并等待其内部扫描/保存收尾，再写最终快照。
        var libraryCleanup = Library.CleanupAsync();
        var playlistsCleanup = Playlists.CleanupAsync();

        Playlists.StateChanged -= OnPlaylistsStateChanged;
        _saveCts?.Cancel();

        // 等任何 in-flight SaveAfterDelayAsync 收尾, 否则它的旧快照会在我们写完后覆盖。
        // _saveTask 自身不抛 (内部已捕 IOException 与 TaskCanceledException), 这里防御性兜底。
        if (_saveTask is not null)
        {
            try { await _saveTask.ConfigureAwait(true); } catch { /* 已在内部处理 */ }
        }

        _saveCts?.Dispose();
        _saveCts = null;
        _saveTask = null;

        await Task.WhenAll(libraryCleanup, playlistsCleanup).ConfigureAwait(true);

        if (_hydrated)
        {
            try
            {
                var snapshot = Playlists.BuildSnapshot();
                await _playlistService.SaveAsync(snapshot).ConfigureAwait(true);
            }
            catch (IOException) { /* 吞掉, 关闭路径不阻塞 */ }

            // 关闭路径同时把 PlaylistViewModel.TrackEnded 订阅一并解掉, 与 Hydrate / RemovePlaylist
            // 的容器换出路径对称(Code review M3)。
            foreach (var vm in Playlists.Playlists)
                vm.Cleanup();
            Playlists.LibraryPlayback?.Cleanup();

            try
            {
                await _settings.UpdateAsync(s => s with
                {
                    LibraryViewActive = libraryViewActive,
                    LibraryViewPage = libraryViewActive ? navigation.Page.ToString() : null,
                    LibraryViewArtistName = libraryViewActive &&
                        navigation.Page == LibraryPage.ArtistDetail
                            ? navigation.ArtistName
                            : null,
                    LibraryViewAlbumTrackPath = libraryViewActive &&
                        navigation.Page == LibraryPage.AlbumDetail
                            ? navigation.AlbumTrackPath
                            : null,
                }).ConfigureAwait(true);
            }
            catch
            {
                // 关闭流程不打扰用户；导航状态丢失不影响播放数据。
            }
        }

        await Player.CleanupAsync().ConfigureAwait(true);
        _player.Dispose();
    }

    public async ValueTask DisposeAsync() => await CleanupAsync().ConfigureAwait(false);
}
