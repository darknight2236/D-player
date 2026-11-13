using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 音频输出后端工厂 —— 用于在 WASAPI Shared / Exclusive / ASIO 间切换。
/// <see cref="NAudioPlaybackService"/> 通过本工厂创建播放链的输出入口
/// （默认实现为 <see cref="StubAudioOutputFactory"/> → WasapiPlayerBuilder → WasapiPlayer），
/// 测试可注入假输出；后续可扩展为按 AppSettings.OutputMode 创建对应的 IWavePlayer。
/// 契约用 IWavePlayer 而非具体类型：WasapiPlayer 等实现类都能装进它。
/// </summary>
public interface IAudioOutputFactory
{
    IWavePlayer CreateOutput();
}
