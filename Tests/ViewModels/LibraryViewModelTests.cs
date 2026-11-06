using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.Services.PlaylistFiles;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

public sealed class LibraryViewModelTests : IDisposable
{
    private readonly ISettingsPersistence _settings = Substitute.For<ISettingsPersistence>();
    private readonly ILibraryScannerService _scanner = Substitute.For<ILibraryScannerService>();
    private readonly ILibraryCache _cache = Substitute.For<ILibraryCache>();
    private readonly ITrackMetadataReader _metadata = Substitute.For<ITrackMetadataReader>();
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly IFileDialogService _fileDialog = Substitute.For<IFileDialogService>();
    private readonly IPlaylistFileService _playlistFiles = Substitute.For<IPlaylistFileService>();

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"dplayer-library-{Guid.NewGuid():N}");

    public LibraryViewModelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private LibraryViewModel CreateVm(params string[] roots)
    {
        _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
        {
            LibraryFolders = roots,
        }));
        _metadata.CreateFallback(Arg.Any<string>())
            .Returns(ci => Fallback(ci.ArgAt<string>(0)));

        var playlists = new PlaylistsViewModel(
            seed => new PlaylistViewModel(
                seed, _player, _fileDialog, _metadata, _playlistFiles),
            Substitute.For<ILibraryRootScanner>());

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        try
        {
            return new LibraryViewModel(_settings, _scanner, _cache, _metadata, playlists);
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
        await _metadata.DidNotReceive().ReadAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ScanInBackgroundAsync_AddsNewFiles_SavesCache_UsesFallbackWhenMetadataFails()
    {
        var newFile = Path.Combine(_root, "new.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            Array.Empty<LibraryCacheEntry>()));
        _scanner.ScanFolderWithStatus(_root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(new[] { newFile }, IsUnavailable: false));
        _scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(
                new[] { newFile }, Array.Empty<string>(), Array.Empty<LibraryCacheEntry>()));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>()));

        await vm.InitializeAsync();
        await vm.ScanInBackgroundAsync();

        Assert.Single(vm.Albums);
        Assert.Equal("new", vm.Albums[0].Tracks[0].Title);
        Assert.False(vm.IsScanning);
        await _cache.Received(1).SaveAsync(
            _root,
            Arg.Is<IReadOnlyList<LibraryCacheEntry>>(list =>
                list.Count == 1 && list[0].FilePath == newFile));
    }

    [Fact]
    public async Task ScanInBackgroundAsync_RemovesDeletedTracks()
    {
        var removed = Path.Combine(_root, "gone.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(removed, "Gone", "Artist", "Album") }));
        _scanner.ScanFolderWithStatus(_root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(Array.Empty<string>(), IsUnavailable: false));
        _scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(
                Array.Empty<string>(), new[] { removed },
                Array.Empty<LibraryCacheEntry>()));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>()));

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
        _scanner.DidNotReceive().ScanFolderWithStatus(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _scanner.DidNotReceive().ScanFolder(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _scanner.DidNotReceive().ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanInBackgroundAsync_UnavailableRoot_KeepsCacheAndSkipsSave()
    {
        var cachedPath = Path.Combine(_root, "keep.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(cachedPath, "Keep", "Artist", "Album") }));
        _scanner.ScanFolderWithStatus(_root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(Array.Empty<string>(), IsUnavailable: true));

        await vm.InitializeAsync();
        Assert.Single(vm.Albums);

        await vm.ScanInBackgroundAsync();

        // 不可用 ≠ 全部删除：内存里的专辑与其缓存条目都必须保留。
        Assert.Single(vm.Albums);
        Assert.Single(vm.Artists);
        Assert.Equal("Keep", vm.CachedEntriesByPath[cachedPath].Title);
        Assert.Equal(1, vm.UnavailableRootCount);
        Assert.False(vm.IsScanning);
        _scanner.DidNotReceive().ComputeDiff(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<LibraryCacheEntry>>());
        await _cache.DidNotReceive().SaveAsync(
            Arg.Any<string>(), Arg.Any<IReadOnlyList<LibraryCacheEntry>>());
    }

    [Fact]
    public async Task ScanInBackgroundAsync_EmptyAvailableRoot_RemovesTracksAndSavesEmptyCache()
    {
        var cachedPath = Path.Combine(_root, "gone.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(cachedPath, "Gone", "Artist", "Album") }));
        _scanner.ScanFolderWithStatus(_root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(Array.Empty<string>(), IsUnavailable: false));
        _scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(
                Array.Empty<string>(), new[] { cachedPath },
                Array.Empty<LibraryCacheEntry>()));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>()));

        await vm.InitializeAsync();
        Assert.Single(vm.Albums);

        await vm.ScanInBackgroundAsync();

        // 可枚举但为空 → 真的是全部删除：内存与缓存都应被清空。
        Assert.Empty(vm.Albums);
        Assert.Empty(vm.Artists);
        Assert.Equal(0, vm.UnavailableRootCount);
        Assert.False(vm.IsScanning);
        await _cache.Received(1).SaveAsync(
            _root,
            Arg.Is<IReadOnlyList<LibraryCacheEntry>>(list => list.Count == 0));
    }

    [Fact]
    public async Task ScanInBackgroundAsync_SaveThrows_KeepsInMemoryAlbums()
    {
        var newFile = Path.Combine(_root, "new.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            Array.Empty<LibraryCacheEntry>()));
        _scanner.ScanFolderWithStatus(_root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(new[] { newFile }, IsUnavailable: false));
        _scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(
                new[] { newFile }, Array.Empty<string>(), Array.Empty<LibraryCacheEntry>()));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>()));
        _cache.SaveAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(Task.FromException(new IOException("disk full")));

        await vm.InitializeAsync();
        await vm.ScanInBackgroundAsync();

        Assert.Single(vm.Albums);
        Assert.Single(vm.Albums[0].Tracks);
        Assert.False(vm.IsScanning);
        await _cache.Received(1).SaveAsync(
            _root, Arg.Any<IReadOnlyList<LibraryCacheEntry>>());
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
        _scanner.ScanFolderWithStatus(rootA, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(new[] { shared }, IsUnavailable: false));
        _scanner.ScanFolderWithStatus(rootB, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(new[] { shared }, IsUnavailable: false));
        _scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(
                new[] { shared }, Array.Empty<string>(), Array.Empty<LibraryCacheEntry>()));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>()));

        await vm.InitializeAsync();
        await vm.ScanInBackgroundAsync();

        // 嵌套根目录返回同一文件：端到端只能计一次（专辑/曲目/缓存都是）。
        Assert.Single(vm.Artists);
        Assert.Single(vm.Albums);
        Assert.Single(vm.Albums[0].Tracks);
        Assert.Single(vm.CachedEntriesByPath);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task CachedEntriesByPath_ReturnsSnapshot_NotLiveDictionary()
    {
        var cachedPath = Path.Combine(_root, "snap.mp3");
        var vm = CreateVm(_root);
        _cache.LoadAsync(_root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
            new[] { Entry(cachedPath, "Snap", "Artist", "Album") }));
        _scanner.ScanFolderWithStatus(_root, Arg.Any<CancellationToken>())
            .Returns(new LibraryScanResult(Array.Empty<string>(), IsUnavailable: false));
        _scanner.ComputeDiff(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyList<LibraryCacheEntry>>())
            .Returns(new LibraryDiff(
                Array.Empty<string>(), new[] { cachedPath },
                Array.Empty<LibraryCacheEntry>()));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>()));

        await vm.InitializeAsync();
        var snapshot = vm.CachedEntriesByPath;
        Assert.Single(snapshot);
        Assert.NotSame(snapshot, vm.CachedEntriesByPath);

        await vm.ScanInBackgroundAsync();

        // 先前拿到的快照不受后续扫描影响，内部字典已被清空。
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
        _scanner.ClearReceivedCalls();
        _cache.ClearReceivedCalls();

        await vm.RefreshIfRootsChangedAsync();

        _scanner.DidNotReceive().ScanFolderWithStatus(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().LoadAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task RefreshIfRootsChangedAsync_ChangedRoots_Scans()
    {
        var other = Path.Combine(Path.GetTempPath(), $"dplayer-library-other-{Guid.NewGuid():N}");
        Directory.CreateDirectory(other);
        try
        {
            var vm = CreateVm(_root);
            _cache.LoadAsync(Arg.Any<string>()).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                Array.Empty<LibraryCacheEntry>()));
            _scanner.ScanFolderWithStatus(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new LibraryScanResult(Array.Empty<string>(), IsUnavailable: false));
            await vm.InitializeAsync();
            _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
            {
                LibraryFolders = new[] { other },
            }));
            _scanner.ClearReceivedCalls();

            await vm.RefreshIfRootsChangedAsync();

            _scanner.Received(1).ScanFolderWithStatus(other, Arg.Any<CancellationToken>());
        }
        finally
        {
            try { Directory.Delete(other, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
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
}
