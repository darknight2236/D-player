using System.IO;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// ILibraryScannerService 的默认实现(Phase 10)。
///
/// ScanFolder: 递归枚举文件, 按 AudioConstants.AudioExtensions 白名单过滤。
/// ReadMetadataBatchAsync: 逐条调用 ITrackMetadataReader.ReadAsync, 失败静默跳过。
/// ComputeDiff: 路径大小写不敏感比对 + 元数据质量检测(零时长/零采样率视为需重新读取)。
/// </summary>
public sealed class LibraryScannerService : ILibraryScannerService
{
    private readonly ITrackMetadataReader _metadataReader;

    public LibraryScannerService(ITrackMetadataReader metadataReader)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ScanFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return Array.Empty<string>();

        try
        {
            if (!Directory.Exists(folderPath))
                return Array.Empty<string>();

            var extensions = new HashSet<string>(AudioConstants.AudioExtensions, StringComparer.OrdinalIgnoreCase);

            return Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f)))
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Track>> ReadMetadataBatchAsync(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0)
            return Array.Empty<Track>();

        var result = new List<Track>(paths.Count);

        foreach (var path in paths)
        {
            try
            {
                var track = await _metadataReader.ReadAsync(path);
                if (track is not null)
                    result.Add(track);
            }
            catch
            {
                // 失败静默跳过
            }
        }

        return result;
    }

    /// <inheritdoc />
    public LibraryDiff ComputeDiff(IReadOnlyList<string> currentFiles, IReadOnlyList<LibraryCacheEntry> cached)
    {
        // 规范化磁盘文件路径
        var currentSet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in currentFiles)
        {
            var normalized = Path.GetFullPath(f);
            currentSet[normalized] = normalized;
        }

        // 规范化缓存路径
        var cacheMap = new Dictionary<string, LibraryCacheEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in cached)
        {
            var normalized = Path.GetFullPath(entry.FilePath);
            cacheMap[normalized] = entry with { FilePath = normalized };
        }

        var added = new List<string>();
        var removed = new List<string>();
        var unchanged = new List<LibraryCacheEntry>();

        // 磁盘上有但缓存没有的 → 新增
        foreach (var path in currentSet.Keys)
        {
            if (!cacheMap.ContainsKey(path))
                added.Add(path);
        }

        // 缓存中有但磁盘上没有的 → 删除
        foreach (var (path, entry) in cacheMap)
        {
            if (!currentSet.ContainsKey(path))
            {
                removed.Add(path);
            }
            else
            {
                // 磁盘上存在且缓存中有 → 检查元数据质量
                bool hasInvalidMetadata = entry.Duration == TimeSpan.Zero
                    && (entry.SampleRate is null or 0);

                if (hasInvalidMetadata)
                    added.Add(path); // 视为需要重新读取
                else
                    unchanged.Add(entry);
            }
        }

        return new LibraryDiff(added, removed, unchanged);
    }
}
