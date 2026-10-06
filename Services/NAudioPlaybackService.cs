using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 基于 NAudio 的播放服务实现。
///
/// 播放链路：MediaFoundationReader → EqualizerSampleProvider → SampleAggregator → VolumeSampleProvider → WasapiOut(Shared)
///   - MediaFoundationReader：调用 Windows Media Foundation 原生解码 MP3/WMA/FLAC/AAC/WAV
///   - EqualizerSampleProvider：10 段图形均衡器（Phase 14），置于 SampleAggregator 之前 → 频谱反映 EQ 后信号
///   - SampleAggregator     ：透明截取 PCM 数据执行 FFT 频谱分析（Phase 13）
///   - VolumeSampleProvider ：在样本层做线性音量缩放
///   - WasapiOut(Shared)    ：共享模式输出，100ms 缓冲（低延迟与稳定性的折中）
///
/// 线程模型：构造时捕获 UI 线程 SynchronizationContext，
///          所有事件通过 _syncContext.Post 派发，VM 可直接绑定属性。
/// </summary>
public sealed class NAudioPlaybackService : IPlaybackService
{
    private readonly SynchronizationContext _syncContext;
    private IWavePlayer? _wavePlayer;
    private MediaFoundationReader? _reader;
    private VolumeSampleProvider? _volumeProvider;
    private Track? _currentTrack;
    private PlayState _state = PlayState.Stopped;
    private float _volume = 0.8f;

    /// <summary>
    /// 播放链生命周期闸门。LoadAsync 在线程池上重建整条链，而 Unload/Dispose/传输命令
    /// 可能来自 UI 线程；没有串行化时，后一次重建的 DisposePlayback 会释放前一次
    /// 正在 Init 的 WasapiOut 实例（audioClient 被置空 / COM 包装分离），异常从
    /// NAudio 内部抛出并冒到 async void 事件处理器 —— 表现为"播完一首歌后进程崩溃"。
    ///
    /// 纪律：持锁期间不得 await；不得阻塞等待 UI 线程（事件一律 Post 异步派发）。
    /// NAudio 播放线程上的 OnPlaybackStopped **不能**取此锁：持锁方可能正阻塞在
    /// _wavePlayer.Stop() 的 Join(playThread) 上，取锁会立即死锁。
    /// </summary>
    private readonly object _chainGate = new();

    // Phase 13: 频谱分析
    private SampleAggregator? _sampleAggregator;
    private SpectrumConfig _spectrumConfig = new();

    // Phase 14: 均衡器
    private EqualizerSampleProvider? _equalizer;
    private EqualizerConfig _equalizerConfig = new();

    // 位置事件节流：33ms ≈ 30Hz，刚好覆盖 60Hz 屏的"每两帧一次"，再高对感知无帮助
    private static readonly TimeSpan PositionThrottle = TimeSpan.FromMilliseconds(33);
    /// <summary>「自然播完」判定容差：播放头距 TotalTime 在此范围内视为正常播完，区别于用户 Stop。</summary>
    private static readonly TimeSpan NaturalEndTolerance = TimeSpan.FromMilliseconds(200);
    private DateTime _lastPositionEvent = DateTime.MinValue;

