using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 音乐库扫描服务(Phase 10)。
/// 负责递归扫描文件夹、批量读元数据、增量同步比对。
/// </summary>
public interface ILibraryScannerService
{
    /// <summary>递归扫描文件夹, 返回所有音频文件的绝对路径。目录不存在返回空集合(绝不抛)。</summary>
    IReadOnlyList<string> ScanFolder(string folderPath, CancellationToken ct = default);

    /// <summary>
    /// 递归扫描文件夹并附带根目录可用状态(绝不抛)。
    /// 路径为空、目录不存在、无访问权限或 IO 失败时返回 <see cref="ScanUnavailable"/>;
    /// 目录可枚举时返回 <see cref="ScanAvailable"/>, 其 Files 为空表示没有音频文件。
    /// </summary>
    LibraryScanResult ScanFolderWithStatus(string folderPath, CancellationToken ct = default);

    /// <summary>批量读取元数据。失败静默跳过。</summary>
    Task<IReadOnlyList<Track>> ReadMetadataBatchAsync(
        IReadOnlyList<string> paths, CancellationToken ct = default);

    /// <summary>增量同步: 比较磁盘文件与缓存, 返回 (added, removed, unchanged)。</summary>
    LibraryDiff ComputeDiff(IReadOnlyList<string> currentFiles, IReadOnlyList<LibraryCacheEntry> cached);
}
