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
