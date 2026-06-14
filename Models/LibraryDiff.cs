namespace UmaPlayer.Models;

/// <summary>
/// 文件夹扫描增量同步结果(Phase 10)。
/// AddedPaths: 新增文件的绝对路径(需读元数据)。
/// RemovedPaths: 已从磁盘删除的文件路径(需从 Queue 移除)。
/// Unchanged: 未变化的缓存条目(可直接转 Track, 无需重新读元数据)。
/// </summary>
public sealed record LibraryDiff(
    IReadOnlyList<string> AddedPaths,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<LibraryCacheEntry> Unchanged);
