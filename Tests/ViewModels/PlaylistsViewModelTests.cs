using System;
using System.Threading.Tasks;
using NSubstitute;
using UmaPlayer.Models;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;
using Xunit;

namespace UmaPlayer.Tests.ViewModels;

/// <summary>
/// PlaylistsViewModel 单元测试。
/// 覆盖：HandleDoubleClickPlay、RemovePlaylist 边界、StateChanged 节流。
/// </summary>
public class PlaylistsViewModelTests
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly IFileDialogService _fileDialog = Substitute.For<IFileDialogService>();
    private readonly ITrackMetadataReader _metadataReader = Substitute.For<ITrackMetadataReader>();

    private PlaylistViewModel CreatePlaylistVm(string id = null, string name = "Test")
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

        return new PlaylistViewModel(seed, _player, _fileDialog, _metadataReader);
    }

    private PlaylistsViewModel CreateContainerVm()
    {
        return new PlaylistsViewModel(seed => CreatePlaylistVm(seed.Id, seed.Name));
    }

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
}
