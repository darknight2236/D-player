using DPlayer.Models;
using Xunit;

namespace DPlayer.Tests.Models;

public class EqualizerConfigTests
{
    [Fact]
    public void Default_IsDisabledFlatTransparent()
    {
        var c = new EqualizerConfig();
        Assert.False(c.Enabled);
        Assert.Equal(0, c.PreampDb);
        Assert.Equal(EqualizerPresets.Flat, c.Preset);
        Assert.Equal(EqualizerPresets.BandCount, c.BandGainsDb.Count);
        Assert.All(c.BandGainsDb, g => Assert.Equal(0, g));
    }

    [Fact]
    public void Create_ClampsOutOfRangeGains()
    {
        var c = EqualizerConfig.Create(true, 99, new double[] { 50, -50 }, "Custom");
        Assert.Equal(12, c.PreampDb);           // Clamp 到 +12
        Assert.Equal(12, c.BandGainsDb[0]);      // Clamp 到 +12
        Assert.Equal(-12, c.BandGainsDb[1]);     // Clamp 到 -12
        Assert.Equal(0, c.BandGainsDb[2]);       // 不足补 0
    }

    [Fact]
    public void Create_TruncatesExcessBands()
    {
        var many = new double[15];
        Array.Fill(many, 3.0);
        var c = EqualizerConfig.Create(true, 0, many, "X");
        Assert.Equal(EqualizerPresets.BandCount, c.BandGainsDb.Count); // 超出 10 段被截断
        Assert.All(c.BandGainsDb, g => Assert.Equal(3, g));
    }

    [Fact]
    public void PreampLinearGain_ConvertsDbToLinear()
    {
        Assert.Equal(1f, new EqualizerConfig { PreampDb = 0 }.PreampLinearGain, 3);
        Assert.True(new EqualizerConfig { PreampDb = 6 }.PreampLinearGain > 1.9f);
        Assert.True(new EqualizerConfig { PreampDb = -6 }.PreampLinearGain < 0.6f);
    }
}
