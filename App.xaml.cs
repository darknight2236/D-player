using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmaPlayer.Extensions;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddUmaPlayerServices(configuration);
        _services = services.BuildServiceProvider();

        var vm = _services.GetRequiredService<MainViewModel>();
        var persistence = _services.GetRequiredService<ISettingsPersistence>();
        var mainWindow = new Views.MainWindow(vm, persistence);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
