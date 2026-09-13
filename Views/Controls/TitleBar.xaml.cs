using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Threading;

namespace DPlayer.Views.Controls;

/// <summary>
/// 自绘无边框标题栏（Phase 17）。依赖属性 Title / ShowMaximize。
/// 动作经 SystemCommands 作用于父 Window；最大化时按 WorkArea 差值补边距防内容贴屏边；Maximized 时切换 Maximize/Restore 图标。
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TitleBar), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty ShowMaximizeProperty =
        DependencyProperty.Register(nameof(ShowMaximize), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnShowMaximizeChanged));

    public bool ShowMaximize
    {
        get => (bool)GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    private Window? _window;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is null) return;
        _window.StateChanged += OnWindowStateChanged;
        ApplyShowMaximize();
        ApplyWindowState();
    }

    private static void OnShowMaximizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TitleBar)d).ApplyShowMaximize();

    private void ApplyShowMaximize()
    {
        var vis = ShowMaximize ? Visibility.Visible : Visibility.Collapsed;
        MinButton.Visibility = vis;
        MaxRestoreButton.Visibility = vis;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => ApplyWindowState();

    private void ApplyWindowState()
    {
        if (_window is null) return;
        var maximized = _window.WindowState == WindowState.Maximized;
        MaxIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;

        if (_window.Content is FrameworkElement root)
        {
            if (!maximized)
            {
                root.Margin = new Thickness(0);
                return;
            }
            // 无边框窗最大化会铺满整显示器并被 DWM 裁到工作区；按 窗口实际边界 vs 工作区 差值补边距
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var wa = SystemParameters.WorkArea;
                var left = wa.Left - _window.Left;
                var top = wa.Top - _window.Top;
                var right = (_window.Left + _window.ActualWidth) - wa.Right;
                var bottom = (_window.Top + _window.ActualHeight) - wa.Bottom;
                root.Margin = new Thickness(left < 0 ? 0 : left, top < 0 ? 0 : top, right < 0 ? 0 : right, bottom < 0 ? 0 : bottom);
            }), DispatcherPriority.Loaded);
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null) SystemCommands.MinimizeWindow(_window);
    }

    private void MaxRestore_Click(object sender, RoutedEventArgs e)
    {
        if (_window is null) return;
        if (_window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(_window);
        else SystemCommands.MaximizeWindow(_window);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null) SystemCommands.CloseWindow(_window);
    }
}
