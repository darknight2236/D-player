using System.IO;
using DPlayer.Models;
using DPlayer.Services;

namespace DPlayer.ViewModels;

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
    private Task? _saveTask;
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

        // Phase 10: 后台扫描文件夹绑定歌单(不阻塞 UI)
        _ = Playlists.RescanFolderBoundPlaylistsAsync();
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
    public async Task CleanupAsync()
    {
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
        }

        await Player.CleanupAsync().ConfigureAwait(true);
        _player.Dispose();
    }

    public async ValueTask DisposeAsync() => await CleanupAsync().ConfigureAwait(false);
}
