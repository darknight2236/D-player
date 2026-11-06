using System.IO;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;

namespace DPlayer.ViewModels;

public sealed partial class LibraryViewModel
{
    private readonly ILibraryRootScanner _rootScanner;
    private readonly LibraryRefreshScheduler _scheduler;
    private readonly Dictionary<string, IReadOnlyList<LibraryCacheEntry>> _rootEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _scanUnavailableRoots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _watchUnavailableRoots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _cacheWriteFailedRoots =
        new(StringComparer.OrdinalIgnoreCase);

    private Task? _initializeTask;
    private Task? _monitorTask;
    private Task? _scanTask;
    private volatile bool _monitoringRequested;
    private int _refreshErrorCount;

    private IReadOnlyList<string> RootsSnapshot
    {
        get { lock (_stateGate) return _roots.ToArray(); }
    }

    private async Task InitializeCoreAsync()
    {
        var roots = await LoadRootsAsync().ConfigureAwait(false);
        var rootEntries = await LoadRootEntriesAsync(roots).ConfigureAwait(false);
        var merged = MergeRoots(roots, rootEntries);
        var tracks = merged.Values.Select(TrackFromEntry).ToArray();
        var grouping = LibraryGrouping.Build(tracks);

        await RunOnUiAsync(() =>
        {
            if (_cleaned) return;
            lock (_stateGate)
            {
                _roots = roots;
                _rootEntries.Clear();
                foreach (var pair in rootEntries)
                    _rootEntries[pair.Key] = pair.Value;
                _scanUnavailableRoots.Clear();
                _watchUnavailableRoots.Clear();
                _cacheWriteFailedRoots.Clear();
                _cachedEntries.Clear();
                foreach (var pair in merged)
                    _cachedEntries[pair.Key] = pair.Value;
                _tracksByPath.Clear();
                foreach (var track in tracks)
                    _tracksByPath[Path.GetFullPath(track.FilePath)] = track;
                _libraryVersion++;
            }

            HasLibraryRoots = roots.Count > 0;
            UnavailableRootCount = 0;
            PublishGrouping(grouping, preserveSelection: false);
            StatusText = BuildStatus();
        }).ConfigureAwait(false);
    }

    private async Task ScanInBackgroundCoreAsync()
    {
        var roots = RootsSnapshot;
        if (roots.Count == 0)
        {
            roots = await LoadRootsAsync().ConfigureAwait(false);
            if (roots.Count == 0)
            {
                await RunOnUiAsync(() =>
                {
                    if (!_cleaned) HasLibraryRoots = false;
                }).ConfigureAwait(false);
                return;
            }

            await ChangeRootsAsync(roots, LibraryRefreshReason.Startup)
                .ConfigureAwait(false);
            return;
        }

        if (_cleaned) return;
        _scheduler.ReplaceRoots(roots);
        try
        {
            await _scheduler.RefreshAllAsync(LibraryRefreshReason.Startup)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cleanup 或更新的一轮刷新接管。
        }
    }

    private async Task RefreshFromSettingsAsync(bool forceEvenIfUnchanged)
    {
        var roots = await LoadRootsAsync().ConfigureAwait(false);
        if (!RootsEqual(roots, RootsSnapshot))
        {
            await ChangeRootsAsync(roots, LibraryRefreshReason.RootsChanged)
                .ConfigureAwait(false);
            return;
        }

        if (!forceEvenIfUnchanged) return;

        if (roots.Count > 0 && _scheduler.RootEpoch == 0)
            _scheduler.ReplaceRoots(roots);

        if (_monitoringRequested)
            await ApplyWatcherRootsAsync(_scheduler.RootEpoch, roots)
                .ConfigureAwait(false);

        if (roots.Count == 0)
        {
            await RunOnUiAsync(() =>
            {
                if (!_cleaned)
                {
                    HasLibraryRoots = false;
                    StatusText = BuildStatus();
                }
            }).ConfigureAwait(false);
            return;
        }

        try
        {
            await _scheduler.RefreshAllAsync(LibraryRefreshReason.Manual)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 更新的一轮刷新接管。
        }
    }

