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

public sealed record LibraryNavigationSnapshot(
    LibraryPage Page,
    string? ArtistName,
    LibraryAlbumKey? Album,
    bool AlbumOpenedFromArtist,
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
