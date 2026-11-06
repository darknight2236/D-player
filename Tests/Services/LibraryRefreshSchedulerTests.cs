using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.Tests.TestSupport;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class LibraryRefreshSchedulerTests
{
    private static LibraryRootSnapshot Snapshot(
        string root, IEnumerable<string>? newInvalid = null,
        IEnumerable<string>? invalid = null)
    {
        var fresh = newInvalid?.ToArray() ?? Array.Empty<string>();
        var all = invalid?.ToArray() ?? fresh;
        return new LibraryRootSnapshot(root, Array.Empty<LibraryCacheEntry>(),
            false, false, fresh, all);
    }

    [Fact]
    public void ManualTimeProvider_Advance_FiresRescheduledTimersAndDisposeStops()
    {
        var clock = new ManualTimeProvider();
        var fired = 0;
        var timer = clock.CreateTimer(_ => fired++, null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(3, fired);

        timer.Dispose();
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(3, fired);
    }

    [Fact]
    public async Task Invalidate_DebouncesEachRootIndependently()
    {
        var clock = new ManualTimeProvider();
        var entered = new TaskCompletionSource<LibraryRefreshBatch>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new LibraryRefreshScheduler(clock, (batch, lease) =>
        {
            entered.TrySetResult(batch);
            return Task.FromResult<IReadOnlyList<LibraryRootSnapshot>>(
                Array.Empty<LibraryRootSnapshot>());
        }, ex => failure.TrySetResult(ex));
        var epoch = scheduler.ReplaceRoots(new[] { @"C:\A", @"C:\B" });
        scheduler.Invalidate(@"C:\A", epoch);
        clock.Advance(TimeSpan.FromMilliseconds(600));
        scheduler.Invalidate(@"C:\B", epoch);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        var batch = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(new[] { @"C:\A" }, batch.Roots);
        Assert.False(failure.Task.IsCompleted);
    }

    [Fact]
    public async Task Invalidate_SustainedEvents_UsesFiveSecondMaximumWait()
    {
        var clock = new ManualTimeProvider();
        var entered = new TaskCompletionSource<LibraryRefreshBatch>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new LibraryRefreshScheduler(clock, (batch, lease) =>
        {
            entered.TrySetResult(batch);
            return Task.FromResult<IReadOnlyList<LibraryRootSnapshot>>(
                Array.Empty<LibraryRootSnapshot>());
        }, _ => { });
        var epoch = scheduler.ReplaceRoots(new[] { @"C:\A" });

        for (var i = 0; i < 10; i++)
        {
            scheduler.Invalidate(@"C:\A", epoch);
            clock.Advance(TimeSpan.FromMilliseconds(600));
        }

        var batch = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(new[] { @"C:\A" }, batch.Roots);
    }

    [Fact]
    public async Task RefreshAll_WhileWatchBlocked_RunsFullBeforePendingWatch()
    {
        var clock = new ManualTimeProvider();
        var watchEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWatch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allCalls = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<LibraryRefreshBatch>();
        await using var scheduler = new LibraryRefreshScheduler(clock, async (batch, lease) =>
        {
            int count;
            lock (calls)
            {
                calls.Add(batch);
                count = calls.Count;
            }
            if (count == 3) allCalls.TrySetResult();
            if (batch.Reason == LibraryRefreshReason.Watch &&
                !watchEntered.Task.IsCompleted)
            {
                watchEntered.TrySetResult();
                await releaseWatch.Task.ConfigureAwait(false);
            }
            return Array.Empty<LibraryRootSnapshot>();
        }, _ => { });
        var epoch = scheduler.ReplaceRoots(new[] { @"C:\A" });
        scheduler.Invalidate(@"C:\A", epoch);
        clock.Advance(TimeSpan.FromMilliseconds(800));
        await watchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        var full = scheduler.RefreshAllAsync(LibraryRefreshReason.Manual);
        scheduler.Invalidate(@"C:\A", epoch);
        releaseWatch.TrySetResult();
        await full.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        await allCalls.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        lock (calls)
        {
            Assert.Equal(
                new[]
                {
                    LibraryRefreshReason.Watch,
                    LibraryRefreshReason.Manual,
                    LibraryRefreshReason.Watch,
                },
                calls.Select(call => call.Reason));
            Assert.Equal(new[] { @"C:\A" }, calls[1].Roots);
        }
    }

    [Fact]
    public async Task Invalidate_OldEpoch_IsIgnored()
    {
        var clock = new ManualTimeProvider();
        var entered = new TaskCompletionSource<LibraryRefreshBatch>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var scheduler = new LibraryRefreshScheduler(clock, (batch, lease) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult(batch);
            return Task.FromResult<IReadOnlyList<LibraryRootSnapshot>>(
                Array.Empty<LibraryRootSnapshot>());
        }, _ => { });
        var oldEpoch = scheduler.ReplaceRoots(new[] { @"C:\A" });
        var currentEpoch = scheduler.ReplaceRoots(new[] { @"C:\B" });

        scheduler.Invalidate(@"C:\A", oldEpoch);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, Volatile.Read(ref calls));

        scheduler.Invalidate(@"C:\B", currentEpoch);
        clock.Advance(TimeSpan.FromMilliseconds(800));
        var batch = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(new[] { @"C:\B" }, batch.Roots);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Retry_StopsAfterTwoAttempts()
    {
        var clock = new ManualTimeProvider();
        const string bad = @"C:\A\bad.flac";
        var firstRetry = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRetry = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdRetry = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retryCalls = 0;
        await using var scheduler = new LibraryRefreshScheduler(clock, (batch, lease) =>
        {
            var result = batch.Reason == LibraryRefreshReason.Retry
                ? Snapshot(@"C:\A", invalid: new[] { bad })
                : Snapshot(@"C:\A", new[] { bad });
            if (batch.Reason == LibraryRefreshReason.Retry)
            {
                var count = Interlocked.Increment(ref retryCalls);
                if (count == 1) firstRetry.TrySetResult();
                else if (count == 2) secondRetry.TrySetResult();
                else thirdRetry.TrySetResult();
            }
            return Task.FromResult<IReadOnlyList<LibraryRootSnapshot>>(new[] { result });
        }, _ => { });
        scheduler.ReplaceRoots(new[] { @"C:\A" });

        await scheduler.RefreshAllAsync(LibraryRefreshReason.Manual)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        await firstRetry.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        await secondRetry.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);

        Assert.False(thirdRetry.Task.IsCompleted);
        Assert.Equal(2, Volatile.Read(ref retryCalls));
    }

    [Fact]
    public async Task StopAsync_DrainsBlockedExecute_AndRejectsNewWork()
    {
        var clock = new ManualTimeProvider();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var scheduler = new LibraryRefreshScheduler(clock, async (batch, lease) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return Array.Empty<LibraryRootSnapshot>();
        }, _ => { });
        var epoch = scheduler.ReplaceRoots(new[] { @"C:\A" });
        var full = scheduler.RefreshAllAsync(LibraryRefreshReason.Manual);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        var stop = scheduler.StopAsync();
        Assert.False(stop.IsCompleted);
        release.TrySetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => full.WaitAsync(TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

        scheduler.Invalidate(@"C:\A", epoch);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref calls));
        await scheduler.RefreshAllAsync(LibraryRefreshReason.Manual);
    }

    [Fact]
    public async Task Execute_UnexpectedException_IsReported_AndWorkerContinues()
    {
        var clock = new ManualTimeProvider();
        var errors = 0;
        var calls = 0;
        await using var scheduler = new LibraryRefreshScheduler(clock, (batch, lease) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return Task.FromException<IReadOnlyList<LibraryRootSnapshot>>(
                    new InvalidOperationException("boom"));
            return Task.FromResult<IReadOnlyList<LibraryRootSnapshot>>(
                Array.Empty<LibraryRootSnapshot>());
        }, _ =>
        {
            Interlocked.Increment(ref errors);
            throw new Exception("reporter failure");
        });
        scheduler.ReplaceRoots(new[] { @"C:\A" });

        var first = scheduler.RefreshAllAsync(LibraryRefreshReason.Manual);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => first.WaitAsync(TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        await scheduler.RefreshAllAsync(LibraryRefreshReason.Manual)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.Equal(1, Volatile.Read(ref errors));
    }

    [Fact]
    public async Task IsCurrent_TracksRootEpochAndGeneration()
    {
        var clock = new ManualTimeProvider();
        await using var scheduler = new LibraryRefreshScheduler(clock,
            (batch, lease) => Task.FromResult<IReadOnlyList<LibraryRootSnapshot>>(
                Array.Empty<LibraryRootSnapshot>()), _ => { });
        var epoch = scheduler.ReplaceRoots(new[] { @"C:\A" });
        var initial = new LibraryRefreshBatch(epoch, 0,
            LibraryRefreshReason.Watch, new[] { @"C:\A" });
        Assert.True(scheduler.IsCurrent(initial));

        await scheduler.RefreshAllAsync(LibraryRefreshReason.Manual)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var afterFull = new LibraryRefreshBatch(epoch, 1,
            LibraryRefreshReason.Manual, new[] { @"C:\A" });
        Assert.True(scheduler.IsCurrent(afterFull));
        Assert.False(scheduler.IsCurrent(initial));

        var nextEpoch = scheduler.ReplaceRoots(new[] { @"C:\A" });
        Assert.False(scheduler.IsCurrent(afterFull));
        Assert.True(scheduler.IsCurrent(new LibraryRefreshBatch(nextEpoch, 1,
            LibraryRefreshReason.Watch, new[] { @"C:\A" })));
    }
}
