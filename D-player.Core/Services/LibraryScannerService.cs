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
        ArgumentNullException.ThrowIfNull(metadataReader);
        _metadataReader = metadataReader;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ScanFolder(string folderPath, CancellationToken ct = default)
        => ScanFolderWithStatus(folderPath, ct).Files;

    /// <inheritdoc />
    public LibraryScanResult ScanFolderWithStatus(
        string folderPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 只有“枚举成功但没有音频文件”才是空库；路径为空/不存在/无权限/IO 失败
        // 都必须标记为不可用，调用方才不会拿空结果覆盖该根目录的缓存。
        if (string.IsNullOrWhiteSpace(folderPath))
            return Unavailable();

        try
        {
            if (!Directory.Exists(folderPath))
                return Unavailable();

            var files = new List<string>();
            foreach (var file in Directory.EnumerateFiles(
                folderPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (AudioConstants.IsAudioFile(file))
                    files.Add(file);
            }
            return new LibraryScanResult(files, IsUnavailable: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Unavailable();
        }
        catch (IOException)
        {
            return Unavailable();
        }
    }

    private static LibraryScanResult Unavailable()
        => new(Array.Empty<string>(), IsUnavailable: true);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Track>> ReadMetadataBatchAsync(
        IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        if (paths is null || paths.Count == 0)
            return Array.Empty<Track>();

        var result = new List<Track>(paths.Count);

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var track = await _metadataReader.ReadAsync(path);
                ct.ThrowIfCancellationRequested();
                if (track is not null)
                    result.Add(track);
            }
            catch (OperationCanceledException)
            {
                throw;
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

        List<string> added = [];
        List<string> removed = [];
        List<LibraryCacheEntry> unchanged = [];

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
