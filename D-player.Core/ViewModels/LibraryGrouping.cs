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
    internal const string VariousArtists = "多位艺术家";

    private sealed class ArtistBucket
    {
        public ArtistBucket(string name, bool isUnknown)
        {
            Name = name;
            IsUnknown = isUnknown;
        }

        public string Name { get; }
        public bool IsUnknown { get; }
        public List<Track> Tracks { get; } = new();
        public HashSet<LibraryAlbum> Albums { get; } = new();
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

        var buckets = new Dictionary<LibraryAlbumKey, List<Track>>(
            LibraryIdentity.AlbumComparer);
        foreach (var (_, track) in unique)
        {
            // 专辑身份 = 专辑名 + 所在文件夹：没有共同 AlbumArtist 的合辑
            // 仍会被逐曲 Artist 拆开，而文件夹是本地专辑最可靠的边界。
            var key = LibraryIdentity.AlbumOf(track);
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

                var artistNames = kv.Value
                    .Select(t => Normalize(t.Artist))
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var displayArtist = artistNames.Count switch
                {
                    0 => UnknownArtist,
                    1 => artistNames[0],
                    _ => VariousArtists,
                };

                var year = kv.Value.Select(t => t.Year).FirstOrDefault(y => y is > 0);
                var albumTracks = kv.Value
                    .OrderBy(t => LibraryIdentity.DiscOrderOf(t))
                    .ThenBy(t => t.TrackNumber ?? int.MaxValue)
                    .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new LibraryAlbum(
                    displayAlbum,
                    displayArtist,
                    string.IsNullOrEmpty(kv.Key.Album),
                    year,
                    albumTracks);
            })
            .OrderBy(a => a.IsUnknown)
            .ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // 艺术家列表按逐曲 Artist 建，而不是按专辑的 display artist 建：
        // 合辑的每一位贡献者都要有自己的艺术家页，并能看到这张合辑。
        var artistBuckets = new Dictionary<string, ArtistBucket>(
            StringComparer.CurrentCultureIgnoreCase);
        foreach (var album in albums)
        {
            foreach (var track in album.Tracks)
            {
                var artistKey = Normalize(track.Artist);
                if (!artistBuckets.TryGetValue(artistKey, out var bucket))
                {
                    var name = artistKey.Length == 0
                        ? UnknownArtist
                        : track.Artist!.Trim();
                    bucket = new ArtistBucket(name, artistKey.Length == 0);
                    artistBuckets[artistKey] = bucket;
                }

                bucket.Tracks.Add(track);
                bucket.Albums.Add(album);
            }
        }

        var artists = artistBuckets.Values
            .Select(b => new LibraryArtist(
                b.Name,
                b.IsUnknown,
                b.Albums
                    .OrderBy(a => a.IsUnknown)
                    .ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(a => a.Artist, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                b.Tracks
                    .OrderBy(t => Normalize(t.Album), StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(t => LibraryIdentity.DiscOrderOf(t))
                    .ThenBy(t => t.TrackNumber ?? int.MaxValue)
                    .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(a => a.IsUnknown)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new LibraryGroupingResult(artists, albums);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
