using System;
using UmaPlayer.Models;
using Xunit;

namespace UmaPlayer.Tests.Smoke;

public class SmokeTests
{
    [Fact]
    public void Track_Record_StructuralEquality()
    {
        var a = new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero, null);
        var b = new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero, null);
        Assert.Equal(a, b);
    }
}
