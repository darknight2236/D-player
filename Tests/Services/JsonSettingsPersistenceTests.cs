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

    [Fact]
    public async Task UpdateAsync_WriteFailure_KeepsOldSettings()
    {
        var persistence = new JsonSettingsPersistence(new DPlayerDataPaths { Root = _dir });
        await persistence.UpdateAsync(s => s with { DefaultVolume = 0.5f });
        var path = Path.Combine(_dir, "settings.json");

        await using (var hold = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.None))
        {
            // 见 JsonFileStoreTests：Windows 的 Move 覆盖失败是 UnauthorizedAccessException。
            var ex = await Record.ExceptionAsync(() =>
                persistence.UpdateAsync(s => s with { DefaultVolume = 0.2f }));
            Assert.True(ex is IOException or UnauthorizedAccessException,
                $"expected locked settings write to fail, got {ex?.GetType().Name ?? "no exception"}");
        }

        var text = await File.ReadAllTextAsync(path,
            TestContext.Current.CancellationToken);
        Assert.Contains("0.5", text);
    }
}
