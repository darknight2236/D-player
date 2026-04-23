using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmaPlayer.Configuration;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUmaPlayerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration — bind Player section only
        services.Configure<AppSettings>(configuration.GetSection("Player"));

        // Services (Singleton — manage audio device lifecycle)
        services.AddSingleton<IPlaybackService, NAudioPlaybackService>();
        services.AddSingleton<IFileDialogService, Win32FileDialogService>();
        services.AddSingleton<ISettingsPersistence, JsonSettingsPersistence>();

        // Reserved (register stubs for future use)
        services.AddSingleton<IAudioDeviceManager, StubAudioDeviceManager>();

        // Audio output factory (Transient — created fresh, disposed by IPlaybackService)
        services.AddTransient<IAudioOutputFactory, StubAudioOutputFactory>();

        // ViewModel
        services.AddTransient<MainViewModel>();

        return services;
    }
}
