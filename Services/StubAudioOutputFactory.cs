using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 占位实现 —— 固定返回 WASAPI 共享模式输出（100ms 缓冲）。
///
/// 这里继续用 WasapiOut 是刻意的：它在 NAudio 3 里虽被降级为 legacy placeholder，但依然可用；
/// 而本接缝的契约是 IWavePlayer，WasapiPlayer 并不实现 IWavePlayer，所以换类不是改一行 new，
/// 而是要重新设计 IAudioOutputFactory 的返回类型。那属于"未来接多后端"的一部分（和 OutputMode
/// 选择一起看），不在本次依赖迁移范围内。NAudioPlaybackService 已迁到 WasapiPlayer，与本类
/// 保留旧类并不矛盾：本类只是接缝的占位实现，从未被播放链使用。
/// </summary>
public sealed class StubAudioOutputFactory : IAudioOutputFactory
{
    // WasapiOut 在 NAudio 3 被标记过时（官方建议 WasapiPlayerBuilder → WasapiPlayer），但它仍然可用；
    // WasapiPlayer 不实现 IWavePlayer，本工厂的契约换不过去 —— 故有意保留，理由见上方类注释。
#pragma warning disable CS0618
    public IWavePlayer CreateOutput() => new WasapiOut(AudioClientShareMode.Shared, 100);
#pragma warning restore CS0618
}
