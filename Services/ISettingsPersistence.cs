using UmaPlayer.Configuration;

namespace UmaPlayer.Services;

public interface ISettingsPersistence
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}
