using System;
using System.Collections.Generic;
using NAudio.Dsp;
using NAudio.Wave;
using DPlayer.Models;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class SampleAggregatorTests
{
    private const int FftSize = 8192;
    private const int BarCount = 32;
    private const int SampleRate = 44100;

    private sealed class BufferSource : ISampleProvider
    {
        private readonly float[] _data;
        private int _position;

        public BufferSource(float[] data, int channels = 1)
        {
            _data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            int count = Math.Min(buffer.Length, _data.Length - _position);
            if (count <= 0) return 0;
            _data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }

    private static SampleAggregator Create(ISampleProvider source) =>
        new(source, new SpectrumConfig { FftSize = FftSize, BarCount = BarCount });

    private static void ReadAll(SampleAggregator aggregator, int totalFrames)
    {
        var buffer = new float[256];
        int read = 0;
        while (read < totalFrames)
        {
            int n = aggregator.Read(buffer.AsSpan(0, Math.Min(buffer.Length, totalFrames - read)));
            if (n <= 0) break;
            read += n;
        }
    }

    private static float[] ReferenceBars(float[] samples)
    {
        var window = new float[FftSize];
        for (int j = 0; j < FftSize; j++)
            window[j] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * j / (FftSize - 1))));

        var fft = new Complex[FftSize];
        for (int j = 0; j < FftSize; j++)
        {
            fft[j].X = samples[j] * window[j];
            fft[j].Y = 0;
        }
        FastFourierTransform.FFT(true, 13, fft);

        const float minFreq = 20f, maxFreq = 16000f;
        float logMin = MathF.Log10(minFreq);
        float logMax = MathF.Log10(maxFreq);
        int binCount = FftSize / 2;
        var bars = new float[BarCount];
        for (int bar = 0; bar < BarCount; bar++)
        {
            float f0 = MathF.Pow(10, logMin + (logMax - logMin) * bar / BarCount);
            float f1 = MathF.Pow(10, logMin + (logMax - logMin) * (bar + 1) / BarCount);
            int startBin = Math.Max(1, (int)(f0 / SampleRate * FftSize));
            int endBin = Math.Min(binCount, (int)(f1 / SampleRate * FftSize) + 1);
            float sumSquared = 0;
            int count = 0;
            for (int i = startBin; i < endBin; i++)
            {
                float magnitude = (float)Math.Sqrt(fft[i].X * fft[i].X + fft[i].Y * fft[i].Y);
                sumSquared += magnitude * magnitude;
                count++;
            }
            float rms = count > 0 ? MathF.Sqrt(sumSquared / count) : 0;
            float db = rms > 0 ? 20 * MathF.Log10(rms * 8f) : -100;
            float normalized = Math.Clamp((db + 60) / 60, 0, 1);
            bars[bar] = MathF.Pow(normalized, 0.8f);
        }
        return bars;
    }

    [Fact]
    public void Overlap_UsesTimeDomainSamples_NotPreviousFftOutput()
    {
        var firstFrame = new float[FftSize];
        for (int i = 0; i < FftSize; i++) firstFrame[i] = 0.1f;
        var secondChunk = new float[FftSize / 2];
        Array.Fill(secondChunk, 0.5f);

        var data = new float[FftSize + FftSize / 2];
        firstFrame.CopyTo(data, 0);
        secondChunk.CopyTo(data, FftSize);

        var source = new BufferSource(data);
        using var aggregator = Create(source);
        var events = new List<float[]>();
        using var ready = new ManualResetEventSlim();
        aggregator.SpectrumDataReady += bars =>
        {
            lock (events)
            {
                events.Add(bars);
                if (events.Count >= 2) ready.Set();
            }
        };

        ReadAll(aggregator, FftSize + FftSize / 2);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "两帧频谱超时");

        float[] actual;
        lock (events) actual = events[1];
        var expected = ReferenceBars(data.AsSpan(FftSize / 2, FftSize).ToArray());
        for (int i = 0; i < BarCount; i++)
            Assert.Equal(expected[i], actual[i], 5);
    }

    [Fact]
    public void KnownSine_PeaksInExpectedBar()
    {
        var data = new float[FftSize];
        for (int i = 0; i < FftSize; i++)
            data[i] = 0.3f * MathF.Sin(2 * MathF.PI * 1000 * i / SampleRate);

        using var aggregator = Create(new BufferSource(data));
        float[]? bars = null;
        using var ready = new ManualResetEventSlim();
        aggregator.SpectrumDataReady += b => { bars = b; ready.Set(); };
        ReadAll(aggregator, FftSize);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "频谱帧超时");

        Assert.NotNull(bars);
        int peak = 0;
        for (int i = 1; i < bars.Length; i++)
            if (bars[i] > bars[peak]) peak = i;
        Assert.InRange(peak, 17, 19);
    }

    [Fact]
    public void PrecomputedTables_MatchReferenceBars()
    {
        var data = new float[FftSize];
        for (int i = 0; i < FftSize; i++)
            data[i] = 0.2f * MathF.Sin(2 * MathF.PI * 250 * i / SampleRate)
                    + 0.1f * MathF.Sin(2 * MathF.PI * 3000 * i / SampleRate);

        using var aggregator = Create(new BufferSource(data));
        float[]? bars = null;
        using var ready = new ManualResetEventSlim();
        aggregator.SpectrumDataReady += b => { bars = b; ready.Set(); };
        ReadAll(aggregator, FftSize);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "频谱帧超时");

        Assert.NotNull(bars);
        var expected = ReferenceBars(data);
        for (int i = 0; i < BarCount; i++)
            Assert.Equal(expected[i], bars[i], 5);
    }

    [Fact]
    public void Silence_NoNaN_AllZero()
    {
        using var aggregator = Create(new BufferSource(new float[FftSize]));
        float[]? bars = null;
        using var ready = new ManualResetEventSlim();
        aggregator.SpectrumDataReady += b => { bars = b; ready.Set(); };
        ReadAll(aggregator, FftSize);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "静音频谱帧超时");

        Assert.NotNull(bars);
        Assert.All(bars, x => Assert.False(float.IsNaN(x)));
        Assert.All(bars, x => Assert.Equal(0f, x));
    }

    [Fact]
    public void Disabled_PassesThroughExactly_NoEvent()
    {
        var data = new float[FftSize];
        for (int i = 0; i < data.Length; i++) data[i] = i % 7 * 0.1f;
        using var aggregator = new SampleAggregator(
            new BufferSource((float[])data.Clone()),
            new SpectrumConfig { FftSize = FftSize, BarCount = BarCount })
        {
            Enabled = false
        };
        int events = 0;
        aggregator.SpectrumDataReady += _ => events++;

        var output = new float[data.Length];
        int read = 0;
        var sourceBuffer = new float[256];
        while (read < data.Length)
        {
            int n = aggregator.Read(sourceBuffer.AsSpan(0, Math.Min(256, data.Length - read)));
            sourceBuffer.AsSpan(0, n).CopyTo(output.AsSpan(read));
            read += n;
        }

        Assert.Equal(0, events);
        Assert.Equal(data, output);
    }

    [Fact]
    public void SubscriberMutation_DoesNotAffectNextFrame()
    {
        // 3 帧（50% hop → 在 8192 / 12288 / 16384 个样本处触发）。三帧才能同时发现：
        //  - 单缓冲复用：三帧交付同一数组引用（NotSame 失败，且快照 0 被后续帧重写）；
        //  - 双缓冲交替复用：快照 0 与快照 2 同引用且被重新填充（NotSame / 全零断言失败）。
        const int frameCount = 3;
        var data = new float[FftSize * 2];
        // 必须使用有频域能量的信号：直流信号在 bar 起始 bin（>=3）上无能量
        for (int i = 0; i < data.Length; i++)
            data[i] = 0.3f * MathF.Sin(2 * MathF.PI * 1000 * i / SampleRate);

        using var aggregator = Create(new BufferSource(data));
        var events = new List<float[]>();
        using var ready = new ManualResetEventSlim();
        aggregator.SpectrumDataReady += bars =>
        {
            lock (events)
            {
                events.Add(bars);
                // 只篡改第一帧快照：验证订阅者改动不会污染后续帧，且该快照之后不再被重写
                if (events.Count == 1) Array.Fill(bars, 0f);
                if (events.Count >= frameCount) ready.Set();
            }
        };

        ReadAll(aggregator, data.Length);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "三帧频谱超时");

        float[][] snapshots;
        lock (events) snapshots = events.GetRange(0, frameCount).ToArray();
        Assert.Equal(frameCount, snapshots.Length);

        // 每帧必须交付独立快照：复用/池化输出缓冲（含交替双缓冲）都会在此暴露
        for (int i = 0; i < snapshots.Length; i++)
            for (int j = i + 1; j < snapshots.Length; j++)
                Assert.NotSame(snapshots[i], snapshots[j]);

        // 订阅者篡改第一帧后，后续帧仍应包含频域能量（原测试意图）
        Assert.Contains(snapshots[1], x => x > 0f);
        Assert.Contains(snapshots[2], x => x > 0f);

        // 第一帧快照在后续帧处理完成后必须仍是订阅者写入的全零内容：
        // 若聚合器复用同一输出缓冲，后续帧会把它重写成新的非零柱
        Assert.All(snapshots[0], x => Assert.Equal(0f, x));
    }

    [Fact]
    public void Dispose_StopsThread_NoEventsAfterDispose()
    {
        var data = new float[FftSize * 2];
        Array.Fill(data, 0.2f);
        using var aggregator = Create(new BufferSource(data));
        int events = 0;
        using var firstFrame = new ManualResetEventSlim();
        aggregator.SpectrumDataReady += _ =>
        {
            Interlocked.Increment(ref events);
            firstFrame.Set();
        };

        ReadAll(aggregator, FftSize);
        Assert.True(firstFrame.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "FFT 线程未产出首帧");
        aggregator.Dispose();
        int afterDispose = Volatile.Read(ref events);
        Thread.Sleep(100);
        ReadAll(aggregator, FftSize);

        Assert.Equal(afterDispose, Volatile.Read(ref events));
    }

    [Fact]
    public void EnabledToggle_SkipsStaleFrames()
    {
        var data = new float[FftSize * 2];
        // 前半静音，后半 1 kHz 正弦（直流信号在 20 Hz 起的频谱柱上没有能量，
        // 无法区分"算到了新样本"与"只算到静音"）。
        for (int i = FftSize; i < data.Length; i++)
            data[i] = 0.8f * MathF.Sin(2 * MathF.PI * 1000 * (i - FftSize) / SampleRate);
        using var aggregator = Create(new BufferSource(data));
        aggregator.Enabled = false;

        ReadAll(aggregator, FftSize); // 禁用期间读掉静音段
        aggregator.Enabled = true;

        float[]? bars = null;
        using var ready = new ManualResetEventSlim();
        aggregator.SpectrumDataReady += b => { bars = b; ready.Set(); };
        ReadAll(aggregator, FftSize); // 启用后读响段
        Assert.True(ready.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "启用后频谱帧超时");

        Assert.NotNull(bars);
        Assert.Contains(bars, x => x > 0f);
        Assert.All(bars, x => Assert.False(float.IsNaN(x)));
    }
}
