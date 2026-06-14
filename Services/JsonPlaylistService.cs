using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// queue.json 的 JSON 实现(Phase 6, 替换 JsonQueuePersistence)。
/// LoadAsync 内含 v1→v2 一次性迁移: 检测顶层 SchemaVersion, ==1 则把 Items/CurrentIndex/
/// Shuffle/Repeat 包成单条名为"默认歌单"的 v2 Playlist; 立即 SaveAsync 覆盖文件。
/// 迁移阶段 SaveAsync 失败 → 吞掉, 内存里仍是 v2 表示, 下次启动重迁(幂等)。
/// </summary>
public sealed class JsonPlaylistService : IPlaylistService
{
    private const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonPlaylistService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "UmaPlayer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "queue.json");
    }

    public async Task<QueueState> LoadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return Seed();

            string text;
            await using (var stream = File.OpenRead(_path))
            using (var reader = new StreamReader(stream))
                text = await reader.ReadToEndAsync().ConfigureAwait(false);

            int version;
            try
            {
                using var doc = JsonDocument.Parse(text);
                version = doc.RootElement.TryGetProperty("SchemaVersion", out var v) ? v.GetInt32() : 1;
            }
            catch
            {
                return Seed();
            }

            if (version == 1)
                return await MigrateV1ToV2Async(text).ConfigureAwait(false);

            if (version is 2 or 3)
            {
                QueueState? loaded;
                try { loaded = JsonSerializer.Deserialize<QueueState>(text, JsonOptions); }
                catch { return Seed(); }

                if (loaded is null || loaded.Playlists.Count == 0)
                    return Seed();

                var match = loaded.Playlists.Any(p => p.Id == loaded.CurrentPlaylistId);
                return match ? loaded : loaded with { CurrentPlaylistId = loaded.Playlists[0].Id };
            }

            return Seed();
        }
        catch
        {
            return Seed();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(QueueState snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            // 原子写: 先写到 .tmp, 再 Move 覆盖。否则进程在 File.Create 之后/写完之前
            // 被杀, queue.json 会被截断为空, 下次 LoadAsync.JsonDocument.Parse 失败,
            // 用户全部歌单丢失。
            var tmp = _path + ".tmp";
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions).ConfigureAwait(false);
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static QueueState Seed()
    {
        var defaultPlaylist = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: "默认歌单",
            Items: Array.Empty<string>(),
            CurrentIndex: -1,
            ShuffleEnabled: false,
            RepeatMode: RepeatMode.Off);
        return new QueueState
        {
            Playlists = new[] { defaultPlaylist },
            CurrentPlaylistId = defaultPlaylist.Id,
        };
    }

    private async Task<QueueState> MigrateV1ToV2Async(string v1Text)
    {
        QueueStateV1? v1;
        try { v1 = JsonSerializer.Deserialize<QueueStateV1>(v1Text, JsonOptions); }
        catch { return Seed(); }
        if (v1 is null) return Seed();

        var items = v1.Items ?? Array.Empty<string>();
        // 防御腐损 v1: CurrentIndex 越界(如截断后 99/3) → -1, 让用户首播放从头开始,
        // 而不是被静悄悄弹到 "track 0"。
        var clampedIndex = (v1.CurrentIndex >= 0 && v1.CurrentIndex < items.Count)
            ? v1.CurrentIndex
            : -1;

        var defaultPlaylist = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: "默认歌单",
            Items: items,
            CurrentIndex: clampedIndex,
            ShuffleEnabled: v1.ShuffleEnabled,
            RepeatMode: v1.RepeatMode);
        var v2 = new QueueState
        {
            Playlists = new[] { defaultPlaylist },
            CurrentPlaylistId = defaultPlaylist.Id,
        };

        try
        {
            // 迁移阶段也走原子写, 与 SaveAsync 路径一致。
            var tmp = _path + ".tmp";
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, v2, JsonOptions).ConfigureAwait(false);
            File.Move(tmp, _path, overwrite: true);
        }
        catch { /* swallow — 内存仍是 v2, 下次启动重迁 */ }

        return v2;
    }

    private sealed record QueueStateV1
    {
        public int SchemaVersion { get; init; } = 1;
        public IReadOnlyList<string>? Items { get; init; }
        public int CurrentIndex { get; init; } = -1;
        public bool ShuffleEnabled { get; init; }
        public RepeatMode RepeatMode { get; init; } = RepeatMode.Off;
    }
}
