using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmaPlayer.Extensions;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer;

/// <summary>
/// 应用入口（替代默认 StartupUri 启动方式，便于注入 DI 容器）。
///
/// 启动流程：
///   1) 读取 appsettings.json 构建 IConfiguration
///   2) 注册所有服务 → 构建 ServiceProvider
///   3) 解析 MainViewModel + ISettingsPersistence
///   4) 创建并显示 MainWindow
///
/// 退出时释放 ServiceProvider —— 触发所有 Singleton 的 Dispose。
/// </summary>
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

        // 注意：MainWindow 需要 persistence 用于恢复/保存窗口位置，
        // 因此这里显式解析后通过构造函数传入（而非让 DI 解析窗口）
        var vm = _services.GetRequiredService<MainViewModel>();
        var persistence = _services.GetRequiredService<ISettingsPersistence>();
        var mainWindow = new Views.MainWindow(vm, persistence);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 触发 Singleton 服务的 Dispose（NAudioPlaybackService 借此释放音频设备）
        (_services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
