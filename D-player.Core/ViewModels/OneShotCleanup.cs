using System.Threading.Tasks;

namespace DPlayer.ViewModels;

internal sealed class OneShotCleanup
{
    private readonly object _gate = new();
    private Task? _task;

    internal Task RunAsync(
        Func<Task> core,
        Action? onFirst = null,
        bool continueOnCapturedContext = false)
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_task is not null) return _task;
            onFirst?.Invoke();
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _task = completion.Task;
        }
        _ = CompleteAsync(core, completion, continueOnCapturedContext);
        return completion.Task;
    }

    private static async Task CompleteAsync(
        Func<Task> core,
        TaskCompletionSource completion,
        bool continueOnCapturedContext)
    {
        try
        {
            await core().ConfigureAwait(continueOnCapturedContext);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }
}
