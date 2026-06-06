using System.ComponentModel;
using System.Windows;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Views;

/// <summary>
/// 主窗口 —— 仅作为 PlayerBar 的容器。
/// 负责窗口尺寸/位置的恢复与保存，以及关闭时触发 VM 清理。
///
/// 为什么不让 DI 解析窗口？
///   因为窗口的构造需要 persistence 提前同步读取设置（拿到位置），
///   而 DI 容器无法表达"先读盘再 new"，由 App.OnStartup 显式编排更清晰。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ISettingsPersistence _persistence;

    public MainWindow(MainViewModel vm, ISettingsPersistence persistence)
    {
        InitializeComponent();
        _vm = vm;
        _persistence = persistence;
        DataContext = _vm;

        // 同步加载窗口几何 —— 文件极小，启动期阻塞可忽略
        try
        {
            var settings = _persistence.LoadAsync().GetAwaiter().GetResult();
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            EnsureVisible();
        }
        catch
        {
            // 读盘失败 → 让 WPF 自己居中
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>关闭时：合并最新窗口几何到设置文件，再让 VM 清理播放器资源。</summary>
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            // 先读再写，保留 VM 写入的其他字段（如 DefaultVolume）
            var settings = await _persistence.LoadAsync();
            settings = settings with
            {
                WindowLeft = Left, WindowTop = Top,
                WindowWidth = Width, WindowHeight = ActualHeight
            };
            await _persistence.SaveAsync(settings);
        }
        catch { /* 关闭流程不打扰用户 */ }

        await _vm.CleanupAsync();
    }

    /// <summary>
    /// 防外接屏拔掉后窗口"飘到屏外"：
    /// 检查窗口中心是否在虚拟屏（所有显示器合集）内，不在则回落到主屏居中。
    /// </summary>
    private void EnsureVisible()
    {
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        if (centerX < SystemParameters.VirtualScreenLeft ||
            centerX > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth ||
            centerY < SystemParameters.VirtualScreenTop ||
            centerY > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        }
    }
}
