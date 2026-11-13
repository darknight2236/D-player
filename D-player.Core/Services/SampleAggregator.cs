using NAudio.Dsp;
using NAudio.Wave;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 音频频谱分析器 —— 插入 NAudio 播放链中截取 PCM 数据，执行 FFT 计算频谱。
/// 音频线程只做混音与写环形缓冲（producer）；FFT 与事件发布在后台分析线程（consumer）。
/// 实现 ISampleProvider 接口，作为透明中间件不修改音频流。
/// </summary>
public sealed class SampleAggregator : ISampleProvider, IDisposable
{
    private readonly ISampleProvider _source;
    private readonly int _fftSize;
    private readonly int _barCount;
    private readonly int _channels;
    private readonly int _hop;
    private readonly float[] _window;
    private readonly int[] _barStartBins;
    private readonly int[] _barEndBins;
    private readonly float[] _ring;
    private readonly int _ringMask;
    private readonly float[] _frame;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _spectrumData;
    private readonly AutoResetEvent _frameSignal = new(false);
    private readonly Thread _analysisThread;
    private long _writeIndex;
    private long _lastSignaledEnd;
    private long _lastFrameEnd;
    private long _enabledAtWrite;
    private volatile bool _enabled = true;
    private volatile bool _stop;
    private int _disposed;

    /// <summary>频谱数据可用事件（在 FFT 分析线程触发，订阅者需自行处理线程封送）</summary>
    public event Action<float[]>? SpectrumDataReady;

