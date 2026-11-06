using System;
using System.Linq;
using System.Threading;
using DPlayer.Models;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

public sealed class LibrarySearchTests
{
    private static Track T(string path, string title, string? artist, string? album)
        => new(path, title, artist, album, null, null, 44100, null,
            TimeSpan.FromMinutes(3), 1);

    [Fact]
    public void Match_UsesTrimmedLiteralTextAcrossAllThreeGroups()
    {
        var track = new Track(@"C:\Library\Album\song.flac", "夜の歌", "Artist",
            "Album", null, null, 44100, null, TimeSpan.FromMinutes(3), 1);
        var groups = LibraryGrouping.Build(new[] { track });
        var snapshot = new LibrarySearchSnapshot(1, groups.Artists, groups.Albums,
            new[] { track });
        var result = LibrarySearch.Match(snapshot, " aRTist ", CancellationToken.None);
        Assert.Single(result.Artists);
        Assert.Single(result.Albums);
        Assert.Same(track, Assert.Single(result.Tracks));
        Assert.Empty(LibrarySearch.Match(snapshot, "  ", CancellationToken.None).Tracks);
        Assert.Empty(LibrarySearch.Match(snapshot, "Art*", CancellationToken.None).Tracks);
    }

    [Theory]
    [InlineData("夜")]
    [InlineData("NIGHT")]
    [InlineData("ht So")]
    public void Match_SupportsCjkCaseAndInternalSpaces(string query)
    {
        var track = T(@"C:\Library\Album\song.flac", "Night Song 夜の歌", "Artist", "Album");
        var snapshot = new LibrarySearchSnapshot(1, Array.Empty<LibraryArtist>(),
            Array.Empty<LibraryAlbum>(), new[] { track });

        var result = LibrarySearch.Match(snapshot, query, CancellationToken.None);

        Assert.Same(track, Assert.Single(result.Tracks));
    }

    [Fact]
    public void Match_UnknownPlaceholders_AreSearchable()
    {
        var track = T(@"C:\Library\Unknown\u.flac", "U", null, null);
        var groups = LibraryGrouping.Build(new[] { track });
        var snapshot = new LibrarySearchSnapshot(1, groups.Artists, groups.Albums,
            new[] { track });

        var result = LibrarySearch.Match(snapshot, "未知", CancellationToken.None);

        Assert.Equal(LibraryGrouping.UnknownArtist, Assert.Single(result.Artists).Name);
        Assert.Equal(LibraryGrouping.UnknownAlbum, Assert.Single(result.Albums).Title);
        Assert.Empty(result.Tracks);
    }

    [Fact]
    public void AlbumComparer_SeparatesDirectories_AndIgnoresPathCase()
    {
        var first = LibraryIdentity.AlbumOf(T(@"C:\Library\DiscA\01.flac", "Album", "A", "Same"));
        var second = LibraryIdentity.AlbumOf(T(@"C:\Library\DiscB\01.flac", "Album", "A", "Same"));
        var upper = LibraryIdentity.AlbumOf(T(@"c:\library\DISCA\01.FLAC", "Album", "A", "Same"));

        Assert.False(LibraryIdentity.AlbumComparer.Equals(first, second));
        Assert.True(LibraryIdentity.AlbumComparer.Equals(first, upper));
    }

    [Fact]
    public void AlbumOf_EmptyAlbum_UsesDisplayTitleAndEmptyDirectory()
    {
        var unknown = new LibraryAlbum("未知专辑", "未知艺术家", true, null,
            Array.Empty<Track>());
        var titled = new LibraryAlbum(" Album ", "Artist", false, null,
            Array.Empty<Track>());

        Assert.Equal(new LibraryAlbumKey(string.Empty, string.Empty),
            LibraryIdentity.AlbumOf(unknown));
        Assert.Equal(new LibraryAlbumKey("Album", string.Empty),
            LibraryIdentity.AlbumOf(titled));
    }

    [Fact]
    public void Match_MixedInputOrder_ReturnsStablePathOrder()
    {
        var snapshot = new LibrarySearchSnapshot(1, Array.Empty<LibraryArtist>(),
            Array.Empty<LibraryAlbum>(), new[]
            {
                T(@"C:\Music\b.flac", "Same", "Artist", "Album"),
                T(@"C:\Music\c.flac", "Same", "Artist", "Album"),
                T(@"C:\Music\a.flac", "Same", "Artist", "Album"),
            });

        var result = LibrarySearch.Match(snapshot, "Same", CancellationToken.None);

        Assert.Equal(new[] { @"C:\Music\a.flac", @"C:\Music\b.flac", @"C:\Music\c.flac" },
            result.Tracks.Select(t => t.FilePath));
    }

    [Fact]
    public void Match_CancelledToken_Throws()
    {
        var track = T(@"C:\Music\a.flac", "Song", "Artist", "Album");
        var snapshot = new LibrarySearchSnapshot(1, Array.Empty<LibraryArtist>(),
            Array.Empty<LibraryAlbum>(), new[] { track });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => LibrarySearch.Match(snapshot, "Song", cts.Token));
    }
}
