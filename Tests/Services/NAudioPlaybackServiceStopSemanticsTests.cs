using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DPlayer.Models;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

/// <summary>
/// 传输命令在"播放头已接近曲尾"时的语义回归测试。
///
/// 背景："用户按停止"与"曲目自然播完"到达服务时是同一个 PlaybackStopped 回调：
/// 输出类不会告诉我们是谁发起的停止，而仅凭"播放头距 TotalTime 在容差内"也区分不了——
/// 曲尾 200ms 容差内的停止会被当成自然播完，于是向 VM 发出 TrackEnded 并自动推进下一首。
/// 这个不确定性不随实现变化：无论回调是同步触发还是经 SynchronizationContext 异步派发、
/// 无论回调时播放头是否已被归零，两条路径的信号都完全相同，所以停止意图必须由发起方
/// 显式标记（即服务里的 `_stopRequested`）。旧实现缺这一步，才有上面那类误判。
///
/// 这些用例需要真实音频设备（WASAPI）；用静音样本，运行时不发声。
/// </summary>
public sealed class NAudioPlaybackServiceStopSemanticsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _wav;

    public NAudioPlaybackServiceStopSemanticsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DPlayerStopSem_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _wav = Path.Combine(_tempDir, "silent.wav");
        TestAudio.WriteWav(_wav, seconds: 1, amplitude: 0);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private Track TrackFor(string path) =>
        new(path, Path.GetFileName(path), null, null, null, null, null, null, TimeSpan.Zero, null);

    /// <summary>起播并确认播放线程真的在推进（否则本组用例无从谈起）。</summary>
    private static async Task StartAndWaitForPlayheadAsync(NAudioPlaybackService service)
    {
        service.Play();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (service.Position <= TimeSpan.Zero)
        {
            Assert.True(DateTime.UtcNow < deadline, "播放线程未推进，本用例无意义");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Stop_WhenPlayheadIsAtTrackEnd_DoesNotRaiseTrackEnded()
    {
        var service = new NAudioPlaybackService();
        int ended = 0;
        service.TrackEnded += () => Interlocked.Increment(ref ended);

        try
        {
            await service.LoadAsync(TrackFor(_wav));
            await StartAndWaitForPlayheadAsync(service);

            // 推到距曲尾 100ms —— 落在服务判定"自然播完"的 200ms 容差内，再按停止
            service.Seek(service.Duration - TimeSpan.FromMilliseconds(100));
            await Task.Delay(50, TestContext.Current.CancellationToken);
            service.Stop();
            await Task.Delay(300, TestContext.Current.CancellationToken); // 事件经 Post 派发，留出窗口

            Assert.Equal(0, ended);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Fact]
    public async Task Unload_WhenPlayheadIsAtTrackEnd_DoesNotRaiseTrackEnded()
    {
        var service = new NAudioPlaybackService();
        int ended = 0;
        service.TrackEnded += () => Interlocked.Increment(ref ended);

        try
        {
            await service.LoadAsync(TrackFor(_wav));
            await StartAndWaitForPlayheadAsync(service);

            service.Seek(service.Duration - TimeSpan.FromMilliseconds(100));
            await Task.Delay(50, TestContext.Current.CancellationToken);
            service.Unload();
            await Task.Delay(300, TestContext.Current.CancellationToken);

            Assert.Equal(0, ended);
        }
        finally
        {
            service.Dispose();
        }
    }
}
