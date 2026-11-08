using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.Services.PlaylistFiles;
using DPlayer.Tests.TestSupport;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

public sealed class LibraryViewModelTests : IAsyncDisposable
{
    private readonly ISettingsPersistence _settings = Substitute.For<ISettingsPersistence>();
    private readonly ILibraryRootScanner _rootScanner = Substitute.For<ILibraryRootScanner>();
    private readonly ILibraryCache _cache = Substitute.For<ILibraryCache>();
    private readonly ITrackMetadataReader _metadata = Substitute.For<ITrackMetadataReader>();
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly IFileDialogService _fileDialog = Substitute.For<IFileDialogService>();
    private readonly IPlaylistFileService _playlistFiles = Substitute.For<IPlaylistFileService>();
    private readonly ILibraryWatcher _watcher = Substitute.For<ILibraryWatcher>();
    private readonly ManualTimeProvider _clock = new();
    private readonly List<LibraryViewModel> _vms = new();
    private PlaylistsViewModel? _lastPlaylists;

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"dplayer-library-{Guid.NewGuid():N}");

    public LibraryViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _watcher.ReplaceRootsAsync(Arg.Any<long>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var vm in _vms)
            await vm.CleanupAsync();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private LibraryViewModel CreateVm(params string[] roots)
        => CreateVmWithContext(null, roots);

    private LibraryViewModel CreateVmWithContext(
        SynchronizationContext? syncContext, params string[] roots)
    {
        _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
        {
            LibraryFolders = roots,
        }));
        _metadata.CreateFallback(Arg.Any<string>())
            .Returns(ci => Fallback(ci.ArgAt<string>(0)));
        _metadata.ReadAsync(Arg.Any<string>())
            .Returns(ci => Task.FromResult(Fallback(ci.ArgAt<string>(0))));

        var playlists = new PlaylistsViewModel(
            seed => new PlaylistViewModel(
                seed, _player, _fileDialog, _metadata, _playlistFiles),
            Substitute.For<ILibraryRootScanner>());
        _lastPlaylists = playlists;

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            syncContext ?? new SynchronizationContext());
        try
        {
            var vm = new LibraryViewModel(
                _settings, _rootScanner, _cache, _metadata, playlists,
                _watcher, _clock);
            _vms.Add(vm);
            return vm;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static Track Fallback(string path) =>
        new(path, Path.GetFileNameWithoutExtension(path), null, null, null,
            null, null, null, TimeSpan.Zero, null);

    private static LibraryCacheEntry Entry(
        string path, string title, string? artist, string? album, int? number = null)
        => new(path, title, artist, album, null, null,
            TimeSpan.FromMinutes(3), 44100, number);

    private static LibraryRootSnapshot Snapshot(
        string root, params LibraryCacheEntry[] entries)
        => new(root, entries, false, false,
            Array.Empty<string>(), Array.Empty<string>());

    private static LibraryRootSnapshot Unavailable(string root)
        => new(root, Array.Empty<LibraryCacheEntry>(), true, false,
            Array.Empty<string>(), Array.Empty<string>());

    private void SetScan(string root, LibraryRootSnapshot snapshot)
        => _rootScanner.ScanAsync(root, Arg.Any<LibraryScanLease>())
            .Returns(Task.FromResult(snapshot));

    private static async Task WaitForSearchAsync(
        LibraryViewModel vm, Func<LibrarySearchResults, bool> predicate,
        CancellationToken ct)
    {
        if (predicate(vm.SearchResults)) return;

        var matched = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(LibraryViewModel.SearchResults) &&
                predicate(vm.SearchResults))
                matched.TrySetResult();
        }

        vm.PropertyChanged += Handler;
        try
        {
            await matched.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        finally
        {
            vm.PropertyChanged -= Handler;
        }
    }

    [Fact]
    public async Task InitializeAsync_LoadsCachedTracks_WithoutReadingTags()
    {
        var pathA = Path.Combine(_root, "a.mp3");
        var pathB = Path.Combine(_root, "b.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[]
            {
                Entry(pathA, "A", "Artist", "Album", 2),
                Entry(pathB, "B", "Artist", "Album", 1),
            }));

        await vm.InitializeAsync();

        Assert.True(vm.HasLibraryRoots);
        Assert.Single(vm.Artists);
        Assert.Single(vm.Albums);
        Assert.Equal(new[] { "B", "A" }, vm.Albums[0].Tracks.Select(t => t.Title));
        Assert.Equal("A", vm.CachedEntriesByPath[pathA].Title);
        await _rootScanner.DidNotReceive().ScanAsync(
            Arg.Any<string>(), Arg.Any<LibraryScanLease>());
        await _metadata.DidNotReceive().ReadAsync(Arg.Any<string>());
        await _watcher.DidNotReceive().ReplaceRootsAsync(
            Arg.Any<long>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanInBackgroundAsync_AddsNewFiles_UsesSnapshotEntries()
    {
        var newFile = Path.Combine(_root, "new.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            Array.Empty<LibraryCacheEntry>()));
        SetScan(_root, Snapshot(_root, Entry(newFile, "New", "Artist", "Album")));

        await vm.InitializeAsync();
        await vm.ScanInBackgroundAsync();

        Assert.Single(vm.Albums);
        Assert.Equal("New", vm.Albums[0].Tracks[0].Title);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanInBackgroundAsync_RemovesDeletedTracks()
    {
        var removed = Path.Combine(_root, "gone.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(removed, "Gone", "Artist", "Album") }));
        SetScan(_root, Snapshot(_root));

        await vm.InitializeAsync();
        Assert.Single(vm.Albums);

        await vm.ScanInBackgroundAsync();

        Assert.Empty(vm.Albums);
        Assert.Empty(vm.Artists);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotScanTheFileSystem()
    {
        var cached = Path.Combine(_root, "cached.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(cached, "Cached", "Artist", "Album") }));

        await vm.InitializeAsync();

        Assert.Single(vm.Albums);
        await _rootScanner.DidNotReceive().ScanAsync(
            Arg.Any<string>(), Arg.Any<LibraryScanLease>());
        await _cache.DidNotReceive().SaveAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<LibraryCacheEntry>>());
    }

    [Fact]
    public async Task ScanInBackgroundAsync_UnavailableRoot_KeepsCacheAndSkipsSave()
    {
        var cachedPath = Path.Combine(_root, "keep.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(cachedPath, "Keep", "Artist", "Album") }));
        SetScan(_root, Unavailable(_root));

        await vm.InitializeAsync();
        Assert.Single(vm.Albums);

        await vm.ScanInBackgroundAsync();

        Assert.Single(vm.Albums);
        Assert.Single(vm.Artists);
        Assert.Equal("Keep", vm.CachedEntriesByPath[cachedPath].Title);
        Assert.Equal(1, vm.UnavailableRootCount);
        Assert.False(vm.IsScanning);
        await _cache.DidNotReceive().SaveAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<LibraryCacheEntry>>());
    }

    [Fact]
    public async Task ScanInBackgroundAsync_EmptyAvailableRoot_RemovesTracks()
    {
        var cachedPath = Path.Combine(_root, "gone.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(cachedPath, "Gone", "Artist", "Album") }));
        SetScan(_root, Snapshot(_root));

        await vm.InitializeAsync();
        Assert.Single(vm.Albums);

        await vm.ScanInBackgroundAsync();

        Assert.Empty(vm.Albums);
        Assert.Empty(vm.Artists);
        Assert.Equal(0, vm.UnavailableRootCount);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanInBackgroundAsync_CacheWriteFailed_KeepsInMemoryAlbums()
    {
        var newFile = Path.Combine(_root, "new.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            Array.Empty<LibraryCacheEntry>()));
        _rootScanner.ScanAsync(_root, Arg.Any<LibraryScanLease>())
            .Returns(Task.FromResult(new LibraryRootSnapshot(
                _root, new[] { Entry(newFile, "New", "Artist", "Album") },
                false, true, Array.Empty<string>(), Array.Empty<string>())));

        await vm.InitializeAsync();
        await vm.ScanInBackgroundAsync();

        Assert.Single(vm.Albums);
        Assert.Single(vm.Albums[0].Tracks);
        Assert.Contains("缓存未保存", vm.StatusText);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanInBackgroundAsync_SamePathInTwoRoots_CountsOnce()
    {
        var rootA = Path.Combine(_root, "library");
        var rootB = Path.Combine(rootA, "nested");
        Directory.CreateDirectory(rootB);
        var shared = Path.Combine(rootB, "shared.mp3");

        var vm = CreateVm(rootA, rootB);
        _cache.LoadAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
        var entry = Entry(shared, "Shared", "Artist", "Album");
        SetScan(rootA, Snapshot(rootA, entry));
        SetScan(rootB, Snapshot(rootB, entry));

        await vm.InitializeAsync();
        await vm.ScanInBackgroundAsync();

        Assert.Single(vm.Artists);
        Assert.Single(vm.Albums);
        Assert.Single(vm.Albums[0].Tracks);
        Assert.Single(vm.CachedEntriesByPath);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanInBackgroundAsync_UnchangedSnapshot_DoesNotResetCollections()
    {
        var path = Path.Combine(_root, "same.mp3");
        var entry = Entry(path, "Same", "Artist", "Album");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { entry }));
        SetScan(_root, Snapshot(_root, entry));

        await vm.InitializeAsync();
        var album = Assert.Single(vm.Albums);
        var resets = 0;
        vm.Albums.CollectionChanged += (_, _) => resets++;

        await vm.ScanInBackgroundAsync();

        Assert.Equal(0, resets);
        Assert.Same(album, Assert.Single(vm.Albums));
    }

    [Fact]
    public async Task ScanInBackgroundAsync_SameTitleDifferentFolder_KeepsAlbumSelection()
    {
        var pathA = Path.Combine(_root, "A", "song.mp3");
        var pathB = Path.Combine(_root, "B", "song.mp3");
        var pathB2 = Path.Combine(_root, "B", "song2.mp3");
        var entryA = Entry(pathA, "Song A", "Artist A", "Same");
        var entryB = Entry(pathB, "Song B", "Artist B", "Same");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { entryA, entryB }));
        SetScan(_root, Snapshot(_root, entryA, entryB,
            Entry(pathB2, "Song B2", "Artist B", "Same")));

        await vm.InitializeAsync();
        var selected = vm.Albums.Single(album =>
            album.Tracks[0].FilePath == pathA);
        var selectedKey = LibraryIdentity.AlbumOf(selected);
        vm.OpenAlbum(selected, fromArtist: false);

        await vm.ScanInBackgroundAsync();

        Assert.NotNull(vm.SelectedAlbum);
        Assert.True(LibraryIdentity.AlbumComparer.Equals(
            selectedKey, LibraryIdentity.AlbumOf(vm.SelectedAlbum!)));
        Assert.Equal(pathA, vm.SelectedAlbum!.Tracks[0].FilePath);
    }

    [Fact]
    public async Task CachedEntriesByPath_ReturnsSnapshot_NotLiveDictionary()
    {
        var cachedPath = Path.Combine(_root, "snap.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(cachedPath, "Snap", "Artist", "Album") }));
        SetScan(_root, Snapshot(_root));

        await vm.InitializeAsync();
        var snapshot = vm.CachedEntriesByPath;
        Assert.Single(snapshot);
        Assert.NotSame(snapshot, vm.CachedEntriesByPath);

        await vm.ScanInBackgroundAsync();

        Assert.Single(snapshot);
        Assert.Empty(vm.CachedEntriesByPath);
    }

    [Fact]
    public async Task EnsureCoverAsync_ReadsFirstTrackOnce()
    {
        var path = Path.Combine(_root, "cover.mp3");
        var art = new byte[] { 1, 2, 3 };
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(path, "Cover", "Artist", "Album") }));
        _metadata.ReadAsync(path).Returns(Task.FromResult(new Track(
            path, "Cover", "Artist", "Album", null, null, null, art,
            TimeSpan.FromMinutes(3), null)));

        await vm.InitializeAsync();
        var album = Assert.Single(vm.Albums);

        await vm.EnsureCoverAsync(album);
        await vm.EnsureCoverAsync(album);

        Assert.Same(art, album.CoverArt);
        await _metadata.Received(1).ReadAsync(path);
    }

    [Fact]
    public async Task RefreshIfRootsChangedAsync_SameRoots_DoesNotScan()
    {
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            Array.Empty<LibraryCacheEntry>()));
        await vm.InitializeAsync();
        _rootScanner.ClearReceivedCalls();
        _cache.ClearReceivedCalls();
        _watcher.ClearReceivedCalls();

        await vm.RefreshIfRootsChangedAsync();

        await _rootScanner.DidNotReceive().ScanAsync(
            Arg.Any<string>(), Arg.Any<LibraryScanLease>());
        await _cache.DidNotReceive().LoadAsync(Arg.Any<string>());
        await _watcher.DidNotReceive().ReplaceRootsAsync(
            Arg.Any<long>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshIfRootsChangedAsync_ChangedRoots_Scans()
    {
        var other = Path.Combine(Path.GetTempPath(),
            $"dplayer-library-other-{Guid.NewGuid():N}");
        Directory.CreateDirectory(other);
        try
        {
            var vm = CreateVm(_root);
            _cache.LoadAsync(Arg.Any<string>())
                .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                    Array.Empty<LibraryCacheEntry>()));
            SetScan(other, Snapshot(other));
            await vm.InitializeAsync();
            _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
            {
                LibraryFolders = new[] { other },
            }));
            _rootScanner.ClearReceivedCalls();

            await vm.RefreshIfRootsChangedAsync();

            await _rootScanner.Received(1).ScanAsync(
                other, Arg.Any<LibraryScanLease>());
        }
        finally
        {
            try { Directory.Delete(other, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task RefreshIfRootsChanged_RemovingParent_KeepsChildMembership()
    {
        var child = Path.Combine(_root, "Disc");
        Directory.CreateDirectory(child);
        var path = Path.Combine(child, "song.flac");
        var vm = CreateVm(_root, child);
        var entries = new[] { Entry(path, "Song", "A", "Album") };
        _cache.LoadAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(entries));
        SetScan(child, Snapshot(child, entries));

        await vm.InitializeAsync();
        _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
        {
            LibraryFolders = new[] { child },
        }));

        await vm.RefreshIfRootsChangedAsync();

        Assert.Single(vm.CachedEntriesByPath);
        Assert.Equal(path, Assert.Single(Assert.Single(vm.Albums).Tracks).FilePath);
    }

    [Fact]
    public async Task StartMonitoringAsync_SubscribesBeforeCalibration()
    {
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            Array.Empty<LibraryCacheEntry>()));
        await vm.InitializeAsync();

        var watcherEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWatcher = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _watcher.ReplaceRootsAsync(Arg.Any<long>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                watcherEntered.TrySetResult();
                await releaseWatcher.Task.ConfigureAwait(false);
                return (IReadOnlyList<string>)Array.Empty<string>();
            });
        var scanEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _rootScanner.ScanAsync(_root, Arg.Any<LibraryScanLease>())
            .Returns(_ =>
            {
                scanEntered.TrySetResult();
                return Task.FromResult(Snapshot(_root));
            });

        var monitor = vm.StartMonitoringAsync();
        await watcherEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.False(scanEntered.Task.IsCompleted);

        releaseWatcher.TrySetResult();
        await monitor.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await scanEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartMonitoringAsync_NoRoots_ThenRootsAdded_Subscribes()
    {
        var vm = CreateVm();
        await vm.InitializeAsync();
        await vm.StartMonitoringAsync();
        _watcher.ClearReceivedCalls();

        _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
        {
            LibraryFolders = new[] { _root },
        }));
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            Array.Empty<LibraryCacheEntry>()));
        SetScan(_root, Snapshot(_root));

        await vm.RefreshIfRootsChangedAsync();

        await _watcher.Received(1).ReplaceRootsAsync(
            Arg.Any<long>(),
            Arg.Is<IReadOnlyList<string>>(roots => roots.Count == 1 && roots[0] == _root),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WatchInvalidation_OnlyRefreshesAffectedRoot()
    {
        var rootA = Path.Combine(_root, "A");
        var rootB = Path.Combine(_root, "B");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        var vm = CreateVm(rootA, rootB);
        _cache.LoadAsync(Arg.Any<string>())
            .Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
        _rootScanner.ScanAsync(Arg.Any<string>(), Arg.Any<LibraryScanLease>())
            .Returns(ci => Task.FromResult(Snapshot(ci.ArgAt<string>(0))));

        long epoch = -1;
        _watcher.ReplaceRootsAsync(Arg.Do<long>(value => epoch = value),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        await vm.InitializeAsync();
        await vm.StartMonitoringAsync();
        Assert.True(epoch >= 0);
        _rootScanner.ClearReceivedCalls();

        var scanA = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _rootScanner.ScanAsync(Arg.Any<string>(), Arg.Any<LibraryScanLease>())
            .Returns(ci =>
            {
                if (string.Equals(ci.ArgAt<string>(0), rootA,
                    StringComparison.OrdinalIgnoreCase))
                    scanA.TrySetResult();
                return Task.FromResult(Snapshot(ci.ArgAt<string>(0)));
            });

        _watcher.Changed += Raise.Event<Action<LibraryWatchNotification>>(
            new LibraryWatchNotification(epoch, rootA,
                Path.Combine(rootA, "new.flac"), null,
                LibraryWatchKind.Invalidated));
        _clock.Advance(TimeSpan.FromMilliseconds(800));
        await scanA.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        await _rootScanner.Received(1).ScanAsync(
            rootA, Arg.Any<LibraryScanLease>());
        await _rootScanner.DidNotReceive().ScanAsync(
            rootB, Arg.Any<LibraryScanLease>());
    }

    [Fact]
    public async Task CleanupAsync_ReturnsSameTask_AndSkipsLateSnapshot()
    {
        var keep = Path.Combine(_root, "keep.mp3");
        var late = Path.Combine(_root, "late.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(keep, "Keep", "Artist", "Album") }));
        await vm.InitializeAsync();

        var scanEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScan = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _rootScanner.ScanAsync(_root, Arg.Any<LibraryScanLease>())
            .Returns(async _ =>
            {
                scanEntered.TrySetResult();
                await releaseScan.Task.ConfigureAwait(false);
                return Snapshot(_root, Entry(late, "Late", "Artist", "Album"));
            });

        var scan = vm.ScanInBackgroundAsync();
        await scanEntered.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        var first = vm.CleanupAsync();
        var second = vm.CleanupAsync();
        Assert.Same(first, second);

        releaseScan.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await scan.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(keep, Assert.Single(vm.Albums).Tracks[0].FilePath);
        Assert.Single(vm.CachedEntriesByPath);
        Assert.DoesNotContain(late, vm.CachedEntriesByPath.Keys);
    }

    [Fact]
    public async Task WatchRemoval_UpdatesLibrary_ButLeavesPlaybackQueueIntact()
    {
        var path1 = Path.Combine(_root, "01.flac");
        var path2 = Path.Combine(_root, "02.flac");
        var entries = new[]
        {
            Entry(path1, "One", "Artist", "Album", 1),
            Entry(path2, "Two", "Artist", "Album", 2),
        };
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            entries));

        long epoch = -1;
        _watcher.ReplaceRootsAsync(Arg.Do<long>(value => epoch = value),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        var watchMode = false;
        var watchScanned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _rootScanner.ScanAsync(Arg.Any<string>(), Arg.Any<LibraryScanLease>())
            .Returns(_ =>
            {
                if (watchMode) watchScanned.TrySetResult();
                return Task.FromResult(Snapshot(_root, entries));
            });

        await vm.InitializeAsync();
        await vm.StartMonitoringAsync();
        await vm.PlayAlbumAsync(Assert.Single(vm.Albums));
        _player.ClearReceivedCalls();

        entries = new[] { Entry(path1, "One", "Artist", "Album", 1) };
        watchMode = true;
        var albumUpdated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnAlbumsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (vm.Albums.Count == 1 && vm.Albums[0].Tracks.Count == 1)
                albumUpdated.TrySetResult();
        }
        vm.Albums.CollectionChanged += OnAlbumsChanged;
        _watcher.Changed += Raise.Event<Action<LibraryWatchNotification>>(
            new LibraryWatchNotification(epoch, _root, path2, null,
                LibraryWatchKind.Invalidated));
        _clock.Advance(TimeSpan.FromMilliseconds(800));
        await watchScanned.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await albumUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        vm.Albums.CollectionChanged -= OnAlbumsChanged;

        Assert.Single(Assert.Single(vm.Albums).Tracks);
        Assert.Equal(2, _lastPlaylists!.LibraryPlayback!.Queue.Count);
        _player.DidNotReceive().Stop();
        _player.DidNotReceive().Pause();
        _player.DidNotReceive().Unload();
        await _lastPlaylists.CleanupAsync();
        _lastPlaylists.LibraryPlayback!.Cleanup();
    }

    [Fact]
    public async Task Search_Clear_RestoresAlbumDetailWithoutPersistingQuery()
    {
        var path = Path.Combine(_root, "song.flac");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(path, "Song", "Artist", "Album") }));
        await vm.InitializeAsync();
        vm.OpenAlbum(Assert.Single(vm.Albums), fromArtist: false);

        var search = vm.UpdateSearchAsync(" Artist ");
        Assert.True(vm.IsSearchPage);
        Assert.True(vm.CanGoBack);
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await search.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Single(vm.SearchResults.Tracks);
        Assert.Equal(LibraryPage.AlbumDetail, vm.CaptureNavigation().Page);

        vm.ClearSearch();

        Assert.Equal(LibraryPage.AlbumDetail, vm.Page);
        Assert.Equal(path, vm.SelectedAlbum!.Tracks[0].FilePath);
        Assert.Equal(string.Empty, vm.SearchQuery);
    }

    [Fact]
    public async Task Search_WhitespaceQuery_DoesNotEnterSearch()
    {
        var vm = CreateVm();

        await vm.UpdateSearchAsync("   ");

        Assert.False(vm.IsSearchPage);
        Assert.Equal(string.Empty, vm.SearchQuery);
        Assert.Empty(vm.SearchResults.Tracks);
    }

    [Fact]
    public async Task Search_OpenArtist_ClearsQueryAndReturnsToArtistList()
    {
        var path = Path.Combine(_root, "song.flac");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(path, "Song", "Artist", "Album") }));
        await vm.InitializeAsync();
        vm.ShowAlbums();

        var search = vm.UpdateSearchAsync("Artist");
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await search.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var artist = Assert.Single(vm.SearchResults.Artists);

        vm.OpenSearchArtist(artist);

        Assert.Equal(LibraryPage.ArtistDetail, vm.Page);
        Assert.False(vm.IsSearchPage);
        Assert.Equal(string.Empty, vm.SearchQuery);
        vm.Back();
        Assert.Equal(LibraryPage.Artists, vm.Page);
    }

    [Fact]
    public async Task Search_OpenAlbum_ReturnsToAlbumsOnBack()
    {
        var path = Path.Combine(_root, "song.flac");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(path, "Song", "Artist", "Album") }));
        await vm.InitializeAsync();
        vm.ShowArtists();

        var search = vm.UpdateSearchAsync("Album");
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await search.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var album = Assert.Single(vm.SearchResults.Albums);

        vm.OpenSearchAlbum(album);

        Assert.Equal(LibraryPage.AlbumDetail, vm.Page);
        Assert.Equal(string.Empty, vm.SearchQuery);
        vm.Back();
        Assert.Equal(LibraryPage.Albums, vm.Page);
    }

    [Fact]
    public async Task Search_ShowArtists_DoesNotRestoreSourcePage()
    {
        var path = Path.Combine(_root, "song.flac");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(path, "Song", "Artist", "Album") }));
        await vm.InitializeAsync();
        vm.OpenAlbum(Assert.Single(vm.Albums), fromArtist: false);
        var search = vm.UpdateSearchAsync("Song");
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await search.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        vm.ShowArtists();

        Assert.Equal(LibraryPage.Artists, vm.Page);
        Assert.Equal(string.Empty, vm.SearchQuery);
    }

    [Fact]
    public async Task Search_PlayTrack_UsesFullAlbumContext()
    {
        var albumDir = Path.Combine(_root, "Album");
        var path1 = Path.Combine(albumDir, "01.flac");
        var path2 = Path.Combine(albumDir, "02.flac");
        var path3 = Path.Combine(albumDir, "03.flac");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[]
            {
                Entry(path1, "One", "Artist", "Album", 1),
                Entry(path2, "Two", "Artist", "Album", 2),
                Entry(path3, "Three", "Artist", "Album", 3),
            }));
        await vm.InitializeAsync();

        var search = vm.UpdateSearchAsync("Two");
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await search.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        var track = Assert.Single(vm.SearchResults.Tracks);

        await vm.PlaySearchTrackAsync(track);

        Assert.Equal(LibraryPage.Search, vm.Page);
        Assert.Equal("Two", vm.SearchQuery);
        var playback = _lastPlaylists!.LibraryPlayback;
        Assert.NotNull(playback);
        Assert.Equal(3, playback!.Queue.Count);
        Assert.Equal(1, playback.CurrentIndex);
        Assert.Equal("Album", playback.Name);
        await _player.Received(1).LoadAsync(
            Arg.Is<Track>(item => item.FilePath == path2));
        _player.Received(1).Play();
        await _lastPlaylists!.CleanupAsync();
        playback.Cleanup();
    }

    [Fact]
    public async Task Search_PlayMissingTrack_KeepsQueryAndMessage()
    {
        var path = Path.Combine(_root, "song.flac");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(path, "Song", "Artist", "Album") }));
        await vm.InitializeAsync();
        var search = vm.UpdateSearchAsync("Song");
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await search.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        await vm.PlaySearchTrackAsync(new Track(
            Path.Combine(_root, "gone.flac"), "Gone", "Artist", "Album",
            null, null, 44100, null, TimeSpan.FromMinutes(3), 1));

        Assert.Equal(LibraryPage.Search, vm.Page);
        Assert.Equal("Song", vm.SearchQuery);
        Assert.Equal("该曲目已不在音乐库中", vm.SearchMessage);
        Assert.Null(_lastPlaylists!.LibraryPlayback);
        await _player.DidNotReceive().LoadAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task Search_LateQueryResult_DoesNotOverwriteNewQuery()
    {
        var pathA = Path.Combine(_root, "a.flac");
        var pathB = Path.Combine(_root, "b.flac");
        using var context = new QueuedSynchronizationContext();
        var vm = CreateVmWithContext(context, _root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[]
            {
                Entry(pathA, "Song A", "Artist A", "Album A"),
                Entry(pathB, "Song B", "Artist B", "Album B"),
            }));

        var init = vm.InitializeAsync();
        await context.WaitForPendingAsync(1, TestContext.Current.CancellationToken);
        await context.RunNextAsync(TestContext.Current.CancellationToken);
        await init.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        var first = vm.UpdateSearchAsync("Artist A");
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await context.WaitForPendingAsync(1, TestContext.Current.CancellationToken);

        var second = vm.UpdateSearchAsync("Artist B");
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await context.WaitForPendingAsync(2, TestContext.Current.CancellationToken);

        await context.RunNextAsync(TestContext.Current.CancellationToken);
        await context.RunNextAsync(TestContext.Current.CancellationToken);
        await first.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await second.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal("Artist B", vm.SearchQuery);
        Assert.Equal("Artist B", Assert.Single(vm.SearchResults.Artists).Name);
    }

    [Fact]
    public async Task Search_LibraryChange_RecomputesCurrentQuery()
    {
        var path1 = Path.Combine(_root, "one.flac");
        var path2 = Path.Combine(_root, "two.flac");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(path1, "Song A", "Artist", "Album") }));
        SetScan(_root, Snapshot(_root,
            Entry(path1, "Song A", "Artist", "Album"),
            Entry(path2, "Song B", "Artist", "Album")));
        await vm.InitializeAsync();
        var search = vm.UpdateSearchAsync("Song");
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await search.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Single(vm.SearchResults.Tracks);

        await vm.ScanInBackgroundAsync();
        await WaitForSearchAsync(vm, results => results.Tracks.Count == 2,
            TestContext.Current.CancellationToken);

        Assert.Equal(LibraryPage.Search, vm.Page);
        Assert.Equal("Song", vm.SearchQuery);
        Assert.Equal(2, vm.SearchResults.Tracks.Count);
    }

    [Fact]
    public void Navigation_ArtistsToAlbumAndBack()
    {
        var vm = CreateVm();
        var album = new LibraryAlbum(
            "Album", "Artist", false, 2020,
            new[] { Fallback("x.mp3") });
        var artist = new LibraryArtist(
            "Artist", false, new[] { album }, album.Tracks);
        vm.Artists.Add(artist);
        vm.Albums.Add(album);

        vm.OpenArtist(artist);
        Assert.Equal(LibraryPage.ArtistDetail, vm.Page);
        Assert.True(vm.CanGoBack);

        vm.OpenAlbum(album, fromArtist: true);
        Assert.Equal(LibraryPage.AlbumDetail, vm.Page);
        Assert.Equal("Album", vm.HeaderTitle);

        vm.Back();
        Assert.Equal(LibraryPage.ArtistDetail, vm.Page);

        vm.Back();
        Assert.Equal(LibraryPage.Artists, vm.Page);
        Assert.False(vm.CanGoBack);
    }

    // —— Phase 26: 库 → 歌单转发 ——

    [Fact]
    public void AddTracksToPlaylist_ForwardsTracksInOrder()
    {
        var library = CreateVm();
        var playlists = _lastPlaylists!;
        playlists.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });
        var tracks = new[] { Fallback("a.mp3"), Fallback("b.mp3") };

        var appended = library.AddTracksToPlaylist(playlists.Playlists[0], tracks);

        Assert.True(appended);
        Assert.Equal(new[] { "a.mp3", "b.mp3" },
            playlists.Playlists[0].Queue.Select(t => t.FilePath));
    }

    [Fact]
    public void AddTracksToNewPlaylist_CreatesUnselectedPlaylist()
    {
        var library = CreateVm();
        var playlists = _lastPlaylists!;
        playlists.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });
        var viewed = playlists.ViewedPlaylist;
        var current = playlists.CurrentPlaylistId;

        var created = library.AddTracksToNewPlaylist(
            "New", new[] { Fallback("a.mp3") });

        Assert.NotNull(created);
        Assert.Equal("New", created!.Name);
        Assert.Same(viewed, playlists.ViewedPlaylist);
        Assert.Equal(current, playlists.CurrentPlaylistId);
    }
}