    public PlayState State => _state;
    public Track? CurrentTrack => _currentTrack;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_volumeProvider != null)
                _volumeProvider.Volume = _volume;
        }
    }

    public event Action<PlayState>? StateChanged;
    public event Action<TimeSpan>? PositionChanged;
    public event Action<TimeSpan>? DurationChanged;
    public event Action<Track?>? TrackChanged;
    public event Action<string>? PlaybackError;
    public event Action? TrackEnded;

    // Phase 13: 频谱事件和配置
    public event Action<float[]>? SpectrumDataAvailable;

    public SpectrumConfig SpectrumConfig
    {
        get => _spectrumConfig;
        set
        {
            _spectrumConfig = value;
            if (_sampleAggregator != null)
            {
                _sampleAggregator.Enabled = value.Enabled;
            }
        }
    }

    // Phase 14: 均衡器配置（setter 语义对齐 SpectrumConfig：存字段 + 在链 provider 实时下发）
    public EqualizerConfig EqualizerConfig
    {
        get => _equalizerConfig;
        set
        {
            _equalizerConfig = value;
            _equalizer?.Update(value);
        }
    }

    public NAudioPlaybackService()
    {
        // App.OnStartup 在 UI 线程解析本服务，因此 Current 一定非空；
        // 容错给一个新 SyncContext，避免单元测试场景 NRE。
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    /// <summary>
    /// 加载一首曲目：销毁旧播放链 → 新建解码器/音量/输出 → 回填 Duration 与 SampleRate。
    /// 重量级操作放在 Task.Run 上避免阻塞 UI。
    /// </summary>
    public async Task LoadAsync(Track track)
    {
        await Task.Run(() =>
        {
            lock (_chainGate)
            {
                DisposePlayback();

                try
                {
                    _reader = new MediaFoundationReader(track.FilePath);

                    // Phase 13/14: ToSample → EqualizerSampleProvider → SampleAggregator
                    // EQ 置于 SampleAggregator 之前 → 频谱可视化反映 EQ 处理后的信号
                    var sampleProvider = _reader.ToSampleProvider();
                    _equalizer = new EqualizerSampleProvider(sampleProvider, _equalizerConfig);
                    _sampleAggregator = new SampleAggregator(_equalizer, _spectrumConfig);
                    _sampleAggregator.SpectrumDataReady += OnSpectrumDataReady;

                    _volumeProvider = new VolumeSampleProvider(_sampleAggregator)
                    {
                        Volume = _volume
                    };
                    _wavePlayer = new WasapiOut(AudioClientShareMode.Shared, 100);
                    _wavePlayer.Init(_volumeProvider);

                    _wavePlayer.PlaybackStopped += OnPlaybackStopped;

                    // 用解码器实测值回填 Track；调用方传入的 Duration 通常为 Zero
                    _currentTrack = track with
                    {
                        Duration = _reader.TotalTime,
                        SampleRate = _reader.WaveFormat.SampleRate
                    };

                    // 先归零 Position，再广播新 Duration —— 否则切换到时长更短的曲目时，
                    // VM 的旧 Position（如上一首播完的 4:05）会和新 Duration（3:20）短暂并存，
                    // 进度条出现"4:05 / 3:20"的越界显示，直到下一帧 PollPositionAsync 才纠正。
                    RaiseOnUIThread(PositionChanged, TimeSpan.Zero);
                    RaiseOnUIThread(DurationChanged, _reader.TotalTime);
                    RaiseOnUIThread(TrackChanged, _currentTrack);
                }
                catch (Exception ex)
                {
                    RaiseOnUIThread(PlaybackError, ex.Message);
                    throw;
                }
            }
        });
    }

    public void Play()
    {
        lock (_chainGate)
        {
            if (_wavePlayer == null) return;
            _wavePlayer.Play();
            SetState(PlayState.Playing);
        }
        _ = PollPositionAsync(); // fire-and-forget：循环在 PlaybackState!=Playing 时自然退出
    }

    public void Pause()
    {
        lock (_chainGate)
        {
            if (_wavePlayer == null) return;
            _wavePlayer.Pause();
            SetState(PlayState.Paused);
        }
    }

    public void Stop()
    {
        lock (_chainGate)
        {
            if (_wavePlayer == null) return;
            _wavePlayer.Stop();
            // 同时将播放头归零，下次 Play 从头开始
            if (_reader != null)
                _reader.CurrentTime = TimeSpan.Zero;
            SetState(PlayState.Stopped);
        }
    }

    public void Seek(TimeSpan position)
    {
        TimeSpan clamped;
        lock (_chainGate)
        {
            if (_reader == null) return;
            _reader.CurrentTime = position;
            // Clamp 到 [0, TotalTime]：解码器对 seek 到尾部允许越界几十毫秒。
            clamped = ClampToDuration(_reader.CurrentTime);
        }

        // 主动广播一次新位置：暂停态下 PollPositionAsync 已退出，否则 VM.Position 不刷新，
        // 进度条会停留在旧位置直到用户按 Play 才被轮询拽回（用户视角看起来像"没跳转"）。
        RaiseOnUIThread(PositionChanged, clamped);
    }

    /// <summary>
    /// 卸载当前曲：释放底层 reader/wavePlayer，清掉 _currentTrack，
    /// 并广播 TrackChanged(null) / DurationChanged(Zero) 让 VM 清屏（标题/封面/时长归零）。
    /// 下次 Play() 会因 _wavePlayer == null 直接 no-op。
    /// </summary>
    public void Unload()
    {
        lock (_chainGate)
        {
            DisposePlayback();
            _currentTrack = null;
        }
        RaiseOnUIThread(TrackChanged, (Track?)null);
        RaiseOnUIThread(DurationChanged, TimeSpan.Zero);
        RaiseOnUIThread(PositionChanged, TimeSpan.Zero);
        SetState(PlayState.Stopped);
    }

    private void SetState(PlayState newState)
    {
        if (_state == newState) return; // 去重，避免连续触发同状态事件
        _state = newState;
        RaiseOnUIThread(StateChanged, _state);
    }

    /// <summary>
    /// NAudio 在以下情况触发 PlaybackStopped：
    /// (1) 播放到曲尾  (2) 用户调用 Stop()  (3) 设备出错
    /// 区分逻辑:
    ///   - 有异常 → 上报 PlaybackError + Stopped 状态
    ///   - 无异常 + 播放位置接近 TotalTime (200ms 容差) → 自然播完 → 触发 TrackEnded
    ///     (注：用户 Stop() 已先把 CurrentTime 归零，差值 = TotalTime，不会误判)
    ///   - 其他 → 仅 Stopped 状态（如:从中段 Pause 后再 Stop 的边缘场景）
    /// </summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            RaiseOnUIThread(PlaybackError, e.Exception.Message);
            SetState(PlayState.Stopped);
            return;
        }

        // 自然播完判定：播放头距 TotalTime 不超过容差，且时长大于 0（避免空 reader 误判）。
        // 本方法运行在 NAudio 播放线程上，不能取 _chainGate —— 持锁方可能正阻塞在
        // _wavePlayer.Stop() 的 Join(playThread) 上，取锁会立即死锁。因此这里只做防御性读取：
        // reader 可能正被并发的 DisposePlayback 释放，而在音频线程上抛出会直接崩进程。
        bool naturalEnd = false;
        var reader = _reader;
        if (reader != null)
        {
            try
            {
                naturalEnd = reader.TotalTime > TimeSpan.Zero
                    && (reader.TotalTime - reader.CurrentTime) <= NaturalEndTolerance;
            }
            catch
            {
                // reader 已被并发释放 → 按"非自然结束"处理
            }
        }

        if (naturalEnd)
        {
            RaiseOnUIThread(TrackEnded);
            // 状态仍设为 Stopped；VM 的 TrackEnded handler 决定是否随即 Play 下一首
        }

        SetState(PlayState.Stopped);
    }

    /// <summary>
    /// 频谱数据回调（在音频线程触发）→ 封送到 UI 线程触发 SpectrumDataAvailable 事件。
    /// </summary>
    private void OnSpectrumDataReady(float[] data)
    {
        RaiseOnUIThread(SpectrumDataAvailable, data);
    }

    /// <summary>
    /// 后台轮询循环：每 ~33ms 上报一次 Position；
    /// 通过 PlaybackState != Playing 自动退出（Pause/Stop 都会让其下一轮终止）。
    /// </summary>
    private async Task PollPositionAsync()
    {
        while (_wavePlayer?.PlaybackState == PlaybackState.Playing)
        {
            await Task.Delay(33); // ~30Hz

            var now = DateTime.UtcNow;
            if (now - _lastPositionEvent >= PositionThrottle)
            {
                _lastPositionEvent = now;
                var pos = ClampToDuration(_reader?.CurrentTime ?? TimeSpan.Zero);
                RaiseOnUIThread(PositionChanged, pos);
            }
        }
    }

    /// <summary>把 Position 截断到 [0, TotalTime]，防止解码器尾部浮点越界（如 245.012s 上报到时长 245.000s 的曲）。</summary>
    private TimeSpan ClampToDuration(TimeSpan pos)
    {
        var total = _reader?.TotalTime ?? TimeSpan.Zero;
        if (pos < TimeSpan.Zero) return TimeSpan.Zero;
        if (total > TimeSpan.Zero && pos > total) return total;
        return pos;
    }

    /// <summary>将事件回调封送到 UI 线程；VM 中所有 handler 可直接更新绑定属性。</summary>
    private void RaiseOnUIThread<T>(Action<T>? handler, T value)
    {
        if (handler == null) return;
        _syncContext.Post(_ => handler(value), null);
    }

    /// <summary>将无参事件回调封送到 UI 线程。</summary>
    private void RaiseOnUIThread(Action? handler)
    {
        if (handler == null) return;
        _syncContext.Post(_ => handler(), null);
    }

    /// <summary>释放当前播放链；切歌前与 Dispose 都会调用。顺序：解订阅 → 停止 → 释放。</summary>
    private void DisposePlayback()
    {
        // Phase 13: 清理 SampleAggregator
        if (_sampleAggregator != null)
        {
            _sampleAggregator.SpectrumDataReady -= OnSpectrumDataReady;
            _sampleAggregator = null;
        }

        // Phase 14: 清理 EqualizerSampleProvider（无事件订阅，仅置空引用，随播放链释放）
        _equalizer = null;

        if (_wavePlayer != null)
        {
            _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            _wavePlayer.Stop();
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }
        _volumeProvider = null;
        if (_reader != null)
        {
            _reader.Dispose();
            _reader = null;
        }
    }

    public void Dispose()
    {
        lock (_chainGate)
        {
            DisposePlayback();
        }
    }
}
