using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"dplayer-jsonstore-{Guid.NewGuid():N}");

    public JsonFileStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task WriteAtomicAsync_ReplacesExistingFile_AndRemovesTemp()
    {
        var path = Path.Combine(_dir, "data.json");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);

        await JsonFileStore.WriteAtomicAsync(path, new { Value = 1 },
            new JsonSerializerOptions { WriteIndented = true });

        var text = await File.ReadAllTextAsync(path,
            TestContext.Current.CancellationToken);
        Assert.Contains("\"Value\": 1", text);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task WriteAtomicAsync_LockedTarget_KeepsOldFile()
    {
        var path = Path.Combine(_dir, "locked.json");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);

        await using (var hold = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.None))
        {
            // Windows 上 File.Move 覆盖一个缺 FILE_SHARE_DELETE 的目标会返回
            // ERROR_ACCESS_DENIED → UnauthorizedAccessException；其他平台可能是 IOException。
            var ex = await Record.ExceptionAsync(() =>
                JsonFileStore.WriteAtomicAsync(path, new { Value = 2 },
                    new JsonSerializerOptions()));
            Assert.True(ex is IOException or UnauthorizedAccessException,
                $"expected locked-target write to fail, got {ex?.GetType().Name ?? "no exception"}");
        }

        Assert.Equal("old", await File.ReadAllTextAsync(path,
            TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
