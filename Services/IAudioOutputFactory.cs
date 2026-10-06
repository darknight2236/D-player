using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 音频输出后端工厂（预留）—— 用于在 WASAPI Shared / Exclusive / ASIO 间切换。
/// 当前 NAudioPlaybackService 自行构造输出（NAudio 3 的 WasapiPlayerBuilder → WasapiPlayer），
/// 并未使用本工厂；后续抽离时将由它根据 AppSettings.OutputMode 创建对应的 IWavePlayer。
/// WasapiPlayer 实现了 IWavePlayer，本契约足以承载它；StubAudioOutputFactory 仍返回 WasapiOut
/// 的理由见该类的注释。
/// </summary>
public interface IAudioOutputFactory
{
    IWavePlayer CreateOutput();
}
