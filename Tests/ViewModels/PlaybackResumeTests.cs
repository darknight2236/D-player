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
            seed => new PlaylistViewModel(seed, _player, _fileDialog, _metadataReader, _playlistFiles));
        return new MainViewModel(CreatePlayerVm(), playlists, _player, _playlistService, _settings);
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
}
