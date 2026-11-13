using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 基于 NAudio 的播放服务实现。
///
/// 播放链路：MediaFoundationAudioSource(MediaFoundationReader) → EqualizerSampleProvider → SampleAggregator → VolumeSampleProvider → WasapiPlayer(Shared)
///   - MediaFoundationReader（经 MediaFoundationAudioSource 适配）：调用 Windows Media Foundation 原生解码 MP3/WMA/FLAC/AAC/WAV
///   - EqualizerSampleProvider：10 段图形均衡器（Phase 14），置于 SampleAggregator 之前 → 频谱反映 EQ 后信号
///   - SampleAggregator     ：透明截取 PCM 数据执行 FFT 频谱分析（Phase 13）
///   - VolumeSampleProvider ：在样本层做线性音量缩放
///   - WasapiPlayer         ：共享模式 + 事件同步输出，100ms 缓冲（低延迟与稳定性的折中）
///
/// 线程模型（Phase 30）：整条播放链封装为 <see cref="PlaybackChain"/> 快照，_current 指向当前链；
/// 装载（打开解码器、初始化输出）、拆除旧链都在 _chainGate 之外完成，闸门内只交换引用与配置快照。
/// 装载期间传输命令看到 _current == null，按"无链"语义立即 no-op，不再阻塞 UI 线程。
/// 构造时捕获 UI 线程 SynchronizationContext，所有事件通过 _syncContext.Post 派发，VM 可直接绑定属性。
/// </summary>
public sealed class NAudioPlaybackService : IPlaybackService
{
    private readonly SynchronizationContext _syncContext;
    private readonly IAudioSourceFactory _sourceFactory;
    private readonly IAudioOutputFactory _outputFactory;

    /// <summary>
    /// 链发布闸门：只保护 _current 引用与随链配置快照的交接，持锁时间是几次字段读写。
    /// 打开解码器 / 初始化输出 / 拆除旧链等重量级操作**不得**在锁内执行——否则 UI 线程的
    /// Play/Pause/Stop/Seek/Unload 会被设备初始化阻塞（Phase 30 之前的实测行为：装载中按
    /// Play 会卡住 UI 线程直到 Init 返回）。装载由独立的 _loadGate 串行化。
    ///
    /// 纪律：持锁期间不得 await；不得阻塞等待 UI 线程（事件一律 Post 异步派发）；
    /// 播放线程回调 OnPlaybackStopped **不能**取此锁——持锁方可能正阻塞在输出类的
    /// Stop()/Dispose() 上等待播放线程退出（3.1.0 文档只把 Dispose 标为 blocking），取锁即死锁。
    /// 因此 OnPlaybackStopped 只用 Volatile.Read 判定链代际。
    /// </summary>
    private readonly object _chainGate = new();

    /// <summary>
    /// 装载闸门：串行化 LoadAsync，保证同一时刻只有一次装载在构建/安装链。
    /// 与 _chainGate 分离的原因：装载可能阻塞数百毫秒（打开 reader、Init 输出），
    /// 而传输命令与 Unload 必须保持即时返回；两者共用一把锁会把"装载中"变成"UI 冻结"。
    /// </summary>
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    /// <summary>当前播放链；null 表示无链（未装载、装载中、卸载后或已释放）。</summary>
    private PlaybackChain? _current;

    /// <summary>装载取消代际：Unload/Dispose 递增；装载完成时若代际已变，则丢弃新链不安装。</summary>
    private long _loadCancelVersion;

    /// <summary>播放会话代际：每次 Play 递增，轮询循环发现代际变化即退出，避免旧循环叠加。</summary>
    private long _playSession;

    private volatile bool _disposed;
    private volatile PlayState _state = PlayState.Stopped;
    private float _volume = 0.8f;

    // Phase 13: 频谱配置（建链快照 + 当前链 Enabled 实时下发）
    private SpectrumConfig _spectrumConfig = new();

    // Phase 14: 均衡器配置（建链快照 + 当前链无锁 Update 实时下发）
    private EqualizerConfig _equalizerConfig = new();

