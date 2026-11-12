using System.Linq;
using System.Threading.Channels;

namespace DPlayer.Services;

internal enum LibraryRefreshReason
{
    Startup,
    Manual,
    RootsChanged,
    Watch,
    Retry,
}

internal sealed record LibraryRefreshBatch(
    long RootEpoch,
    long Generation,
    LibraryRefreshReason Reason,
    IReadOnlyList<string> Roots);

internal sealed class LibraryRefreshScheduler : IAsyncDisposable
{
    private static readonly TimeSpan QuietWindow = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SecondRetryDelay = TimeSpan.FromSeconds(3);

    private readonly TimeProvider _clock;
    private readonly Func<LibraryRefreshBatch, LibraryScanLease,
        Task<IReadOnlyList<LibraryRootSnapshot>>> _execute;
    private readonly Action<Exception> _reportError;
    private readonly Lock _gate = new();
    private readonly Channel<bool> _wake = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Dictionary<string, PendingRoot> _pending =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RetryCycle> _retries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _roots = new();
    private readonly Dictionary<string, int> _rootOrder =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Task _worker;

    private ITimer? _timer;
    private long _rootEpoch;
    private long _scanGeneration;
    private FullRefreshRequest? _pendingFull;
    private CancellationTokenSource? _activeCts;
    private TaskCompletionSource? _stopCompletion;
    private Task? _stopTask;
    private bool _stopped;
    private bool _disposed;

