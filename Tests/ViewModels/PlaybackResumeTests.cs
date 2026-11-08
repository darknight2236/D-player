using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.Services.PlaylistFiles;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

/// <summary>
/// 断点续播单元测试：PlayerViewModel 的"上次播放"落盘时机（暂停/停止/切歌即写、播放中 30s 节流、
/// 关闭兜底）与 MainViewModel 的启动恢复（只就位不出声；文件缺失/不在歌单中则不恢复并给出提示）。
/// </summary>
public class PlaybackResumeTests : IDisposable
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly ISettingsPersistence _settings = Substitute.For<ISettingsPersistence>();
    private readonly IPlaylistService _playlistService = Substitute.For<IPlaylistService>();
    private readonly IFileDialogService _fileDialog = Substitute.For<IFileDialogService>();
    private readonly ITrackMetadataReader _metadataReader = Substitute.For<ITrackMetadataReader>();
    private readonly IPlaylistFileService _playlistFiles = Substitute.For<IPlaylistFileService>();
    private readonly ILibraryRootScanner _rootScanner = Substitute.For<ILibraryRootScanner>();
    private readonly ILibraryCache _cache = Substitute.For<ILibraryCache>();
    private readonly ILibraryWatcher _watcher = Substitute.For<ILibraryWatcher>();
    private readonly DPlayer.Tests.TestSupport.ManualTimeProvider _clock = new();

    /// <summary>每次 UpdateAsync 的 mutator 应用到全新 AppSettings 后的结果（按调用顺序）。</summary>
    private readonly List<AppSettings> _writes = new();

    private readonly string _tempTrack = Path.Combine(Path.GetTempPath(), $"dplayer-resume-{Guid.NewGuid():N}.mp3");
    private readonly string _tempTrack2 = Path.Combine(Path.GetTempPath(), $"dplayer-resume-{Guid.NewGuid():N}.mp3");

    public PlaybackResumeTests()
    {
        // 只要求 File.Exists 为真（歌单水化会过滤不存在的文件）；元数据全走替代物
        File.WriteAllBytes(_tempTrack, new byte[] { 0 });
        File.WriteAllBytes(_tempTrack2, new byte[] { 0 });

        _settings.LoadAsync().Returns(Task.FromResult(new AppSettings()));
        _settings.UpdateAsync(Arg.Do<Func<AppSettings, AppSettings>>(f => _writes.Add(f(new AppSettings()))));

        _metadataReader.CreateFallback(Arg.Any<string>())
            .Returns(ci => new Track(ci.ArgAt<string>(0), Path.GetFileName(ci.ArgAt<string>(0)),
                null, null, null, null, null, null, TimeSpan.Zero, null));
        _rootScanner.ScanAsync(Arg.Any<string>(), Arg.Any<LibraryScanLease>())
            .Returns(ci => Task.FromResult(new LibraryRootSnapshot(
                ci.ArgAt<string>(0), Array.Empty<LibraryCacheEntry>(),
                IsUnavailable: true, CacheWriteFailed: false,
                Array.Empty<string>(), Array.Empty<string>())));
        _watcher.ReplaceRootsAsync(Arg.Any<long>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
    }

    public void Dispose()
    {
        File.Delete(_tempTrack);
        File.Delete(_tempTrack2);
    }

    private PlayerViewModel CreatePlayerVm()
        => new(_player, _settings, Options.Create(new AppSettings { DefaultVolume = 0.5f }));

    private MainViewModel CreateMainVm()
    {
        var playlists = new PlaylistsViewModel(
            seed => new PlaylistViewModel(seed, _player, _fileDialog, _metadataReader, _playlistFiles),
            Substitute.For<ILibraryRootScanner>());
        var library = new LibraryViewModel(
            _settings, _rootScanner, _cache, _metadataReader, playlists,
            _watcher, _clock);
        return new MainViewModel(
            CreatePlayerVm(), playlists, library, _player, _playlistService, _settings);
    }

    private static Track TrackWith(string path)
        => new(path, "T", "A", null, null, null, null, null, TimeSpan.Zero, null);

    private static QueueState SnapshotWith(params string[] items)
        => new()
        {
            SchemaVersion = 3,
            Playlists = new[] { new Playlist("p1", "P", items, 0, false, RepeatMode.Off) },
            CurrentPlaylistId = "p1",
        };

    private static AppSettings LastPlayed(string path, double seconds)
        => new() { LastPlayedPath = path, LastPlayedPositionSeconds = seconds };

    private AppSettings? LastPlayedWrite => _writes.LastOrDefault(w => w.LastPlayedPath is not null);

    // —— 落盘时机（PlayerViewModel） ——

    [Fact]
    public void Pause_WritesLastPlayedState()
    {
        _player.CurrentTrack.Returns(TrackWith(_tempTrack));
        _player.Position.Returns(TimeSpan.FromSeconds(92));
        _ = CreatePlayerVm();

        _player.StateChanged += Raise.Event<Action<PlayState>>(PlayState.Paused);

        Assert.Equal(_tempTrack, LastPlayedWrite?.LastPlayedPath);
        Assert.Equal(92d, LastPlayedWrite!.LastPlayedPositionSeconds, 3);
    }

    [Fact]
    public void TrackChanged_WritesNewTrackAtStart()
    {
        _player.CurrentTrack.Returns(TrackWith(_tempTrack));
        _player.Position.Returns(TimeSpan.Zero);
        _ = CreatePlayerVm();

        _player.TrackChanged += Raise.Event<Action<Track?>>(TrackWith(_tempTrack));

        Assert.Equal(_tempTrack, LastPlayedWrite?.LastPlayedPath);
        Assert.Equal(0d, LastPlayedWrite!.LastPlayedPositionSeconds, 3);
    }

    [Fact]
    public void PositionTicks_WhilePlaying_AreThrottledToASingleWrite()
    {
        _player.State.Returns(PlayState.Playing);
        _player.CurrentTrack.Returns(TrackWith(_tempTrack));
        _player.Position.Returns(TimeSpan.FromSeconds(10));
        _ = CreatePlayerVm();
        _settings.ClearReceivedCalls();

        _player.PositionChanged += Raise.Event<Action<TimeSpan>>(TimeSpan.FromSeconds(10));
        _player.PositionChanged += Raise.Event<Action<TimeSpan>>(TimeSpan.FromSeconds(10.03));
        _player.PositionChanged += Raise.Event<Action<TimeSpan>>(TimeSpan.FromSeconds(10.06));

        _settings.Received(1).UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>());
    }

    [Fact]
    public void NoCurrentTrack_DoesNotWrite()
    {
        _player.CurrentTrack.Returns((Track?)null);
        _ = CreatePlayerVm();
        _settings.ClearReceivedCalls();

        _player.StateChanged += Raise.Event<Action<PlayState>>(PlayState.Stopped);

        _settings.DidNotReceive().UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>());
    }

    [Fact]
    public async Task CleanupAsync_WritesFinalState()
    {
        _player.CurrentTrack.Returns(TrackWith(_tempTrack));
        _player.Position.Returns(TimeSpan.FromSeconds(7));
        var vm = CreatePlayerVm();

        await vm.CleanupAsync();

        Assert.Equal(_tempTrack, LastPlayedWrite?.LastPlayedPath);
        Assert.Equal(7d, LastPlayedWrite!.LastPlayedPositionSeconds, 3);
    }

    // —— 启动恢复（MainViewModel） ——

    [Fact]
    public async Task InitializeAsync_RestoresTrackAndPosition_WithoutPlaying()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
        _settings.LoadAsync().Returns(Task.FromResult(LastPlayed(_tempTrack, 42)));
        var vm = CreateMainVm();

        var warning = await vm.InitializeAsync();

        Assert.Null(warning);
        await _player.Received(1).LoadAsync(Arg.Is<Track>(t => t.FilePath == _tempTrack));
        _player.Received(1).Seek(TimeSpan.FromSeconds(42));
        _player.DidNotReceive().Play();
    }

    [Fact]
    public async Task InitializeAsync_ZeroPosition_LoadsWithoutSeeking()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
        _settings.LoadAsync().Returns(Task.FromResult(LastPlayed(_tempTrack, 0)));
        var vm = CreateMainVm();

        var warning = await vm.InitializeAsync();

        Assert.Null(warning);
        await _player.Received(1).LoadAsync(Arg.Any<Track>());
        _player.DidNotReceive().Seek(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task InitializeAsync_MissingFile_SkipsRestoreAndWarns()
    {
        var gone = Path.Combine(Path.GetTempPath(), $"dplayer-gone-{Guid.NewGuid():N}.mp3");
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
        _settings.LoadAsync().Returns(Task.FromResult(LastPlayed(gone, 42)));
        var vm = CreateMainVm();

        var warning = await vm.InitializeAsync();

        Assert.NotNull(warning);
        Assert.Contains(gone, warning);
        await _player.DidNotReceive().LoadAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task InitializeAsync_NotInAnyPlaylist_SkipsRestoreAndWarns()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
        _settings.LoadAsync().Returns(Task.FromResult(LastPlayed(_tempTrack2, 42)));
        var vm = CreateMainVm();

        var warning = await vm.InitializeAsync();

        Assert.NotNull(warning);
        Assert.Contains(_tempTrack2, warning);
        await _player.DidNotReceive().LoadAsync(Arg.Any<Track>());
    }

    /// <summary>
    /// 恢复的曲目可能属于另一个歌单（旧版本从 ViewedPlaylist 发起播放时不会同步 CurrentPlaylistId）。
    /// 恢复时必须把"当前播放歌单"指到曲目真正所属的歌单，否则用户按 ▶ 出声后、
    /// 自然播完时 TrackEnded 守卫会推进错误的歌单。
    /// </summary>
    [Fact]
    public async Task InitializeAsync_RestoredTrackInAnotherPlaylist_ClaimsCurrentPlaylistId()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(new QueueState
        {
            SchemaVersion = 3,
            Playlists = new[]
            {
                new Playlist("p1", "P1", Array.Empty<string>(), 0, false, RepeatMode.Off),
                new Playlist("p2", "P2", new[] { _tempTrack }, 0, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "p1",
        }));
        _settings.LoadAsync().Returns(Task.FromResult(LastPlayed(_tempTrack, 0)));
        var vm = CreateMainVm();

        var warning = await vm.InitializeAsync();

        Assert.Null(warning);
        Assert.Equal("p2", vm.Playlists.CurrentPlaylistId);
        Assert.False(vm.Playlists.Playlists[0].IsActivePlaylist);
        Assert.True(vm.Playlists.Playlists[1].IsActivePlaylist);
        await _player.Received(1).LoadAsync(Arg.Is<Track>(t => t.FilePath == _tempTrack));
    }

    [Fact]
    public async Task InitializeAsync_NoLastPlayed_DoesNotRestoreNorWarn()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
        var vm = CreateMainVm();

        var warning = await vm.InitializeAsync();

        Assert.Null(warning);
        await _player.DidNotReceive().LoadAsync(Arg.Any<Track>());
    }

    [Fact]
    public async Task InitializeAsync_Restore_PersistsTheRestoredState()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
        _settings.LoadAsync().Returns(Task.FromResult(LastPlayed(_tempTrack, 42)));
        // 恢复后的回写读的是播放服务的实时状态（真实实现里 LoadAsync/Seek 会让它指向恢复目标）
        _player.CurrentTrack.Returns(TrackWith(_tempTrack));
        _player.Position.Returns(TimeSpan.FromSeconds(42));
        var vm = CreateMainVm();

        await vm.InitializeAsync();

        // 恢复后立即回写一次：启动后若立刻退出/被杀，不会把位置退回 0
        Assert.Equal(_tempTrack, LastPlayedWrite?.LastPlayedPath);
        Assert.Equal(42d, LastPlayedWrite!.LastPlayedPositionSeconds, 3);
    }

    // —— Phase 24: 库播放队列断点续播与导航 ——

    /// <summary>
    /// 库会话续播必须拿到**缓存回填后**的库播放队列曲目，并且当前播放上下文留在库队列上。
    /// 为了让"回填先于恢复"这件事真的被钉住：
    ///   - 真实存在的库根目录 + 该目录的缓存条目（标题 "Cached"），否则 CachedEntriesByPath
    ///     为空、回填是空操作，测试无法区分顺序；
    ///   - 同一个文件同时出现在可见歌单 p1 与库播放队列里，只有库队列的标题是 "Cached"，
    ///     所以断言标题即可区分"回填过的库队列"与"占位/可见歌单的兜底曲目"；
    ///   - 根目录标记为不可用，后台扫描直接跳过它，不会与断言竞争。
    /// </summary>
    [Fact]
    public async Task InitializeAsync_RestoresLibraryPlaybackTrack_AndPosition()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dplayer-resume-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            const string cachedTitle = "Cached";
            var libraryPlayback = new Playlist(
                PlaylistsViewModel.LibraryPlaybackId, "音乐库播放",
                new[] { _tempTrack }, 0, false, RepeatMode.Off);
            _playlistService.LoadAsync().Returns(Task.FromResult(new QueueState
            {
                Playlists = new[]
                {
                    // 同一文件也在可见歌单里：占位标题 = 文件名，与 "Cached" 可区分。
                    new Playlist("p1", "P", new[] { _tempTrack }, 0, false, RepeatMode.Off),
                },
                CurrentPlaylistId = libraryPlayback.Id,
                LibraryPlayback = libraryPlayback,
            }));
            _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
            {
                LibraryFolders = new[] { root },
                LastPlayedPath = _tempTrack,
                LastPlayedPositionSeconds = 42,
            }));
            _cache.LoadAsync(root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                new[]
                {
                    new LibraryCacheEntry(_tempTrack, cachedTitle, "Artist", "Album",
                        null, 2024, TimeSpan.FromMinutes(3), 44100, 1),
                }));
            // 根目录不可用 → 后台扫描跳过该根，不能改写缓存/分组。

            var vm = CreateMainVm();
            var warning = await vm.InitializeAsync();

            Assert.Null(warning);
            Assert.NotNull(vm.Playlists.LibraryPlayback);
            Assert.Equal(libraryPlayback.Id, vm.Playlists.CurrentPlaylistId);
            // 关键断言：恢复的是被缓存回填过的库队列曲目（Title = "Cached"），
            // 而不是占位曲目/可见歌单里的同名文件。删掉回填或把它挪到恢复之后，这里会变红。
            await _player.Received(1).LoadAsync(Arg.Is<Track>(
                t => t.FilePath == _tempTrack && t.Title == cachedTitle));
            _player.Received(1).Seek(TimeSpan.FromSeconds(42));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task InitializeAsync_RestoredLibraryTrackWithoutAlbumArt_ReadsCoverFromFile()
    {
        var libraryPlayback = new Playlist(
            PlaylistsViewModel.LibraryPlaybackId, "音乐库播放",
            new[] { _tempTrack }, 0, false, RepeatMode.Off);
        _playlistService.LoadAsync().Returns(Task.FromResult(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("p1", "P", new[] { _tempTrack }, 0, false, RepeatMode.Off),
            },
            CurrentPlaylistId = libraryPlayback.Id,
            LibraryPlayback = libraryPlayback,
        }));
        _settings.LoadAsync().Returns(Task.FromResult(LastPlayed(_tempTrack, 42)));
        var art = new byte[] { 1, 2, 3 };
        _metadataReader.ReadAsync(_tempTrack).Returns(Task.FromResult(new Track(
            _tempTrack, "Disk Title", "Disk Artist", "Disk Album",
            null, null, 44100, art, TimeSpan.FromMinutes(2), 1)));

        var vm = CreateMainVm();
        var warning = await vm.InitializeAsync();

        Assert.Null(warning);
        await _metadataReader.Received(1).ReadAsync(_tempTrack);
        await _player.Received(1).LoadAsync(Arg.Is<Track>(
            t => t.FilePath == _tempTrack && t.AlbumArt != null && t.AlbumArt.Length == art.Length));
    }

    [Fact]
    public async Task InitializeAsync_SetsIsInitialized_AfterRestore()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
        var vm = CreateMainVm();

        Assert.False(vm.IsInitialized);
        await vm.InitializeAsync();

        Assert.True(vm.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotStartWatcherMonitoring()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith()));
        var vm = CreateMainVm();

        await vm.InitializeAsync();

        await _watcher.DidNotReceive().ReplaceRootsAsync(
            Arg.Any<long>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
        await vm.CleanupAsync();
    }

    [Fact]
    public async Task InitializeAsync_RestoresLibraryAlbumDetailNavigation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dplayer-resume-nav-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
            _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
            {
                LibraryFolders = new[] { root },
                LibraryViewActive = true,
                LibraryViewPage = nameof(LibraryPage.AlbumDetail),
                LibraryViewAlbumTrackPath = _tempTrack,
            }));
            _cache.LoadAsync(root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                new[]
                {
                    new LibraryCacheEntry(_tempTrack, "Title", "Artist", "Album",
                        null, 2024, TimeSpan.FromMinutes(3), 44100, 1),
                }));

            var vm = CreateMainVm();
            var warning = await vm.InitializeAsync();

            Assert.Null(warning);
            Assert.True(vm.IsLibraryViewActive);
            Assert.Equal(LibraryPage.AlbumDetail, vm.Library.Page);
            Assert.Equal("Album", vm.Library.SelectedAlbum?.Title);
            Assert.Contains(vm.Library.SelectedAlbum!.Tracks, t => t.FilePath == _tempTrack);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task InitializeAsync_RestoresLibraryView_ClearsViewedPlaylist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dplayer-resume-view-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
            _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
            {
                LibraryFolders = new[] { root },
                LibraryViewActive = true,
                LibraryViewPage = nameof(LibraryPage.Artists),
            }));

            var vm = CreateMainVm();
            await vm.InitializeAsync();

            Assert.True(vm.IsLibraryViewActive);
            Assert.Null(vm.Playlists.ViewedPlaylist);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task CleanupAsync_PersistsLibraryNavigationState()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
        var vm = CreateMainVm();
        await vm.InitializeAsync();

        var album = new LibraryAlbum(
            "Album", "Artist", false, 2024, new[] { TrackWith(_tempTrack) });
        vm.IsLibraryViewActive = true;
        vm.Library.OpenAlbum(album, fromArtist: false);
        _writes.Clear();

        await vm.CleanupAsync();

        var nav = _writes.LastOrDefault(w => w.LibraryViewActive);
        Assert.NotNull(nav);
        Assert.Equal(nameof(LibraryPage.AlbumDetail), nav.LibraryViewPage);
        Assert.Equal(_tempTrack, nav.LibraryViewAlbumTrackPath);
    }

    [Fact]
    public async Task CleanupAsync_IsIdempotent_AndReturnsSameTask()
    {
        _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith()));
        var vm = CreateMainVm();
        await vm.InitializeAsync();

        var first = vm.CleanupAsync();
        var second = vm.CleanupAsync();

        Assert.Same(first, second);
        await first.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CleanupAsync_WhileSearching_PersistsSourceNavigation()
    {
        var root = Path.Combine(Path.GetTempPath(),
            $"dplayer-resume-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith()));
            _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
            {
                LibraryFolders = new[] { root },
            }));
            _cache.LoadAsync(root).Returns(Task.FromResult<IReadOnlyList<LibraryCacheEntry>>(
                new[]
                {
                    new LibraryCacheEntry(_tempTrack, "Title", "Artist", "Album",
                        null, 2024, TimeSpan.FromMinutes(3), 44100, 1),
                }));
            var vm = CreateMainVm();
            await vm.InitializeAsync();
            vm.IsLibraryViewActive = true;
            vm.Library.OpenAlbum(Assert.Single(vm.Library.Albums), fromArtist: false);
            _writes.Clear();

            var search = vm.Library.UpdateSearchAsync("Title");
            _clock.Advance(TimeSpan.FromMilliseconds(200));
            await search.WaitAsync(TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.True(vm.Library.IsSearchPage);

            await vm.CleanupAsync();

            var nav = _writes.LastOrDefault(w => w.LibraryViewActive);
            Assert.NotNull(nav);
            Assert.Equal(nameof(LibraryPage.AlbumDetail), nav!.LibraryViewPage);
            Assert.Equal(_tempTrack, nav.LibraryViewAlbumTrackPath);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// 库初始化失败（缓存读抛异常）是可选能力的失败：启动仍要完成断点续播，
    /// 不能把 MainViewModel 留在半初始化状态。钉住 InitializeAsync 的 try/catch 包裹。
    /// </summary>
    [Fact]
    public async Task InitializeAsync_LibraryInitThrows_StillRestoresResume()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dplayer-resume-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            _playlistService.LoadAsync().Returns(Task.FromResult(SnapshotWith(_tempTrack)));
            _settings.LoadAsync().Returns(Task.FromResult(new AppSettings
            {
                LibraryFolders = new[] { root },
                LastPlayedPath = _tempTrack,
                LastPlayedPositionSeconds = 42,
            }));
            _cache.LoadAsync(root).Returns(Task.FromException<IReadOnlyList<LibraryCacheEntry>>(
                new InvalidOperationException("probe: library cache blew up")));

            var vm = CreateMainVm();
            var warning = await vm.InitializeAsync();

            Assert.Null(warning);
            await _player.Received(1).LoadAsync(Arg.Is<Track>(t => t.FilePath == _tempTrack));
            _player.Received(1).Seek(TimeSpan.FromSeconds(42));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void ShowLibrary_AndShowPlaylist_ToggleShellState()
    {
        var vm = CreateMainVm();

        vm.ShowLibrary(LibraryPage.Albums);
        Assert.True(vm.IsLibraryViewActive);
        Assert.Equal(LibraryPage.Albums, vm.Library.Page);

        vm.ShowPlaylist();
        Assert.False(vm.IsLibraryViewActive);
    }
}
