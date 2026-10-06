using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 音频输出后端工厂（预留）—— 用于在 WASAPI Shared / Exclusive / ASIO 间切换。
/// 当前 NAudioPlaybackService 自行构造输出（NAudio 3 的 WasapiPlayerBuilder → WasapiPlayer），
/// 并未使用本工厂；后续抽离时将由它根据 AppSettings.OutputMode 创建对应的 IWavePlayer。
/// 契约用 IWavePlayer 而非具体类型：WasapiPlayer 等实现类都能装进它。
/// </summary>
public interface IAudioOutputFactory
{
    IWavePlayer CreateOutput();
}
