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
    public void Build_GroupsByAlbumAndArtist_AndOrdersTracksByNumber()
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
    public void Build_SameAlbumTitleDifferentArtist_StaysSeparate()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\a.mp3", "A", "Artist A", "Greatest Hits"),
            T(@"C:\Music\b.mp3", "B", "Artist B", "Greatest Hits"),
        });

        Assert.Equal(2, result.Albums.Count);
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
