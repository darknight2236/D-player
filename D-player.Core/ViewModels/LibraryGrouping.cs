using System.IO;
using DPlayer.Models;

namespace DPlayer.ViewModels;

internal sealed record LibraryGroupingResult(
    IReadOnlyList<LibraryArtist> Artists,
    IReadOnlyList<LibraryAlbum> Albums);

internal static class LibraryGrouping
{
    internal const string UnknownArtist = "未知艺术家";
    internal const string UnknownAlbum = "未知专辑";

    private readonly record struct AlbumKey(string Album, string Artist);

    private sealed class AlbumKeyComparer : IEqualityComparer<AlbumKey>
    {
        public bool Equals(AlbumKey x, AlbumKey y) =>
            string.Equals(x.Album, y.Album, StringComparison.CurrentCultureIgnoreCase) &&
            string.Equals(x.Artist, y.Artist, StringComparison.CurrentCultureIgnoreCase);

        public int GetHashCode(AlbumKey obj) => HashCode.Combine(
            StringComparer.CurrentCultureIgnoreCase.GetHashCode(obj.Album),
            StringComparer.CurrentCultureIgnoreCase.GetHashCode(obj.Artist));
    }

    internal static LibraryGroupingResult Build(IEnumerable<Track> tracks)
    {
        var unique = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in tracks)
        {
            if (string.IsNullOrWhiteSpace(track.FilePath)) continue;

            string fullPath;
            try { fullPath = Path.GetFullPath(track.FilePath); }
            catch { continue; }

            // 同一路径在多个根目录重复出现时保留先到者，保证结果与输入顺序确定。
            unique.TryAdd(fullPath, track);
        }

        var buckets = new Dictionary<AlbumKey, List<Track>>(new AlbumKeyComparer());
        foreach (var track in unique.Values)
        {
            var key = new AlbumKey(Normalize(track.Album), Normalize(track.Artist));
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<Track>();
            list.Add(track);
        }

        var albums = buckets
            .Select(kv =>
            {
                var displayAlbum = kv.Value
                    .Select(t => Normalize(t.Album))
                    .FirstOrDefault(s => s.Length > 0) ?? UnknownAlbum;
                var displayArtist = kv.Value
                    .Select(t => Normalize(t.Artist))
                    .FirstOrDefault(s => s.Length > 0) ?? UnknownArtist;
                var year = kv.Value.Select(t => t.Year).FirstOrDefault(y => y is > 0);
                var albumTracks = kv.Value
                    .OrderBy(t => t.TrackNumber ?? int.MaxValue)
                    .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new LibraryAlbum(
                    displayAlbum,
                    displayArtist,
                    string.IsNullOrEmpty(kv.Key.Album) || string.IsNullOrEmpty(kv.Key.Artist),
                    year,
                    albumTracks);
            })
            .OrderBy(a => a.IsUnknown)
            .ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var artists = albums
            .GroupBy(a => a.Artist, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new LibraryArtist(
                g.Key,
                string.Equals(g.Key, UnknownArtist, StringComparison.Ordinal),
                g.OrderBy(a => a.IsUnknown)
                    .ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                g.SelectMany(a => a.Tracks)
                    .OrderBy(t => Normalize(t.Album), StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(t => t.TrackNumber ?? int.MaxValue)
                    .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()))
            .OrderBy(a => a.IsUnknown)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new LibraryGroupingResult(artists, albums);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
