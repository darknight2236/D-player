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
/// 线程模型：Read 在 NAudio 音频线程；Update 在 UI 线程。二者用 buffer 粒度 lock 互斥，
/// 防止撕裂系数。Update 用 SetPeakingEq 就地重算（保留 x1/x2/y1/y2 状态，避免拖动爆音）。
/// </summary>
public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly BiQuadFilter?[][] _filters; // [channel][band]，null = 该频段旁路
    private readonly object _lock = new();

    private float _preampGain = 1f;
    private bool _enabled;

    public EqualizerSampleProvider(ISampleProvider source, EqualizerConfig config)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sampleRate = source.WaveFormat.SampleRate;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _filters = new BiQuadFilter?[_channels][];
        for (int c = 0; c < _channels; c++)
            _filters[c] = new BiQuadFilter?[EqualizerPresets.BandCount];
        Update(config);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (!_enabled || read <= 0) return read; // 透明旁路（零处理成本）

        lock (_lock)
        {
            for (int i = 0; i < read; i++)
            {
                int c = _channels > 1 ? i % _channels : 0; // 交织样本 → 声道索引
                float s = buffer[offset + i] * _preampGain;
                var bank = _filters[c];
                for (int b = 0; b < bank.Length; b++)
                {
                    var f = bank[b];
                    if (f != null) s = f.Transform(s);
                }
                buffer[offset + i] = s;
            }
        }
        return read;
    }

    /// <summary>
    /// 运行时更新配置：就地重算系数（保留滤波状态，避免爆音）。
    /// 中心频率 ≥ 奈奎斯特的频段旁路（PeakingEQ 在 ≥Nyquist 时不稳定）。
    /// </summary>
    public void Update(EqualizerConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        lock (_lock)
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
                    if (bypass) { _filters[c][b] = null; continue; }
                    var existing = _filters[c][b];
                    if (existing == null)
                        _filters[c][b] = BiQuadFilter.PeakingEQ(_sampleRate, (float)centre, EqualizerPresets.Q, gainDb);
                    else
                        existing.SetPeakingEq(_sampleRate, (float)centre, EqualizerPresets.Q, gainDb);
                }
            }
        }
    }
}
