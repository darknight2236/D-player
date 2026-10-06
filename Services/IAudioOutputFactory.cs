using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 音频输出后端工厂（预留）—— 用于在 WASAPI Shared / Exclusive / ASIO 间切换。
/// 当前 NAudioPlaybackService 自行构造输出（NAudio 3 的 WasapiPlayer），并未使用本工厂；
/// 后续抽离时将由它根据 AppSettings.OutputMode 创建对应的输出实例。
/// 注意：WasapiPlayer 不实现 IWavePlayer，所以本契约（返回 IWavePlayer）的重设计属于
/// 未来多后端工作的一部分，见 StubAudioOutputFactory 的类注释。
/// </summary>
public interface IAudioOutputFactory
{
    IWavePlayer CreateOutput();
}
