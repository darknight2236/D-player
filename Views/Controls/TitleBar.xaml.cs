using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

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
            // 无边框窗最大化会铺满整显示器（四周含隐藏 resize 边框）并被 DWM 裁到工作区。
            // 用「工作区偏移 + 隐藏边框厚度」的常量式边距，使内容恰好填满工作区；
            // 不读窗口实际边界，避免布局时序导致 right/bottom 边距偏大（大片留空）。
            var rb = SystemParameters.WindowResizeBorderThickness;
            var wa = SystemParameters.WorkArea;
            root.Margin = new Thickness(
                wa.Left + rb.Left,
                wa.Top + rb.Top,
                SystemParameters.PrimaryScreenWidth - wa.Right + rb.Right,
                SystemParameters.PrimaryScreenHeight - wa.Bottom + rb.Bottom);
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
