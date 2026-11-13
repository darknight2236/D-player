using NAudio.Dsp;
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
        public int Read(Span<float> buffer)
        {
            int n = Math.Min(buffer.Length, _data.Length - _pos);
            if (n <= 0) return 0;
            _data.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }
    }

    private sealed class EndlessSineProvider : ISampleProvider
    {
        private readonly double _step;
        private double _phase;

        public EndlessSineProvider(int sampleRate = 44100)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            _step = 2 * Math.PI * 1000 / sampleRate;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (float)(0.3 * Math.Sin(_phase));
                _phase += _step;
            }
            return buffer.Length;
        }
    }

    private static float[] Sine(float freq, int n, int sr = 44100, float amp = 0.3f)
    {
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = amp * MathF.Sin(2 * MathF.PI * freq * i / sr);
        return d;
    }

    /// <summary>
    /// 参考实现：与 provider 相同的段级联顺序（每段依次处理整块区间，等价于逐样本按段顺序级联），
    /// 直接在调用方提供的数组上就地变换。
    /// </summary>
    private static void ApplyReferenceFilters(BiQuadFilter[] filters, float[] data, int offset, int count)
    {
        foreach (var filter in filters)
        {
            for (int i = offset; i < offset + count; i++)
                data[i] = filter.Transform(data[i]);
        }
    }

    /// <summary>
    /// 整块逐样本与参考滤波组比对：任一样本（expected 或 actual）非有限即失败，
    /// 其余样本最大绝对偏差须 &lt; 1e-4（防浮点实现差异）。
    /// </summary>
    private static void AssertMatchesReference(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        int worstIndex = 0;
        float worstDiff = 0f;
        int nonFiniteIndex = -1;
        for (int i = 0; i < expected.Length; i++)
        {
            float e = expected[i];
            float a = actual[i];
            float diff = Math.Abs(e - a);
            // NaN/Inf 参与的比较恒为 false（NaN > worstDiff 永远不成立），只看 diff 会让
            // 整块 NaN 被当成 0 偏差而“通过”。非有限样本必须单独记录并直接判失败。
            if (!float.IsFinite(e) || !float.IsFinite(a) || !float.IsFinite(diff))
            {
                if (nonFiniteIndex < 0) nonFiniteIndex = i;
                continue;
            }
            if (diff > worstDiff) { worstDiff = diff; worstIndex = i; }
        }

        if (nonFiniteIndex >= 0)
        {
            Assert.Fail(
                $"输出含非有限样本: sample {nonFiniteIndex} (expected={expected[nonFiniteIndex]:G9}, actual={actual[nonFiniteIndex]:G9})");
        }

        Assert.True(worstDiff < 1e-4f,
            $"输出偏离参考滤波组: maxAbsDiff={worstDiff:G9} @ sample {worstIndex} (expected={expected[worstIndex]:G9}, actual={actual[worstIndex]:G9})");
    }

    [Fact]
    public void Disabled_PassesThroughExactly()
    {
        var data = new float[] { 0.1f, -0.2f, 0.3f, -0.4f, 0.5f, -0.6f };
        var src = new FakeSampleProvider((float[])data.Clone());
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig { Enabled = false });

        var buf = new float[data.Length];
        int read = eq.Read(buf);

        Assert.Equal(data.Length, read);
        Assert.Equal(data, buf); // 未处理 → 逐位相等
    }

    [Fact]
    public void FlatEnabled_IsExactPassthrough()
    {
        var data = new[] { 0.1f, -0.2f, 0.3f, -0.4f, 0.5f, -0.6f, 0.7f, -0.8f };
        var eq = new EqualizerSampleProvider(
            new FakeSampleProvider((float[])data.Clone()),
            new EqualizerConfig { Enabled = true });
        var buffer = new float[data.Length];
        int read = eq.Read(buffer);
        Assert.Equal(data.Length, read);
        Assert.Equal(data, buffer); // 0 dB 频段全体恒等 → 逐位透传（快速路径的等价性钉子）
    }

    [Fact]
    public void FlatEnabled_IsUnity_ForDcSignal()
    {
        var data = new float[8];
        Array.Fill(data, 0.5f);
        var src = new FakeSampleProvider((float[])data.Clone());
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig { Enabled = true }); // Flat, preamp 0

        var buf = new float[data.Length];
        eq.Read(buf);

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
            new EqualizerConfig { Enabled = true }).Read(flatBuf);

        var boostBuf = new float[n];
        new EqualizerSampleProvider(new FakeSampleProvider(Sine(31, n, sr)),
            EqualizerConfig.Create(true, 0, boostGains, "Bass Boost")).Read(boostBuf);

        double Rms(float[] b) { double s = 0; for (int i = skip; i < n; i++) s += b[i] * b[i]; return Math.Sqrt(s / (n - skip)); }
        Assert.True(Rms(boostBuf) > Rms(flatBuf) * 1.2, $"低音增强应显著提升 31Hz 幅度 (flat={Rms(flatBuf):F4}, boost={Rms(boostBuf):F4})");
    }

    [Fact]
    public void PreampGain_SixDb_ScalesRmsByAboutTwo()
    {
        const int sr = 44100, n = 16384, skip = 4096;
        var data = Sine(1000, n, sr);

        var flatBuf = new float[n];
        new EqualizerSampleProvider(new FakeSampleProvider((float[])data.Clone()),
            new EqualizerConfig { Enabled = true }).Read(flatBuf);

        var boostedBuf = new float[n];
        new EqualizerSampleProvider(new FakeSampleProvider((float[])data.Clone()),
            EqualizerConfig.Create(true, 6, null, "Flat")).Read(boostedBuf);

        double Rms(float[] b) { double s = 0; for (int i = skip; i < n; i++) s += b[i] * b[i]; return Math.Sqrt(s / (n - skip)); }
        double ratio = Rms(boostedBuf) / Rms(flatBuf);
        Assert.InRange(ratio, 1.9, 2.1); // 10^(6/20) = 1.995
    }

    [Fact]
    public void Update_BandBoost_IncreasesAmplitude_AfterRuntimeChange()
    {
        const int sr = 44100, n = 16384, skip = 4096;
        var eq = new EqualizerSampleProvider(new FakeSampleProvider(Sine(1000, 2 * n, sr)),
            new EqualizerConfig { Enabled = true });

        var flatBuf = new float[n];
        eq.Read(flatBuf);

        var gains = new double[EqualizerPresets.BandCount];
        gains[5] = 12; // 1kHz 段 +12dB
        eq.Update(EqualizerConfig.Create(true, 0, gains, "Custom"));

        var boostedBuf = new float[n];
        eq.Read(boostedBuf);

        double Rms(float[] b) { double s = 0; for (int i = skip; i < n; i++) s += b[i] * b[i]; return Math.Sqrt(s / (n - skip)); }
        Assert.True(Rms(boostedBuf) > Rms(flatBuf) * 2.5,
            $"运行时 +12dB 应显著提升 1kHz 幅度 (flat={Rms(flatBuf):F4}, boosted={Rms(boostedBuf):F4})");
    }

    [Fact]
    public void Stereo_ChannelsFilteredIndependently()
    {
        // 左声道 31Hz、右声道静音；BassBoost 后右声道应仍≈0（证明每声道独立、无串扰）
        const int n = 16384;
        var interleaved = new float[n];
        var l = Sine(31, n / 2);
        for (int i = 0; i < n / 2; i++) { interleaved[2 * i] = l[i]; interleaved[2 * i + 1] = 0f; }
        var gains = EqualizerPresets.All.First(p => p.Name == "Bass Boost").Gains;

        var buf = new float[n];
        new EqualizerSampleProvider(new FakeSampleProvider(interleaved, channels: 2),
            EqualizerConfig.Create(true, 0, gains, "Bass Boost")).Read(buf);

        // 右声道（奇数下标）建立期后应仍≈0
        for (int i = 4097; i < n; i += 2) Assert.True(Math.Abs(buf[i]) < 1e-3f); // 奇数下标 = 右声道
    }

    [Fact]
    public void NyquistBand_IsBypassed_AtLowSampleRate()
    {
        // sr=22050 → nyquist=11025，16k 段（>=nyquist）应被旁路（不产生不稳定/放大输出）
        const int sr = 22050, n = 8192;
        var gains = new double[10]; gains[9] = 12; // 仅提升 16k 段
        var src = new FakeSampleProvider(Sine(9000, n, sr), channels: 1, sampleRate: sr);

        var buf = new float[n];
        new EqualizerSampleProvider(src, EqualizerConfig.Create(true, 0, gains, "Custom")).Read(buf);

        // 16k 段被旁路 → 9kHz 幅度不应因该段被显著抬升（输入幅度 0.3）
        Assert.All(buf[4096..], x => Assert.True(Math.Abs(x) <= 0.35f));
    }

    [Fact]
    public void Update_AppliedAtBufferBoundary_MatchesReferenceFilterBank()
    {
        const int n = 4096;
        var data = Sine(1000, n * 2);

        // 测试侧参考滤波组：10 段各一个 PeakingEQ，flat（0dB）起步，与 provider 构造期状态一致。
        // 注意 NAudio 3.3.0 的 SetPeakingEq 经 SetCoefficients 会清零 x1/x2/y1/y2（它有独立的
        // ResetState；Update* 保留状态的变体只存在于 low/high/band-pass/notch）。因此本 oracle
        // 精确验证的是“配置在 buffer 边界整段换系数”，而不是跨边界状态连续性——后者用此 API
        // 无法实现，provider 当前行为同样是重置双二阶状态（见 task-8 fix report）。
        var reference = new BiQuadFilter[EqualizerPresets.BandCount];
        for (int b = 0; b < reference.Length; b++)
            reference[b] = BiQuadFilter.PeakingEQ(44100f, (float)EqualizerPresets.CenterFrequencies[b], EqualizerPresets.Q, 0f);

        // 参考组先处理第一块：既得到第一块的期望值，也把组内状态推进到样本 n 处。
        var expectedFirst = data[..n].ToArray();
        ApplyReferenceFilters(reference, expectedFirst, 0, n);

        var eq = new EqualizerSampleProvider(
            new FakeSampleProvider((float[])data.Clone()),
            new EqualizerConfig { Enabled = true });

        var first = new float[n];
        eq.Read(first);
        AssertMatchesReference(expectedFirst, first); // Update 之前的块必须仍是 flat：配置只在边界生效

        var gains = new double[EqualizerPresets.BandCount];
        gains[5] = 12;
        eq.Update(EqualizerConfig.Create(true, 0, gains, "Custom"));

        // 参考组对全部 10 段换系数（与 ApplyConfig 相同顺序/参数，0dB 段也重算），再处理第二块。
        // 频段顺序、增益、以及“第二块才生效”任何一处错了都会超出 1e-4 容差。
        // 两个配置的 preamp 都是 0 dB，本用例不覆盖 preamp（由 PreampGain_SixDb_ScalesRmsByAboutTwo 覆盖）。
        for (int b = 0; b < reference.Length; b++)
            reference[b].SetPeakingEq(44100f, (float)EqualizerPresets.CenterFrequencies[b], EqualizerPresets.Q, (float)gains[b]);

        var expectedSecond = data[n..].ToArray();
        ApplyReferenceFilters(reference, expectedSecond, 0, n);

        var second = new float[n];
        eq.Read(second);
        AssertMatchesReference(expectedSecond, second);
    }

    [Fact]
    public void PendingConfig_WhileDisabled_EnablesOnNextRead()
    {
        var data = Sine(1000, 2048);
        var eq = new EqualizerSampleProvider(
            new FakeSampleProvider((float[])data.Clone()),
            new EqualizerConfig { Enabled = false });
        var gains = new double[EqualizerPresets.BandCount];
        gains[5] = 12;
        eq.Update(EqualizerConfig.Create(true, 0, gains, "Custom"));

        var buffer = new float[data.Length];
        eq.Read(buffer);

        // 若 pending 配置被丢弃、或在 _enabled 判断之后才应用、或仍保持禁用，输出就是原样透传：
        // 逐位相等且 RMS 比为 1。下面两个断言都只有“本块内真的启用了 +12dB 提升”才能满足。
        Assert.False(buffer.AsSpan().SequenceEqual(data), "pending 配置未在本块生效：输出与原样透传逐位相同");

        double Rms(ReadOnlySpan<float> b)
        {
            double s = 0;
            for (int i = 256; i < b.Length; i++) s += b[i] * b[i]; // 跳过滤波器建立期
            return Math.Sqrt(s / (b.Length - 256));
        }
        double ratio = Rms(buffer) / Rms(data);
        Assert.InRange(ratio, 3.0, 4.5); // 1kHz 段 +12dB ≈ ×3.98（10^(12/20)）；透传时为 1
    }

    [Fact]
    public async Task Update_ConcurrentWithRead_NoExceptions_FiniteOutput()
    {
        const int n = 8192;
        var eq = new EqualizerSampleProvider(
            new EndlessSineProvider(),
            new EqualizerConfig { Enabled = true });
        var buffer = new float[n];
        var stop = new ManualResetEventSlim();
        Exception? failure = null;

        var reader = Task.Run(() =>
        {
            try
            {
                while (!stop.IsSet)
                {
                    eq.Read(buffer);
                    Assert.All(buffer, x => Assert.False(float.IsNaN(x) || float.IsInfinity(x)));
                }
            }
            catch (Exception ex) { failure = ex; }
        }, TestContext.Current.CancellationToken);

        var gains = new double[EqualizerPresets.BandCount];
        for (int i = 0; i < 200; i++)
        {
            gains[5] = (i % 24) - 12;
            eq.Update(EqualizerConfig.Create(true, 0, gains, "Custom"));
        }

        stop.Set();
        await reader.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Null(failure);
    }
}
