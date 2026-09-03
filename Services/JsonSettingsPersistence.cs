using System.IO;
using System.Text.Json;
using DPlayer.Configuration;

namespace DPlayer.Services;

/// <summary>
/// 将 AppSettings 序列化到 %LocalAppData%\D-player\settings.json。
///
/// 并发控制：用 SemaphoreSlim(1,1) 把整个"读盘 → mutator → 写盘"封进临界区，
/// 调用方只需提供 mutator (s => s with { Field = newValue })，
/// 字段合并由实现保证。
/// </summary>
public sealed class JsonSettingsPersistence : ISettingsPersistence
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonSettingsPersistence()
    {
        // 使用 LocalApplicationData 而非 ApplicationData：本机配置不漫游，避免多机互覆盖
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "D-player");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    /// <summary>文件不存在则返回 record 默认值（首次启动场景）。</summary>
    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_path))
            return new AppSettings();

        await _lock.WaitAsync();
        try
        {
            return await ReadFromDiskNoLockAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 锁内 read-modify-write。读盘失败 → 把 new AppSettings() 喂给 mutator
    /// （与 LoadAsync 失败回落语义一致），写盘失败照样抛出（关闭流程调用方自行 catch）。
    /// </summary>
    public async Task UpdateAsync(Func<AppSettings, AppSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        await _lock.WaitAsync();
        try
        {
            AppSettings current;
            try
            {
                current = await ReadFromDiskNoLockAsync().ConfigureAwait(false);
            }
            catch
            {
                // 读失败（文件损坏 / 权限）→ 以默认值为起点，让 mutator 仍可应用变更
                current = new AppSettings();
            }

            var next = mutator(current);

            var json = JsonSerializer.Serialize(next, new JsonSerializerOptions
            {
                WriteIndented = true // 人类可读，方便手动调试
            });
            await File.WriteAllTextAsync(_path, json).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>不获取锁的读 —— 调用方负责持锁。文件不存在返回默认值。</summary>
    private async Task<AppSettings> ReadFromDiskNoLockAsync()
    {
        if (!File.Exists(_path))
            return new AppSettings();

        var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }
}
