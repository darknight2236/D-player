using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace DPlayer.Views.Controls;

/// <summary>
/// 自绘无边框标题栏（Phase 17）。依赖属性 Title / ShowMaximize。
/// 动作经 SystemCommands 作用于父 Window；最大化时给窗口根容器加
/// WindowResizeBorderThickness 边距防内容贴屏边；Maximized 时切换 Maximize/Restore 图标。
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

        // 最大化边距修正：无边框窗最大化时内容会顶到屏边/任务栏下，加 resize 边框厚度补偿
        if (_window.Content is FrameworkElement root)
        {
            var b = SystemParameters.WindowResizeBorderThickness;
            root.Margin = maximized ? new Thickness(b.Left, b.Top, b.Right, b.Bottom) : new Thickness(0);
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