    // 位置事件节流：33ms ≈ 30Hz，刚好覆盖 60Hz 屏的"每两帧一次"，再高对感知无帮助
    private static readonly TimeSpan PositionThrottle = TimeSpan.FromMilliseconds(33);
    /// <summary>「自然播完」判定容差：播放头距 TotalTime 在此范围内视为正常播完，区别于用户 Stop。</summary>
    private static readonly TimeSpan NaturalEndTolerance = TimeSpan.FromMilliseconds(200);
    private DateTime _lastPositionEvent = DateTime.MinValue;

    public PlayState State => _state;
    public Track? CurrentTrack => Volatile.Read(ref _current)?.Track;
    public TimeSpan Duration => Volatile.Read(ref _current)?.Track.Duration ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get
        {
            var chain = Volatile.Read(ref _current);
            if (chain == null) return TimeSpan.Zero;
            // 源可能正被并发拆除（Unload 在锁外 Dispose 旧链）→ 已释放按零处理
            try { return ClampToDuration(chain.Source.CurrentTime, chain.Track.Duration); }
            catch (ObjectDisposedException) { return TimeSpan.Zero; }
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            lock (_chainGate)
            {
                _volume = clamped;
                var chain = _current;
                if (chain != null)
                    chain.Volume.Volume = clamped;
            }
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
            lock (_chainGate)
            {
                _spectrumConfig = value;
                // 运行时只支持 Enabled（FftSize/BarCount 是建链参数，见 Phase 30 设计 §8）
                var chain = _current;
                if (chain != null)
                    chain.Aggregator.Enabled = value.Enabled;
            }
        }
    }

    // Phase 14: 均衡器配置（setter 语义对齐 SpectrumConfig：存字段 + 在链 provider 实时下发）
    public EqualizerConfig EqualizerConfig
    {
        get => _equalizerConfig;
        set
        {
            lock (_chainGate)
            {
                _equalizerConfig = value;
                // EQ 走无锁发布（Phase 30 Task 8）：音频线程在下一个 Read 取走配置
                _current?.Equalizer.Update(value);
            }
        }
    }

    public NAudioPlaybackService()
        : this(
            new MediaFoundationAudioSourceFactory(),
            new StubAudioOutputFactory(),
            // App.OnStartup 在 UI 线程解析本服务，因此 Current 一定非空；
            // 容错给一个新 SyncContext，避免单元测试场景 NRE。
            SynchronizationContext.Current ?? new SynchronizationContext())
    {
    }

    internal NAudioPlaybackService(
        IAudioSourceFactory sourceFactory,
        IAudioOutputFactory outputFactory,
        SynchronizationContext syncContext)
    {
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _outputFactory = outputFactory ?? throw new ArgumentNullException(nameof(outputFactory));
        _syncContext = syncContext ?? throw new ArgumentNullException(nameof(syncContext));
    }

    /// <summary>
    /// 加载一首曲目：先在闸门内摘下旧链，再在线程池上拆除旧链并新建解码器/音量/输出，
    /// 最后回到闸门安装新链（期间若发生 Unload/Dispose 则丢弃新链）。
    /// 装载重活不持 <see cref="_chainGate"/>，因此装载期间的传输命令立即 no-op 而非阻塞。
    /// </summary>
    public async Task LoadAsync(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (_disposed) throw new ObjectDisposedException(nameof(NAudioPlaybackService));

        await _loadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NAudioPlaybackService));

            PlaybackChain? old;
            long cancelVersion;
            float volume;
            EqualizerConfig eqConfig;
            SpectrumConfig spectrumConfig;

            lock (_chainGate)
            {
                old = _current;
                _current = null;
                if (old != null) old.StopRequested = true; // 拆除同样是我们发起的停止，别让它冒充"自然播完"
                cancelVersion = _loadCancelVersion;
                volume = _volume;
                eqConfig = _equalizerConfig;
                spectrumConfig = _spectrumConfig;
                SetState(PlayState.Stopped);
            }

            // 旧链拆除与新链构建都在锁外、线程池上：设备 Stop/Dispose 可能阻塞数百毫秒。
            PlaybackChain chain;
            try
            {
                chain = await Task.Run(() =>
                {
                    old?.Dispose();
                    return BuildChain(track, volume, eqConfig, spectrumConfig);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaiseOnUIThread(PlaybackError, ex.Message);
                throw;
            }

            bool install;
            lock (_chainGate)
            {
                // 装载期间 Unload/Dispose 会递增代际：新链已过时，绝不安装。
                install = !_disposed && cancelVersion == _loadCancelVersion;
                if (install) _current = chain;
            }

            if (!install)
            {
                chain.Dispose();
                return;
            }

            // 先归零 Position，再广播新 Duration —— 否则切换到时长更短的曲目时，
            // VM 的旧 Position（如上一首播完的 4:05）会和新 Duration（3:20）短暂并存，
            // 进度条出现"4:05 / 3:20"的越界显示，直到下一帧位置轮询才纠正。
            RaiseOnUIThread(PositionChanged, TimeSpan.Zero);
            RaiseOnUIThread(DurationChanged, chain.Track.Duration);
            RaiseOnUIThread(TrackChanged, chain.Track);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void Play()
    {
        long session;
        lock (_chainGate)
        {
            var chain = _current;
            if (chain == null) return; // 无链（含装载中）：按设计立即 no-op
            chain.StopRequested = false; // 新的播放会话开始：此后的曲尾停止才是"自然播完"
            chain.Output.Play();
            session = ++_playSession;
            SetState(PlayState.Playing);
        }
        _ = PollPositionAsync(session); // fire-and-forget：会话代际/无链/非 Playing 时自然退出
    }

    public void Pause()
    {
        lock (_chainGate)
        {
            var chain = _current;
            if (chain == null) return;
            chain.Output.Pause();
            SetState(PlayState.Paused);
        }
    }

    public void Stop()
    {
        lock (_chainGate)
        {
            var chain = _current;
            if (chain == null) return;
            chain.StopRequested = true; // 必须在调用输出类的停止之前置位：回调可能在其内部同步发生
            chain.Output.Stop();
            // 同时将播放头归零，下次 Play 从头开始
            try { chain.Source.CurrentTime = TimeSpan.Zero; } catch (ObjectDisposedException) { }
            SetState(PlayState.Stopped);
        }
    }

    public void Seek(TimeSpan position)
    {
        TimeSpan clamped;
        lock (_chainGate)
        {
            var chain = _current;
            if (chain == null) return;
            chain.Source.CurrentTime = position;
            // Clamp 到 [0, TotalTime]：解码器对 seek 到尾部允许越界几十毫秒。
            clamped = ClampToDuration(chain.Source.CurrentTime, chain.Track.Duration);
        }

        // 主动广播一次新位置：暂停态下轮询已退出，否则 VM.Position 不刷新，
        // 进度条会停留在旧位置直到用户按 Play 才被轮询拽回（用户视角看起来像"没跳转"）。
        RaiseOnUIThread(PositionChanged, clamped);
    }

    /// <summary>
    /// 卸载当前曲：在闸门内摘下链并递增装载取消代际（装载中的新链随后会被丢弃），
    /// 锁外释放底层 reader/wavePlayer，并广播 TrackChanged(null) / DurationChanged(Zero)
    /// 让 VM 清屏（标题/封面/时长归零）。下次 Play() 会因无链直接 no-op。
    /// 装载进行中调用本方法会立即返回（不等待装载完成），并阻止该次装载安装新链。
    /// </summary>
    public void Unload()
    {
        PlaybackChain? old;
        lock (_chainGate)
        {
            old = _current;
            _current = null;
            _loadCancelVersion++;
            if (old != null) old.StopRequested = true;
            SetState(PlayState.Stopped);
        }

        old?.Dispose();
        RaiseOnUIThread(TrackChanged, (Track?)null);
        RaiseOnUIThread(DurationChanged, TimeSpan.Zero);
        RaiseOnUIThread(PositionChanged, TimeSpan.Zero);
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
    /// 区分逻辑：
    ///   - 回调捕获链，若链已不是当前链（已切歌/卸载/释放）→ 直接忽略；
    ///   - 有异常 → 上报 PlaybackError + Stopped 状态；
    ///   - 链标记了 StopRequested（Stop/Unload/Dispose/切歌发起）→ 仅置 Stopped；
    ///   - 无异常 + 播放位置接近 TotalTime (200ms 容差) → 自然播完 → 触发 TrackEnded。
    ///     注：归零发生在输出类 Stop() 之后，回调时播放头可能仍停在曲尾，故须由 StopRequested 区分意图。
    ///
    /// 本方法由播放线程回调，**不能**取 _chainGate —— 持锁方可能正阻塞在输出类的
    /// Stop()/Dispose() 上等待播放线程退出（后者文档明确 blocking），取锁会立即死锁。
    /// 因此这里只用 Volatile.Read 判定代际，并对读取源做防御性 try/catch（源可能正被并发释放）。
    /// </summary>
    private void OnPlaybackStopped(PlaybackChain chain, StoppedEventArgs e)
    {
        if (!ReferenceEquals(Volatile.Read(ref _current), chain)) return;

        if (e.Exception != null)
        {
            RaiseOnUIThread(PlaybackError, e.Exception.Message);
            SetState(PlayState.Stopped);
            return;
        }

        // 我们主动发起的停止（Stop/Unload/Dispose/切歌）也会走到这里，且可能在播放头接近曲尾时发生；
        // 停止意图由发起方显式标记（见 PlaybackChain.StopRequested）——少了这一步，用户在曲尾
        // 200ms 内按停止会被误判为"自然播完"，停止之后又自动推进下一首。
        if (chain.StopRequested)
        {
            SetState(PlayState.Stopped);
            return;
        }

        // 自然播完判定：播放头距 TotalTime 不超过容差，且时长大于 0（避免空 reader 误判）。
        bool naturalEnd = false;
        try
        {
            naturalEnd = chain.Track.Duration > TimeSpan.Zero
                && (chain.Track.Duration - chain.Source.CurrentTime) <= NaturalEndTolerance;
        }
        catch
        {
            // 源已被并发释放 → 按"非自然结束"处理
        }

        if (naturalEnd)
        {
            RaiseOnUIThread(TrackEnded);
            // 状态仍设为 Stopped；VM 的 TrackEnded handler 决定是否随即 Play 下一首
        }

        SetState(PlayState.Stopped);
    }

    /// <summary>
    /// 后台轮询循环（Task 10 临时版）：每 ~33ms 上报一次 Position。
    /// Task 11 会把 Task.Delay 换成 PeriodicTimer 并删除 DateTime 节流；本版保留节流以维持现状。
    /// 退出条件：播放会话代际变化（新 Play）、链已摘除（切歌/Unload/Dispose）或状态不再 Playing。
    /// </summary>
    private async Task PollPositionAsync(long session)
    {
        while (true)
        {
            await Task.Delay(33).ConfigureAwait(false); // ~30Hz

            var chain = Volatile.Read(ref _current);
            if (session != Volatile.Read(ref _playSession) || chain == null || _state != PlayState.Playing)
                return;

            var now = DateTime.UtcNow;
            if (now - _lastPositionEvent < PositionThrottle)
                continue;
            _lastPositionEvent = now;

            TimeSpan position;
            try
            {
                position = ClampToDuration(chain.Source.CurrentTime, chain.Track.Duration);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            RaiseOnUIThread(PositionChanged, position);
        }
    }

    /// <summary>把位置截断到 [0, TotalTime]，防止解码器尾部浮点越界（如 245.012s 上报到时长 245.000s 的曲）。</summary>
    private static TimeSpan ClampToDuration(TimeSpan pos, TimeSpan total)
    {
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

    /// <summary>
    /// 在线程池上构建一条完整播放链（Source → Equalizer → Aggregator → Volume → Output）。
    /// 任何一步失败都必须释放已创建的资源后重抛：调用方依赖"失败不泄漏"。
    /// </summary>
    private PlaybackChain BuildChain(
        Track track, float volume, EqualizerConfig eqConfig, SpectrumConfig spectrumConfig)
    {
        IAudioSource? source = null;
        SampleAggregator? aggregator = null;
        IWavePlayer? output = null;
        PlaybackChain? chain = null;

        try
        {
            source = _sourceFactory.Create(track.FilePath);

            // Phase 13/14: ToSample → EqualizerSampleProvider → SampleAggregator
            // EQ 置于 SampleAggregator 之前 → 频谱可视化反映 EQ 处理后的信号
            var equalizer = new EqualizerSampleProvider(source.ToSampleProvider(), eqConfig);
            aggregator = new SampleAggregator(equalizer, spectrumConfig);
            var volumeProvider = new VolumeSampleProvider(aggregator) { Volume = volume };
            // NAudio 3 的输出入口由工厂决定（默认 StubAudioOutputFactory →
            // WasapiPlayerBuilder → WasapiPlayer）；测试可注入假输出。
            output = _outputFactory.CreateOutput();
            output.Init(volumeProvider);

            // 用解码器实测值回填 Track；调用方传入的 Duration 通常为 Zero
            var actualTrack = track with
            {
                Duration = source.TotalTime,
                SampleRate = source.WaveFormat.SampleRate
            };

            chain = new PlaybackChain
            {
                Source = source,
                Equalizer = equalizer,
                Aggregator = aggregator,
                Volume = volumeProvider,
                Output = output,
                Track = actualTrack
            };

            // 频谱回调在音频线程触发：只有当前链才向 UI 派发（旧链的残余帧直接丢弃）
            chain.SpectrumHandler = data =>
            {
                if (ReferenceEquals(Volatile.Read(ref _current), chain))
                    RaiseOnUIThread(SpectrumDataAvailable, data);
            };
            chain.StoppedHandler = (_, e) => OnPlaybackStopped(chain, e);

            aggregator.SpectrumDataReady += chain.SpectrumHandler;
            output.PlaybackStopped += chain.StoppedHandler;
            return chain;
        }
        catch
        {
            if (chain != null)
            {
                chain.Dispose();
            }
            else
            {
                try { output?.Dispose(); } catch { }
                try { aggregator?.Dispose(); } catch { }
                try { source?.Dispose(); } catch { }
            }
            throw;
        }
    }

    /// <summary>
    /// 一条播放链的完整快照。对象一旦安装即为只读（配置通过各自的 provider 实时下发），
    /// 拆除时按"解订阅 → 停止输出 → 释放聚合器（join FFT 线程）→ 释放解码器"的顺序执行。
    /// 输出类的 Stop/Dispose 可能阻塞等待播放线程退出，因此绝不能在 _chainGate 内调用 Dispose。
    /// </summary>
    private sealed class PlaybackChain : IDisposable
    {
        private int _disposed;

        public required IAudioSource Source { get; init; }
        public required EqualizerSampleProvider Equalizer { get; init; }
        public required SampleAggregator Aggregator { get; init; }
        public required VolumeSampleProvider Volume { get; init; }
        public required IWavePlayer Output { get; init; }
        public required Track Track { get; init; }
        public volatile bool StopRequested;
        public Action<float[]>? SpectrumHandler;
        public EventHandler<StoppedEventArgs>? StoppedHandler;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // 先解订阅：输出类 Stop() 会让仍订阅的回调看到"流结束"，
            // 拆链意图必须已由持有者置位（StopRequested）且回调侧按当前链判定。
            if (SpectrumHandler != null) Aggregator.SpectrumDataReady -= SpectrumHandler;
            if (StoppedHandler != null) Output.PlaybackStopped -= StoppedHandler;

            try { Output.Stop(); } catch { }
            try { Output.Dispose(); } catch { }
            try { Aggregator.Dispose(); } catch { }
            try { Source.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        PlaybackChain? old;
        lock (_chainGate)
        {
            if (_disposed) return;
            _disposed = true;
            _loadCancelVersion++;
            old = _current;
            _current = null;
            if (old != null) old.StopRequested = true;
        }
        old?.Dispose();
    }
}
