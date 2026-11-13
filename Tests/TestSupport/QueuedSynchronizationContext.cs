using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DPlayer.Tests.TestSupport;

internal sealed class QueuedSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly Channel<(SendOrPostCallback Callback, object? State)> _queue =
        Channel.CreateUnbounded<(SendOrPostCallback, object?)>();
    private readonly Channel<bool> _signals = Channel.CreateUnbounded<bool>();
    private int _pending;

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (!_queue.Writer.TryWrite((d, state))) return;
        Interlocked.Increment(ref _pending);
        _signals.Writer.TryWrite(true);
    }

    public override void Send(SendOrPostCallback d, object? state)
        => throw new NotSupportedException("Tests drive posted work explicitly.");

    public async Task RunNextAsync(CancellationToken ct)
    {
        var (callback, state) = await _queue.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pending);
        callback(state);
    }

    public bool TryRunNext()
    {
        if (!_queue.Reader.TryRead(out var item)) return false;
        Interlocked.Decrement(ref _pending);
        item.Callback(item.State);
        return true;
    }

    public async Task WaitForPendingAsync(int count, CancellationToken ct)
    {
        while (Volatile.Read(ref _pending) < count)
            await _signals.Reader.ReadAsync(ct);
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _signals.Writer.TryComplete();
    }
}