    internal LibraryRefreshScheduler(
        TimeProvider clock,
        Func<LibraryRefreshBatch, LibraryScanLease,
            Task<IReadOnlyList<LibraryRootSnapshot>>> execute,
        Action<Exception> reportError)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        ArgumentNullException.ThrowIfNull(reportError);
        _reportError = reportError;
        _worker = Task.Run(RunAsync);
    }

    internal long RootEpoch
    {
        get { lock (_gate) return _rootEpoch; }
    }

    internal long ReplaceRoots(IReadOnlyList<string> roots)
    {
        var normalized = LibraryPathNormalizer.NormalizeMany(roots);
        FullRefreshRequest? canceledPending;
        CancellationTokenSource? active;
        long epoch;
        lock (_gate)
        {
            if (_stopped) return _rootEpoch;

            _rootEpoch++;
            epoch = _rootEpoch;
            _roots.Clear();
            _roots.AddRange(normalized);
            _rootOrder.Clear();
            for (var i = 0; i < _roots.Count; i++)
                _rootOrder[_roots[i]] = i;

            _pending.Clear();
            _retries.Clear();
            canceledPending = _pendingFull;
            _pendingFull = null;
            active = _activeCts;
            ScheduleTimerLocked();
        }

        CancelAndDispose(canceledPending);
        CancelQuietly(active);
        _wake.Writer.TryWrite(true);
        return epoch;
    }

    internal void Invalidate(string root, long rootEpoch)
    {
        var normalized = LibraryPathNormalizer.Normalize(root);
        if (normalized is null) return;

        lock (_gate)
        {
            if (_stopped || rootEpoch != _rootEpoch) return;
            if (!_rootOrder.TryGetValue(normalized, out var index)) return;

            var configured = _roots[index];
            var now = _clock.GetUtcNow();
            if (_pending.TryGetValue(configured, out var pending))
            {
                pending.Last = now;
            }
            else
            {
                pending = new PendingRoot(now, now);
                _pending[configured] = pending;
            }

            var quietDue = pending.Last + QuietWindow;
            var maximumDue = pending.First + MaximumWait;
            pending.Due = quietDue <= maximumDue ? quietDue : maximumDue;
            ScheduleTimerLocked();
        }
    }

    internal Task RefreshAllAsync(LibraryRefreshReason reason)
    {
        FullRefreshRequest? canceledPending;
        CancellationTokenSource? active;
        FullRefreshRequest? request = null;
        lock (_gate)
        {
            if (_stopped) return Task.CompletedTask;

            _scanGeneration++;
            _retries.Clear();
            canceledPending = _pendingFull;
            _pendingFull = null;
            active = _activeCts;

            if (_roots.Count > 0)
            {
                request = new FullRefreshRequest(
                    _rootEpoch, _scanGeneration, reason, _roots.ToArray())
                {
                    Cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token),
                };
                _pendingFull = request;
            }
            ScheduleTimerLocked();
        }

        CancelAndDispose(canceledPending);
        CancelQuietly(active);
        _wake.Writer.TryWrite(true);
        return request?.Completion.Task ?? Task.CompletedTask;
    }

    internal bool IsCurrent(LibraryRefreshBatch batch)
    {
        lock (_gate)
            return !_stopped && batch.RootEpoch == _rootEpoch &&
                batch.Generation == _scanGeneration;
    }

    internal Task StopAsync()
    {
        TaskCompletionSource completion;
        FullRefreshRequest? pending;
        CancellationTokenSource? active;
        lock (_gate)
        {
            if (_stopTask is not null) return _stopTask;

            _stopped = true;
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _stopCompletion = completion;
            _stopTask = completion.Task;
            pending = _pendingFull;
            _pendingFull = null;
            active = _activeCts;
            _pending.Clear();
            _retries.Clear();
            ScheduleTimerLocked();
        }

        CancelQuietly(active);
        CancelAndDispose(pending);
        _lifetimeCts.Cancel();
        _wake.Writer.TryWrite(true);
        return _stopTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer?.Dispose();
        _timer = null;
        _wake.Writer.TryComplete();
        _lifetimeCts.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await _wake.Reader.ReadAsync().ConfigureAwait(false);
                while (true)
                {
                    WorkItem? work;
                    lock (_gate)
                    {
                        if (_stopped) break;
                        work = TakeWorkLocked();
                        if (work is null)
                        {
                            ScheduleTimerLocked();
                            break;
                        }
                    }
                    await ExecuteWorkAsync(work).ConfigureAwait(false);
                }
                lock (_gate)
                {
                    if (_stopped) break;
                }
            }
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
        finally
        {
            TaskCompletionSource? stop;
            lock (_gate) stop = _stopCompletion;
            stop?.TrySetResult();
        }
    }

    private WorkItem? TakeWorkLocked()
    {
        if (_pendingFull is { } full)
        {
            _pendingFull = null;
            var batch = new LibraryRefreshBatch(
                full.RootEpoch, full.Generation, full.Reason, full.Roots);
            _activeCts = full.Cts;
            return new WorkItem(batch, full.Cts, full, isRetry: false);
        }

        var now = _clock.GetUtcNow();
        var watchRoots = _pending
            .Where(pair => pair.Value.Due <= now)
            .OrderBy(pair => _rootOrder.TryGetValue(pair.Key, out var index)
                ? index : int.MaxValue)
            .Select(pair => pair.Key)
            .ToList();
        if (watchRoots.Count > 0)
        {
            foreach (var root in watchRoots)
                _pending.Remove(root);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _activeCts = cts;
            return new WorkItem(
                new LibraryRefreshBatch(
                    _rootEpoch, _scanGeneration, LibraryRefreshReason.Watch, watchRoots),
                cts, null, isRetry: false);
        }

        var retryRoots = _retries
            .Where(pair => pair.Value.Due <= now &&
                pair.Value.RootEpoch == _rootEpoch &&
                pair.Value.Generation == _scanGeneration)
            .OrderBy(pair => _rootOrder.TryGetValue(pair.Key, out var index)
                ? index : int.MaxValue)
            .Select(pair => pair.Key)
            .ToList();
        if (retryRoots.Count > 0)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _activeCts = cts;
            return new WorkItem(
                new LibraryRefreshBatch(
                    _rootEpoch, _scanGeneration, LibraryRefreshReason.Retry, retryRoots),
                cts, null, isRetry: true);
        }

        return null;
    }

    private async Task ExecuteWorkAsync(WorkItem work)
    {
        try
        {
            using var lease = new LibraryScanLease(work.Cts.Token);
            IReadOnlyList<LibraryRootSnapshot> results;
            try
            {
                results = await _execute(work.Batch, lease).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (work.Cts.IsCancellationRequested)
            {
                work.Full?.Completion.TrySetCanceled();
                return;
            }
            catch (Exception ex)
            {
                ReportError(ex);
                if (work.Full is not null)
                    work.Full.Completion.TrySetException(ex);
                else if (work.IsRetry)
                    DropRetryCycles(work.Batch.Roots);
                return;
            }

            if (!IsCurrent(work.Batch))
            {
                work.Full?.Completion.TrySetCanceled();
                return;
            }

            if (work.IsRetry)
                UpdateRetryCycles(work.Batch, results);
            else
                RegisterRetryCycles(work.Batch, results);

            work.Full?.Completion.TrySetResult();
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeCts, work.Cts))
                    _activeCts = null;
            }
            work.Cts.Dispose();
        }
    }

    private void RegisterRetryCycles(
        LibraryRefreshBatch batch, IReadOnlyList<LibraryRootSnapshot> results)
    {
        lock (_gate)
        {
            if (_stopped || batch.RootEpoch != _rootEpoch ||
                batch.Generation != _scanGeneration)
                return;

            var now = _clock.GetUtcNow();
            foreach (var snapshot in results)
            {
                var root = ResolveConfiguredRoot(snapshot.Root);
                if (root is null) continue;

                if (_retries.TryGetValue(root, out var existing))
                {
                    existing.Paths.IntersectWith(snapshot.InvalidPaths);
                    foreach (var path in snapshot.NewInvalidPaths)
                        existing.Paths.Add(path);
                    if (existing.Paths.Count == 0)
                        _retries.Remove(root);
                }
                else if (snapshot.NewInvalidPaths.Count > 0)
                {
                    var cycle = new RetryCycle
                    {
                        RootEpoch = batch.RootEpoch,
                        Generation = batch.Generation,
                        First = now,
                        Due = now + FirstRetryDelay,
                    };
                    foreach (var path in snapshot.NewInvalidPaths)
                        cycle.Paths.Add(path);
                    _retries[root] = cycle;
                }
            }
            ScheduleTimerLocked();
        }
    }

    private void UpdateRetryCycles(
        LibraryRefreshBatch batch, IReadOnlyList<LibraryRootSnapshot> results)
    {
        lock (_gate)
        {
            if (_stopped || batch.RootEpoch != _rootEpoch ||
                batch.Generation != _scanGeneration)
                return;

            foreach (var snapshot in results)
            {
                var root = ResolveConfiguredRoot(snapshot.Root);
                if (root is null || !_retries.TryGetValue(root, out var cycle))
                    continue;

                cycle.Paths.IntersectWith(snapshot.InvalidPaths);
                cycle.Attempts++;
                if (cycle.Paths.Count == 0 || cycle.Attempts >= 2)
                    _retries.Remove(root);
                else
                    cycle.Due = cycle.First + SecondRetryDelay;
            }
            ScheduleTimerLocked();
        }
    }

    private void DropRetryCycles(IReadOnlyList<string> roots)
    {
        lock (_gate)
        {
            foreach (var root in roots)
                _retries.Remove(root);
            ScheduleTimerLocked();
        }
    }

    private string? ResolveConfiguredRoot(string root)
    {
        var normalized = LibraryPathNormalizer.Normalize(root);
        if (normalized is null || !_rootOrder.TryGetValue(normalized, out var index))
            return null;
        return _roots[index];
    }

    private DateTimeOffset? NextDueLocked()
    {
        DateTimeOffset? due = null;
        foreach (var pending in _pending.Values)
        {
            if (due is null || pending.Due < due.Value)
                due = pending.Due;
        }
        foreach (var cycle in _retries.Values)
        {
            if (cycle.RootEpoch != _rootEpoch || cycle.Generation != _scanGeneration)
                continue;
            if (due is null || cycle.Due < due.Value)
                due = cycle.Due;
        }
        return due;
    }

    private void ScheduleTimerLocked()
    {
        _timer ??= _clock.CreateTimer(
            static state => ((LibraryRefreshScheduler)state!).OnTimer(),
            this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        if (_stopped)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var due = NextDueLocked();
        if (due is null)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var delay = due.Value - _clock.GetUtcNow();
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void OnTimer() => _wake.Writer.TryWrite(true);

    private void ReportError(Exception ex)
    {
        try { _reportError(ex); }
        catch { /* the reporter must never take down the worker */ }
    }

    private static void CancelQuietly(CancellationTokenSource? cts)
    {
        if (cts is null) return;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private static void CancelAndDispose(FullRefreshRequest? request)
    {
        if (request is null) return;
        CancelQuietly(request.Cts);
        request.Completion.TrySetCanceled();
        request.Cts.Dispose();
    }

    private sealed class PendingRoot(DateTimeOffset first, DateTimeOffset last)
    {
        public DateTimeOffset First { get; } = first;
        public DateTimeOffset Last { get; set; } = last;
        public DateTimeOffset Due { get; set; } = first;
    }

    private sealed class RetryCycle
    {
        public required long RootEpoch { get; init; }
        public required long Generation { get; init; }
        public required DateTimeOffset First { get; init; }
        public HashSet<string> Paths { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public int Attempts { get; set; }
        public DateTimeOffset Due { get; set; }
    }

    private sealed class FullRefreshRequest(
        long rootEpoch, long generation, LibraryRefreshReason reason,
        IReadOnlyList<string> roots)
    {
        public long RootEpoch { get; } = rootEpoch;
        public long Generation { get; } = generation;
        public LibraryRefreshReason Reason { get; } = reason;
        public IReadOnlyList<string> Roots { get; } = roots;
        public required CancellationTokenSource Cts { get; init; }
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class WorkItem(
        LibraryRefreshBatch batch, CancellationTokenSource cts,
        FullRefreshRequest? full, bool isRetry)
    {
        public LibraryRefreshBatch Batch { get; } = batch;
        public CancellationTokenSource Cts { get; } = cts;
        public FullRefreshRequest? Full { get; } = full;
        public bool IsRetry { get; } = isRetry;
    }
}
