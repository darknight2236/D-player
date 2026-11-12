using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using DPlayer.Models;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public sealed class LibraryRootScannerTests
{
    private const string Root = @"C:\Library";

    private static Track TrackAt(string path, string title = "Title") => new(
        path, title, "Artist", "Album", null, 2024, 44100, null,
        TimeSpan.FromMinutes(3), 1);

    private static Track InvalidTrack(string path) => new(
        path, Path.GetFileNameWithoutExtension(path), null, null, null, null, null,
        null, TimeSpan.Zero, null);

    private static LibraryCacheEntry EntryAt(string path, string title = "Title") => new(
        path, title, "Artist", "Album", null, 2024, TimeSpan.FromMinutes(3), 44100, 1);

    private static LibraryCacheEntry InvalidEntryAt(string path) => new(
        path, Path.GetFileNameWithoutExtension(path), null, null, null, null,
        TimeSpan.Zero, null, null);

    [Fact]
    public async Task ScanAsync_UnavailableRoot_DoesNotSaveAnEmptySnapshot()
    {
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        scanner.ScanFolderWithStatus(Root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(Array.Empty<string>(), true));
        using var lease = new LibraryScanLease(CancellationToken.None);
        var service = new LibraryRootScanner(scanner, cache, metadata);

        var result = await service.ScanAsync(Root, lease);

        Assert.True(result.IsUnavailable);
        Assert.Empty(result.Entries);
        await cache.DidNotReceive().SaveAsync(Arg.Any<string>(),
            Arg.Any<IReadOnlyList<LibraryCacheEntry>>());
    }

    [Fact]
    public async Task ScanAsync_InvalidRoot_ThrowsArgumentException()
    {
        using var lease = new LibraryScanLease(CancellationToken.None);
        var service = new LibraryRootScanner(
            Substitute.For<ILibraryScannerService>(),
            Substitute.For<ILibraryCache>(),
            Substitute.For<ITrackMetadataReader>());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ScanAsync("   ", lease));
    }

    [Fact]
    public async Task ScanAsync_SameRootDifferentCase_SerializesWholeTransaction()
    {
        const string file = @"C:\Library\song.flac";
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        var scanCount = 0;
        scanner.ScanFolderWithStatus(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref scanCount);
                return new LibraryScanResult(new[] { file }, false);
            });
        cache.LoadAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(new[] { file }, Array.Empty<string>(),
                Array.Empty<LibraryCacheEntry>()));
        scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(new[] { TrackAt(file) }));

        var saveEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCount = 0;
        cache.SaveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(async _ =>
            {
                if (Interlocked.Increment(ref saveCount) == 1)
                {
                    saveEntered.TrySetResult();
                    await releaseSave.Task.ConfigureAwait(false);
                }
            });

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var firstLease = new LibraryScanLease(CancellationToken.None);
        using var secondLease = new LibraryScanLease(CancellationToken.None);

        var first = service.ScanAsync(Root, firstLease);
        await saveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = service.ScanAsync(@"c:\library", secondLease);

        Assert.Equal(1, Volatile.Read(ref scanCount));

        releaseSave.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, Volatile.Read(ref scanCount));
        Assert.Equal(2, Volatile.Read(ref saveCount));
    }

    [Fact]
    public async Task ScanAsync_RevokedBeforeSave_DoesNotWrite()
    {
        const string file = @"C:\Library\song.flac";
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        var loadEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        scanner.ScanFolderWithStatus(Root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(new[] { file }, false));
        cache.LoadAsync(Root).Returns(async _ =>
        {
            loadEntered.TrySetResult();
            await releaseLoad.Task.ConfigureAwait(false);
            return (IReadOnlyList<LibraryCacheEntry>)Array.Empty<LibraryCacheEntry>();
        });
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(new[] { file }, Array.Empty<string>(),
                Array.Empty<LibraryCacheEntry>()));

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var lease = new LibraryScanLease(CancellationToken.None);
        var scan = service.ScanAsync(Root, lease);
        await loadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        lease.Revoke();
        releaseLoad.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        await cache.DidNotReceive().SaveAsync(Arg.Any<string>(),
            Arg.Any<IReadOnlyList<LibraryCacheEntry>>());
    }

    [Fact]
    public async Task ScanAsync_RevokedAfterSaveAdmission_StillCompletesWrite()
    {
        const string file = @"C:\Library\song.flac";
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        scanner.ScanFolderWithStatus(Root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(new[] { file }, false));
        cache.LoadAsync(Root)
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(new[] { file }, Array.Empty<string>(),
                Array.Empty<LibraryCacheEntry>()));
        scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(new[] { TrackAt(file) }));

        var saveEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cache.SaveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(async _ =>
            {
                saveEntered.TrySetResult();
                await releaseSave.Task.ConfigureAwait(false);
            });

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var lease = new LibraryScanLease(CancellationToken.None);
        var scan = service.ScanAsync(Root, lease);
        await saveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        lease.Revoke();
        releaseSave.TrySetResult();
        var result = await scan.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(result.CacheWriteFailed);
        Assert.Equal(file, Assert.Single(result.Entries).FilePath);
        await cache.Received(1).SaveAsync(Root,
            Arg.Any<IReadOnlyList<LibraryCacheEntry>>());
    }

    [Fact]
    public async Task ScanAsync_ValidUnchangedCache_DoesNotReadTagsOrSave()
    {
        const string file = @"C:\Library\song.flac";
        var cached = EntryAt(file);
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        scanner.ScanFolderWithStatus(Root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(new[] { file }, false));
        cache.LoadAsync(Root)
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(new[] { cached }));
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(Array.Empty<string>(), Array.Empty<string>(),
                new[] { cached }));

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var lease = new LibraryScanLease(CancellationToken.None);
        var result = await service.ScanAsync(Root, lease);

        Assert.Equal(file, Assert.Single(result.Entries).FilePath);
        await scanner.DidNotReceive().ReadMetadataBatchAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await cache.DidNotReceive().SaveAsync(Arg.Any<string>(),
            Arg.Any<IReadOnlyList<LibraryCacheEntry>>());
    }

    [Fact]
    public async Task ScanAsync_ClassifiesNewInvalidPathsAndFallbacks()
    {
        const string good = @"C:\Library\good.flac";
        const string badNew = @"C:\Library\bad-new.flac";
        const string badCached = @"C:\Library\bad-cached.flac";
        const string goodCached = @"C:\Library\good-cached.flac";
        var cachedEntry = EntryAt(goodCached);
        var cachedInvalid = InvalidEntryAt(badCached);
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        scanner.ScanFolderWithStatus(Root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(new[] { good, badNew, badCached, goodCached },
                false));
        cache.LoadAsync(Root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { cachedEntry, cachedInvalid }));
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(new[] { good, badNew, badCached },
                Array.Empty<string>(), new[] { cachedEntry }));
        scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(
                new[] { TrackAt(good), InvalidTrack(badCached) }));
        metadata.CreateFallback(badNew).Returns(InvalidTrack(badNew));

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var lease = new LibraryScanLease(CancellationToken.None);
        var result = await service.ScanAsync(Root, lease);

        Assert.Equal(new[] { badNew }, result.NewInvalidPaths);
        Assert.Equal(new[] { badCached, badNew }, result.InvalidPaths);
        Assert.Equal(4, result.Entries.Count);
        Assert.Contains(result.Entries, entry => entry.FilePath == badNew);
        metadata.Received(1).CreateFallback(badNew);
    }

    [Fact]
    public async Task ScanAsync_CacheWriteFailure_KeepsEntriesAndFlags()
    {
        const string file = @"C:\Library\song.flac";
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        scanner.ScanFolderWithStatus(Root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(new[] { file }, false));
        cache.LoadAsync(Root)
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(new[] { file }, Array.Empty<string>(),
                Array.Empty<LibraryCacheEntry>()));
        scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(new[] { TrackAt(file) }));
        cache.SaveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(Task.FromException(new IOException("disk full")));

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var lease = new LibraryScanLease(CancellationToken.None);
        var result = await service.ScanAsync(Root, lease);

        Assert.True(result.CacheWriteFailed);
        Assert.False(result.IsUnavailable);
        Assert.Equal(file, Assert.Single(result.Entries).FilePath);
    }

    [Fact]
    public async Task ScanAsync_ScannerCancellation_Propagates()
    {
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        scanner.ScanFolderWithStatus(Root, Arg.Any<CancellationToken>())
            .Returns(_ => throw new OperationCanceledException());
        using var lease = new LibraryScanLease(CancellationToken.None);
        var service = new LibraryRootScanner(scanner, cache, metadata);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ScanAsync(Root, lease));
    }

    [Fact]
    public async Task ScanAsync_AfterCompletion_ReleasesRootGate()
    {
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        scanner.ScanFolderWithStatus(Root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(Array.Empty<string>(), false));
        cache.LoadAsync(Root)
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<LibraryCacheEntry>()));

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var lease = new LibraryScanLease(CancellationToken.None);

        await service.ScanAsync(Root, lease);

        Assert.Equal(0, service.ActiveRootGates);
    }

    [Fact]
    public async Task ScanAsync_DifferentRoots_RunInParallel()
    {
        const string rootA = @"C:\A";
        const string rootB = @"C:\B";
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        var enteredA = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseA = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        scanner.ScanFolderWithStatus(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Interlocked.Increment(ref callCount);
                if (string.Equals(call.ArgAt<string>(0), rootA,
                    StringComparison.OrdinalIgnoreCase))
                {
                    enteredA.TrySetResult();
                    releaseA.Task.GetAwaiter().GetResult();
                }
                return new LibraryScanResult(Array.Empty<string>(), false);
            });
        cache.LoadAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<LibraryCacheEntry>()));

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var leaseA = new LibraryScanLease(CancellationToken.None);
        using var leaseB = new LibraryScanLease(CancellationToken.None);

        var first = service.ScanAsync(rootA, leaseA);
        try
        {
            await enteredA.Task.WaitAsync(TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var second = service.ScanAsync(rootB, leaseB);

            await second.WaitAsync(TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.Equal(2, Volatile.Read(ref callCount));
        }
        finally
        {
            releaseA.TrySetResult();
        }

        await first.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ScanAsync_RevokedWhileWaiting_DoesNotLeakGate()
    {
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        scanner.ScanFolderWithStatus(Root, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                return new LibraryScanResult(Array.Empty<string>(), false);
            });
        cache.LoadAsync(Root)
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<LibraryCacheEntry>()));

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var lease = new LibraryScanLease(CancellationToken.None);
        var scan = service.ScanAsync(Root, lease);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            lease.Revoke();
        }
        finally
        {
            release.TrySetResult();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        Assert.Equal(0, service.ActiveRootGates);
    }

    [Fact]
    public async Task ScanAsync_PreCancelledWaiter_DoesNotLeakGate()
    {
        var scanner = Substitute.For<ILibraryScannerService>();
        var cache = Substitute.For<ILibraryCache>();
        var metadata = Substitute.For<ITrackMetadataReader>();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        scanner.ScanFolderWithStatus(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    firstEntered.TrySetResult();
                    releaseFirst.Task.GetAwaiter().GetResult();
                }
                return new LibraryScanResult(Array.Empty<string>(), false);
            });
        cache.LoadAsync(Root)
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
        scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<LibraryCacheEntry>()));

        var service = new LibraryRootScanner(scanner, cache, metadata);
        using var firstLease = new LibraryScanLease(CancellationToken.None);
        using var cancelledLease = new LibraryScanLease(
            new CancellationToken(canceled: true));

        var first = service.ScanAsync(Root, firstLease);
        try
        {
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ScanAsync(Root, cancelledLease));
            Assert.Equal(1, service.ActiveRootGates);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        await first.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(0, service.ActiveRootGates);
    }
}
