using System;
using System.IO;
using System.Threading.Tasks;
using DPlayer.Models;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

/// <summary>
/// 播放链生命周期的并发回归测试。
///
/// 背景：<see cref="NAudioPlaybackService.LoadAsync"/> 在线程池上重建整条播放链，
/// 而 Unload/Dispose 可能来自 UI 线程。两者没有串行化时，后一次重建的
/// DisposePlayback 会释放前一次正在 Init 的 WasapiOut 实例 —— 异常从
/// NAudio 内部（WasapiOut.Init / provider 构造）抛出，并一路冒到 async void 事件处理器，
/// 表现为"播完一首歌后进程崩溃"。
///
/// 这些用例需要真实音频设备（WASAPI）与 naudio 解码链，属集成级测试；
/// 修复（链生命周期闸门）之后它们必须稳定通过。
/// </summary>
public sealed class NAudioPlaybackServiceConcurrencyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _wav;

    public NAudioPlaybackServiceConcurrencyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DPlayerAudioRace_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _wav = Path.Combine(_tempDir, "tone.wav");
        TestAudio.WriteWav(_wav, seconds: 1);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private Track TrackFor(string path) =>
        new(path, Path.GetFileName(path), null, null, null, null, null, null, TimeSpan.Zero, null);

    [Fact]
    public async Task LoadAsync_TwoOverlappingCalls_DoNotThrow()
    {
        foreach (var staggerMs in new[] { 1, 2, 3, 5 })
        {
            var service = new NAudioPlaybackService();

            var ex = await Record.ExceptionAsync(async () =>
            {
                var first = service.LoadAsync(TrackFor(_wav));
                await Task.Delay(staggerMs);           // 让第二次的 DisposePlayback 落进第一次的 Init 窗口
                var second = service.LoadAsync(TrackFor(_wav));
                await Task.WhenAll(first, second);
            });

            Assert.Null(ex);
        }
    }

    [Fact]
    public async Task Unload_DuringLoadAsync_DoesNotThrow()
    {
        foreach (var staggerMs in new[] { 1, 2, 3, 5 })
        {
            var service = new NAudioPlaybackService();

            var ex = await Record.ExceptionAsync(async () =>
            {
                var load = service.LoadAsync(TrackFor(_wav));
                await Task.Delay(staggerMs);
                service.Unload();                      // UI 线程路径：直接 DisposePlayback
                await load;
            });

            Assert.Null(ex);
        }
    }

    /// <summary>
    /// 用户报告的复现路径：一首播到自然结束（触发 TrackEnded 自动推进），
    /// 随即加载下一首。用静音样本，测试运行时不会发声。
    /// </summary>
    [Fact]
    public async Task PlayToNaturalEnd_ThenAdvance_DoesNotThrow()
    {
        var silent = Path.Combine(_tempDir, "silent.wav");
        TestAudio.WriteWav(silent, seconds: 1, amplitude: 0);

        var service = new NAudioPlaybackService();
        var ended = new TaskCompletionSource();
        service.TrackEnded += () => ended.TrySetResult();

        try
        {
            var ex = await Record.ExceptionAsync(async () =>
            {
                await service.LoadAsync(TrackFor(silent));
                service.Play();

                var finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(15)));
                Assert.Same(ended.Task, finished);   // 必须真的播到自然结束，否则本用例无意义

                await service.LoadAsync(TrackFor(silent));   // ← 崩溃复现点：结束后推进下一首
                service.Play();
            });

            Assert.Null(ex);
        }
        finally
        {
            service.Dispose();
        }
    }
}
