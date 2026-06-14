namespace UmaPlayer.Models;

/// <summary>
/// 支持的音频后缀白名单(Phase 10)。
/// 从 Views/Controls/DragDropExtensions 提取到 Models 层, 让 Service 层可以引用而不违反依赖方向。
/// DragDropExtensions.AudioExtensions 改为代理到此常量。
/// </summary>
public static class AudioConstants
{
    /// <summary>支持的音频后缀白名单（小写，含点）。</summary>
    public static readonly IReadOnlyList<string> AudioExtensions = new[]
    {
        ".mp3", ".wma", ".flac", ".aac", ".wav"
    };
}
