using NAudio.Dsp;
using NAudio.Wave;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 10 段图形均衡器中间件（Phase 14）。实现 ISampleProvider，透明插入播放链
/// （位于 ToSampleProvider 与 SampleAggregator 之间 → 频谱反映 EQ 后信号）。
///
/// 每声道独立一组 BiQuadFilter（峰值 EQ），避免立体声共享滤波状态导致串扰。
/// 0 dB 频段被视为恒等、排除出 active 级联（不参与热路径）；在 NAudio 3.3.0 的
/// <c>SetPeakingEq</c> 重置状态语义下，这与完整 10 段级联的输出等价（浮点容差内）。
/// 当启用但所有频段均为 0 dB 且 preamp 为 0 dB 时，<see cref="Read"/> 直接返回源数据（零处理成本）。
///
/// 线程模型（Phase 30）：UI 线程 <see cref="Update"/> 只发布不可变 <see cref="EqualizerConfig"/>
/// 引用（Volatile.Write，不触碰滤波器）；音频线程在 <see cref="Read"/> 开头
/// Interlocked.Exchange 取走待应用配置并就地应用，因而不在音频线程上加锁。
/// 配置在 buffer 边界生效：<c>SetPeakingEq</c> 在块边界更新系数。NAudio 3.3.0 在更新系数时
/// 会重置滤波器状态（x1/x2/y1/y2），因此不声称跨边界的状态连续性；保留状态的滤波器或
/// 交叉淡化属于后续候选改进。
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
    // 是否至少有一个声道存在非 0 dB 有效段。音频线程独占：仅构造期/Read 开头的 ApplyConfig 写。
    private bool _hasActiveFilters;

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

        // 启用但无 active 滤波器（全 0 dB）且 preamp 为 0 dB：恒等快速路径，零处理成本。
        // preamp ≠ 1 时仍需 Process 应用增益；_hasActiveFilters 只在本方法开头被 ApplyConfig 写。
        if (!_hasActiveFilters && _preampGain == 1f) return read;

        Process(buffer, read);
        return read;
    }

    /// <summary>
    /// 运行时更新配置：只发布不可变配置引用（无锁、不触碰滤波器/状态）。
    /// 音频线程在下一个 Read 开头取走并就地应用；<c>SetPeakingEq</c> 在块边界换系数，
    /// 且 NAudio 3.3.0 此时会重置滤波器状态（不保证跨块状态连续）。
    /// 同一 buffer 间隔内的多次 Update 会合并，最后一次生效。
    /// </summary>
    public void Update(EqualizerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Volatile.Write(ref _pendingConfig, config); // release 语义：配置内容先于引用对音频线程可见
    }

    /// <summary>
    /// 在构造或音频线程 Read 开头应用配置：就地重算系数（<c>SetPeakingEq</c> 在块边界换系数；
    /// NAudio 3.3.0 会重置滤波器状态，不保证跨块连续），并重建每声道有效滤波器数组：
    /// 中心频率 ≥ 奈奎斯特的频段旁路（PeakingEQ 此时不稳定）；0 dB 频段视为恒等、排除出
    /// active 级联（NAudio 3.3.0 更新系数会重置状态，故与完整 10 段级联的输出在浮点容差内等价）。
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

        _hasActiveFilters = false;
        for (int c = 0; c < _channels; c++)
        {
            int count = 0;
            for (int b = 0; b < EqualizerPresets.BandCount; b++)
            {
                float gainDb = (float)config.BandGainsDb[b];
                if (gainDb != 0f && _filters[c][b] is { } filter)
                    _activeFilters[c][count++] = filter;
            }
            _activeCounts[c] = count;
            if (count > 0) _hasActiveFilters = true;
        }
    }

    /// <summary>
    /// 只读已应用状态（音频线程独占写；Process 在 Read 内同步调用）。
    /// 按声道数分派到专用循环：热路径无逐样本取模、无逐段空判断。
    /// </summary>
    private void Process(Span<float> buffer, int read)
    {
        if (_channels == 1)
        {
            ProcessMono(buffer, read, _activeFilters[0], _activeCounts[0]);
        }
        else if (_channels == 2)
        {
            ProcessStereo(buffer, read);
        }
        else
        {
            ProcessMultiChannel(buffer, read);
        }
    }

    private void ProcessMono(Span<float> buffer, int read, BiQuadFilter[] filters, int count)
    {
        for (int i = 0; i < read; i++)
        {
            float sample = buffer[i] * _preampGain;
            for (int b = 0; b < count; b++) sample = filters[b].Transform(sample);
            buffer[i] = sample;
        }
    }

    private void ProcessStereo(Span<float> buffer, int read)
    {
        var left = _activeFilters[0];
        var right = _activeFilters[1];
        int leftCount = _activeCounts[0];
        int rightCount = _activeCounts[1];
        int frames = read / 2;

        for (int frame = 0; frame < frames; frame++)
        {
            int i = frame * 2;
            float l = buffer[i] * _preampGain;
            float r = buffer[i + 1] * _preampGain;
            for (int b = 0; b < leftCount; b++) l = left[b].Transform(l);
            for (int b = 0; b < rightCount; b++) r = right[b].Transform(r);
            buffer[i] = l;
            buffer[i + 1] = r;
        }
    }

    private void ProcessMultiChannel(Span<float> buffer, int read)
    {
        int frames = read / _channels;
        for (int frame = 0; frame < frames; frame++)
        {
            int i = frame * _channels;
            for (int c = 0; c < _channels; c++)
            {
                float sample = buffer[i + c] * _preampGain;
                int count = _activeCounts[c];
                var filters = _activeFilters[c];
                for (int b = 0; b < count; b++) sample = filters[b].Transform(sample);
                buffer[i + c] = sample;
            }
        }
    }
}
