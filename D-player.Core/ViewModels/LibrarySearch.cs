using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DPlayer.Models;

namespace DPlayer.ViewModels;

internal sealed record LibrarySearchSnapshot(
    long Version,
    IReadOnlyList<LibraryArtist> Artists,
    IReadOnlyList<LibraryAlbum> Albums,
    IReadOnlyList<Track> Tracks);

public sealed record LibrarySearchResults(
    IReadOnlyList<LibraryArtist> Artists,
    IReadOnlyList<LibraryAlbum> Albums,
    IReadOnlyList<Track> Tracks);

internal static class LibrarySearch
{
    internal static LibrarySearchResults Match(
        LibrarySearchSnapshot snapshot, string query, CancellationToken ct)
    {
        var needle = query.Trim();
        if (needle.Length == 0)
            return new LibrarySearchResults(Array.Empty<LibraryArtist>(),
                Array.Empty<LibraryAlbum>(), Array.Empty<Track>());

        bool Contains(string? text) => text?.Contains(
            needle, StringComparison.OrdinalIgnoreCase) == true;

        var artists = new List<LibraryArtist>();
        foreach (var artist in snapshot.Artists)
        {
            ct.ThrowIfCancellationRequested();
            if (Contains(artist.Name)) artists.Add(artist);
        }

        var albums = new List<LibraryAlbum>();
        foreach (var album in snapshot.Albums)
        {
            ct.ThrowIfCancellationRequested();
            if (Contains(album.Title) || Contains(album.Artist)) albums.Add(album);
        }

        var tracks = new List<Track>();
        foreach (var track in snapshot.Tracks)
        {
            ct.ThrowIfCancellationRequested();
            if (Contains(track.Title) || Contains(track.Artist) || Contains(track.Album))
                tracks.Add(track);
        }

        var ordered = tracks
            .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Album, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LibrarySearchResults(
            Array.AsReadOnly(artists.ToArray()),
            Array.AsReadOnly(albums.ToArray()),
            Array.AsReadOnly(ordered));
    }
}
