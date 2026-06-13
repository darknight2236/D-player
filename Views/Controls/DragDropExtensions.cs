using System.IO;
using System.Windows;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 拖拽相关的 attached DependencyProperty 与静态辅助（Phase 5）。
///
/// IsDragOver: 由 PlaylistView code-behind 在 DragEnter/DragLeave 切换，
/// XAML 用 Style.Trigger 高亮根 Border 的 BorderBrush。
///
/// AudioExtensions: 与 IFileDialogService 在 OpenFiles 中使用的过滤器
/// "*.mp3;*.wma;*.flac;*.aac;*.wav" 严格对齐，单一来源，避免漂移。
/// </summary>
public static class DragDropExtensions
{
    /// <summary>支持的音频后缀白名单（小写，含点）。</summary>
    public static readonly IReadOnlyList<string> AudioExtensions = new[]
    {
        ".mp3", ".wma", ".flac", ".aac", ".wav"
    };

    /// <summary>过滤一组路径，仅保留后缀在白名单中的（大小写不敏感）。文件夹/缺失文件会被自动剔除。</summary>
    public static IReadOnlyList<string> FilterAudioPaths(IEnumerable<string>? paths)
    {
        if (paths is null) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            // 文件夹一般 Path.GetExtension 返回 ""，自动被白名单排除
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) continue;
            foreach (var allowed in AudioExtensions)
            {
                if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(path);
                    break;
                }
            }
        }
        return result;
    }

    // —— IsDragOver attached property ——

    public static readonly DependencyProperty IsDragOverProperty =
        DependencyProperty.RegisterAttached(
            "IsDragOver",
            typeof(bool),
            typeof(DragDropExtensions),
            new PropertyMetadata(false));

    public static void SetIsDragOver(DependencyObject element, bool value) =>
        element.SetValue(IsDragOverProperty, value);

    public static bool GetIsDragOver(DependencyObject element) =>
        (bool)element.GetValue(IsDragOverProperty);
}