    private async Task ChangeRootsAsync(
        IReadOnlyList<string> roots, LibraryRefreshReason reason)
    {
        var rootEntries = await UpdateRootEntriesAsync(roots).ConfigureAwait(false);
        var merged = MergeRoots(roots, rootEntries);
        var tracks = merged.Values.Select(TrackFromEntry).ToArray();
        var grouping = LibraryGrouping.Build(tracks);

        await RunOnUiAsync(() =>
        {
            if (_cleaned) return;
            int unavailableCount;
            lock (_stateGate)
            {
                _roots = roots.ToArray();
                _rootEntries.Clear();
                foreach (var pair in rootEntries)
                    _rootEntries[pair.Key] = pair.Value;

                var rootSet = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
                _scanUnavailableRoots.RemoveWhere(root => !rootSet.Contains(root));
                _watchUnavailableRoots.RemoveWhere(root => !rootSet.Contains(root));
                _cacheWriteFailedRoots.RemoveWhere(root => !rootSet.Contains(root));

                _cachedEntries.Clear();
                foreach (var pair in merged)
                    _cachedEntries[pair.Key] = pair.Value;
                _tracksByPath.Clear();
                foreach (var track in tracks)
                    _tracksByPath[Path.GetFullPath(track.FilePath)] = track;
                _libraryVersion++;
                unavailableCount = _scanUnavailableRoots.Count;
            }

            HasLibraryRoots = roots.Count > 0;
            UnavailableRootCount = unavailableCount;
            PublishGrouping(grouping, preserveSelection: true);
            StatusText = BuildStatus();
        }).ConfigureAwait(false);

        if (_cleaned) return;
        var epoch = _scheduler.ReplaceRoots(roots);
        if (_monitoringRequested)
            await ApplyWatcherRootsAsync(epoch, roots).ConfigureAwait(false);

        if (roots.Count == 0) return;
        try
        {
            await _scheduler.RefreshAllAsync(reason).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cleanup 或更新的一轮刷新接管。
        }
    }

