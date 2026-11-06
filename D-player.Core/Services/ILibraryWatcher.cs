namespace DPlayer.Services;

public enum LibraryWatchKind
{
    Invalidated,
    Overflow,
    Unavailable,
}

public sealed record LibraryWatchNotification(
    long RootEpoch,
    string Root,
    string? Path,
    string? OldPath,
    LibraryWatchKind Kind);

public interface ILibraryWatcher : IDisposable
{
    event Action<LibraryWatchNotification>? Changed;

    Task<IReadOnlyList<string>> ReplaceRootsAsync(
        long rootEpoch, IReadOnlyList<string> roots, CancellationToken ct);
}
