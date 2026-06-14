using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 音乐库扫描服务(Phase 10)。
/// 负责递归扫描文件夹、批量读元数据、增量同步比对。
/// </summary>
public interface ILibraryScannerService
{
    /// <summary>递归扫描文件夹, 返回所有音频文件的绝对路径。目录不存在返回空集合(绝不抛)。</summary>
    IReadOnlyList<string> ScanFolder(string folderPath);

    /// <summary>批量读取元数据。失败静默跳过。</summary>
    Task<IReadOnlyList<Track>> ReadMetadataBatchAsync(IReadOnlyList<string> paths);

    /// <summary>增量同步: 比较磁盘文件与缓存, 返回 (added, removed, unchanged)。</summary>
    LibraryDiff ComputeDiff(IReadOnlyList<string> currentFiles, IReadOnlyList<LibraryCacheEntry> cached);
}
