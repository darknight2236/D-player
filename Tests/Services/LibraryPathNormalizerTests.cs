using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class LibraryPathNormalizerTests
{
    [Fact]
    public void Normalize_TrimsTrailingSeparator()
        => Assert.Equal(@"C:\Music", LibraryPathNormalizer.Normalize(@"C:\Music\"));

    [Fact]
    public void Normalize_DriveRoot_KeepsSeparator()
        => Assert.Equal(@"C:\", LibraryPathNormalizer.Normalize(@"C:\"));

    [Fact]
    public void Normalize_Whitespace_ReturnsNull()
        => Assert.Null(LibraryPathNormalizer.Normalize("   "));

    [Fact]
    public void NormalizeMany_DropsDuplicates_CaseInsensitive()
    {
        var result = LibraryPathNormalizer.NormalizeMany(
            new[] { @"C:\Music\", @"c:\music", @"C:\Music\Rock" });

        Assert.Equal(new[] { @"C:\Music", @"C:\Music\Rock" }, result);
    }
}
