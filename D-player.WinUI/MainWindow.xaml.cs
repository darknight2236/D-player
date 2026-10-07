using DPlayer.ViewModels;
using DPlayer.WinUI.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
// MicaBackdrop 就住在这个命名空间（不是 Microsoft.UI.Composition.SystemBackdrops —— 那里面的是
// MicaController / SystemBackdropConfiguration 那套手动路线，本壳不用）。2026-10-07 实测：
// 删掉本行 → CS0246 找不到 MicaBackdrop；删掉 SystemBackdrops 那行 → 构建照样 0/0。
// WinUI 不在任何门禁里，这种 using 一旦被当成"死引用"清掉，只有单壳构建能发现（见 README 的门禁规则）。
using Microsoft.UI.Xaml.Media;

namespace DPlayer.WinUI;

/// <summary>
/// 主窗口：只剩窗口级职责——标题栏、Mica、关闭 flush，以及把四个控件装配进三栏 + 底栏。
/// 左栏 / 中区 / 右栏 / 底栏各自的逻辑在 DPlayer.WinUI.Views 的四个 UserControl 里（Task 1 拆分）。
///
/// Mica 走框架的 Window.SystemBackdrop（不是手动 MicaController），前提是根 Grid 背景为 Transparent，
/// 实测数据与判读见 MainWindow.xaml 顶部注释。只消费 Core 的公开成员；关闭路径复刻 WPF 壳的
/// cancel-and-close（见 AppWindow_Closing）。
///
/// 本类不再实现 INotifyPropertyChanged：底部栏的三个呈现投影（PlayPauseGlyph / NowPlayingText /
/// TimeText）连同 x:Bind 一起搬进了 Views/PlayerBar.xaml.cs，那边自己发通知。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    /// <summary>cancel-and-close 守卫：首次 Closing 拦下、做完异步收尾再放行。</summary>
    private bool _isClosing;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        Title = "D-player";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Mica 生效（fix round 1 实测，见 MainWindow.xaml 顶部注释）：材质挂在窗口上，
        // 但它只在**没有不透明背景刷**的表面后面可见——所以根 Grid 必须是 Transparent，
        // 否则整扇材质会被页面底色盖住（上一轮"材质未挂载"的结论就是这个遮挡造成的误判）。
        SystemBackdrop = new MicaBackdrop();

        // 装配四个控件。槽位（三栏 + 底栏行）在 XAML 里，实例在代码里造：
        // 计划钉住的构造签名各收一个 Core 的 VM，而 XAML 声明元素需要 public 无参构造，
        // 两者不可兼得；x:Bind 的源也必须在控件自己的 InitializeComponent() 之前就位。
        // 数据源按 spec §3.1 分给各控件：导航与列表看 Playlists（ViewedPlaylist 是唯一真源），
        // 信息面板与底栏看 Player。
        var rail = new NavRail(_vm.Playlists);
        var tracks = new TrackList(_vm.Playlists);
        var info = new InfoPanel(_vm.Player);
        var bar = new PlayerBar(_vm.Player) { Playlists = _vm.Playlists };

        Grid.SetColumn(rail, 0);
        Grid.SetColumn(tracks, 1);
        Grid.SetColumn(info, 2);
        BodyGrid.Children.Add(rail);
        BodyGrid.Children.Add(tracks);
        BodyGrid.Children.Add(info);

        Grid.SetRow(bar, 2);
        RootGrid.Children.Add(bar);

        AppWindow.Closing += AppWindow_Closing;
    }

    // —— 关闭：落盘 + 释放音频设备（R-4） ——

    /// <summary>
    /// 复刻 WPF 壳 Views/MainWindow.xaml.cs 的 cancel-and-close：首次 Closing 取消关闭，
    /// 跑完 <see cref="MainViewModel.CleanupAsync"/>（写最终断点位置 + 释放 WASAPI 设备）后再真关。
    /// 少了这一步，最终位置只能靠 30 秒节流或"先暂停"才落盘，验收项⑤ 不成立。
    /// </summary>
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isClosing) return;    // 第二次进入：异步工作已完成 → 放行
        args.Cancel = true;
        _isClosing = true;

        try { await _vm.CleanupAsync(); }
        catch { /* 关闭流程不打扰用户 */ }

        Close();
    }
}
