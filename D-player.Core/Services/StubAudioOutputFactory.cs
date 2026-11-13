using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 默认输出实现 —— 固定返回 WASAPI 共享模式输出（100ms 缓冲），构造参数与播放链
/// （<see cref="NAudioPlaybackService"/> 内的 WasapiPlayerBuilder）保持一致。
/// 由 <see cref="NAudioPlaybackService"/> 通过 <see cref="IAudioOutputFactory"/> 消费
/// （无参构造函数注入本类）；后续可扩展为按 AppSettings.OutputMode 创建对应输出。
/// </summary>
public sealed class StubAudioOutputFactory : IAudioOutputFactory
{
    public IWavePlayer CreateOutput() =>
        new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithEventSync()
            .WithLatency(100)
            .Build();
}
