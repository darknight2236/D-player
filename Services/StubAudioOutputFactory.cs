using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 占位实现 —— 固定返回 WASAPI 共享模式输出（100ms 缓冲）。
/// 与 NAudioPlaybackService 内部直接 new 出来的实例参数一致，便于后续无痛接入。
/// </summary>
public sealed class StubAudioOutputFactory : IAudioOutputFactory
{
    // WasapiOut 在 NAudio 3 被标记过时（建议 WasapiPlayerBuilder → WasapiPlayer）。
    // 迁移会改变播放输出的语义（同步模式、teardown 行为），需要独立验证，故此处有意保留。
#pragma warning disable CS0618
    public IWavePlayer CreateOutput() => new WasapiOut(AudioClientShareMode.Shared, 100);
#pragma warning restore CS0618
}
