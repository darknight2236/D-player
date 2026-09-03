using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 音频输出后端工厂（预留）—— 用于在 WASAPI Shared / Exclusive / ASIO 间切换。
/// 当前 NAudioPlaybackService 直接 new WasapiOut，并未使用本工厂；
/// 后续抽离时将由它根据 AppSettings.OutputMode 创建对应的 IWavePlayer。
/// </summary>
public interface IAudioOutputFactory
{
    IWavePlayer CreateOutput();
}
