using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

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

    public JsonLibraryCache()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "UmaPlayer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "library-cache.json");
    }

    internal JsonLibraryCache(string? overrideDir)
    {
        var dir = overrideDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UmaPlayer");
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

            string text;
            await using (var stream = File.OpenRead(_path))
            using (var reader = new StreamReader(stream))
                text = await reader.ReadToEndAsync().ConfigureAwait(false);

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
                string text;
                await using (var stream = File.OpenRead(_path))
                using (var reader = new StreamReader(stream))
                    text = await reader.ReadToEndAsync().ConfigureAwait(false);

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

            // 原子写: 先写到 .tmp, 再 Move 覆盖。
            var tmp = _path + ".tmp";
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, cache, JsonOptions).ConfigureAwait(false);
            File.Move(tmp, _path, overwrite: true);
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
