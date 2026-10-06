using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DPlayer.Services;

/// <summary>
/// 占位实现 —— 固定返回 WASAPI 共享模式输出（100ms 缓冲）。
///
/// 这里继续用 WasapiOut 是刻意的，理由不是接口对不上：WasapiPlayer 实现 IWavePlayer，
/// 换过去本契约一字不用改。真正的理由是 IAudioOutputFactory 只被注册、没有任何调用点，
/// 改它的输出策略是一次独立决策，不在本次「不改行为」的依赖迁移范围内 —— 那属于
/// "未来接多后端"（和 OutputMode 选择一起看）。NAudioPlaybackService 已迁到 WasapiPlayer，
/// 与本类保留旧类并不矛盾：本类只是接缝的占位实现，从未被播放链使用。
/// </summary>
public sealed class StubAudioOutputFactory : IAudioOutputFactory
{
    // WasapiOut 在 NAudio 3 被标记过时（官方建议 WasapiPlayerBuilder → WasapiPlayer），但它仍然可用；
    // 本工厂无调用点，切换输出策略留待多后端工作 —— 故有意保留，理由见上方类注释。
#pragma warning disable CS0618
    public IWavePlayer CreateOutput() => new WasapiOut(AudioClientShareMode.Shared, 100);
#pragma warning restore CS0618
}
