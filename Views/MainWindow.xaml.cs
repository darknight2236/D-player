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
    private readonly IQueuePersistence _queuePersistence;

    public MainWindow(
        MainViewModel vm,
        ISettingsPersistence persistence,
        IQueuePersistence queuePersistence)
    {
        InitializeComponent();
        _vm = vm;
        _persistence = persistence;
        _queuePersistence = queuePersistence;
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
    }

    /// <summary>关闭时：合并最新窗口几何到设置文件，再让 VM 清理播放器资源，最后写队列快照。</summary>
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
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

        // Phase 4：保存队列快照到 queue.json。
        // 必须在 CleanupAsync 之后调 SnapshotState 也 OK ——
        // Cleanup 仅解绑 TrackEnded，不修改 Queue/CurrentIndex/Shuffle/Repeat。
        try
        {
            var snapshot = _vm.Playlist.SnapshotState();    // UI 线程纯读
            await _queuePersistence.SaveAsync(snapshot);
        }
        catch { /* 写盘失败 = 用户下次启动队列丢失，与 settings 写盘失败行为对称 */ }
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
