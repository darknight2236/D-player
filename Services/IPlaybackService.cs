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

    /// <summary>
    /// 卸载当前曲目：停止播放并释放底层解码器/输出设备。
    /// 与 Stop() 区别：Stop() 仅暂停并把位置归零（下次 Play 会重播同一首），
    /// Unload() 之后调用 Play() 是 no-op，必须先 LoadAsync 一首新曲。
    /// 用于"清空队列"/"删除当前曲"等场景。
    /// </summary>
    void Unload();

    /// <summary>跳转到指定播放位置。</summary>
    void Seek(TimeSpan position);

    // —— 事件（所有事件保证在 UI 线程触发） ——
    event Action<PlayState> StateChanged;
    event Action<TimeSpan> PositionChanged;
    event Action<TimeSpan> DurationChanged;
    /// <summary>
    /// 当前曲目变化。新加载曲目时携带 Track；调用 Unload() 卸载时携带 null
    /// 通知 VM 清屏（清除标题/艺术家/专辑/封面显示）。
    /// </summary>
    event Action<Track?> TrackChanged;
    event Action<string>? PlaybackError;

    /// <summary>
    /// 曲目「自然播完」时触发（区别于用户 Stop 或异常）。
    /// 用于实现自动下一首：VM 订阅此事件计算并播放下一首。
    /// </summary>
    event Action? TrackEnded;

    /// <summary>
    /// 频谱数据可用事件（每帧 FFT 计算后触发，已在 UI 线程）
    /// </summary>
    event Action<float[]>? SpectrumDataAvailable;

    /// <summary>
    /// 频谱配置（运行时可更新）
    /// </summary>
    SpectrumConfig SpectrumConfig { get; set; }
}
