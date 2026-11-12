using System.IO;
using System.Linq;
using DPlayer.Models;

namespace DPlayer.Services;

public sealed class LibraryRootScanner : ILibraryRootScanner
{
    private readonly RootGateRegistry _rootGates = new();

    internal int ActiveRootGates => _rootGates.ActiveCount;

    private readonly ILibraryScannerService _scanner;
    private readonly ILibraryCache _cache;
    private readonly ITrackMetadataReader _metadata;

    public LibraryRootScanner(
        ILibraryScannerService scanner,
        ILibraryCache cache,
        ITrackMetadataReader metadata)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        _scanner = scanner;
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata;
    }

    public async Task<LibraryRootSnapshot> ScanAsync(string root, LibraryScanLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var normalized = LibraryPathNormalizer.Normalize(root)
            ?? throw new ArgumentException("Invalid library root.", nameof(root));
        using var gateLease = await _rootGates.EnterAsync(normalized, lease.Token)
            .ConfigureAwait(false);
        return await Task.Run(() => ScanLockedAsync(normalized, lease), lease.Token)
            .ConfigureAwait(false);
    }

    private async Task<LibraryRootSnapshot> ScanLockedAsync(
        string root, LibraryScanLease lease)
    {
        var ct = lease.Token;
        ct.ThrowIfCancellationRequested();

        LibraryScanResult scan;
        try
        {
            scan = _scanner.ScanFolderWithStatus(root, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return Unavailable(root);
        }
        catch (UnauthorizedAccessException)
        {
            return Unavailable(root);
        }

        if (scan.IsUnavailable)
            return Unavailable(root);

        ct.ThrowIfCancellationRequested();
        var cached = await _cache.LoadAsync(root).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var diff = _scanner.ComputeDiff(scan.Files, cached);
        var fresh = diff.AddedPaths.Count == 0
            ? Array.Empty<Track>()
            : await _scanner.ReadMetadataBatchAsync(diff.AddedPaths, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var freshByPath = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in fresh)
        {
            if (string.IsNullOrWhiteSpace(track.FilePath)) continue;
            freshByPath[Path.GetFullPath(track.FilePath)] = track;
        }

        var cachedPaths = new HashSet<string>(
            cached.Select(entry => Path.GetFullPath(entry.FilePath)),
            StringComparer.OrdinalIgnoreCase);

        var entries = new List<LibraryCacheEntry>(diff.Unchanged.Count + diff.AddedPaths.Count);
        entries.AddRange(diff.Unchanged);
        var newInvalid = new List<string>();
        foreach (var addedPath in diff.AddedPaths)
        {
            var path = Path.GetFullPath(addedPath);
            var track = freshByPath.TryGetValue(path, out var read)
                ? read
                : _metadata.CreateFallback(path);
            var entry = ToEntry(track, path);
            entries.Add(entry);
            if (IsInvalid(entry) && !cachedPaths.Contains(path))
                newInvalid.Add(path);
        }

        entries.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.FilePath, right.FilePath));

        var invalidPaths = entries.Where(IsInvalid)
            .Select(entry => entry.FilePath)
            .ToList();
        invalidPaths.Sort(StringComparer.OrdinalIgnoreCase);
        newInvalid.Sort(StringComparer.OrdinalIgnoreCase);

        var cacheWriteFailed = false;
        var cachedOrdered = cached
            .OrderBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!EntriesEqual(entries, cachedOrdered) && lease.TryEnterSave())
        {
            try
            {
                await _cache.SaveAsync(root, entries).ConfigureAwait(false);
            }
            catch (IOException)
            {
                cacheWriteFailed = true;
            }
        }

        return new LibraryRootSnapshot(root, entries.AsReadOnly(), false,
            cacheWriteFailed, newInvalid.AsReadOnly(), invalidPaths.AsReadOnly());
    }

    private static LibraryRootSnapshot Unavailable(string root) => new(
        root, Array.Empty<LibraryCacheEntry>(), IsUnavailable: true,
        CacheWriteFailed: false, Array.Empty<string>(), Array.Empty<string>());

    private static LibraryCacheEntry ToEntry(Track track, string filePath) => new(
        filePath, track.Title, track.Artist, track.Album, track.Genre, track.Year,
        track.Duration, track.SampleRate, track.TrackNumber);

    private static bool IsInvalid(LibraryCacheEntry entry) =>
        entry.Duration == TimeSpan.Zero && (entry.SampleRate is null or 0);

    private static bool EntriesEqual(
        List<LibraryCacheEntry> left, IReadOnlyList<LibraryCacheEntry> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (left[i] != right[i]) return false;
        return true;
    }
}
