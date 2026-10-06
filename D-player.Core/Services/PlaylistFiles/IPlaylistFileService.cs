using System.Collections.Generic;
using System.Threading.Tasks;
using DPlayer.Models;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// 播放列表文件读写门面（Phase 18）。
///
/// 错误策略刻意不对称（对齐项目既有约定）：
///   ImportAsync 绝不抛 —— 读侧对齐 JsonPlaylistService.LoadAsync，任何失败都退化为空结果，
///   由 View 的"没有可导入的条目"报告兜住，用户仍有反馈而不是崩溃。
///   ExportAsync 可抛 IOException / UnauthorizedAccessException —— 写侧对齐设置/EQ 对话框的
///   try/catch + 错误框，由 VM 捕获转成错误文案交给 View。
/// </summary>
public interface IPlaylistFileService
{
    /// <summary>解析 .m3u / .m3u8 / .pls，返回归一化并过滤后的条目与跳过计数。绝不抛。</summary>
    Task<PlaylistImportResult> ImportAsync(string playlistFilePath);

    /// <summary>把曲目写出为 extended M3U8（UTF-8 无 BOM，CRLF，绝对路径）。失败抛 IOException。</summary>
    Task ExportAsync(string destPath, IReadOnlyList<Track> tracks);
}
