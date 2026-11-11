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

    [Fact]
    public void Build_MultiDiscFolders_MergeIntoOneAlbum()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\Artist\Album\Disc 1\01.flac", "D1T1", "Artist", "Album", 1),
            T(@"C:\Music\Artist\Album\Disc 1\02.flac", "D1T2", "Artist", "Album", 2),
            T(@"C:\Music\Artist\Album\Disc 2\01.flac", "D2T1", "Artist", "Album", 1),
        });

        var album = Assert.Single(result.Albums);
        Assert.Equal("Album", album.Title);
        Assert.Equal(3, album.Tracks.Count);
        Assert.Equal(1, Assert.Single(result.Artists).AlbumCount);
    }

    [Fact]
    public void Build_MultiDisc_OrdersByDiscThenTrackNumber()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\Album\Disc 2\01.flac", "D2T1", "A", "Album", 1),
            T(@"C:\Music\Album\Disc 1\02.flac", "D1T2", "A", "Album", 2),
            T(@"C:\Music\Album\Disc 2\02.flac", "D2T2", "A", "Album", 2),
            T(@"C:\Music\Album\Disc 1\01.flac", "D1T1", "A", "Album", 1),
        });

        var album = Assert.Single(result.Albums);
        Assert.Equal(new[] { "D1T1", "D1T2", "D2T1", "D2T2" },
            album.Tracks.Select(t => t.Title));
    }

    [Theory]
    [InlineData("Disc 1", "Disc 2")]
    [InlineData("CD1", "CD2")]
    [InlineData("disc 1", "disc 2")]
    [InlineData("Disc3", "Disc4")]
    [InlineData("Disk 1", "Disk 2")]
    [InlineData("碟1", "碟2")]
    [InlineData("ディスク 1", "ディスク 2")]
    [InlineData("Disc-1", "Disc-2")]
    public void Build_DiscFolderNameVariants_MergeIntoOneAlbum(string disc1, string disc2)
    {
        var result = LibraryGrouping.Build(new[]
        {
            T($@"C:\Music\Album\{disc1}\01.flac", "T1", "A", "Album", 1),
            T($@"C:\Music\Album\{disc2}\01.flac", "T2", "A", "Album", 1),
        });

        Assert.Single(result.Albums);
    }

    [Fact]
    public void Build_DiscFoldersWithDifferentAlbumTags_StaySeparate()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\Album\Disc 1\01.flac", "T1", "A", "Album One", 1),
            T(@"C:\Music\Album\Disc 2\01.flac", "T2", "A", "Album Two", 1),
        });

        Assert.Equal(2, result.Albums.Count);
    }

    [Fact]
    public void Build_FoldersWithoutDiscNumber_StaySeparate()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\Album\Discography\01.flac", "T1", "A", "Album", 1),
            T(@"C:\Music\Album\CD Backup\01.flac", "T2", "A", "Album", 1),
            T(@"C:\Music\Album\Disc\01.flac", "T3", "A", "Album", 1),
        });

        Assert.Equal(3, result.Albums.Count);
    }

    [Fact]
    public void Build_MultiDisc_ArtistTracksFollowDiscOrder()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\Album\Disc 2\01.flac", "D2T1", "A", "Album", 1),
            T(@"C:\Music\Album\Disc 1\02.flac", "D1T2", "A", "Album", 2),
            T(@"C:\Music\Album\Disc 1\01.flac", "D1T1", "A", "Album", 1),
        });

        var artist = Assert.Single(result.Artists);
        Assert.Equal(new[] { "D1T1", "D1T2", "D2T1" },
            artist.Tracks.Select(t => t.Title));
    }

    [Fact]
    public void Build_MultiDisc_AlbumKeyMatchesEveryMemberTrack()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\Artist\Album\Disc 1\01.flac", "T1", "A", "Album", 1),
            T(@"C:\Music\Artist\Album\Disc 2\01.flac", "T2", "A", "Album", 1),
        });

        var album = Assert.Single(result.Albums);
        var key = LibraryIdentity.AlbumOf(album);
        Assert.Equal(new LibraryAlbumKey("Album", @"C:\Music\Artist\Album"), key);
        Assert.All(album.Tracks, t => Assert.Equal(key, LibraryIdentity.AlbumOf(t)));
    }

    [Fact]
    public void Build_MultiDiscUnderDifferentParents_StaySeparate()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\A1\Album\Disc 1\01.flac", "T1", "A", "Album", 1),
            T(@"C:\Music\A2\Album\Disc 1\01.flac", "T2", "A", "Album", 1),
        });

        Assert.Equal(2, result.Albums.Count);
    }

    [Fact]
    public void Build_RootTracksAndDiscSubfolder_RootTracksFirst()
    {
        var result = LibraryGrouping.Build(new[]
        {
            T(@"C:\Music\Album\Disc 2\01.flac", "D2T1", "A", "Album", 1),
            T(@"C:\Music\Album\01.flac", "Root", "A", "Album", 1),
        });

        var album = Assert.Single(result.Albums);
        Assert.Equal(new[] { "Root", "D2T1" }, album.Tracks.Select(t => t.Title));
    }
}
