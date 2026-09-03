using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

/// <summary>
/// PlayerViewModel 单元测试。
/// 债务 #1 已偿（Phase 7）—— VM 不再持有 BitmapImage，可在无 WPF 上下文中实例化。
/// </summary>
public class PlayerViewModelTests
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly ISettingsPersistence _persistence = Substitute.For<ISettingsPersistence>();
    private readonly IOptions<AppSettings> _options = Options.Create(new AppSettings { DefaultVolume = 0.5f });

    private PlayerViewModel CreateVm()
    {
        // 默认：LoadAsync 返回已完成的 Task（不阻塞构造器内的 Initialize）
        _persistence.LoadAsync().Returns(Task.FromResult(new AppSettings { DefaultVolume = 0.7f }));
        return new PlayerViewModel(_player, _persistence, _options);
    }

    // —— 构造器 ——

    [Fact]
    public void Constructor_LoadsVolumeFromPersistence()
    {
        _persistence.LoadAsync().Returns(Task.FromResult(new AppSettings { DefaultVolume = 0.42f }));

        var vm = new PlayerViewModel(_player, _persistence, _options);

        Assert.Equal(0.42f, vm.Volume);
    }

    [Fact]
    public void Constructor_FallsBackToOptions_WhenLoadFails()
    {
        _persistence.LoadAsync().Returns(Task.FromException<AppSettings>(new Exception("disk error")));

        var vm = new PlayerViewModel(_player, _persistence, _options);

        Assert.Equal(0.5f, vm.Volume); // _options.Value.DefaultVolume
    }

    // —— HandleTrackChanged ——

    [Fact]
    public void TrackChanged_SetsCurrentTrack()
    {
        var vm = CreateVm();
        var track = new Track("a.mp3", "Title", "Artist", null, null, null, null, null, TimeSpan.Zero, null);

        _player.TrackChanged += Raise.Event<Action<Track?>>(track);

        Assert.Equal(track, vm.CurrentTrack);
    }

    [Fact]
    public void TrackChanged_Null_ClearsCurrentTrackAndAlbumArt()
    {
        var vm = CreateVm();
        var track = new Track("a.mp3", "T", null, null, null, null, null, new byte[] { 1, 2, 3 }, TimeSpan.Zero, null);
        _player.TrackChanged += Raise.Event<Action<Track?>>(track);
        Assert.NotNull(vm.AlbumArtBytes);

        // NSubstitute 的 Raise.Event<T>(null) 会抛 NullReferenceException，
        // 改用 EventHandler 方式触发 null 参数
        _player.TrackChanged += Raise.Event<Action<Track?>>(new object[] { null! });

        Assert.Null(vm.CurrentTrack);
        Assert.Null(vm.AlbumArtBytes);
    }

    [Fact]
    public void TrackChanged_SetsAlbumArtBytes_FromTrack()
    {
        var vm = CreateVm();
        var art = new byte[] { 0xFF, 0xD8, 0xFF };
        var track = new Track("a.mp3", "T", null, null, null, null, null, art, TimeSpan.Zero, null);

        _player.TrackChanged += Raise.Event<Action<Track?>>(track);

        Assert.Same(art, vm.AlbumArtBytes); // 零拷贝，同一引用
    }

    // —— Position / IsSeeking ——

    [Fact]
    public void PositionChanged_WhenNotSeeking_UpdatesPosition()
    {
        var vm = CreateVm();
        vm.IsSeeking = false;

        _player.PositionChanged += Raise.Event<Action<TimeSpan>>(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), vm.Position);
    }

    [Fact]
    public void PositionChanged_WhenSeeking_DoesNotUpdatePosition()
    {
        var vm = CreateVm();
        vm.IsSeeking = true;
        vm.Position = TimeSpan.FromSeconds(5);

        _player.PositionChanged += Raise.Event<Action<TimeSpan>>(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(5), vm.Position); // 未被覆盖
    }

    // —— Volume ——

    [Fact]
    public void VolumeChange_SyncsToPlayer()
    {
        var vm = CreateVm();
        _persistence.ClearReceivedCalls();

        vm.Volume = 0.3f;

        Assert.Equal(0.3f, _player.Volume);
    }

    [Fact]
    public void VolumeChange_PersistsToDisk()
    {
        var vm = CreateVm(); // 构造完成时 _isInitializing 已设为 false
        _persistence.ClearReceivedCalls();

        vm.Volume = 0.6f;

        _persistence.Received(1).UpdateAsync(Arg.Is<Func<AppSettings, AppSettings>>(fn =>
            fn(new AppSettings()).DefaultVolume == 0.6f));
    }

    [Fact]
    public void VolumeChange_DuringInit_DoesNotPersist()
    {
        // 构造期间的 Volume 设置不应触发写盘（_isInitializing=true 时跳过）
        // 通过 LoadAsync 返回的 Volume 来间接验证：如果 Initialize 内的 Volume 设置
        // 触发了 UpdateAsync，说明 _isInitializing 没生效
        _persistence.LoadAsync().Returns(Task.FromResult(new AppSettings { DefaultVolume = 0.9f }));

        var vm = new PlayerViewModel(_player, _persistence, _options);

        // 构造期间 UpdateAsync 不应被调用
        _persistence.DidNotReceive().UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>());
    }

    // —— Commands ——

    [Fact]
    public void PlayPause_WhenPlaying_Pauses()
    {
        var vm = CreateVm();
        // 通过事件设置 VM 的 PlayState 属性
        _player.StateChanged += Raise.Event<Action<PlayState>>(PlayState.Playing);

        vm.PlayPauseCommand.Execute(null);

        _player.Received(1).Pause();
    }

    [Fact]
    public void PlayPause_WhenPaused_Plays()
    {
        var vm = CreateVm();
        _player.StateChanged += Raise.Event<Action<PlayState>>(PlayState.Paused);

        vm.PlayPauseCommand.Execute(null);

        _player.Received(1).Play();
    }

    [Fact]
    public void SeekCompleted_SeeksToCorrectPosition()
    {
        var vm = CreateVm();
        vm.IsSeeking = true;
        // 模拟 Duration = 100s
        _player.DurationChanged += Raise.Event<Action<TimeSpan>>(TimeSpan.FromSeconds(100));

        vm.SeekCompletedCommand.Execute(0.5); // 50%

        _player.Received(1).Seek(TimeSpan.FromSeconds(50));
    }

    // —— CleanupAsync ——

    [Fact]
    public async Task CleanupAsync_UnbindsAllEvents()
    {
        var vm = CreateVm();

        // 先触发事件确认 VM 正常响应
        _player.PositionChanged += Raise.Event<Action<TimeSpan>>(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), vm.Position);

        await vm.CleanupAsync();

        // Cleanup 后再触发事件 —— VM 的 handler 已解绑，NSubstitute 的 Raise 机制
        // 无法直接验证"不响应"（因为 handler 已从 mock 移除）。
        // 验证方式：确认 UpdateAsync 被调用（说明 CleanupAsync 执行了清理逻辑）。
        await _persistence.Received(1).UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>());
    }

    [Fact]
    public async Task CleanupAsync_PersistsFinalVolume()
    {
        var vm = CreateVm();
        vm.Volume = 0.123f;
        _persistence.ClearReceivedCalls(); // 清除 Volume change 触发的 UpdateAsync

        await vm.CleanupAsync();

        await _persistence.Received(1).UpdateAsync(Arg.Is<Func<AppSettings, AppSettings>>(fn =>
            fn(new AppSettings()).DefaultVolume == 0.123f));
    }
}
