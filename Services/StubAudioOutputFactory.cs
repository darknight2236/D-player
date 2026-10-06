using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 占位实现 —— 固定返回 WASAPI 共享模式输出（100ms 缓冲），构造参数与播放链
/// （<see cref="NAudioPlaybackService"/> 内的 WasapiPlayerBuilder）保持一致。
/// 本类目前只被注册、没有任何消费方；真正抽离时由它按 AppSettings.OutputMode
/// 创建对应输出（见 <see cref="IAudioOutputFactory"/>）。
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
