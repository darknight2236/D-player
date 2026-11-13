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
        var aggregator = Create(source);
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

        var aggregator = Create(new BufferSource(data));
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
    public void Silence_NoNaN_AllZero()
    {
        var aggregator = Create(new BufferSource(new float[FftSize]));
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
        var aggregator = new SampleAggregator(
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
        var data = new float[FftSize + FftSize / 2];
        // 必须使用有频域能量的信号：直流信号在 bar 起始 bin（>=3）上无能量
        for (int i = 0; i < data.Length; i++)
            data[i] = 0.3f * MathF.Sin(2 * MathF.PI * 1000 * i / SampleRate);

        var aggregator = Create(new BufferSource(data));
        var events = new List<float[]>();
        using var ready = new ManualResetEventSlim();
        aggregator.SpectrumDataReady += bars =>
        {
            lock (events)
            {
                events.Add(bars);
                // 只篡改第一帧快照：验证订阅者改动不会污染下一帧的内部状态
                if (events.Count == 1) Array.Fill(bars, 0f);
                if (events.Count >= 2) ready.Set();
            }
        };

        ReadAll(aggregator, data.Length);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "两帧频谱超时");

        float[] second;
        lock (events) second = events[1];
        Assert.Contains(second, x => x > 0f);
    }
}
