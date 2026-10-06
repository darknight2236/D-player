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

    /// <summary>
    /// 钉住 WPF 壳 App.OnStartup 的真实构造（FolderName="D-player"，Root 取默认 LocalApplicationData）。
    /// 这条是唯一能挡住"默认 Root 被改动 → 每个真实用户的数据目录静默搬家、库被清空"的回归测试。
    /// 只算路径字符串，不碰磁盘、不读写 %LOCALAPPDATA%。
    /// </summary>
    [Fact]
    public void Directory_WpfShellConstruction_IsLocalAppDataDPlayer()
        => Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D-player"),
            new DPlayerDataPaths { FolderName = "D-player" }.Directory);
}
