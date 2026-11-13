using NAudio.Dsp;
using NAudio.Wave;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 10 段图形均衡器中间件（Phase 14）。实现 ISampleProvider，透明插入播放链
/// （位于 ToSampleProvider 与 SampleAggregator 之间 → 频谱反映 EQ 后信号）。
///
/// 每声道独立一组 BiQuadFilter（峰值 EQ），避免立体声共享滤波状态导致串扰。
///
/// 线程模型（Phase 30）：UI 线程 <see cref="Update"/> 只发布不可变 <see cref="EqualizerConfig"/>
/// 引用（Volatile.Write，不触碰滤波器）；音频线程在 <see cref="Read"/> 开头
/// Interlocked.Exchange 取走待应用配置并就地应用，因而不在音频线程上加锁。
/// 配置在 buffer 边界生效；SetPeakingEq 就地重算系数，保留 x1/x2/y1/y2 状态（拖动不爆音）。
/// </summary>
public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly BiQuadFilter?[][] _filters; // [channel][band]，null = 该频段旁路
    private readonly BiQuadFilter[][] _activeFilters; // [channel][有效段]，Process 热路径无空判断
    private readonly int[] _activeCounts; // [channel] 有效段数

    // 待应用的配置引用；仅 UI 线程写、音频线程在 Read 开头取走（Interlocked.Exchange）。
    private EqualizerConfig? _pendingConfig;
    private float _preampGain = 1f;
    private bool _enabled;

    public EqualizerSampleProvider(ISampleProvider source, EqualizerConfig config)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sampleRate = source.WaveFormat.SampleRate;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _filters = new BiQuadFilter?[_channels][];
        _activeFilters = new BiQuadFilter[_channels][];
        _activeCounts = new int[_channels];
        for (int c = 0; c < _channels; c++)
        {
            _filters[c] = new BiQuadFilter?[EqualizerPresets.BandCount];
            _activeFilters[c] = new BiQuadFilter[EqualizerPresets.BandCount];
        }
        ArgumentNullException.ThrowIfNull(config); // 与旧实现一致：构造期 null 配置抛 ArgumentNullException
        ApplyConfig(config);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        // 音频线程在 buffer 边界取走 UI 发布的配置；无锁、无等待。
        var pending = Interlocked.Exchange(ref _pendingConfig, null);
        if (pending != null) ApplyConfig(pending);

        int read = _source.Read(buffer); // NAudio 3 起 ISampleProvider 只有 Span 重载
        if (!_enabled || read <= 0) return read; // 透明旁路（零处理成本）

        Process(buffer, read);
        return read;
    }

    /// <summary>
    /// 运行时更新配置：只发布不可变配置引用（无锁、不触碰滤波器/状态）。
    /// 音频线程在下一个 Read 开头取走并就地应用（保留滤波状态）。
    /// 同一 buffer 间隔内的多次 Update 会合并，最后一次生效。
    /// </summary>
    public void Update(EqualizerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Volatile.Write(ref _pendingConfig, config); // release 语义：配置内容先于引用对音频线程可见
    }

    /// <summary>
    /// 在构造或音频线程 Read 开头应用配置：就地重算系数（保留滤波状态，避免爆音），
    /// 并重建每声道有效滤波器数组（中心频率 ≥ 奈奎斯特的频段旁路，PeakingEQ 此时不稳定）。
    /// </summary>
    private void ApplyConfig(EqualizerConfig config)
    {
        _enabled = config.Enabled;
        _preampGain = config.PreampLinearGain;

        double nyquist = _sampleRate / 2.0;
        for (int b = 0; b < EqualizerPresets.BandCount; b++)
        {
            double centre = EqualizerPresets.CenterFrequencies[b];
            float gainDb = (float)config.BandGainsDb[b];
            bool bypass = centre >= nyquist; // Nyquist 保护

            for (int c = 0; c < _channels; c++)
            {
                if (bypass)
                {
                    _filters[c][b] = null;
                    continue;
                }

                var existing = _filters[c][b];
                if (existing == null)
                    _filters[c][b] = BiQuadFilter.PeakingEQ(_sampleRate, (float)centre, EqualizerPresets.Q, gainDb);
                else
                    existing.SetPeakingEq(_sampleRate, (float)centre, EqualizerPresets.Q, gainDb);
            }
        }

        for (int c = 0; c < _channels; c++)
        {
            int count = 0;
            for (int b = 0; b < EqualizerPresets.BandCount; b++)
            {
                if (_filters[c][b] is { } filter)
                    _activeFilters[c][count++] = filter;
            }
            _activeCounts[c] = count;
        }
    }

    /// <summary>只读已应用状态（音频线程独占写；Process 在 Read 内同步调用）。</summary>
    private void Process(Span<float> buffer, int read)
    {
        for (int i = 0; i < read; i++)
        {
            int c = _channels > 1 ? i % _channels : 0; // 交织样本 → 声道索引
            int count = _activeCounts[c];
            var filters = _activeFilters[c];
            float sample = buffer[i] * _preampGain;
            for (int b = 0; b < count; b++) sample = filters[b].Transform(sample);
            buffer[i] = sample;
        }
    }
}
