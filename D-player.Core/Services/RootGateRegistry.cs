using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DPlayer.Services;

internal sealed class RootGateRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    internal int ActiveCount
    {
        get { lock (_gate) return _entries.Count; }
    }

    internal async ValueTask<Lease> EnterAsync(string key, CancellationToken ct)
    {
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }
            entry.Users++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Exit(key, entry, semaphoreHeld: false);
            throw;
        }

        return new Lease(this, key, entry);
    }

    private void Exit(string key, Entry entry, bool semaphoreHeld)
    {
        bool last;
        lock (_gate)
        {
            last = --entry.Users == 0;
            if (last) _entries.Remove(key);
        }

        if (semaphoreHeld) entry.Semaphore.Release();
        if (last) entry.Semaphore.Dispose();
    }

    internal sealed class Entry
    {
        // Users counts holders and waiters; the semaphore is disposed only at
        // Users == 0 and is never released by a canceled waiter.
        internal readonly SemaphoreSlim Semaphore = new(1, 1);
        internal int Users;
    }

    internal sealed class Lease : IDisposable
    {
        private readonly RootGateRegistry _owner;
        private readonly string _key;
        private readonly Entry _entry;
        private int _disposed;

        internal Lease(RootGateRegistry owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _owner.Exit(_key, _entry, semaphoreHeld: true);
        }
    }
}
