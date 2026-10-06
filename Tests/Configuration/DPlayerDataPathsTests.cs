using System.IO;
using DPlayer.Configuration;
using Xunit;

namespace DPlayer.Tests.Configuration;

public sealed class DPlayerDataPathsTests
{
    [Fact]
    public void Directory_CombinesRootAndFolder()
        => Assert.Equal(Path.Combine(@"C:\root", "D-player"),
            new DPlayerDataPaths(@"C:\root", "D-player").Directory);

    [Fact]
    public void Directory_EmptyFolder_IsRoot()
        => Assert.Equal(@"C:\root", new DPlayerDataPaths(@"C:\root", string.Empty).Directory);

    [Fact]
    public void Default_HasNoFolderName()
        => Assert.Equal(string.Empty, new DPlayerDataPaths { Root = @"C:\root" }.FolderName);
}
