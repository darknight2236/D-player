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
        var errors = new List<string>();
        service.TrackEnded += () => Interlocked.Increment(ref ended);
        service.PlaybackError += message => errors.Add(message);

        await service.LoadAsync(TrackFor("one.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        service.Play();
        await service.LoadAsync(TrackFor("two.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);

        // 旧输出在切链时已被解订阅；RaiseStopped() 走的是当前订阅表，等于什么都没发生。
        // 真实 WASAPI 下播放线程上可能仍有一次在途回调，故向"最后已知 handler"投递异常，
        // 只有当前链身份判定能挡住它（去掉判定即会多报一条 PlaybackError）。
        firstOutput.RaiseStoppedToLastKnownHandler(new InvalidOperationException("stale chain"));
        DrainQueued(sync);

        Assert.Empty(errors);
        Assert.Equal(0, ended);
        Assert.Equal(2, service.Duration.TotalSeconds, 3);
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

    [Fact]
    public async Task Load_DuringLoad_PlayIsNoOp()
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

        // 断言 Play 在装载阻塞于 Init 时仍即时返回：超时即视为失败
        // （Task.WaitAsync 取代 Task.Wait 以通过 xUnit1031 分析器；语义等价）。
        var play = Task.Run(() => service.Play(), ct.Token);
        await play.WaitAsync(TimeSpan.FromMilliseconds(200), ct.Token);
        Assert.Equal(PlayState.Stopped, service.State);

        release.Set();
        await load.WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        Assert.Equal(PlayState.Stopped, service.State);
    }

    [Fact]
    public async Task Load_BlocksInInit_UnloadCancelsInstall()
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

        // Unload 在装载阻塞于 Init 时也必须即时返回（超时即失败）。
        var unload = Task.Run(() => service.Unload(), ct.Token);
        await unload.WaitAsync(TimeSpan.FromMilliseconds(200), ct.Token);

        release.Set();
        await load.WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        Assert.Null(service.CurrentTrack);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task Load_WhileOldChainPlaying_OldIsDetachedBeforeBuild()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sources = new Queue<FakeAudioSource>(new[]
        {
            new FakeAudioSource(TimeSpan.FromSeconds(1)),
            new FakeAudioSource(TimeSpan.FromSeconds(2))
        });
        var firstOutput = new FakeWavePlayer();
        var secondOutput = new FakeWavePlayer();
        var outputs = new Queue<FakeWavePlayer>(new[] { firstOutput, secondOutput });
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => sources.Dequeue()),
            new FakeAudioOutputFactory(() => outputs.Dequeue()),
            sync);

        await service.LoadAsync(TrackFor("one.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);
        service.Play();
        await service.LoadAsync(TrackFor("two.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);

        Assert.Contains("Stop", firstOutput.Log);
        Assert.Contains("Dispose", firstOutput.Log);
        Assert.Contains("Init", secondOutput.Log);
        Assert.Equal(2, service.Duration.TotalSeconds, 3);
    }

    /// <summary>
    /// 轮询 smoke（行为钉，不是竞态证据）：以装载事件结算后的位置计数为基线，
    /// Play 后必须出现新的位置事件（证明确实启动了轮询），Pause 后必须停止增长。
    /// 固定等待 100–150 ms 只是给轮询循环 3–4 个 33 ms tick 的观察窗口，允许 ±1 tick 调度抖动。
    /// </summary>
    [Fact]
    public async Task Play_PostsPositions_AndPauseStopsPolling()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new FakeAudioSource(TimeSpan.FromSeconds(10)) { CurrentTime = TimeSpan.FromSeconds(1) };
        var output = new FakeWavePlayer();
        using var sync = new QueuedSynchronizationContext();
        using var service = new NAudioPlaybackService(
            new FakeAudioSourceFactory(_ => source),
            new FakeAudioOutputFactory(() => output),
            sync);
        int positions = 0;
        service.PositionChanged += _ => Interlocked.Increment(ref positions);

        await service.LoadAsync(TrackFor("a.wav")).WaitAsync(TimeSpan.FromSeconds(2), ct.Token);

        // 装载自己会 Post 一次 PositionChanged(Zero)：先把已排队事件结算成基线，
        // 否则"Play 之前就有 1 次位置事件"会让下面的断言恒真（轮询没启动也能过）。
        DrainQueued(sync);
        int baseline = Volatile.Read(ref positions);

        service.Play();
        await Task.Delay(150, ct.Token);
        DrainQueued(sync);
        Assert.True(Volatile.Read(ref positions) > baseline);

        service.Pause();
        await Task.Delay(100, ct.Token);
        DrainQueued(sync);
        int afterPause = Volatile.Read(ref positions);
        await Task.Delay(150, ct.Token);
        DrainQueued(sync);
        Assert.Equal(afterPause, Volatile.Read(ref positions));
    }
}
