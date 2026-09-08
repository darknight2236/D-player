using DPlayer.Models;
using Xunit;

namespace DPlayer.Tests.Models;

public class EqualizerPresetsTests
{
    [Fact]
    public void CenterFrequencies_Has10IsoOctaveBands()
    {
        Assert.Equal(EqualizerPresets.BandCount, EqualizerPresets.CenterFrequencies.Count);
        Assert.Equal(31, EqualizerPresets.CenterFrequencies[0]);
        Assert.Equal(16000, EqualizerPresets.CenterFrequencies[9]);
    }

    [Fact]
    public void EveryPreset_Has10Bands_Within12dB()
    {
        foreach (var (name, gains) in EqualizerPresets.All)
        {
            Assert.Equal(EqualizerPresets.BandCount, gains.Length);
            Assert.All(gains, g => Assert.InRange(g, EqualizerPresets.MinGainDb, EqualizerPresets.MaxGainDb));
        }
    }

    [Fact]
    public void Flat_IsAllZero()
    {
        Assert.True(EqualizerPresets.TryGet(EqualizerPresets.Flat, out var gains));
        Assert.All(gains, g => Assert.Equal(0, g));
    }

    [Fact]
    public void Names_DoesNotContainCustom()
    {
        Assert.DoesNotContain(EqualizerPresets.Custom, EqualizerPresets.Names);
        Assert.Contains(EqualizerPresets.Flat, EqualizerPresets.Names);
    }

    [Fact]
    public void Match_ReturnsPresetName_OrCustom()
    {
        Assert.True(EqualizerPresets.TryGet("Rock", out var rock));
        Assert.Equal("Rock", EqualizerPresets.Match(rock));
        Assert.Equal(EqualizerPresets.Custom, EqualizerPresets.Match(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
    }
}
