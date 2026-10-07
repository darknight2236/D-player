using DPlayer.ViewModels;
using DPlayer.WinUI.Theme;
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
/// 主窗口：窗口级职责——标题栏（含右栏那颗折叠钮）、Mica、关闭 flush，把四个控件装配进三栏 + 底栏，
/// 以及**三栏的自适应口径**（右栏在窗口宽度越过阈值时自动收起/恢复，带迟滞带，手动优先）。
/// 左栏 / 中区 / 右栏 / 底栏各自的逻辑在 DPlayer.WinUI.Views 的四个控件里（Task 1 拆分）。
/// 左栏的折叠钮在 NavRail 自己栏内（折叠态 48px 仍点得到），所以本类不需要它的引用。
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

    /// <summary>
    /// 右栏实例：自动收起的阈值判断要读它的 <see cref="InfoPanel.IsCollapsed"/> 并调
    /// <see cref="InfoPanel.SetCollapsed"/> —— 这正是 brief 的 Produces 契约要的那道缝。
    /// 只调公开方法/读公开属性，**不**伸进控件的可视化树；其余三个控件仍是局部变量。
    /// </summary>
    private readonly InfoPanel _infoPanel;

    /// <summary>
    /// 用户手动动过右栏：在"手动口径"和"自动口径"重新对得上之前，SizeChanged 不再替用户改宽度
    /// （spec §5「用户手动展开优先于自动收起，直到窗口越回阈值再恢复自动」）。
    /// </summary>
    private bool _infoPanelManualOverride;

    /// <summary>cancel-and-close 守卫：首次 Closing 拦下、做完异步收尾再放行。</summary>
    private bool _isClosing;

    /// <summary>
    /// 缓存自 Tokens.xaml 的阈值（避免每次 SizeChanged 都查字典）。
    /// 与 NavRail 缓存 _expandedWidth/_collapsedWidth 同纪律：尺寸集中在 Tokens，代码只读一次。
    /// </summary>
    private readonly double _autoCollapseThreshold = TokenResource.Double("InfoPanelAutoCollapseWidth");
    private readonly double _autoCollapseHysteresis = TokenResource.Double("AutoCollapseHysteresis");

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
        _infoPanel = new InfoPanel(_vm.Player);
        var bar = new PlayerBar(_vm.Player) { Playlists = _vm.Playlists };

        Grid.SetColumn(rail, 0);
        Grid.SetColumn(tracks, 1);
        Grid.SetColumn(_infoPanel, 2);
        BodyGrid.Children.Add(rail);
        BodyGrid.Children.Add(tracks);
        BodyGrid.Children.Add(_infoPanel);

        Grid.SetRow(bar, 2);
        RootGrid.Children.Add(bar);

        // 阈值 + 迟滞：挂在 RootGrid 上（它的宽度就是客户区宽度，比读 AppWindow.Size 少一层 DPI 换算）。
        // 写在代码里、不写成 XAML 的 SizeChanged="…"，是为了不碰 RootGrid 那行承重的 Background="Transparent"。
        RootGrid.SizeChanged += RootGrid_SizeChanged;
        SyncInfoPanelToggleGlyph();

        AppWindow.Closing += AppWindow_Closing;
    }

    // —— 右栏自适应：阈值 + 迟滞 + 手动优先 ——

    /// <summary>
    /// 窗口宽度低于 <c>InfoPanelAutoCollapseWidth</c>（Tokens，960）时自动收起右栏，带
    /// <c>AutoCollapseHysteresis</c>（Tokens，8px）迟滞带 —— 没有迟滞的话，在阈值上拖 1px 会来回收放，
    /// 那就是 A2 的"抽搐抖动"失败态（spec §8 第一条缓解项）。
    ///
    /// 首帧不播动画（<c>PreviousSize.Width == 0</c> 那一趟）：否则每次启动都看一次右栏当场收拢。
    /// </summary>
    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var animate = e.PreviousSize.Width > 0;

        if (_infoPanelManualOverride)
        {
            var autoWouldCollapse = width < _autoCollapseThreshold;
            var agrees = (autoWouldCollapse && _infoPanel.IsCollapsed)
                || (!autoWouldCollapse && width >= _autoCollapseThreshold + _autoCollapseHysteresis && !_infoPanel.IsCollapsed);
            if (agrees) _infoPanelManualOverride = false;
            return;
        }

        var shouldCollapse = width < _autoCollapseThreshold;
        if (_infoPanel.IsCollapsed != shouldCollapse)
        {
            _infoPanel.SetCollapsed(shouldCollapse, animate);
            // 自动收起/恢复后也要同步 chevron 指向（否则窗口打开 <960px 时 chevron 方向不对）。
            SyncInfoPanelToggleGlyph();
        }
    }

    /// <summary>标题栏那颗右栏折叠钮：置手动覆盖（见上），然后在收起/展开之间往返。</summary>
    private void InfoPanelToggle_Click(object sender, RoutedEventArgs e)
    {
        _infoPanelManualOverride = true;
        _infoPanel.SetCollapsed(!_infoPanel.IsCollapsed, animate: true);
        SyncInfoPanelToggleGlyph();
    }

    /// <summary>
    /// 钮上的 chevron 指向"按下去会发生什么"：展开态 E76C（向右推走），折叠态 E76B（向左拉回）。
    /// 码位写成 <c>((char)0x…)</c> 而不是 \u 转义/字面私有区字符：那两种写法在本轮编辑链路上都被吞过
    /// （实测见 task-2-report.md；Views/NavRail.xaml.cs 里那两个常量是逐字节复核过的字面字符）。
    /// </summary>
    private void SyncInfoPanelToggleGlyph()
        => InfoPanelToggleGlyph.Glyph = ((char)(_infoPanel.IsCollapsed ? 0xE76B : 0xE76C)).ToString();

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
