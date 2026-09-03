using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

/// <summary>
/// PlaylistViewModel 单元测试。
/// 覆盖：队列操作、Shuffle/Repeat 推进算法、RemoveTrack 索引修正。
///
/// 注意：PlaylistViewModel 构造器会用 File.Exists 过滤 seed.Items，
/// 所以测试中不传真实路径（会被过滤掉），而是构造空 VM 后手动 AddToQueue。
/// </summary>
public class PlaylistViewModelTests
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly IFileDialogService _fileDialog = Substitute.For<IFileDialogService>();
    private readonly ITrackMetadataReader _metadataReader = Substitute.For<ITrackMetadataReader>();

    private PlaylistViewModel CreateVm(string id = "test-id", string name = "Test")
    {
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

    /// <summary>手动往 Queue 里加 N 个占位 Track（绕过 File.Exists 过滤）。</summary>
    private void AddTracks(PlaylistViewModel vm, int count)
    {
        for (int i = 0; i < count; i++)
            vm.Queue.Add(_metadataReader.CreateFallback($"track{i}.mp3"));
    }

    // —— 基本属性 ——

    [Fact]
    public void Constructor_SetsIdAndName()
    {
        var vm = CreateVm(id: "abc", name: "My Playlist");
        Assert.Equal("abc", vm.Id);
        Assert.Equal("My Playlist", vm.Name);
    }

    [Fact]
    public void Queue_AddTracksManually()
    {
        var vm = CreateVm();
        AddTracks(vm, 3);
        Assert.Equal(3, vm.Queue.Count);
    }

    [Fact]
    public void Constructor_DefaultCurrentIndex_IsMinusOne()
    {
        var vm = CreateVm();
        AddTracks(vm, 3);
        Assert.Equal(-1, vm.CurrentIndex);
    }

    // —— RemoveTrack ——

    [Fact]
    public void RemoveTrack_BeforeCurrentIndex_DecrementsCurrentIndex()
    {
        var vm = CreateVm();
        AddTracks(vm, 5);
        vm.CurrentIndex = 3;

        vm.RemoveTrackCommand.Execute(1); // 删 index=1

        Assert.Equal(2, vm.CurrentIndex); // 3→2
        Assert.Equal(4, vm.Queue.Count);
    }

    [Fact]
    public void RemoveTrack_AfterCurrentIndex_DoesNotChangeCurrentIndex()
    {
        var vm = CreateVm();
        AddTracks(vm, 5);
        vm.CurrentIndex = 1;

        vm.RemoveTrackCommand.Execute(3); // 删 index=3

        Assert.Equal(1, vm.CurrentIndex);
    }

    [Fact]
    public void RemoveTrack_AtCurrentIndex_ResetsCurrentIndexToMinusOne()
    {
        var vm = CreateVm();
        AddTracks(vm, 3);
        vm.CurrentIndex = 1;

        vm.RemoveTrackCommand.Execute(1);

        Assert.Equal(-1, vm.CurrentIndex);
    }

    [Fact]
    public void RemoveTrack_OutOfRange_IsNoOp()
    {
        var vm = CreateVm();
        AddTracks(vm, 3);
        vm.CurrentIndex = 1;

        vm.RemoveTrackCommand.Execute(99);

        Assert.Equal(1, vm.CurrentIndex);
        Assert.Equal(3, vm.Queue.Count);
    }

    // —— ClearQueue ——

    [Fact]
    public void ClearQueue_EmptiesQueueAndResetsIndex()
    {
        var vm = CreateVm();
        AddTracks(vm, 3);
        vm.CurrentIndex = 1;

        vm.ClearQueueCommand.Execute(null);

        Assert.Empty(vm.Queue);
        Assert.Equal(-1, vm.CurrentIndex);
    }

    // —— ToRecord ——

    [Fact]
    public void ToRecord_PreservesIdAndName()
    {
        var vm = CreateVm(id: "xyz", name: "My List");
        var record = vm.ToRecord();

        Assert.Equal("xyz", record.Id);
        Assert.Equal("My List", record.Name);
    }

    [Fact]
    public void ToRecord_PreservesQueuePaths()
    {
        var vm = CreateVm();
        AddTracks(vm, 2);
        var record = vm.ToRecord();

        Assert.Equal(2, record.Items.Count);
        Assert.Equal("track0.mp3", record.Items[0]);
        Assert.Equal("track1.mp3", record.Items[1]);
    }


    // —— IsActivePlaylist ——

    [Fact]
    public void IsActivePlaylist_DefaultIsFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.IsActivePlaylist);
    }

    // —— HasCurrentTrack ——

    [Fact]
    public void HasCurrentTrack_FalseWhenIndexIsMinusOne()
    {
        var vm = CreateVm();
        AddTracks(vm, 3);
        Assert.False(vm.HasCurrentTrack);
    }

    [Fact]
    public void HasCurrentTrack_TrueWhenIndexIsValid()
    {
        var vm = CreateVm();
        AddTracks(vm, 3);
        vm.CurrentIndex = 0;
        Assert.True(vm.HasCurrentTrack);
    }
}
