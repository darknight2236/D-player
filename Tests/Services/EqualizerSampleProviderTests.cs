using NAudio.Wave;
using DPlayer.Models;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public class EqualizerSampleProviderTests
{
    /// <summary>产生固定样本的假 ISampleProvider（默认单声道 44.1kHz IeeeFloat）。</summary>
    private sealed class FakeSampleProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _pos;
        public FakeSampleProvider(float[] data, int channels = 1, int sampleRate = 44100)
        {
            _data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }

    private static float[] Sine(float freq, int n, int sr = 44100, float amp = 0.3f)
    {
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = amp * MathF.Sin(2 * MathF.PI * freq * i / sr);
        return d;
    }

    [Fact]
    public void Disabled_PassesThroughExactly()
    {
        var data = new float[] { 0.1f, -0.2f, 0.3f, -0.4f, 0.5f, -0.6f };
        var src = new FakeSampleProvider((float[])data.Clone());
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig { Enabled = false });

        var buf = new float[data.Length];
        int read = eq.Read(buf, 0, buf.Length);

        Assert.Equal(data.Length, read);
        Assert.Equal(data, buf); // 未处理 → 逐位相等
    }

    [Fact]
    public void WaveFormat_PassesThroughSource()
    {
        var src = new FakeSampleProvider(new float[4], channels: 2, sampleRate: 48000);
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig());
        Assert.Equal(2, eq.WaveFormat.Channels);
        Assert.Equal(48000, eq.WaveFormat.SampleRate);
    }

    [Fact]
    public void FlatEnabled_IsUnity_ForDcSignal()
    {
        var data = new float[8];
        Array.Fill(data, 0.5f);
        var src = new FakeSampleProvider((float[])data.Clone());
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig { Enabled = true }); // Flat, preamp 0

        var buf = new float[data.Length];
        eq.Read(buf, 0, buf.Length);

        // 0 dB 峰值滤波 = unity；DC 精确透传（4 位小数容差防浮点）
        Assert.All(buf, x => Assert.Equal(0.5f, x, 4));
    }

    [Fact]
    public void BassBoost_IncreasesLowFrequencyAmplitude()
    {
        const int sr = 44100, n = 16384, skip = 4096; // 跳过建立期
        var boostGains = EqualizerPresets.All.First(p => p.Name == "Bass Boost").Gains;

        var flatBuf = new float[n];
        new EqualizerSampleProvider(new FakeSampleProvider(Sine(31, n, sr)),
            new EqualizerConfig { Enabled = true }).Read(flatBuf, 0, n);

        var boostBuf = new float[n];
        new EqualizerSampleProvider(new FakeSampleProvider(Sine(31, n, sr)),
            EqualizerConfig.Create(true, 0, boostGains, "Bass Boost")).Read(boostBuf, 0, n);

        double Rms(float[] b) { double s = 0; for (int i = skip; i < n; i++) s += b[i] * b[i]; return Math.Sqrt(s / (n - skip)); }
        Assert.True(Rms(boostBuf) > Rms(flatBuf) * 1.2, $"低音增强应显著提升 31Hz 幅度 (flat={Rms(flatBuf):F4}, boost={Rms(boostBuf):F4})");
    }

    [Fact]
    public void Update_DoesNotThrow_AndKeepsPlaying()
    {
        var src = new FakeSampleProvider(Sine(1000, 256));
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig { Enabled = true });
        eq.Update(EqualizerConfig.Create(true, -3, new double[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 6 }, "Custom"));
        var buf = new float[256];
        Assert.Equal(256, eq.Read(buf, 0, 256));
    }
}
