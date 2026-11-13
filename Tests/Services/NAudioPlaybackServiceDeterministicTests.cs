using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.Tests.TestSupport;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class NAudioPlaybackServiceDeterministicTests
{
    private static Track TrackFor(string path) =>
        new(path, Path.GetFileName(path), null, null, null, null, null, null, TimeSpan.Zero, null);

    private static void DrainQueued(QueuedSynchronizationContext sync)
    {
        while (sync.TryRunNext()) { }
    }

    private static async Task DrainUntilAsync(
        QueuedSynchronizationContext sync, Task target, CancellationToken ct)
    {
        while (!target.IsCompleted)
        {
            if (!sync.TryRunNext())
                await sync.WaitForPendingAsync(1, ct);
        }
    }

    [Fact]
    public async Task LoadAsync_WithFakes_PublishesTrackAndDuration()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new FakeAudioSource(TimeSpan.FromSeconds(3));
        var output = new FakeWavePlayer();
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => source),
            new FakeAudioOutputFactory(() => output),
            sync);

        Track? published = null;
        service.TrackChanged += track => published = track;

        await service.LoadAsync(TrackFor("fake.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        while (sync.TryRunNext()) { }

        Assert.NotNull(published);
        Assert.Equal(TimeSpan.FromSeconds(3), service.Duration);
        Assert.Contains("Init", output.Log);
    }

    [Fact]
    public async Task TwoOverlappingLoads_Serialized_LastChainWins()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var firstSource = new FakeAudioSource(TimeSpan.FromSeconds(1));
        var secondSource = new FakeAudioSource(TimeSpan.FromSeconds(2));
        int created = 0;
        var outputs = new Queue<FakeWavePlayer>(new[]
        {
            new FakeWavePlayer(release, entered),
            new FakeWavePlayer()
        });
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => Interlocked.Increment(ref created) == 1 ? firstSource : secondSource),
            new FakeAudioOutputFactory(() => outputs.Dequeue()),
            sync);

        var load1 = service.LoadAsync(TrackFor("one.wav"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2), ct.Token));
        var load2 = service.LoadAsync(TrackFor("two.wav"));
        Assert.False(load2.IsCompleted);

        release.Set();
        await load1.WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        await load2.WaitAsync(TimeSpan.FromSeconds(2), ct.Token);

        Assert.Equal(2, service.Duration.TotalSeconds, 3);
        Assert.Equal(1, firstSource.DisposeCount);
        Assert.Equal(0, secondSource.DisposeCount);
    }

    [Fact]
    public async Task Stop_RaisesPlaybackStoppedSynchronously_DoesNotDeadlock()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new FakeAudioSource(TimeSpan.FromSeconds(2));
        var output = new FakeWavePlayer { RaiseStoppedSynchronouslyOnStop = true };
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => source),
            new FakeAudioOutputFactory(() => output),
            sync);
        int ended = 0;
        service.TrackEnded += () => Interlocked.Increment(ref ended);

        await service.LoadAsync(TrackFor("a.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        service.Play();

        await Task.Run(() => service.Stop(), ct.Token).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        DrainQueued(sync);

        Assert.Equal(0, ended);
    }

    [Fact]
    public async Task Stop_WhenPlayheadAtEnd_DoesNotRaiseTrackEnded()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new FakeAudioSource(TimeSpan.FromSeconds(1)) { CurrentTime = TimeSpan.FromMilliseconds(950) };
        var output = new FakeWavePlayer { RaiseStoppedSynchronouslyOnStop = true };
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => source),
            new FakeAudioOutputFactory(() => output),
            sync);
        int ended = 0;
        service.TrackEnded += () => Interlocked.Increment(ref ended);

        await service.LoadAsync(TrackFor("a.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        service.Play();
        service.Stop();
        DrainQueued(sync);

        Assert.Equal(0, ended);
    }

    [Fact]
    public async Task NaturalEnd_RaisesTrackEndedOnce()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new FakeAudioSource(TimeSpan.FromSeconds(1));
        var output = new FakeWavePlayer();
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => source),
            new FakeAudioOutputFactory(() => output),
            sync);
        int ended = 0;
        service.TrackEnded += () => Interlocked.Increment(ref ended);

        await service.LoadAsync(TrackFor("a.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        service.Play();
        source.CurrentTime = source.TotalTime;
        output.RaiseStopped();
        DrainQueued(sync);

        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task ReplacedChain_LatePlaybackStopped_IsIgnored()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sources = new Queue<FakeAudioSource>(new[]
        {
            new FakeAudioSource(TimeSpan.FromSeconds(1)),
            new FakeAudioSource(TimeSpan.FromSeconds(2))
        });
        var outputs = new Queue<FakeWavePlayer>(new[] { new FakeWavePlayer(), new FakeWavePlayer() });
        var firstOutput = outputs.Peek();
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => sources.Dequeue()),
            new FakeAudioOutputFactory(() => outputs.Dequeue()),
            sync);
        int ended = 0;
        service.TrackEnded += () => Interlocked.Increment(ref ended);

        await service.LoadAsync(TrackFor("one.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        service.Play();
        await service.LoadAsync(TrackFor("two.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        firstOutput.RaiseStopped();
        DrainQueued(sync);

        Assert.Equal(0, ended);
    }

    [Fact]
    public async Task ChainDispose_UnsubscribesBeforeStop()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new FakeAudioSource(TimeSpan.FromSeconds(1));
        var output = new FakeWavePlayer();
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => source),
            new FakeAudioOutputFactory(() => output),
            sync);

        await service.LoadAsync(TrackFor("a.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        service.Unload();

        int unsubscribe = output.Log.IndexOf("Unsubscribe");
        int stop = output.Log.IndexOf("Stop");
        Assert.True(unsubscribe >= 0 && stop >= 0 && unsubscribe < stop);
    }

    [Fact]
    public async Task Events_ArePostedToCapturedSynchronizationContext()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new FakeAudioSource(TimeSpan.FromSeconds(2));
        var output = new FakeWavePlayer();
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => source),
            new FakeAudioOutputFactory(() => output),
            sync);
        Track? published = null;
        service.TrackChanged += track => published = track;

        await service.LoadAsync(TrackFor("a.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        Assert.Null(published);

        DrainQueued(sync);
        Assert.NotNull(published);
    }

    [Fact]
    public async Task Unload_DuringLoad_NewChainNotInstalled()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var source = new FakeAudioSource(TimeSpan.FromSeconds(2));
        var output = new FakeWavePlayer(release, entered);
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => source),
            new FakeAudioOutputFactory(() => output),
            sync);

        var load = service.LoadAsync(TrackFor("a.wav"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2), ct.Token));
        var unload = Task.Run(() => service.Unload(), ct.Token);
        release.Set();
        await load.WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        await unload.WaitAsync(TimeSpan.FromSeconds(2), ct.Token);

        Assert.Null(service.CurrentTrack);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task Dispose_DuringLoad_DisposesNewChain()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var source = new FakeAudioSource(TimeSpan.FromSeconds(2));
        var output = new FakeWavePlayer(release, entered);
        using var sync = new QueuedSynchronizationContext();
        var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => source),
            new FakeAudioOutputFactory(() => output),
            sync);

        var load = service.LoadAsync(TrackFor("a.wav"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2), ct.Token));
        var dispose = Task.Run(() => service.Dispose(), ct.Token);
        release.Set();
        await load.WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        await dispose.WaitAsync(TimeSpan.FromSeconds(2), ct.Token);

        Assert.Equal(1, source.DisposeCount);
        Assert.Contains("Dispose", output.Log);
    }
}
