using System.IO;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class JsonPlaylistServiceTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"dplayer-queue-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private JsonPlaylistService CreateService()
        => new(new DPlayerDataPaths { Root = _dir });

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsLibraryPlayback()
    {
        var service = CreateService();
        var libraryPlayback = new Playlist(
            "__library-playback__", "音乐库播放",
            new[] { @"C:\Music\a.mp3" }, 0, false, RepeatMode.Off);

        await service.SaveAsync(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("p1", "P", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = libraryPlayback.Id,
            LibraryPlayback = libraryPlayback,
        });

        var loaded = await service.LoadAsync();

        Assert.NotNull(loaded.LibraryPlayback);
        Assert.Equal(libraryPlayback.Id, loaded.CurrentPlaylistId);
        Assert.Equal("音乐库播放", loaded.LibraryPlayback!.Name);
        Assert.Equal(@"C:\Music\a.mp3", loaded.LibraryPlayback.Items.Single());
    }

    [Fact]
    public async Task LoadAsync_OlderSnapshotWithoutLibraryPlayback_ReturnsNull()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "queue.json"), """
        {
          "SchemaVersion": 3,
          "Playlists": [
            { "Id": "p1", "Name": "P", "Items": [], "CurrentIndex": -1,
              "ShuffleEnabled": false, "RepeatMode": "Off" }
          ],
          "CurrentPlaylistId": "p1"
        }
        """, TestContext.Current.CancellationToken);

        var loaded = await CreateService().LoadAsync();

        Assert.Null(loaded.LibraryPlayback);
        Assert.Equal("p1", loaded.CurrentPlaylistId);
        Assert.Single(loaded.Playlists);
    }

    [Fact]
    public async Task LoadAsync_OnlyLibraryPlayback_KeepsItInsteadOfSeeding()
    {
        var service = CreateService();
        var libraryPlayback = new Playlist(
            "__library-playback__", "音乐库播放",
            Array.Empty<string>(), -1, false, RepeatMode.Off);

        await service.SaveAsync(new QueueState
        {
            Playlists = Array.Empty<Playlist>(),
            CurrentPlaylistId = libraryPlayback.Id,
            LibraryPlayback = libraryPlayback,
        });

        var loaded = await service.LoadAsync();

        Assert.NotNull(loaded.LibraryPlayback);
        Assert.Empty(loaded.Playlists);
        Assert.Equal(libraryPlayback.Id, loaded.CurrentPlaylistId);
    }
}
