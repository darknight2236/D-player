using System.IO;
using System.Text.Json;
using UmaPlayer.Configuration;

namespace UmaPlayer.Services;

public sealed class JsonSettingsPersistence : ISettingsPersistence
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonSettingsPersistence()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "UmaPlayer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

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
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_path, json).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