    /// <summary>是否启用频谱分析</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            // 重新启用时对齐到当前写位置：不补算禁用期间的历史样本。
            // consumer 在取帧前检查该门槛，保证第一帧只来自重新启用后的样本。
            if (value)
                Volatile.Write(ref _enabledAtWrite, Volatile.Read(ref _writeIndex));
        }
    }

    public SampleAggregator(ISampleProvider source, SpectrumConfig config)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _fftSize = config.FftSize;
        _barCount = config.BarCount;
        if (_fftSize < 256 || _fftSize > 65536 || (_fftSize & (_fftSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(config), "FftSize must be a power of two in [256, 65536].");
        if (_barCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "BarCount must be positive.");

        _channels = Math.Max(1, source.WaveFormat.Channels);
        _hop = _fftSize / 2;
        _ring = new float[_fftSize * 4];
        _ringMask = _ring.Length - 1;
        _frame = new float[_fftSize];
        _fftBuffer = new Complex[_fftSize];
        _spectrumData = new float[_barCount];

        // 预计算汉宁窗表（避免每帧重复计算 Cos）
        _window = new float[_fftSize];
        for (int j = 0; j < _fftSize; j++)
            _window[j] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * j / (_fftSize - 1))));

        // 预计算各频谱柱的 bin 区间（对数频率分布 20Hz ~ 16kHz）：
        // 人耳对频率的感知是对数的（低频区分更细），因此使用对数分组。
        _barStartBins = new int[_barCount];
        _barEndBins = new int[_barCount];
        float sampleRate = _source.WaveFormat.SampleRate;
        const float minFreq = 20f;
        const float maxFreq = 16000f;
        float logMin = MathF.Log10(minFreq);
        float logMax = MathF.Log10(maxFreq);
        int binCount = _fftSize / 2;
        for (int bar = 0; bar < _barCount; bar++)
        {
            float f0 = MathF.Pow(10, logMin + (logMax - logMin) * bar / _barCount);
            float f1 = MathF.Pow(10, logMin + (logMax - logMin) * (bar + 1) / _barCount);
            // 频率转 bin 索引：bin = freq / sampleRate * fftSize
            _barStartBins[bar] = Math.Max(1, (int)(f0 / sampleRate * _fftSize));
            _barEndBins[bar] = Math.Min(binCount, (int)(f1 / sampleRate * _fftSize) + 1);
        }

        _analysisThread = new Thread(AnalysisLoop)
        {
            IsBackground = true,
            Name = "DPlayer.SpectrumFft",
            Priority = ThreadPriority.BelowNormal
        };
        _analysisThread.Start();
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        // Dispose 后音频线程可能仍持旧引用调用 Read：直接返回 0，不触碰已释放信号量。
        if (_stop) return 0;

        // 1. 从源读取 PCM 数据（NAudio 3 起 ISampleProvider 只有 Span 重载）
        int read = _source.Read(buffer);
        if (!_enabled || read <= 0) return read;

        int frames = read / _channels;
        long write = _writeIndex;

        // 2. 混音为单声道并写入 SPSC 环形缓冲（音频线程不做 FFT、不分配、不取锁、不等待）
        for (int frame = 0; frame < frames; frame++)
        {
            int i = frame * _channels;
            float sample = _channels switch
            {
                1 => buffer[i],
                2 => (buffer[i] + buffer[i + 1]) * 0.5f,
                _ => Mix(buffer.Slice(i, _channels), _channels)
            };
            _ring[(int)(write & _ringMask)] = sample;
            write++;
        }

        // release：环形缓冲写入必须先于写索引对 consumer 可见
        Volatile.Write(ref _writeIndex, write);

        // 每跨过一个 hop 唤醒一次 consumer（AutoResetEvent 不累积，落后时自然丢帧）
        if (write - _lastSignaledEnd >= _hop)
        {
            _lastSignaledEnd = write;
            try { _frameSignal.Set(); } catch (ObjectDisposedException) { }
        }
        return read;
    }

    private static float Mix(ReadOnlySpan<float> samples, int channels)
    {
        float sum = 0;
        for (int c = 0; c < channels; c++) sum += samples[c];
        return sum / channels;
    }

    private void AnalysisLoop()
    {
        while (!_stop)
        {
            // 100ms 超时仅用于周期性检查停止标志
            _frameSignal.WaitOne(100);
            if (_stop) break;
            if (!_enabled) continue;

            long write = Volatile.Read(ref _writeIndex);
            long enabledAt = Volatile.Read(ref _enabledAtWrite);

            // hop 前进 FftSize/2：正常追赶时逐帧处理，保持 50% 重叠语义；
            // 重新启用后的第一帧不得早于 _enabledAtWrite。
            long frameEnd = Math.Max(_lastFrameEnd + _hop, _fftSize);
            if (frameEnd < enabledAt + _fftSize) frameEnd = enabledAt + _fftSize;
            if (frameEnd > write) continue;

            // 消费者落后超过环形缓冲可容纳的历史：丢帧不排队，直接跳到最新窗口。
            if (write - frameEnd > _ring.Length - _fftSize)
                frameEnd = write;

            while (frameEnd <= write && !_stop)
            {
                _lastFrameEnd = frameEnd;

                // 帧 = 环形缓冲中截至 frameEnd 的最后 FftSize 个真实时域样本
                long start = frameEnd - _fftSize;
                for (int j = 0; j < _fftSize; j++)
                    _frame[j] = _ring[(int)((start + j) & _ringMask)];

                // 加窗只作用于 FFT 工作缓冲，不污染原始样本
                for (int j = 0; j < _fftSize; j++)
                {
                    _fftBuffer[j].X = _frame[j] * _window[j];
                    _fftBuffer[j].Y = 0;
                }

                FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _fftBuffer);
                ExtractSpectrumData();

                try
                {
                    // 每帧新建快照：订阅者不能篡改共享缓冲，也不影响下一帧
                    SpectrumDataReady?.Invoke(_spectrumData.ToArray());
                }
                catch
                {
                    // 单帧订阅者异常不得杀死 FFT 线程。
                }

                frameEnd += _hop;
            }
        }
    }

    /// <summary>
    /// 从 FFT 结果提取幅度并映射到频谱柱（bin 区间已在构造函数中预计算）
    /// </summary>
    private void ExtractSpectrumData()
    {
        for (int bar = 0; bar < _barCount; bar++)
        {
            int startBin = _barStartBins[bar];
            int endBin = _barEndBins[bar];

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
            const float gain = 8f;

            // 对数幅度映射（dB Scale）
            float db = rms > 0 ? 20 * MathF.Log10(rms * gain) : -100;
            // 映射范围：-60dB ~ 0dB → 0.0 ~ 1.0
            float normalized = Math.Clamp((db + 60) / 60, 0, 1);

            // 应用 gamma 曲线增强对比度
            _spectrumData[bar] = MathF.Pow(normalized, 0.8f);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop = true;
        _frameSignal.Set();
        _analysisThread.Join(TimeSpan.FromSeconds(2));
        _frameSignal.Dispose();
    }
}
