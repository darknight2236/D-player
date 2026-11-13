using NAudio.Dsp;
using NAudio.Wave;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 音频频谱分析器 —— 插入 NAudio 播放链中截取 PCM 数据，执行 FFT 计算频谱。
/// 实现 ISampleProvider 接口，作为透明中间件不修改音频流。
/// </summary>
public sealed class SampleAggregator : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _rawBuffer;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _spectrumData;
    private readonly int _fftSize;
    private readonly int _barCount;
    private int _bufferPosition;

    /// <summary>频谱数据可用事件（在音频线程触发，订阅者需自行处理线程封送）</summary>
    public event Action<float[]>? SpectrumDataReady;

    /// <summary>是否启用频谱分析</summary>
    public bool Enabled { get; set; } = true;

    public SampleAggregator(ISampleProvider source, SpectrumConfig config)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _fftSize = config.FftSize;
        _barCount = config.BarCount;
        if (_fftSize < 256 || _fftSize > 65536 || (_fftSize & (_fftSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(config), "FftSize must be a power of two in [256, 65536].");
        if (_barCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "BarCount must be positive.");
        _rawBuffer = new float[_fftSize];
        _fftBuffer = new Complex[_fftSize];
        _spectrumData = new float[_barCount];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        // 1. 从源读取 PCM 数据（NAudio 3 起 ISampleProvider 只有 Span 重载）
        int read = _source.Read(buffer);
        if (!Enabled || read <= 0) return read;

        int channels = Math.Max(1, _source.WaveFormat.Channels);
        int frames = read / channels;

        // 2. 填充原始样本环形历史（FFT 工作缓冲在 ProcessFrame 内单独使用）
        for (int frame = 0; frame < frames; frame++)
        {
            int i = frame * channels;
            float sample = channels switch
            {
                1 => buffer[i],
                2 => (buffer[i] + buffer[i + 1]) * 0.5f,
                _ => Mix(buffer.Slice(i, channels), channels)
            };

            _rawBuffer[_bufferPosition++] = sample;
            if (_bufferPosition >= _fftSize)
            {
                ProcessFrame();

                // 50% 重叠：直接保留原始样本的后半段，而非上一次 FFT 输出
                Array.Copy(_rawBuffer, _fftSize / 2, _rawBuffer, 0, _fftSize / 2);
                _bufferPosition = _fftSize / 2;
            }
        }

        // 3. 原始 PCM 数据原样传递
        return read;
    }

    private static float Mix(ReadOnlySpan<float> samples, int channels)
    {
        float sum = 0;
        for (int c = 0; c < channels; c++) sum += samples[c];
        return sum / channels;
    }

    private void ProcessFrame()
    {
        // 1. 将原始样本复制到 FFT 工作缓冲（不污染重叠历史）
        for (int j = 0; j < _fftSize; j++)
        {
            _fftBuffer[j].X = _rawBuffer[j];
            _fftBuffer[j].Y = 0;
        }

        // 2. 应用汉宁窗（在 FFT 前统一加窗，避免重叠时窗口错位）
        for (int j = 0; j < _fftSize; j++)
            _fftBuffer[j].X *= (float)(0.5 * (1 - Math.Cos(2 * Math.PI * j / (_fftSize - 1))));

        // 3. 执行 FFT
        FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _fftBuffer);

        // 4. 提取幅度并映射到频谱柱
        ExtractSpectrumData();

        // 5. 触发事件（复制数组，避免共享可变缓冲区被订阅者篡改）
        SpectrumDataReady?.Invoke(_spectrumData.ToArray());
    }

    /// <summary>
    /// 从 FFT 结果提取幅度并映射到频谱柱（对数频率分组）
    /// 人耳对频率的感知是对数的（低频区分更细），因此使用对数分组。
    /// </summary>
    private void ExtractSpectrumData()
    {
        int binCount = _fftSize / 2;
        float sampleRate = _source.WaveFormat.SampleRate;

        // 频率范围：20Hz ~ 16kHz（人耳可听范围，跳过超高频）
        const float minFreq = 20f;
        const float maxFreq = 16000f;
        float logMin = MathF.Log10(minFreq);
        float logMax = MathF.Log10(maxFreq);

        for (int bar = 0; bar < _barCount; bar++)
        {
            // 对数分布的频率范围
            float freqStart = MathF.Pow(10, logMin + (logMax - logMin) * bar / _barCount);
            float freqEnd = MathF.Pow(10, logMin + (logMax - logMin) * (bar + 1) / _barCount);

            // 频率转 bin 索引：bin = freq / sampleRate * fftSize
            int startBin = Math.Max(1, (int)(freqStart / sampleRate * _fftSize));
            int endBin = Math.Min(binCount, (int)(freqEnd / sampleRate * _fftSize) + 1);

            // 取该范围内的平均幅度（使用 RMS 均方根，更稳定）
            float sumSquared = 0;
            int count = 0;
            for (int i = startBin; i < endBin; i++)
            {
                float magnitude = (float)Math.Sqrt(
                    _fftBuffer[i].X * _fftBuffer[i].X +
                    _fftBuffer[i].Y * _fftBuffer[i].Y);
                sumSquared += magnitude * magnitude;
                count++;
            }

            float rms = count > 0 ? MathF.Sqrt(sumSquared / count) : 0;

            // 增益系数（适中，避免饱和）
            float gain = 8f;

            // 对数幅度映射（dB Scale）
            float db = rms > 0 ? 20 * MathF.Log10(rms * gain) : -100;
            // 映射范围：-60dB ~ 0dB → 0.0 ~ 1.0
            float normalized = Math.Clamp((db + 60) / 60, 0, 1);

            // 应用 gamma 曲线增强对比度
            _spectrumData[bar] = MathF.Pow(normalized, 0.8f);
        }
    }
}
