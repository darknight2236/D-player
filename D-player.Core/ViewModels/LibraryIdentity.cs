using System;
using System.Collections.Generic;
using System.IO;
using DPlayer.Models;

namespace DPlayer.ViewModels;

public readonly record struct LibraryAlbumKey(string Album, string Directory);

public static class LibraryIdentity
{
    public static IEqualityComparer<LibraryAlbumKey> AlbumComparer { get; } =
        new AlbumKeyComparer();

    public static LibraryAlbumKey AlbumOf(Track track) => new(
        (track.Album ?? string.Empty).Trim(),
        Path.GetDirectoryName(Path.GetFullPath(track.FilePath)) ?? string.Empty);

    public static LibraryAlbumKey AlbumOf(LibraryAlbum album) => album.Tracks.Count > 0
        ? AlbumOf(album.Tracks[0])
        : new LibraryAlbumKey(
            album.IsUnknown ? string.Empty : album.Title.Trim(), string.Empty);

    private sealed class AlbumKeyComparer : IEqualityComparer<LibraryAlbumKey>
    {
        public bool Equals(LibraryAlbumKey x, LibraryAlbumKey y) =>
            string.Equals(x.Album, y.Album, StringComparison.CurrentCultureIgnoreCase) &&
            string.Equals(x.Directory, y.Directory, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(LibraryAlbumKey obj) => HashCode.Combine(
            StringComparer.CurrentCultureIgnoreCase.GetHashCode(obj.Album),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Directory));
    }
}
