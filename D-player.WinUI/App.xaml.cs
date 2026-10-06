using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using DPlayer.Configuration;
using DPlayer.Extensions;
using DPlayer.Services;
using DPlayer.ViewModels;
using DPlayer.WinUI.Services;

namespace DPlayer.WinUI;

/// <summary>
/// WinUI 壳入口：在 UI 线程建 DI 容器并起主窗口。
///
/// 为什么必须在 UI 线程构造容器：NAudioPlaybackService 与 PlaylistsViewModel 都在构造时捕获
/// SynchronizationContext.Current（WinUI 3 的 UI 线程上它是 DispatcherQueueSynchronizationContext，
/// 已实测），事件与 await 续延靠它回到 UI 线程。
///
/// 刻意**不**在本壳 Dispose ServiceProvider：WPF 壳 OnExit 里同步 Dispose 会把已知的
/// "关闭期容器 Dispose 与 UI 收尾抢跑"缺陷一起复制过来；切片期不新增这条路径。
/// 音频设备的释放在窗口关闭路径上由 MainViewModel.CleanupAsync 负责（MainWindow.AppWindow_Closing）。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private Window? _window;

    public App()
    {
        InitializeComponent();
        // spec §4.4「保留 D-player 深色身份」：`Application.RequestedTheme = Dark`。
        // 必须在 App 构造函数里、窗口内容建立之前设置（不设 = 跟随系统主题，两壳就可能各随一侧）。
        // 取证状态分两半：已取证的只是"起窗后各表面都是深色"（未遮挡截图
        // final-fix-evidence-window-topmost.png；像素读数只有一份 final-fix-evidence-mica-ab.txt，
        // 它是同一次运行的三个窗口位置：#202020 标题栏/导航栏、#272727 内容区/播放器栏）；
        // **"浅色 Windows 下窗口仍是深色"这半边未验证** —— 这台机器本来就是深色
        // （AppsUseLightTheme=0），本机造不出对照条件，所以强制 Dark 与"系统本来就 Dark"无法区分。
        // 那半边记在 final-fix-report §C.3 的 NOT VERIFIED，需要用户在浅色系统下看一眼
        // （docs/PHASE20-COMPARISON.md §2.1 的 ＋ 项）。
        // 类型注意：WinUI 3 的 `Application.RequestedTheme` 是 `ApplicationTheme`（只有
        // Light/Dark 两个值），不是 FrameworkElement 上的 `ElementTheme`（写它是 CS0266）。
        RequestedTheme = ApplicationTheme.Dark;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        // WinUI 壳自己的数据目录：与 WPF 壳的 D-player 分开，两个壳互不覆盖落盘。
        services.AddDPlayerCore(configuration, new DPlayerDataPaths { FolderName = "D-player-winui" });
        // Core 的 PlaylistViewModel 工厂从容器解析 IFileDialogService，注册在各壳自己做（R-7）。
        services.AddSingleton<IFileDialogService, WinUiFileDialogService>();
        _services = services.BuildServiceProvider();

        // 需在 UI 线程构造状态与恢复；窗口显示后立刻执行
        var vm = _services.GetRequiredService<MainViewModel>();
        _window = new MainWindow(vm);
        _window.Activate();

        // 启动后就位（断点续播）——不自动出声
        _ = StartAsync(vm);
    }

    /// <summary>
    /// 水化容器 + 断点续播就位。切片期没有对话框宿主，恢复被跳过时只写调试输出
    /// （WPF 壳在这里弹 ConfirmDialog；属第二阶段对齐项）。
    /// </summary>
    private static async Task StartAsync(MainViewModel vm)
    {
        try
        {
            var restoreWarning = await vm.InitializeAsync();
            if (restoreWarning is not null)
                Debug.WriteLine($"[WinUI 壳] 断点续播被跳过：{restoreWarning}");
        }
        catch (Exception ex)
        {
            // 与 WPF 壳同样只作护栏：服务层自身已回退，这里不让启动因为水化失败而崩。
            Debug.WriteLine($"[WinUI 壳] InitializeAsync 抛出：{ex}");
        }
    }
}
