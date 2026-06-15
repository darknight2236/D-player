using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;
using UmaPlayer.Views.Dialogs;

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

            // Phase 2 一次性迁移: Phase 1 持久化的高度可能 < 500 (PlaylistView 不可见)
            // 检测并提升到 650, 让用户首次看到完整 UI。
            Height = settings.WindowHeight < 500 ? 650 : settings.WindowHeight;

            EnsureVisible();
        }
        catch
        {
            // 读盘失败 → 让 WPF 自己居中
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 异步水化多歌单容器(读 queue.json + 可能的 v1→v2 迁移)。
        // 失败 → JsonPlaylistService 内部已回 seed; UI 仍能用。
        try
        {
            await _vm.InitializeAsync();
        }
        catch
        {
            // 一道额外护栏: 服务保证不抛, 这里只防御未来回归。
        }
    }

    private bool _isClosing;

    /// <summary>
    /// 关闭时：合并最新窗口几何到设置文件，再让 VM 清理播放器资源，最后写队列快照。
    ///
    /// 关键纪律（cancel-and-close 模式）：本方法是 async void，多次 await 会让 WPF
    /// 在第一个 await yield 后立即继续关闭流程 —— ShutdownMode.OnLastWindowClose 会
    /// 触发 Application.Shutdown → Dispatcher.InvokeShutdown，把后续 await 的
    /// 续延扔进死消息循环。Phase 3 时仅有 2 个 await，settings 写盘抢在 dispatcher
    /// 关停前完成；Phase 4 加入 queue.json 写盘后 await 链变深，必须把首次 Closing
    /// 取消、做完异步工作再 Close()。Phase 6 把 queue.json 写盘下沉到 MainViewModel.CleanupAsync,
    /// 此处 await 链变成 settings UpdateAsync + CleanupAsync 两段, cancel-and-close 模式继续保护。
    /// </summary>
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // 第二次进入：异步工作已完成 → 真正关闭窗口
        if (_isClosing) return;

        // 首次进入：拦下关闭，跑完所有异步写盘
        e.Cancel = true;
        _isClosing = true;

        // 提前在 UI 线程读出 WPF DependencyProperty —— UpdateAsync 内部
        // ConfigureAwait(false) 后 mutator 会在 thread-pool 上执行，
        // 那里读 Left/Top/Width/ActualHeight 会抛 InvalidOperationException
        var left = Left;
        var top = Top;
        var width = Width;
        var height = ActualHeight;

        try
        {
            // 锁内 read-modify-write：仅改窗口几何，DefaultVolume 等其他字段保留磁盘最新值
            await _persistence.UpdateAsync(s => s with
            {
                WindowLeft = left, WindowTop = top,
                WindowWidth = width, WindowHeight = height
            });
        }
        catch { /* 关闭流程不打扰用户 */ }

        await _vm.CleanupAsync();

        // 异步链跑完，重新触发 Closing —— 此次 _isClosing == true，直接放行
        Close();
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

    /// <summary>Ctrl+, 打开设置对话框。</summary>
    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SettingsDialog.Show(this, _persistence);
            e.Handled = true;
        }
    }
}
