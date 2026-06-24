using System.IO;
using UmaPlayer.Models;
using UmaPlayer.Services;
using Xunit;

namespace UmaPlayer.Tests.Services;

public sealed class JsonLibraryCacheTests : IDisposable
{
    private readonly string _tempDir;

    public JsonLibraryCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UmaPlayerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsEmpty()
    {
        var cache = new JsonLibraryCache(_tempDir);

        var result = await cache.LoadAsync(@"C:\Music\FolderA");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var cache = new JsonLibraryCache(_tempDir);
        var folder = Path.Combine(_tempDir, "Music");
        var entries = new List<LibraryCacheEntry>
        {
            new(FilePath: @"C:\Music\song1.mp3", Title: "Song One", Artist: "Artist A",
                Album: "Album X", Genre: "Rock", Year: 2020,
                Duration: TimeSpan.FromMinutes(3), SampleRate: 44100, TrackNumber: null),
            new(FilePath: @"C:\Music\song2.flac", Title: "Song Two", Artist: null,
                Album: null, Genre: null, Year: null,
                Duration: TimeSpan.FromMinutes(5), SampleRate: 96000, TrackNumber: null),
        };

        await cache.SaveAsync(folder, entries);
        var loaded = await cache.LoadAsync(folder);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Song One", loaded[0].Title);
        Assert.Equal("Artist A", loaded[0].Artist);
        Assert.Equal(44100, loaded[0].SampleRate);
        Assert.Equal("Song Two", loaded[1].Title);
        Assert.Null(loaded[1].Artist);
        Assert.Equal(96000, loaded[1].SampleRate);
    }

    [Fact]
    public async Task SaveAsync_MultipleFolders_Independently()
    {
        var cache = new JsonLibraryCache(_tempDir);
        var folderA = Path.Combine(_tempDir, "FolderA");
        var folderB = Path.Combine(_tempDir, "FolderB");

        var entriesA = new List<LibraryCacheEntry>
        {
            new(FilePath: @"C:\A\track.mp3", Title: "Track A", Artist: null,
                Album: null, Genre: null, Year: null,
                Duration: TimeSpan.FromMinutes(2), SampleRate: null, TrackNumber: null),
        };
        var entriesB = new List<LibraryCacheEntry>
        {
            new(FilePath: @"C:\B\track.mp3", Title: "Track B", Artist: "B Artist",
                Album: null, Genre: null, Year: null,
                Duration: TimeSpan.FromMinutes(4), SampleRate: 48000, TrackNumber: null),
        };

        await cache.SaveAsync(folderA, entriesA);
        await cache.SaveAsync(folderB, entriesB);

        var loadedA = await cache.LoadAsync(folderA);
        var loadedB = await cache.LoadAsync(folderB);

        Assert.Single(loadedA);
        Assert.Equal("Track A", loadedA[0].Title);

        Assert.Single(loadedB);
        Assert.Equal("Track B", loadedB[0].Title);
        Assert.Equal("B Artist", loadedB[0].Artist);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmpty()
    {
        var cache = new JsonLibraryCache(_tempDir);
        var cacheFile = Path.Combine(_tempDir, "library-cache.json");
        await File.WriteAllTextAsync(cacheFile, "{ not valid json !!!");

        var result = await cache.LoadAsync(@"C:\Music\AnyFolder");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveAsync_OverwritesPreviousEntries()
    {
        var cache = new JsonLibraryCache(_tempDir);
        var folder = Path.Combine(_tempDir, "OverwriteFolder");

        var first = new List<LibraryCacheEntry>
        {
            new(FilePath: @"C:\Old\old.mp3", Title: "Old", Artist: null,
                Album: null, Genre: null, Year: null,
                Duration: TimeSpan.FromMinutes(1), SampleRate: null, TrackNumber: null),
        };
        var second = new List<LibraryCacheEntry>
        {
            new(FilePath: @"C:\New\new1.mp3", Title: "New One", Artist: null,
                Album: null, Genre: null, Year: null,
                Duration: TimeSpan.FromMinutes(3), SampleRate: null, TrackNumber: null),
            new(FilePath: @"C:\New\new2.mp3", Title: "New Two", Artist: null,
                Album: null, Genre: null, Year: null,
                Duration: TimeSpan.FromMinutes(4), SampleRate: null, TrackNumber: null),
        };

        await cache.SaveAsync(folder, first);
        await cache.SaveAsync(folder, second);

        var loaded = await cache.LoadAsync(folder);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("New One", loaded[0].Title);
        Assert.Equal("New Two", loaded[1].Title);
    }
}
