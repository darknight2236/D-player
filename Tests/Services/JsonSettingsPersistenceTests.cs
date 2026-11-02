using System.IO;
using DPlayer.Configuration;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class JsonSettingsPersistenceTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"dplayer-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task UpdateAsync_PersistsLibraryFolders()
    {
        var persistence = new JsonSettingsPersistence(new DPlayerDataPaths { Root = _dir });

        await persistence.UpdateAsync(s => s with
        {
            LibraryFolders = new[] { @"C:\Music", @"D:\Audio" }
        });

        var loaded = await persistence.LoadAsync();
        Assert.Equal(new[] { @"C:\Music", @"D:\Audio" }, loaded.LibraryFolders);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmptyLibraryFolders()
    {
        var persistence = new JsonSettingsPersistence(new DPlayerDataPaths { Root = _dir });

        Assert.Empty((await persistence.LoadAsync()).LibraryFolders);
    }
}
