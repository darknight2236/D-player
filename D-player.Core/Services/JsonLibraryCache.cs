using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DPlayer.Configuration;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// library-cache.json 的 JSON 实现(Phase 10)。
/// 所有文件夹绑定歌单共用一个缓存文件, 按 SourceFolder 路径分组。
/// LoadAsync 绝不抛(失败回空列表), SaveAsync IO 失败抛 IOException。
/// </summary>
public sealed class JsonLibraryCache : ILibraryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonLibraryCache(DPlayerDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var dir = paths.Directory;
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "library-cache.json");
    }

    public async Task<IReadOnlyList<LibraryCacheEntry>> LoadAsync(string folderPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        var key = NormalizePath(folderPath);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return Array.Empty<LibraryCacheEntry>();

            var text = await JsonFileStore.ReadAllTextAsync(_path).ConfigureAwait(false);

            Dictionary<string, List<LibraryCacheEntry>>? cache;
            try
            {
                cache = JsonSerializer.Deserialize<Dictionary<string, List<LibraryCacheEntry>>>(text, JsonOptions);
            }
            catch
            {
                return Array.Empty<LibraryCacheEntry>();
            }

            if (cache is null || !cache.TryGetValue(key, out var entries))
                return Array.Empty<LibraryCacheEntry>();

            return entries;
        }
        catch
        {
            return Array.Empty<LibraryCacheEntry>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string folderPath, IReadOnlyList<LibraryCacheEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        ArgumentNullException.ThrowIfNull(entries);
        var key = NormalizePath(folderPath);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            Dictionary<string, List<LibraryCacheEntry>> cache;

            if (File.Exists(_path))
            {
                var text = await JsonFileStore.ReadAllTextAsync(_path).ConfigureAwait(false);

                try
                {
                    cache = JsonSerializer.Deserialize<Dictionary<string, List<LibraryCacheEntry>>>(text, JsonOptions)
                            ?? new Dictionary<string, List<LibraryCacheEntry>>();
                }
                catch
                {
                    cache = new Dictionary<string, List<LibraryCacheEntry>>();
                }
            }
            else
            {
                cache = new Dictionary<string, List<LibraryCacheEntry>>();
            }

            cache[key] = new List<LibraryCacheEntry>(entries);

            await JsonFileStore.WriteAtomicAsync(_path, cache, JsonOptions)
                .ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
    }
}