    private async Task<IReadOnlyList<LibraryRootSnapshot>> RunRefreshBatchAsync(
        LibraryRefreshBatch batch, LibraryScanLease lease)
    {
        if (_cleaned)
            return Array.Empty<LibraryRootSnapshot>();

        await RunOnUiAsync(() =>
        {
            if (!_cleaned) IsScanning = true;
        }).ConfigureAwait(false);

        try
        {
            var results = new List<LibraryRootSnapshot>(batch.Roots.Count);
            foreach (var root in batch.Roots)
            {
                lease.Token.ThrowIfCancellationRequested();
                results.Add(await _rootScanner.ScanAsync(root, lease)
                    .ConfigureAwait(false));
            }
            lease.Token.ThrowIfCancellationRequested();

            Dictionary<string, IReadOnlyList<LibraryCacheEntry>> entryCopy;
            IReadOnlyList<string> roots;
            HashSet<string> scanFailed;
            HashSet<string> cacheFailed;
            lock (_stateGate)
            {
                entryCopy = new Dictionary<string, IReadOnlyList<LibraryCacheEntry>>(
                    _rootEntries, StringComparer.OrdinalIgnoreCase);
                roots = _roots.ToArray();
                scanFailed = new HashSet<string>(
                    _scanUnavailableRoots, StringComparer.OrdinalIgnoreCase);
                cacheFailed = new HashSet<string>(
                    _cacheWriteFailedRoots, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var snapshot in results)
            {
                var root = FindRoot(roots, snapshot.Root);
                if (root is null) continue;

                if (snapshot.IsUnavailable)
                {
                    scanFailed.Add(root);
                    cacheFailed.Remove(root);
                    continue;
                }

                scanFailed.Remove(root);
                entryCopy[root] = snapshot.Entries;
                if (snapshot.CacheWriteFailed)
                    cacheFailed.Add(root);
                else
                    cacheFailed.Remove(root);
            }

            var merged = MergeRoots(roots, entryCopy);
            var tracks = merged.Values.Select(TrackFromEntry).ToArray();
            var grouping = LibraryGrouping.Build(tracks);

            await RunOnUiAsync(() =>
            {
                if (_cleaned || !_scheduler.IsCurrent(batch)) return;

                bool dataChanged;
                lock (_stateGate)
                {
                    dataChanged = !EntriesEqual(_cachedEntries, merged);

                    _rootEntries.Clear();
                    foreach (var pair in entryCopy)
                        _rootEntries[pair.Key] = pair.Value;

                    _scanUnavailableRoots.Clear();
                    foreach (var root in scanFailed)
                        _scanUnavailableRoots.Add(root);
                    _cacheWriteFailedRoots.Clear();
                    foreach (var root in cacheFailed)
                        _cacheWriteFailedRoots.Add(root);

                    if (dataChanged)
                    {
                        _cachedEntries.Clear();
                        foreach (var pair in merged)
                            _cachedEntries[pair.Key] = pair.Value;
                        _tracksByPath.Clear();
                        foreach (var track in tracks)
                            _tracksByPath[Path.GetFullPath(track.FilePath)] = track;
                        _libraryVersion++;
                    }
                }

                UnavailableRootCount = scanFailed.Count;
                if (dataChanged)
                    PublishGrouping(grouping, preserveSelection: true);
                StatusText = BuildStatus();
            }).ConfigureAwait(false);

            return results;
        }
        finally
        {
            try
            {
                await RunOnUiAsync(() =>
                {
                    if (!_cleaned) IsScanning = false;
                }).ConfigureAwait(false);
            }
            catch
            {
                // 关闭期间 UI 队列可能已停止；扫描结果不再需要发布。
            }
        }
    }

    public Task StartMonitoringAsync()
    {
        lock (_cleanupGate)
        {
            if (_cleaned) return Task.CompletedTask;
            _monitoringRequested = true;
            if (_monitorTask is not null) return _monitorTask;
            _monitorTask = StartMonitoringCoreAsync();
            return _monitorTask;
        }
    }

    private async Task StartMonitoringCoreAsync()
    {
        _watcher.Changed += OnWatcherChanged;

        var roots = RootsSnapshot;
        if (roots.Count == 0) return;

        var epoch = _scheduler.ReplaceRoots(roots);
        await ApplyWatcherRootsAsync(epoch, roots).ConfigureAwait(false);
        if (_cleaned) return;

        try
        {
            await _scheduler.RefreshAllAsync(LibraryRefreshReason.Startup)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cleanup 或更新的一轮刷新接管。
        }
    }

    private async Task ApplyWatcherRootsAsync(
        long epoch, IReadOnlyList<string> roots)
    {
        if (!_monitoringRequested || _cleaned) return;

        IReadOnlyList<string> failed;
        try
        {
            failed = await _watcher.ReplaceRootsAsync(
                epoch, roots, _lifetimeCts.Token).ConfigureAwait(false)
                ?? Array.Empty<string>();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception) when (_cleaned)
        {
            return;
        }

        var failedSet = new HashSet<string>(
            failed, StringComparer.OrdinalIgnoreCase);
        await RunOnUiAsync(() =>
        {
            if (_cleaned) return;
            lock (_stateGate)
            {
                var rootSet = new HashSet<string>(
                    roots, StringComparer.OrdinalIgnoreCase);
                _watchUnavailableRoots.RemoveWhere(root => !rootSet.Contains(root));
                foreach (var root in roots)
                {
                    if (failedSet.Contains(root))
                        _watchUnavailableRoots.Add(root);
                    else
                        _watchUnavailableRoots.Remove(root);
                }
            }
            StatusText = BuildStatus();
        }).ConfigureAwait(false);
    }

    private void OnWatcherChanged(LibraryWatchNotification notification)
    {
        if (_cleaned) return;

        if (notification.Kind == LibraryWatchKind.Unavailable)
        {
            _syncContext.Post(_ =>
            {
                if (_cleaned) return;
                lock (_stateGate)
                    _watchUnavailableRoots.Add(notification.Root);
                StatusText = BuildStatus();
            }, null);
            return;
        }

        IReadOnlyList<string> roots;
        lock (_stateGate)
            roots = _roots.ToArray();
        if (roots.Count == 0) return;

        var affected = notification.Kind == LibraryWatchKind.Overflow ||
            notification.Path is null
                ? FileSystemLibraryWatcher.AffectedRoots(
                    roots, notification.Root, null)
                : FileSystemLibraryWatcher.AffectedRoots(
                    roots, notification.Path, notification.OldPath);
        if (affected.Count == 0) return;

        bool clearedWatchError;
        lock (_stateGate)
            clearedWatchError = _watchUnavailableRoots.Remove(notification.Root);
        if (clearedWatchError)
        {
            _syncContext.Post(_ =>
            {
                if (!_cleaned) StatusText = BuildStatus();
            }, null);
        }

        foreach (var root in affected)
            _scheduler.Invalidate(root, notification.RootEpoch);
    }

    private async Task CompleteCleanupAsync(TaskCompletionSource completion)
    {
        try
        {
            _watcher.Changed -= OnWatcherChanged;
            _watcher.Dispose();
            _lifetimeCts.Cancel();
            await _scheduler.StopAsync().ConfigureAwait(false);

            await WaitQuietAsync(_initializeTask).ConfigureAwait(false);
            await WaitQuietAsync(_monitorTask).ConfigureAwait(false);
            await WaitQuietAsync(_scanTask).ConfigureAwait(false);

            Task[] covers;
            lock (_cleanupGate)
                covers = _coverTasks.ToArray();
            foreach (var cover in covers)
                await WaitQuietAsync(cover).ConfigureAwait(false);

            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private static async Task WaitQuietAsync(Task? task)
    {
        if (task is null) return;
        try { await task.ConfigureAwait(false); }
        catch { /* shutdown drains abandoned work */ }
    }

    private Task RunOnUiAsync(Action action)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(_ =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, null);
        return completion.Task;
    }

    private void ReportRefreshError(Exception ex)
    {
        _syncContext.Post(_ =>
        {
            if (_cleaned) return;
            lock (_stateGate)
                _refreshErrorCount++;
            StatusText = BuildStatus();
        }, null);
    }

    private async Task<IReadOnlyList<string>> LoadRootsAsync()
    {
        AppSettings settings;
        try { settings = await _settings.LoadAsync().ConfigureAwait(false); }
        catch { return Array.Empty<string>(); }

        return LibraryPathNormalizer.NormalizeMany(settings.LibraryFolders);
    }

    private async Task<Dictionary<string, IReadOnlyList<LibraryCacheEntry>>>
        LoadRootEntriesAsync(IReadOnlyList<string> roots)
    {
        var result = new Dictionary<string, IReadOnlyList<LibraryCacheEntry>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            result[root] = await LoadCachedRootAsync(root).ConfigureAwait(false);
        return result;
    }

    private async Task<Dictionary<string, IReadOnlyList<LibraryCacheEntry>>>
        UpdateRootEntriesAsync(IReadOnlyList<string> roots)
    {
        Dictionary<string, IReadOnlyList<LibraryCacheEntry>> current;
        lock (_stateGate)
        {
            current = new Dictionary<string, IReadOnlyList<LibraryCacheEntry>>(
                _rootEntries, StringComparer.OrdinalIgnoreCase);
        }

        var rootSet = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        foreach (var root in current.Keys
            .Where(root => !rootSet.Contains(root)).ToList())
        {
            current.Remove(root);
        }

        foreach (var root in roots)
        {
            if (current.ContainsKey(root)) continue;
            current[root] = await LoadCachedRootAsync(root).ConfigureAwait(false);
        }
        return current;
    }

    private async Task<IReadOnlyList<LibraryCacheEntry>> LoadCachedRootAsync(
        string root)
    {
        IReadOnlyList<LibraryCacheEntry> cached;
        try
        {
            cached = await _cache.LoadAsync(root).ConfigureAwait(false)
                ?? Array.Empty<LibraryCacheEntry>();
        }
        catch
        {
            cached = Array.Empty<LibraryCacheEntry>();
        }
        return cached
            .OrderBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, LibraryCacheEntry> MergeRoots(
        IReadOnlyList<string> roots,
        IReadOnlyDictionary<string, IReadOnlyList<LibraryCacheEntry>> rootEntries)
    {
        var merged = new Dictionary<string, LibraryCacheEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!rootEntries.TryGetValue(root, out var entries)) continue;
            foreach (var entry in entries)
            {
                string path;
                try { path = Path.GetFullPath(entry.FilePath); }
                catch { continue; }
                merged.TryAdd(path, entry);
            }
        }
        return merged;
    }

    private static bool EntriesEqual(
        IReadOnlyDictionary<string, LibraryCacheEntry> left,
        IReadOnlyDictionary<string, LibraryCacheEntry> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var other) || pair.Value != other)
                return false;
        }
        return true;
    }

    private static string? FindRoot(IReadOnlyList<string> roots, string root)
    {
        foreach (var candidate in roots)
        {
            if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return null;
    }

    private static bool RootsEqual(
        IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count) return false;
        var rightSet = new HashSet<string>(right, StringComparer.OrdinalIgnoreCase);
        return left.All(rightSet.Contains);
    }
}
