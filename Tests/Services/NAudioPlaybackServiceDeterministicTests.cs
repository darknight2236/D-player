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
}
