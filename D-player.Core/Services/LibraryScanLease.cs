using System.Threading;

namespace DPlayer.Services;

public sealed class LibraryScanLease : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationTokenRegistration _registration;
    private bool _revoked;

    public LibraryScanLease(CancellationToken parent)
        => _registration = parent.Register(Revoke);

    public CancellationToken Token => _cts.Token;

    public void Revoke()
    {
        lock (_gate) _revoked = true;
        _cts.Cancel();
    }

    public bool TryEnterSave()
    {
        lock (_gate) return !_revoked;
    }

    public void Dispose()
    {
        _registration.Dispose();
        _cts.Dispose();
    }
}
