using System;
using System.IO;
using DPlayer.Configuration;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class DisposalTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"dplayer-dispose-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void JsonServices_DisposeTwice_DoNotThrow()
    {
        var paths = new DPlayerDataPaths { Root = _dir };
        var cache = new JsonLibraryCache(paths);
        var playlists = new JsonPlaylistService(paths);
        var settings = new JsonSettingsPersistence(paths);

        cache.Dispose();
        cache.Dispose();
        playlists.Dispose();
        playlists.Dispose();
        settings.Dispose();
        settings.Dispose();
    }
}
