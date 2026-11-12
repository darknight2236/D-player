using System;
using System.Threading;
using System.Threading.Tasks;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

public sealed class OneShotCleanupTests
{
    [Fact]
    public async Task RunAsync_ReturnsSameTask_AndRunsOnce()
    {
        var cleanup = new OneShotCleanup();
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var first = cleanup.RunAsync(async () =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task.ConfigureAwait(false);
        });
        var second = cleanup.RunAsync(() => Task.CompletedTask);

        Assert.Same(first, second);
        gate.TrySetResult();
        await first;
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RunAsync_PropagatesException()
    {
        var cleanup = new OneShotCleanup();

        var task = cleanup.RunAsync(
            () => Task.FromException(new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    }

    [Fact]
    public async Task RunAsync_LateCompletion_IsDiscarded()
    {
        var cleanup = new OneShotCleanup();
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var first = cleanup.RunAsync(async () =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task.ConfigureAwait(false);
        });
        var second = cleanup.RunAsync(() =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });

        gate.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, calls);
    }
}
