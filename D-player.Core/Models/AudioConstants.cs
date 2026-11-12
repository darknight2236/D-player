using System.Collections.Frozen;
using System.IO;
using System.Linq;

namespace DPlayer.Models;

/// <summary>
/// 支持的音频后缀白名单(Phase 10)。
/// 从 Views/Controls/DragDropExtensions 提取到 Models 层, 让 Service 层可以引用而不违反依赖方向。
/// DragDropExtensions.AudioExtensions 改为代理到此常量。
/// Phase 29 Task 7 起后缀判定统一走 <see cref="IsAudioFile"/>（OrdinalIgnoreCase），
/// 对话框过滤器统一走 <see cref="FileDialogFilter"/>，消费点不再各自维护 HashSet/循环。
/// </summary>
public static class AudioConstants
{
    /// <summary>支持的音频后缀白名单（小写，含点）。</summary>
    public static readonly IReadOnlyList<string> AudioExtensions = new[]
    {
        ".mp3", ".wma", ".flac", ".aac", ".wav"
    };

    private static readonly FrozenSet<string> AudioExtensionSet =
        AudioExtensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>文件对话框过滤器（与 <see cref="AudioExtensions"/> 严格对齐）。</summary>
    public const string FileDialogFilter =
        "Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav";

    /// <summary>路径后缀是否在音频白名单中（大小写不敏感；空/无后缀/目录返回 false）。</summary>
    public static bool IsAudioFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && AudioExtensionSet.Contains(ext);
    }
}
