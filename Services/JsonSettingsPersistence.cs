using System.IO;
using System.Text.Json;
using UmaPlayer.Configuration;

namespace UmaPlayer.Services;

/// <summary>
/// 将 AppSettings 序列化到 %LocalAppData%\UmaPlayer\settings.json。
///
/// 并发控制：用 SemaphoreSlim(1,1) 串行化所有读写，
/// 防止 VM 的音量变更与 MainWindow.Window_Closing 的窗口尺寸保存竞态写文件。
/// </summary>
public sealed class JsonSettingsPersistence : ISettingsPersistence
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonSettingsPersistence()
    {
        // 使用 LocalApplicationData 而非 ApplicationData：本机配置不漫游，避免多机互覆盖
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "UmaPlayer");
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
            var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
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
}
