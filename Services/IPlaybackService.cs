using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 核心播放服务抽象 —— VM 仅依赖此接口，便于替换底层实现（NAudio / BASS / 自研）。
///
/// 生命周期：DI 容器以 Singleton 注册；首次解析必须发生在 UI 线程，
/// 因为实现会捕获 SynchronizationContext.Current 用于事件回调封送。
/// </summary>
public interface IPlaybackService : IDisposable
{
    // —— 状态属性（只读快照） ——
    PlayState State { get; }
    Track? CurrentTrack { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    /// <summary>音量 0.0~1.0，超出会被 Clamp。</summary>
    float Volume { get; set; }

    // —— 控制指令 ——

    /// <summary>异步加载并准备一首曲目（解码器、输出设备初始化都在此完成）。</summary>
    Task LoadAsync(Track track);
    void Play();
    void Pause();
    void Stop();

    /// <summary>跳转到指定播放位置。</summary>
    void Seek(TimeSpan position);

    // —— 事件（所有事件保证在 UI 线程触发） ——
    event Action<PlayState> StateChanged;
    event Action<TimeSpan> PositionChanged;
    event Action<TimeSpan> DurationChanged;
    event Action<Track> TrackChanged;
    event Action<string>? PlaybackError;
}
