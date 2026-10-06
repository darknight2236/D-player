using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.Services.PlaylistFiles;
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
    private readonly IPlaylistFileService _playlistFiles = Substitute.For<IPlaylistFileService>();

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

        return new PlaylistViewModel(seed, _player, _fileDialog, _metadataReader, _playlistFiles);
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

    // —— SortBy（表头点击排序：PlaylistView.xaml.cs Header_Click → vm.SortBy(column)）——

    /// <summary>
    /// 入队 count 个时长**严格递减**的 Track（track0 最长 = count 秒，trackN-1 最短 = 1 秒）。
    /// 故意让初始顺序 = "按 Duration 降序"，这样"升序"结果必然与初始顺序相反：
    /// 断言的排列不是恒等排列，SortBy 若不再真正重排 Queue，下面的断言立刻失败。
    /// </summary>
    private static void AddTracksByDescendingDuration(PlaylistViewModel vm, int count)
    {
        for (int i = 0; i < count; i++)
        {
            vm.Queue.Add(new Track(
                $"track{i}.mp3", $"Title {i}", null, null, null, null, null, null,
                TimeSpan.FromSeconds(count - i), null));
        }
    }

    /// <summary>Queue 的**物理**顺序（按下标串成一行，失败时可直接比对整条序列）。</summary>
    private static string QueueOrder(PlaylistViewModel vm)
        => string.Join(" > ", vm.Queue.Select(t => t.FilePath));

    [Fact]
    public void SortBy_Duration_ReordersQueuePhysically_AndSecondClickFlipsDirection()
    {
        var vm = CreateVm();
        AddTracksByDescendingDuration(vm, 5);
        var before = QueueOrder(vm); // track0(5s) > track1(4s) > ... > track4(1s)

        vm.SortBy("Duration"); // 第一次点击「时长」表头 → 升序
        Assert.Equal("track4.mp3 > track3.mp3 > track2.mp3 > track1.mp3 > track0.mp3", QueueOrder(vm));

        vm.SortBy("Duration"); // 再次点击同列 → 降序，回到初始物理顺序
        Assert.Equal(before, QueueOrder(vm));
        Assert.Equal(5, vm.Queue.Count); // 重排只改顺序，不丢曲目
    }

    [Fact]
    public void SortBy_Duration_MovesCurrentIndexTogetherWithTheCurrentTrack()
    {
        var vm = CreateVm();
        AddTracksByDescendingDuration(vm, 5);
        vm.CurrentIndex = 1;                 // 队列中段：track1.mp3（4 秒）
        var current = vm.Queue[1];

        vm.SortBy("Duration");               // 升序后 4 秒的曲落到 index 3

        Assert.Equal(3, vm.Queue.IndexOf(current));
        Assert.Equal(3, vm.CurrentIndex);
        Assert.Equal(current, vm.Queue[vm.CurrentIndex]); // ▶ 标记跟着曲子走
        Assert.NotEqual(current, vm.Queue[1]);            // 旧索引已被别的曲子占据
        Assert.True(vm.HasCurrentTrack);
    }

    // —— Phase 18: 播放列表文件导入/导出 ——

    private static PlaylistImportResult ImportResult(
        string name, string[] accepted, int missing = 0, int unsupported = 0)
        => new(name, accepted, accepted.Length + missing + unsupported, missing, unsupported);

    [Fact]
    public async Task ImportPlaylistFileAsync_UserCancelsDialog_ReturnsNull()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(Array.Empty<string>());
        var vm = CreateVm();

        var report = await vm.ImportPlaylistFileAsync();

        Assert.Null(report);
        await _playlistFiles.DidNotReceive().ImportAsync(Arg.Any<string>());
        Assert.Empty(vm.Queue);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_AppendsAcceptedPathsInOrder()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\rock.m3u8" });
        _playlistFiles.ImportAsync(@"D:\lists\rock.m3u8")
            .Returns(ImportResult("rock", new[] { @"D:\m\a.mp3", @"D:\m\b.flac" }));
        _metadataReader.ReadAsync(Arg.Any<string>())
            .Returns(ci => new Track(ci.ArgAt<string>(0), System.IO.Path.GetFileName(ci.ArgAt<string>(0)),
                null, null, null, null, null, null, TimeSpan.Zero, null));
        var vm = CreateVm();

        var report = await vm.ImportPlaylistFileAsync();

        Assert.NotNull(report);
        Assert.Equal(2, report!.Imported);
        Assert.False(report.CreatedNewPlaylist);
        Assert.Equal("Test", report.PlaylistName);
        Assert.Equal("rock.m3u8", report.SourceFile);
        Assert.Equal(2, vm.Queue.Count);
        Assert.Equal(@"D:\m\a.mp3", vm.Queue[0].FilePath);
        Assert.Equal(@"D:\m\b.flac", vm.Queue[1].FilePath);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_AllEntriesSkipped_LeavesQueueUntouched()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\old.pls" });
        _playlistFiles.ImportAsync(@"D:\lists\old.pls")
            .Returns(ImportResult("old", Array.Empty<string>(), missing: 3, unsupported: 2));
        var vm = CreateVm();

        var report = await vm.ImportPlaylistFileAsync();

        Assert.NotNull(report);
        Assert.Equal(0, report!.Imported);
        Assert.Null(report.PlaylistName);
        Assert.Equal(3, report.SkippedMissing);
        Assert.Equal(2, report.SkippedUnsupported);
        Assert.Equal(5, report.Skipped);
        Assert.Empty(vm.Queue);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_PresetPath_SkipsFileDialog()
    {
        _playlistFiles.ImportAsync(@"D:\drop\x.m3u")
            .Returns(ImportResult("x", new[] { @"D:\m\a.mp3" }));
        _metadataReader.ReadAsync(Arg.Any<string>())
            .Returns(ci => new Track(ci.ArgAt<string>(0), "a", null, null, null, null, null, null, TimeSpan.Zero, null));
        var vm = CreateVm();

        var report = await vm.ImportPlaylistFileAsync(@"D:\drop\x.m3u");

        Assert.Equal(1, report!.Imported);
        _fileDialog.DidNotReceive().OpenFiles(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task ExportPlaylistFileAsync_EmptyQueue_ReturnsNullWithoutDialog()
    {
        var vm = CreateVm();

        var error = await vm.ExportPlaylistFileAsync();

        Assert.Null(error);
        _fileDialog.DidNotReceive().SaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ExportPlaylistFileAsync_SanitizesPlaylistNameForDefaultFileName()
    {
        var vm = CreateVm(name: @"My/List:1*");
        vm.Queue.Add(new Track(@"D:\m\a.mp3", "A", null, null, null, null, null, null, TimeSpan.Zero, null));
        _fileDialog.SaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        await vm.ExportPlaylistFileAsync();

        _fileDialog.Received(1).SaveFile(PlaylistFileFormats.SaveFilter, "My_List_1_", ".m3u8");
    }

    [Fact]
    public async Task ExportPlaylistFileAsync_UserCancels_DoesNotCallService()
    {
        var vm = CreateVm();
        vm.Queue.Add(new Track(@"D:\m\a.mp3", "A", null, null, null, null, null, null, TimeSpan.Zero, null));
        _fileDialog.SaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        var error = await vm.ExportPlaylistFileAsync();

        Assert.Null(error);
        await _playlistFiles.DidNotReceive().ExportAsync(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyList<Track>>());
    }

    [Fact]
    public async Task ExportPlaylistFileAsync_ServiceThrows_ReturnsErrorText()
    {
        var vm = CreateVm();
        vm.Queue.Add(new Track(@"D:\m\a.mp3", "A", null, null, null, null, null, null, TimeSpan.Zero, null));
        _fileDialog.SaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(@"D:\out\a.m3u8");
        _playlistFiles.ExportAsync(@"D:\out\a.m3u8", Arg.Any<System.Collections.Generic.IReadOnlyList<Track>>())
            .Returns(Task.FromException(new System.IO.IOException("被占用")));

        var error = await vm.ExportPlaylistFileAsync();

        Assert.NotNull(error);
        Assert.Contains("被占用", error);
        Assert.StartsWith("导出失败：", error);
    }
}
