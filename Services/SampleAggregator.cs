using NAudio.Dsp;
using NAudio.Wave;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 音频频谱分析器 —— 插入 NAudio 播放链中截取 PCM 数据，执行 FFT 计算频谱。
/// 实现 ISampleProvider 接口，作为透明中间件不修改音频流。
/// </summary>
public sealed class SampleAggregator : ISampleProvider
{
    private readonly ISampleProvider _source;
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
        _source = source;
        _fftSize = config.FftSize;
        _barCount = config.BarCount;
        _fftBuffer = new Complex[_fftSize];
        _spectrumData = new float[_barCount];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        // 1. 从源读取 PCM 数据
        int read = _source.Read(buffer, offset, count);

        if (Enabled && read > 0)
        {
            // 2. 填充 FFT 缓冲区
            for (int i = 0; i < read; i++)
            {
                // 应用汉宁窗减少频谱泄漏
                float sample = buffer[offset + i];
                float windowFactor = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * _bufferPosition / (_fftSize - 1))));
                _fftBuffer[_bufferPosition].X = sample * windowFactor;
                _fftBuffer[_bufferPosition].Y = 0;
                _bufferPosition++;

                if (_bufferPosition >= _fftSize)
                {
                    // 3. 执行 FFT
                    FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _fftBuffer);

                    // 4. 提取幅度并映射到频谱柱
                    ExtractSpectrumData();

                    // 5. 触发事件（复制数组，避免共享可变缓冲区被订阅者篡改）
                    SpectrumDataReady?.Invoke(_spectrumData.ToArray());

                    _bufferPosition = 0;
                }
            }
        }

        // 6. 原始 PCM 数据原样传递
        return read;
    }

    /// <summary>
    /// 从 FFT 结果提取幅度并映射到频谱柱（对数频率分组）
    /// 人耳对频率的感知是对数的（低频区分更细），因此使用对数分组。
    /// </summary>
    private void ExtractSpectrumData()
    {
        int binCount = _fftSize / 2;
        float sampleRate = _source.WaveFormat.SampleRate;

        // 频率范围：20Hz ~ 20kHz（人耳可听范围）
        const float minFreq = 20f;
        const float maxFreq = 20000f;
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

            // 增益系数（可根据灵敏度调整）
            float gain = 20f;

            // 对数幅度映射（dB Scale）
            // 将幅度转换为 0~1 范围，使用对数缩放让人耳感知更均匀
            float db = rms > 0 ? 20 * MathF.Log10(rms * gain) : -100;
            // 映射范围：-80dB ~ 0dB → 0.0 ~ 1.0（更宽的范围让两端也能动）
            float normalized = Math.Clamp((db + 80) / 80, 0, 1);

            // 应用 gamma 曲线增强对比度（让中间更明显，两端也有响应）
            _spectrumData[bar] = MathF.Pow(normalized, 0.7f);
        }
    }
}
