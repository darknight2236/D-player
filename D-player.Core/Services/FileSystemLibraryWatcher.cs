using System.IO;

namespace DPlayer.Services;

public sealed class FileSystemLibraryWatcher : ILibraryWatcher
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _desiredRoots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _replaceGate = new(1, 1);
    private long _epoch;
    private bool _disposed;

    internal bool IsMonitoring
    {
        get { lock (_gate) return _watchers.Count > 0; }
    }

    public event Action<LibraryWatchNotification>? Changed;

    public async Task<IReadOnlyList<string>> ReplaceRootsAsync(
        long rootEpoch, IReadOnlyList<string> roots, CancellationToken ct)
    {
        var normalized = LibraryPathNormalizer.NormalizeMany(roots);
        await _replaceGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var toCreate = new List<string>();
            lock (_gate)
            {
                if (_disposed) return normalized;

                _desiredRoots.Clear();
                foreach (var root in normalized)
                    _desiredRoots.Add(root);

                if (rootEpoch != _epoch)
                {
                    foreach (var watcher in _watchers.Values)
                        watcher.Dispose();
                    _watchers.Clear();
                    _epoch = rootEpoch;
                }
                else
                {
                    var removed = _watchers.Keys
                        .Where(root => !_desiredRoots.Contains(root))
                        .ToList();
                    foreach (var root in removed)
                    {
                        _watchers[root].Dispose();
                        _watchers.Remove(root);
                    }
                }

                foreach (var root in normalized)
                    if (!_watchers.ContainsKey(root))
                        toCreate.Add(root);
            }

            var failed = new List<string>();
            foreach (var root in toCreate)
            {
                ct.ThrowIfCancellationRequested();
                var watcher = await Task.Run(
                    () => Create(root, rootEpoch, ct), CancellationToken.None)
                    .ConfigureAwait(false);
                if (watcher is null)
                    failed.Add(root);
            }
            return failed;
        }
        finally
        {
            _replaceGate.Release();
        }
    }

    public void Dispose()
    {
        List<FileSystemWatcher> toDispose;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            toDispose = _watchers.Values.ToList();
            _watchers.Clear();
            _desiredRoots.Clear();
        }

        foreach (var watcher in toDispose)
            watcher.Dispose();
        _replaceGate.Dispose();
    }

    private FileSystemWatcher? Create(
        string root, long rootEpoch, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                Filter = "*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = false,
            };
            watcher.Created += (_, args) =>
                Raise(rootEpoch, root, args.FullPath, null, LibraryWatchKind.Invalidated);
            watcher.Deleted += (_, args) =>
                Raise(rootEpoch, root, args.FullPath, null, LibraryWatchKind.Invalidated);
            watcher.Renamed += (_, args) =>
                Raise(rootEpoch, root, args.FullPath, args.OldFullPath,
                    LibraryWatchKind.Invalidated);
            watcher.Error += (_, args) =>
                OnError(rootEpoch, root, args.GetException());

            lock (_gate)
            {
                if (_disposed || _epoch != rootEpoch || !_desiredRoots.Contains(root))
                {
                    watcher.Dispose();
                    return null;
                }
                _watchers[root] = watcher;
            }

            watcher.EnableRaisingEvents = true;

            if (ct.IsCancellationRequested)
            {
                RemoveWatcher(root, watcher);
                watcher.Dispose();
                return null;
            }

            lock (_gate)
            {
                if (_disposed || _epoch != rootEpoch || !_desiredRoots.Contains(root))
                {
                    RemoveWatcherLocked(root, watcher);
                    watcher.Dispose();
                    return null;
                }
            }
            return watcher;
        }
        catch
        {
            RemoveWatcher(root, watcher);
            watcher?.Dispose();
            return null;
        }
    }

    private void OnError(long rootEpoch, string root, Exception error)
    {
        var kind = ClassifyError(error);
        if (kind == LibraryWatchKind.Unavailable)
        {
            FileSystemWatcher? watcher = null;
            lock (_gate)
            {
                if (_epoch == rootEpoch && !_disposed &&
                    _watchers.TryGetValue(root, out watcher))
                    _watchers.Remove(root);
            }
            watcher?.Dispose();
        }
        Raise(rootEpoch, root, null, null, kind);
    }

    private void Raise(
        long rootEpoch, string root, string? path, string? oldPath,
        LibraryWatchKind kind)
    {
        lock (_gate)
        {
            if (_disposed || _epoch != rootEpoch) return;
        }

        try
        {
            Changed?.Invoke(new LibraryWatchNotification(
                rootEpoch, root, path, oldPath, kind));
        }
        catch
        {
            // A subscriber must not take down the watcher callback thread.
        }
    }

    private void RemoveWatcher(string root, FileSystemWatcher? watcher)
    {
        if (watcher is null) return;
        lock (_gate)
            RemoveWatcherLocked(root, watcher);
    }

    private void RemoveWatcherLocked(string root, FileSystemWatcher watcher)
    {
        if (_watchers.TryGetValue(root, out var current) &&
            ReferenceEquals(current, watcher))
        {
            _watchers.Remove(root);
        }
    }

    internal static LibraryWatchKind ClassifyError(Exception error) =>
        error is InternalBufferOverflowException
            ? LibraryWatchKind.Overflow
            : LibraryWatchKind.Unavailable;

    internal static IReadOnlyList<string> AffectedRoots(
        IReadOnlyList<string> roots, string path, string? oldPath)
    {
        var affected = new List<string>();
        foreach (var root in roots)
        {
            if (IsRelated(root, path) ||
                (oldPath is not null && IsRelated(root, oldPath)))
            {
                affected.Add(root);
            }
        }
        return affected;
    }

    private static bool IsRelated(string root, string path)
    {
        var normalizedRoot = LibraryPathNormalizer.Normalize(root);
        var normalizedPath = LibraryPathNormalizer.Normalize(path);
        if (normalizedRoot is null || normalizedPath is null) return false;

        return IsSameOrChild(normalizedRoot, normalizedPath) ||
            IsSameOrChild(normalizedPath, normalizedRoot);
    }

    private static bool IsSameOrChild(string parent, string child)
    {
        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = parent.EndsWith(Path.DirectorySeparatorChar) ||
            parent.EndsWith(Path.AltDirectorySeparatorChar)
                ? parent
                : parent + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
