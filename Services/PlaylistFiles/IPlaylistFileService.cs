using System.Threading.Tasks;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// 播放列表文件读写门面（Phase 18）。
///
/// 错误策略刻意不对称（对齐项目既有约定）：
///   ImportAsync 绝不抛 —— 读侧对齐 JsonPlaylistService.LoadAsync，任何失败都退化为空结果，
///   由 View 的"没有可导入的条目"报告兜住，用户仍有反馈而不是崩溃。
/// </summary>
public interface IPlaylistFileService
{
    /// <summary>解析 .m3u / .m3u8 / .pls，返回归一化并过滤后的条目与跳过计数。绝不抛。</summary>
    Task<PlaylistImportResult> ImportAsync(string playlistFilePath);
}
