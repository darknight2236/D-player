using System;
using System.Threading.Tasks;
using NSubstitute;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.Services.PlaylistFiles;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

/// <summary>
/// PlaylistsViewModel 单元测试。
/// 覆盖：HandleDoubleClickPlay、RemovePlaylist 边界、StateChanged 节流。
/// </summary>
public class PlaylistsViewModelTests
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly IFileDialogService _fileDialog = Substitute.For<IFileDialogService>();
    private readonly ITrackMetadataReader _metadataReader = Substitute.For<ITrackMetadataReader>();
    private readonly ILibraryScannerService _scanner = Substitute.For<ILibraryScannerService>();
    private readonly ILibraryCache _cache = Substitute.For<ILibraryCache>();
    private readonly IPlaylistFileService _playlistFiles = Substitute.For<IPlaylistFileService>();

    private PlaylistViewModel CreatePlaylistVm(string? id = null, string name = "Test")
    {
        id ??= Guid.NewGuid().ToString();
        var seed = new Playlist(
            Id: id,
            Name: name,
            Items: Array.Empty<string>(),
            CurrentIndex: -1,
            ShuffleEnabled: false,
            RepeatMode: RepeatMode.Off);

        _metadataReader.CreateFallback(Arg.Any<string>())
            .Returns(ci => new Track(ci.ArgAt<string>(0), ci.ArgAt<string>(0), null, null, null, null, null, null, TimeSpan.Zero, null));

        return new PlaylistViewModel(seed, _player, _fileDialog, _metadataReader, _playlistFiles);
    }

    private PlaylistsViewModel CreateContainerVm()
    {
        return new PlaylistsViewModel(seed => CreatePlaylistVm(seed.Id, seed.Name));
    }

    /// <summary>Phase 18 导入用例专用：走公共 ctor，scanner / playlistFiles 可 mock。</summary>
    private PlaylistsViewModel CreateContainerVmWithMocks()
    {
        return new PlaylistsViewModel(
            seed => CreatePlaylistVm(seed.Id, seed.Name),
            _player, _fileDialog, _scanner, _cache, _metadataReader, _playlistFiles);
    }

    private static PlaylistImportResult ImportResult(
        string name, string[] accepted, int missing = 0, int unsupported = 0)
        => new(name, accepted, accepted.Length + missing + unsupported, missing, unsupported);

    private static Track FallbackTrack(string path) =>
        new(path, System.IO.Path.GetFileName(path), null, null, null, null, null, null, TimeSpan.Zero, null);

    // —— Hydrate / BuildSnapshot ——

    [Fact]
    public void Hydrate_CreatesPlaylistsFromSnapshot()
    {
        var container = CreateContainerVm();
        var snapshot = new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        };

        container.Hydrate(snapshot);

        Assert.Equal(2, container.Playlists.Count);
        Assert.Equal("A", container.Playlists[0].Name);
        Assert.Equal("B", container.Playlists[1].Name);
    }

    [Fact]
    public void Hydrate_SetsCurrentPlaylistId()
    {
        var container = CreateContainerVm();
        var snapshot = new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id2"
        };

        container.Hydrate(snapshot);

        Assert.Equal("id2", container.CurrentPlaylistId);
    }

    [Fact]
    public void Hydrate_SetsViewedPlaylist_AlignedToCurrent()
    {
        var container = CreateContainerVm();
        var snapshot = new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id2"
        };

        container.Hydrate(snapshot);

        Assert.Equal("id2", container.ViewedPlaylist?.Id);
    }

    [Fact]
    public void BuildSnapshot_PreservesCurrentPlaylistId()
    {
        var container = CreateContainerVm();
        var snapshot = new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        };
        container.Hydrate(snapshot);

        var result = container.BuildSnapshot();

        Assert.Equal("id1", result.CurrentPlaylistId);
    }

    // —— AddPlaylist ——

    [Fact]
    public void AddPlaylist_AddsToCollection()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });

        container.AddPlaylistCommand.Execute("New Playlist");

        Assert.Equal(2, container.Playlists.Count);
        Assert.Equal("New Playlist", container.Playlists[1].Name);
    }

    [Fact]
    public void AddPlaylist_DefaultsToNewPlaylist_WhenNameEmpty()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });

        container.AddPlaylistCommand.Execute("");

        Assert.Equal("新歌单", container.Playlists[1].Name);
    }

    // —— RemovePlaylist ——

    [Fact]
    public void RemovePlaylist_RemovesFromCollection()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });

        container.RemovePlaylistCommand.Execute(container.Playlists[1]);

        Assert.Single(container.Playlists);
        Assert.Equal("A", container.Playlists[0].Name);
    }

    [Fact]
    public void RemovePlaylist_LastPlaylist_AutoRecreatesDefault()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });

        container.RemovePlaylistCommand.Execute(container.Playlists[0]);

        Assert.Single(container.Playlists);
        Assert.Equal("默认歌单", container.Playlists[0].Name);
    }

    [Fact]
    public void RemovePlaylist_RemovedCurrent_FallsBackToAdjacent()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id3", "C", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id2"
        });

        container.RemovePlaylistCommand.Execute(container.Playlists[1]); // 删 B

        // 删的是当前播放歌单 → 停止播放并清空指针
        Assert.Equal("", container.CurrentPlaylistId);
    }

    // —— RenamePlaylist ——

    [Fact]
    public void RenamePlaylist_ChangesName()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "Old Name", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });

        container.RenamePlaylistCommand.Execute((container.Playlists[0], "New Name"));

        Assert.Equal("New Name", container.Playlists[0].Name);
    }

    [Fact]
    public void RenamePlaylist_SameName_IsNoOp()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "Name", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });
        var stateChangedCount = 0;
        container.StateChanged += (_, _) => stateChangedCount++;

        container.RenamePlaylistCommand.Execute((container.Playlists[0], "Name"));

        Assert.Equal(0, stateChangedCount);
    }

    // —— HandleDoubleClickPlay ——
    // Phase 20: 这条是**两个壳共用**的双击入口（WPF: Views/Controls/PlaylistView.xaml.cs；
    // WinUI: MainWindow.TrackList_DoubleTapped），所以它的语义必须由测试钉住，
    // 而不是让每个壳各自拼一半（那正是两壳漂移的来源）。

    [Fact]
    public async Task HandleDoubleClickPlay_SamePlaylist_DoesNotChangeCurrentPlaylistId()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });
        // 加一个 Track 让 PlayTrackAt 不越界
        container.Playlists[0].Queue.Add(new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero, null));
        _metadataReader.ReadAsync("a.mp3").Returns(Task.FromResult(new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero, null)));

        await container.HandleDoubleClickPlay(container.Playlists[0], 0);

        Assert.Equal("id1", container.CurrentPlaylistId);
        // 有效索引必须真的派出一次播放（load + play），否则"双击出声"这条什么都没测到
        await _player.Received(1).LoadAsync(Arg.Any<Track>());
        _player.Received(1).Play();
        Assert.Equal(0, container.Playlists[0].CurrentIndex);
    }

    [Fact]
    public async Task HandleDoubleClickPlay_DifferentPlaylist_SwitchesCurrentPlaylistId()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });
        container.Playlists[1].Queue.Add(new Track("b.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero, null));
        _metadataReader.ReadAsync("b.mp3").Returns(Task.FromResult(new Track("b.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero, null)));

        await container.HandleDoubleClickPlay(container.Playlists[1], 0);

        Assert.Equal("id2", container.CurrentPlaylistId);
        _player.Received(1).Play();
    }

    /// <summary>
    /// 判别式测试：越界索引必须"什么都不做"。
    /// 只断言 DidNotReceive().Play() 不够 —— PlayTrackAtAsync 自己也有越界分支，
    /// 守卫被删掉时同样不会 Play，那条断言没有鉴别力。删掉 HandleDoubleClickPlay 的范围守卫后，
    /// 调用会落进 PlayTrackAt → PlayTrackAtAsync(99)，其越界分支执行 _player.Stop()
    /// 并把 CurrentIndex 打回 -1 —— 这里钉的就是这两个痕迹。
    /// 判别力实测：把本事实的调用临时换成"守卫已删"的等价调用
    /// <c>target.PlayTrackAtCommand.ExecuteAsync(99)</c> → 该事实变红
    /// （"DidNotReceive(s) Stop() / Actually received 1 matching call: Stop()"），随后已还原。
    /// </summary>
    [Fact]
    public async Task HandleDoubleClickPlay_OutOfRangeIndex_TouchesNothing()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });
        var target = container.Playlists[0];
        target.Queue.Add(new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero, null));
        target.CurrentIndex = 0;   // 留一个可见痕迹：落进越界分支会被打回 -1

        await container.HandleDoubleClickPlay(target, 99);

        _player.DidNotReceive().Play();
        _player.DidNotReceive().Stop();
        await _player.DidNotReceive().LoadAsync(Arg.Any<Track>());
        Assert.Equal(0, target.CurrentIndex);
    }

    // —— IsActivePlaylist ——

    [Fact]
    public void IsActivePlaylist_SetOnHydrate()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });

        Assert.True(container.Playlists[0].IsActivePlaylist);
        Assert.False(container.Playlists[1].IsActivePlaylist);
    }

    [Fact]
    public void IsActivePlaylist_UpdatesOnCurrentPlaylistIdChange()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });

        container.CurrentPlaylistId = "id2";

        Assert.False(container.Playlists[0].IsActivePlaylist);
        Assert.True(container.Playlists[1].IsActivePlaylist);
    }

    // —— MovePlaylist ——

    [Fact]
    public void MovePlaylist_ReordersCollection()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id3", "C", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });

        container.MovePlaylistCommand.Execute((0, 2)); // A 移到 B 后面

        Assert.Equal("B", container.Playlists[0].Name);
        Assert.Equal("A", container.Playlists[1].Name);
        Assert.Equal("C", container.Playlists[2].Name);
    }

    [Fact]
    public void MovePlaylist_SamePosition_IsNoOp()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });
        var stateChangedCount = 0;
        container.StateChanged += (_, _) => stateChangedCount++;

        container.MovePlaylistCommand.Execute((0, 0)); // 原位

        Assert.Equal(0, stateChangedCount);
    }

    [Fact]
    public void MovePlaylist_AdjacentPosition_IsNoOp()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });

        container.MovePlaylistCommand.Execute((0, 1)); // A 移到 B 前面 = 原位

        Assert.Equal("A", container.Playlists[0].Name);
        Assert.Equal("B", container.Playlists[1].Name);
    }

    [Fact]
    public void MovePlaylist_UpdatesViewedPlaylist()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });
        container.ViewedPlaylist = container.Playlists[0]; // 选中 A

        container.MovePlaylistCommand.Execute((0, 2)); // A 移到末尾

        Assert.Equal("id1", container.ViewedPlaylist?.Id); // 跟随对象身份
    }

    // —— StateChanged ——

    [Fact]
    public void StateChanged_FiredOnAddPlaylist()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[] { new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off) },
            CurrentPlaylistId = "id1"
        });
        var fired = false;
        container.StateChanged += (_, _) => fired = true;

        container.AddPlaylistCommand.Execute("New");

        Assert.True(fired);
    }

    [Fact]
    public void StateChanged_NotFiredOnIsActivePlaylistChange()
    {
        var container = CreateContainerVm();
        container.Hydrate(new QueueState
        {
            Playlists = new[]
            {
                new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off),
                new Playlist("id2", "B", Array.Empty<string>(), -1, false, RepeatMode.Off),
            },
            CurrentPlaylistId = "id1"
        });
        var stateChangedCount = 0;
        container.StateChanged += (_, _) => stateChangedCount++;

        // 切 CurrentPlaylistId 会触发 StateChanged（因为 CurrentPlaylistId 本身变了）
        // 但 IsActivePlaylist 的批量设置不应额外触发
        var countBefore = stateChangedCount;
        container.CurrentPlaylistId = "id2";

        // StateChanged 应只触发一次（CurrentPlaylistId 变化），而不是三次（id + 两个 IsActivePlaylist）
        Assert.Equal(countBefore + 1, stateChangedCount);
    }

    // —— Phase 18: 播放列表文件导入（容器级 = 新建歌单） ——

    [Fact]
    public async Task ImportPlaylistFileAsync_CreatesNewPlaylistAndViewIt()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\rock.m3u8" });
        _playlistFiles.ImportAsync(@"D:\lists\rock.m3u8")
            .Returns(ImportResult("rock", new[] { @"D:\m\a.mp3", @"D:\m\b.flac" }));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(new[] { FallbackTrack(@"D:\m\a.mp3"), FallbackTrack(@"D:\m\b.flac") });
        var container = CreateContainerVmWithMocks();
        int stateChanged = 0;
        container.StateChanged += (_, _) => stateChanged++;

        var report = await container.ImportPlaylistFileAsync();

        Assert.NotNull(report);
        Assert.True(report!.CreatedNewPlaylist);
        Assert.Equal(2, report.Imported);
        Assert.Equal("rock", report.PlaylistName);
        Assert.Single(container.Playlists);
        Assert.Equal("rock", container.Playlists[0].Name);
        Assert.Same(container.Playlists[0], container.ViewedPlaylist);
        Assert.Null(container.Playlists[0].SourceFolder);      // 普通歌单，不走 library cache
        Assert.Equal(2, container.Playlists[0].Queue.Count);
        Assert.True(stateChanged > 0);                          // 触发 debounce 存盘
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_AllEntriesSkipped_DoesNotCreatePlaylist()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\old.pls" });
        _playlistFiles.ImportAsync(@"D:\lists\old.pls")
            .Returns(ImportResult("old", Array.Empty<string>(), missing: 3, unsupported: 2));
        var container = CreateContainerVmWithMocks();

        var report = await container.ImportPlaylistFileAsync();

        Assert.Equal(0, report!.Imported);
        Assert.Null(report.PlaylistName);
        Assert.False(report.CreatedNewPlaylist);
        Assert.Empty(container.Playlists);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_NoTracksFromMetadataBatch_DoesNotCreatePlaylist()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\x.m3u" });
        _playlistFiles.ImportAsync(@"D:\lists\x.m3u").Returns(ImportResult("x", new[] { @"D:\m\a.mp3" }));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(Array.Empty<Track>());
        var container = CreateContainerVmWithMocks();

        var report = await container.ImportPlaylistFileAsync();

        Assert.Equal(0, report!.Imported);
        Assert.Empty(container.Playlists);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_UserCancels_ReturnsNull()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(Array.Empty<string>());
        var container = CreateContainerVmWithMocks();

        var report = await container.ImportPlaylistFileAsync();

        Assert.Null(report);
        await _playlistFiles.DidNotReceive().ImportAsync(Arg.Any<string>());
        Assert.Empty(container.Playlists);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_PresetPath_SkipsFileDialog()
    {
        _playlistFiles.ImportAsync(@"D:\drop\y.m3u8").Returns(ImportResult("y", new[] { @"D:\m\a.mp3" }));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(new[] { FallbackTrack(@"D:\m\a.mp3") });
        var container = CreateContainerVmWithMocks();

        var report = await container.ImportPlaylistFileAsync(@"D:\drop\y.m3u8");

        Assert.Equal(1, report!.Imported);
        Assert.Equal("y", container.Playlists[0].Name);
        _fileDialog.DidNotReceive().OpenFiles(Arg.Any<string>(), Arg.Any<bool>());
    }
}
