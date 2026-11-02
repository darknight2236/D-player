using System;
using System.Linq;
using DPlayer.Models;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

public sealed class LibraryGroupingTests
{
    private static Track T(
        string path, string title, string? artist, string? album, int? number = null)
        => new(path, title, artist, album, null, null, null, null,
            TimeSpan.FromMinutes(3), number);

    [Fact]
    public void Build_GroupsByAlbumFolder_AndOrdersTracksByNumber()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\a1.mp3", "A1", "Artist A", "Album X", 2),
            T(@"C:\Music\a2.mp3", "A2", "Artist A", "Album X", 1),
            T(@"C:\Music\b1.mp3", "B1", "Artist B", "Album Y", 1),
        });

        Assert.Equal(2, result.Artists.Count);
        Assert.Equal(2, result.Albums.Count);
        var album = result.Albums.Single(a => a.Title == "Album X");
        Assert.Equal(new[] { "A2", "A1" }, album.Tracks.Select(t => t.Title));
        Assert.Equal(2, result.Artists.Single(a => a.Name == "Artist A").TrackCount);
    }

    [Fact]
    public void Build_UnknownTags_UseFallbackNames_AndSortLast()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\u.mp3", "U", null, null),
            T(@"C:\Music\a.mp3", "A", "Artist A", "Album X"),
        });

        Assert.Equal("未知艺术家", result.Artists[^1].Name);
        Assert.Equal("未知专辑", result.Albums[^1].Title);
    }

    [Fact]
    public void Build_SameAlbumTitleInDifferentFolders_StaysSeparate()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\ArtistA\a.mp3", "A", "Artist A", "Greatest Hits"),
            T(@"C:\Music\ArtistB\b.mp3", "B", "Artist B", "Greatest Hits"),
        });

        Assert.Equal(2, result.Albums.Count);
        Assert.All(result.Albums, album => Assert.Equal("Greatest Hits", album.Title));
    }

    /// <summary>
    /// 合辑：同一文件夹、同一专辑名、逐曲 Artist 不同，必须合并成一张专辑；
    /// 每个贡献艺术家仍要有自己的艺术家页，并在这张专辑下看到它。
    /// 这正是 AlbumArtist 缺失时被 (Album, Artist) 键拆开的案例。
    /// </summary>
    [Fact]
    public void Build_CompilationInOneFolder_MergesAndKeepsContributingArtists()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\Artist\Compilation\01.flac", "T1", "Artist A", "Compilation", 1),
            T(@"C:\Music\Artist\Compilation\02.flac", "T2", "Artist B", "Compilation", 2),
            T(@"C:\Music\Artist\Compilation\03.flac", "T3", "Artist C", "Compilation", 3),
        });

        var album = Assert.Single(result.Albums);
        Assert.Equal("Compilation", album.Title);
        Assert.Equal("多位艺术家", album.Artist);
        Assert.Equal(new[] { "T1", "T2", "T3" }, album.Tracks.Select(t => t.Title));

        Assert.Equal(3, result.Artists.Count);
        foreach (var artist in result.Artists)
        {
            Assert.Equal(1, artist.AlbumCount);
            Assert.Equal(1, artist.TrackCount);
            Assert.Same(album, Assert.Single(artist.Albums));
        }
    }

    [Fact]
    public void Build_DuplicatePaths_AreCountedOnce()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\a.mp3", "A", "Artist", "Album"),
            T(@"c:\music\A.MP3", "A duplicate", "Artist", "Album"),
        });

        Assert.Single(result.Albums);
        Assert.Single(result.Albums[0].Tracks);
        Assert.Equal("A", result.Albums[0].Tracks[0].Title);
    }

    [Fact]
    public void Build_EmptyFilePath_IsIgnored()
    {
        var result = LibraryGrouping.Build(new[] { T("", "bad", null, null) });

        Assert.Empty(result.Albums);
        Assert.Empty(result.Artists);
    }
}
