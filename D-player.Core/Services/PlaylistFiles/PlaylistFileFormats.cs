using System;
using System.Collections.Generic;
using System.IO;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// 播放列表文件格式常量与后缀判定（Phase 18）。
///
/// IsPlaylistFile 是静态而非实例方法：View 层拖拽判定（DragDropExtensions）
/// 不该为了一个后缀判断去 DI 取服务。
/// 后缀集合是本主题的唯一来源，DragDropExtensions.PlaylistFileExtensions 代理到这里。
/// </summary>
public static class PlaylistFileFormats
{
    /// <summary>支持的播放列表后缀（小写，含点）。</summary>
    public static readonly IReadOnlyList<string> Extensions = new[] { ".m3u", ".m3u8", ".pls" };

    /// <summary>导入用文件对话框过滤器。</summary>
    public const string OpenFilter = "播放列表|*.m3u;*.m3u8;*.pls|所有文件|*.*";

    /// <summary>导出用保存对话框过滤器。</summary>
    public const string SaveFilter = "M3U8 播放列表|*.m3u8";

    public static bool IsPlaylistFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;

        foreach (var allowed in Extensions)
        {
            if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
