using CommunityToolkit.Mvvm.ComponentModel;
using DPlayer.Models;

namespace DPlayer.ViewModels;

public enum LibraryPage
{
    Artists,
    Albums,
    ArtistDetail,
    AlbumDetail,
    Search,
}

/// <summary>
/// 音乐库导航快照: 四个互斥的导航分支, 由类型区分(Search 页会回退到进入搜索前的快照)。
/// 快照同时用于关闭窗口时的持久化(页面名 / 艺术家名 / 专辑首曲路径)。
/// </summary>
public union LibraryNavigationSnapshot(
    ArtistsNavigation,
    AlbumsNavigation,
    ArtistDetailNavigation,
    AlbumDetailNavigation);

public sealed record class ArtistsNavigation;

public sealed record class AlbumsNavigation;

public sealed record class ArtistDetailNavigation(string ArtistName);

public sealed record class AlbumDetailNavigation(
    string? ArtistName,
    LibraryAlbumKey Album,
    bool OpenedFromArtist,
    string? AlbumTrackPath);

public sealed class LibraryArtist
{
    public LibraryArtist(
        string name,
        bool isUnknown,
        IReadOnlyList<LibraryAlbum> albums,
        IReadOnlyList<Track> tracks)
    {
        Name = name;
        IsUnknown = isUnknown;
        Albums = albums;
        Tracks = tracks;
    }

    public string Name { get; }
    public bool IsUnknown { get; }
    public IReadOnlyList<LibraryAlbum> Albums { get; }
    public IReadOnlyList<Track> Tracks { get; }
    public int AlbumCount => Albums.Count;
    public int TrackCount => Tracks.Count;

    /// <summary>艺术家列表第二行：N 张专辑 · M 首。</summary>
    public string Summary => $"{AlbumCount} 张专辑 · {TrackCount} 首";
}

public sealed partial class LibraryAlbum : ObservableObject
{
    public LibraryAlbum(
        string title,
        string artist,
        bool isUnknown,
        int? year,
        IReadOnlyList<Track> tracks)
    {
        Title = title;
        Artist = artist;
        IsUnknown = isUnknown;
        Year = year;
        Tracks = tracks;
    }

    public string Title { get; }
    public string Artist { get; }
    public bool IsUnknown { get; }
    public int? Year { get; }
    public IReadOnlyList<Track> Tracks { get; }

    public string Subtitle => Year is { } year ? $"{Artist} · {year}" : Artist;

    [ObservableProperty]
    private byte[]? _coverArt;

    [ObservableProperty]
    private bool _coverAttempted;
}
