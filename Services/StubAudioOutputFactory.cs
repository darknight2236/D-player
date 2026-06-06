using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace UmaPlayer.Services;

/// <summary>
/// 占位实现 —— 固定返回 WASAPI 共享模式输出（100ms 缓冲）。
/// 与 NAudioPlaybackService 内部直接 new 出来的实例参数一致，便于后续无痛接入。
/// </summary>
public sealed class StubAudioOutputFactory : IAudioOutputFactory
{
    public IWavePlayer CreateOutput() => new WasapiOut(AudioClientShareMode.Shared, 100);
}
