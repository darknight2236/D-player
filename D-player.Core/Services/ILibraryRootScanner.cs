using DPlayer.Models;

namespace DPlayer.Services;

public sealed record LibraryRootSnapshot(
    string Root,
    IReadOnlyList<LibraryCacheEntry> Entries,
    bool IsUnavailable,
    bool CacheWriteFailed,
    IReadOnlyList<string> NewInvalidPaths,
    IReadOnlyList<string> InvalidPaths);

public interface ILibraryRootScanner
{
    Task<LibraryRootSnapshot> ScanAsync(string root, LibraryScanLease lease);
}
