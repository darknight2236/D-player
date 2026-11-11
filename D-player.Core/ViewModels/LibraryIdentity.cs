using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DPlayer.Models;

namespace DPlayer.ViewModels;

public readonly record struct LibraryAlbumKey(string Album, string Directory);

public static class LibraryIdentity
{
    private static readonly Regex DiscFolderPattern = new(
        @"^(?:disc|cd|disk|碟|ディスク)[\s\-]*([0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IEqualityComparer<LibraryAlbumKey> AlbumComparer { get; } =
        new AlbumKeyComparer();

    public static LibraryAlbumKey AlbumOf(Track track)
    {
        var directory = TrackDirectory(track);
        // 多碟专辑：Disc N / CD N 等子文件夹上提一层，用父目录作专辑边界。
        if (DiscFolderPattern.IsMatch(Path.GetFileName(directory)))
            directory = Path.GetDirectoryName(directory) ?? directory;
        return new((track.Album ?? string.Empty).Trim(), directory);
    }

    /// <summary>碟序号（Disc N 的 N）；不在碟子文件夹内时为 0，仅供排序。</summary>
    public static int DiscOrderOf(Track track)
    {
        var match = DiscFolderPattern.Match(Path.GetFileName(TrackDirectory(track)));
        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
    }

    public static LibraryAlbumKey AlbumOf(LibraryAlbum album) => album.Tracks.Count > 0
        ? AlbumOf(album.Tracks[0])
        : new LibraryAlbumKey(
            album.IsUnknown ? string.Empty : album.Title.Trim(), string.Empty);

    private static string TrackDirectory(Track track) =>
        Path.GetDirectoryName(Path.GetFullPath(track.FilePath)) ?? string.Empty;

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
