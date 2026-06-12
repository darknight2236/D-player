using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// queue.json 的 JSON 文件实现（Phase 4）。
///
/// 设计要点：
///   - 与 JsonSettingsPersistence 共用相同 ctor 模式（拼路径 + Directory.CreateDirectory）
///   - 自带 SemaphoreSlim(1,1)，与 settings 持久化的锁互不影响（独立文件、独立 Singleton）
///   - LoadAsync 用 catch-all 静默 fallback；SaveAsync 不吞异常（顶层决定如何处理）
///   - RepeatMode 用 JsonStringEnumConverter 写入字符串值，跨版本稳定且人类可读
///   - 不写 .bak / temp-then-rename：极端情况下队列丢失 ≈ 用户重新拖一遍文件，
///     不值得加复杂度（与 settings.json 行为对称）
/// </summary>
public sealed class JsonQueuePersistence : IQueuePersistence
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,                             // 人类可读，方便手动调试
        Converters = { new JsonStringEnumConverter() },   // RepeatMode 字符串化
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonQueuePersistence()
    {
        // 与 JsonSettingsPersistence 同目录：%LocalAppData%\UmaPlayer
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "UmaPlayer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "queue.json");
    }

    /// <summary>
    /// 读取磁盘队列快照。catch-all 静默 fallback —— 任何异常逃出都会让 PlaylistViewModel
    /// 构造函数抛 → 应用启动崩溃。隐式契约：本方法绝不抛。
    /// </summary>
    public async Task<QueueState> LoadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return new QueueState();

            await using var stream = File.OpenRead(_path);
            var loaded = await JsonSerializer
                .DeserializeAsync<QueueState>(stream, JsonOptions)
                .ConfigureAwait(false);

            // null（空文件）/ 版本不匹配 → 视为不可用
            if (loaded is null || loaded.SchemaVersion != CurrentSchemaVersion)
                return new QueueState();

            return loaded;
        }
        catch
        {
            // JSON 损坏 / IO 失败 → 静默 fallback；旧文件不删，保留供用户排查
            return new QueueState();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 序列化并覆盖写入 queue.json。失败抛出，由调用方决定如何处理。
    /// </summary>
    public async Task SaveAsync(QueueState snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(_path);
            await JsonSerializer
                .SerializeAsync(stream, snapshot, JsonOptions)
                .ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
