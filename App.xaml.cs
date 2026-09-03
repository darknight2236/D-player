using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DPlayer.Extensions;
using DPlayer.Services;
using DPlayer.ViewModels;

namespace DPlayer;

/// <summary>
/// 应用入口(替代默认 StartupUri 启动方式, 便于注入 DI 容器)。
///
/// Phase 6 新增 GetService&lt;T&gt; 静态入口 —— 给 View 层 code-behind 在事件
/// 处理(双击播放等)中按需取 PlaylistsViewModel, 避免 PlaylistView 与
/// PlaylistsViewModel 之间硬绑 DataContext 通道。
/// </summary>
public partial class App : Application
{
    private static ServiceProvider? _services;

    /// <summary>
    /// 取一个 DI 单例/瞬态。仅供 View 层 code-behind 在事件处理中使用 ——
    /// VM 之间永远走构造函数注入, 不要调用本方法。
    /// </summary>
    public static T GetService<T>() where T : notnull
    {
        if (_services is null)
            throw new InvalidOperationException("ServiceProvider not initialized; called before App.OnStartup.");
        return _services.GetRequiredService<T>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 更名 UmaPlayer → D-player：在持久化服务被 DI 构造前，先把旧数据目录迁移到新目录
        LegacyDataMigration.MigrateIfNeeded();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddDPlayerServices(configuration);
        _services = services.BuildServiceProvider();

        // 注意:MainWindow 需要 settings persistence 用于恢复/保存窗口位置,
        // 因此这里显式解析后通过构造函数传入(而非让 DI 解析窗口)。
        // queue 持久化已下沉到 MainViewModel.InitializeAsync/CleanupAsync, 不再在此显式拉取。
        var vm = _services.GetRequiredService<MainViewModel>();
        var persistence = _services.GetRequiredService<ISettingsPersistence>();
        var mainWindow = new Views.MainWindow(vm, persistence);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 触发 Singleton 服务的 Dispose(NAudioPlaybackService 借此释放音频设备)
        (_services as IDisposable)?.Dispose();
        _services = null;
        base.OnExit(e);
    }
}
