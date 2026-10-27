using System.Linq;
using DPlayer.Configuration;
using DPlayer.Services;
using DPlayer.ViewModels;
using DPlayer.WinUI.Services;
using DPlayer.WinUI.Theme;
using DPlayer.WinUI.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
// MicaBackdrop 就住在这个命名空间（不是 Microsoft.UI.Composition.SystemBackdrops —— 那里面的是
// MicaController / SystemBackdropConfiguration 那套手动路线，本壳不用）。2026-10-07 实测：
// 删掉本行 → CS0246 找不到 MicaBackdrop；删掉 SystemBackdrops 那行 → 构建照样 0/0。
// WinUI 不在任何门禁里，这种 using 一旦被当成"死引用"清掉，只有单壳构建能发现（见 README 的门禁规则）。
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace DPlayer.WinUI;

/// <summary>
/// 主窗口：窗口级职责——标题栏、Mica、关闭 flush，把四个控件装配进三栏 + 底栏，
/// 以及**三栏的自适应口径**（右栏在窗口宽度越过阈值时自动收起/恢复，带迟滞带，手动优先）。
/// 左栏 / 中区 / 右栏 / 底栏各自的逻辑在 DPlayer.WinUI.Views 的四个控件里（Task 1 拆分）。
/// 两颗折叠钮都在各自栏头（收起后都保留 48px 栏头，钮仍点得到），所以本类不再持有折叠钮，
/// 只订阅 InfoPanel.ToggledManually 维护"手动优先"。
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

    /// <summary>相位 22 Task 5：窗口几何的落盘通道（settings.json 与音量共用一份）。</summary>
    private readonly ISettingsPersistence _settings;

    // 启动几何兜底：保存的宽或高任一小于下限（含从未保存过的 800×450 默认值）时，两维一起用
    // 这两档，不走系统默认小窗。
    private const int DefaultWindowWidth = 1200;
    private const int DefaultWindowHeight = 800;
    private const int MinWindowWidth = 800;
    private const int MinWindowHeight = 500;

    /// <summary>
    /// cancel-and-close 守卫：首次 Closing 拦下、做完异步收尾后置 true 并自行 Close()。
    /// 与旧版 <c>_isClosing</c> 的区别是两次进入的语义拆开了 —— 收尾期间用户再点关闭仍要
    /// 继续取消（<see cref="_isClosing"/>），只有收尾完成后的自家 Close() 才放行。
    /// </summary>
    private bool _cleanupCompleted;

    /// <summary>
    /// 右栏实例：自动收起的阈值判断要读它的 <see cref="InfoPanel.IsCollapsed"/> 并调
    /// <see cref="InfoPanel.SetCollapsed"/> —— 这正是 brief 的 Produces 契约要的那道缝。
    /// 只调公开方法/读公开属性，**不**伸进控件的可视化树；其余三个控件仍是局部变量。
    /// </summary>
    private readonly InfoPanel _infoPanel;

    /// <summary>
    /// 底栏实例：空格键全局播放/暂停（C3）需要调 <see cref="PlayerBar.TogglePlayPause"/>。
    /// </summary>
    private readonly PlayerBar _bar;

    /// <summary>
    /// 用户手动动过右栏：在"手动口径"和"自动口径"重新对得上之前，SizeChanged 不再替用户改宽度
    /// （spec §5「用户手动展开优先于自动收起，直到窗口越回阈值再恢复自动」）。
    /// </summary>
    private bool _infoPanelManualOverride;

    /// <summary>收尾进行中：用户再次点关闭时继续取消，直到 <see cref="_cleanupCompleted"/> 放行自家 Close()。</summary>
    private bool _isClosing;

    /// <summary>
    /// 缓存自 Tokens.xaml 的阈值（避免每次 SizeChanged 都查字典）。
    /// 与 NavRail 缓存 _expandedWidth/_collapsedWidth 同纪律：尺寸集中在 Tokens，代码只读一次。
    /// </summary>
    private readonly double _autoCollapseThreshold = TokenResource.Double("InfoPanelAutoCollapseWidth");
    private readonly double _autoCollapseHysteresis = TokenResource.Double("AutoCollapseHysteresis");

    public MainWindow(MainViewModel vm, ISettingsPersistence settings)
    {
        _vm = vm;
        _settings = settings;
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
        _bar = new PlayerBar(_vm.Player, _vm.Playlists);

        Grid.SetColumn(rail, 0);
        Grid.SetColumn(tracks, 1);
        Grid.SetColumn(_infoPanel, 2);
        BodyGrid.Children.Add(rail);
        BodyGrid.Children.Add(tracks);
        BodyGrid.Children.Add(_infoPanel);

        Grid.SetRow(_bar, 2);
        RootGrid.Children.Add(_bar);

        // 阈值 + 迟滞：挂在 RootGrid 上（它的宽度就是客户区宽度，比读 AppWindow.Size 少一层 DPI 换算）。
        // 写在代码里、不写成 XAML 的 SizeChanged="…"，是为了不碰 RootGrid 那行承重的 Background="Transparent"。
        RootGrid.SizeChanged += RootGrid_SizeChanged;

        // 用户手动点右栏折叠钮时置手动优先，直到宽度口径与自动口径重新对齐（见 RootGrid_SizeChanged）。
        _infoPanel.ToggledManually += (_, _) => _infoPanelManualOverride = true;

        // C3：空格键全局播放/暂停。handledEventsToo=true 确保即使焦点控件已处理也能拦截；
        // handler 内部再判断焦点是否在 Button/Slider/ListViewItem 上来决定是否消费。
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootGrid_KeyDown), handledEventsToo: true);

        // A4：标题栏双击最大化/还原。OverlappedPresenter 在 Microsoft.UI.Windowing 命名空间。
        AppTitleBar.DoubleTapped += AppTitleBar_DoubleTapped;

        AppWindow.Closing += AppWindow_Closing;

        // 几何恢复放在构造函数里（App 在构造之后才 Activate）：先摆好位置再显示，
        // 避免"先居中闪现再跳到记忆位置"。任何失败都不得阻断启动。
        RestoreWindowGeometry();
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
            _infoPanel.SetCollapsed(shouldCollapse, animate);
    }

    // —— C3：空格键全局播放/暂停 ——

    /// <summary>
    /// 空格键播放/暂停（spec §7 C 组 C3）。挂在 RootGrid 上、handledEventsToo=true，
    /// 但**只在焦点不在消费空格的控件上时**才触发——Button/Slider/ListViewItem 自己需要 Space
    /// （按钮点击、滑块微调、列表行选中），抢了就是 regression。用 VisualTree.FindAncestor 向上解析
    /// 判断焦点元素是否在消费空格的控件内部（唯一实现住在 Theme/VisualTree.cs，不在这里留副本）。
    ///
    /// Phase 22 Task 2 追加两条同义守卫：对话框开着（<see cref="WinUiDialogService.IsOpen"/>，
    /// 或任何挂在同一 XamlRoot 上的 ContentDialog 弹层）时一律不抢；焦点在文本输入控件里时
    /// （重命名/新建歌单的 TextBox）空格属于输入内容，不能变成播放/暂停。
    /// </summary>
    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space) return;
        var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
        if (focused is null) return;
        if (WinUiDialogService.IsOpen || IsContentDialogOpen()) return;
        if (IsTextInputFocused(focused)) return;
        // 焦点在 Button（含其子元素）/ Slider / ListViewItem 上时不抢：那是它们的键
        if (VisualTree.FindAncestor<Button>(focused) is not null) return;
        if (focused is Slider) return;
        if (VisualTree.FindAncestor<Slider>(focused) is not null) return;
        if (VisualTree.FindAncestor<ListViewItem>(focused) is not null) return;
        _bar.TogglePlayPause();
        e.Handled = true;
    }

    /// <summary>
    /// 焦点（含其祖先）是否落在文本输入控件里。TextBox 的焦点元素常是模板内的
    /// ScrollViewer/ContentPresenter 而不是 TextBox 本身，所以先判本体、再向上找祖先。
    /// ComboBox 一并算进去：可编辑下拉里的空格同样属于输入。
    /// </summary>
    private static bool IsTextInputFocused(DependencyObject focused)
        => focused is TextBox or PasswordBox or RichEditBox or ComboBox
           || VisualTree.FindAncestor<TextBox>(focused) is not null
           || VisualTree.FindAncestor<PasswordBox>(focused) is not null
           || VisualTree.FindAncestor<RichEditBox>(focused) is not null
           || VisualTree.FindAncestor<ComboBox>(focused) is not null;

    /// <summary>
    /// 同一 XamlRoot 上是否有 ContentDialog 弹层。这条是 <see cref="WinUiDialogService.IsOpen"/>
    /// 的兜底：任何绕过宿主直接 ShowAsync 的对话框（或宿主标志被异常路径跳过）也不能让空格漏过去。
    /// </summary>
    private bool IsContentDialogOpen()
        => RootGrid.XamlRoot is { } root
           && VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
               .Any(p => p.Child is not null && VisualTree.FindDescendant<ContentDialog>(p.Child) is not null);

    // —— A4：标题栏双击最大化/还原 ——

    /// <summary>
    /// 标题栏双击切换最大化/还原（spec §7 清单 A4）。OverlappedPresenter 是 WinUI 3 的窗口呈现器，
    /// 只在 overlapped（非全屏/非 snap）模式下可用；全屏或 snap 时 Presenter 不是 OverlappedPresenter，
    /// as 转换返回 null → 静默跳过，不抛。
    /// </summary>
    private void AppTitleBar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is null) return;
        if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
        else presenter.Maximize();
    }

    // —— 关闭：落盘 + 释放音频设备（R-4） ——

    /// <summary>
    /// 复刻 WPF 壳 Views/MainWindow.xaml.cs 的 cancel-and-close：首次 Closing 取消关闭，
    /// 跑完 <see cref="MainViewModel.CleanupAsync"/>（写最终断点位置 + 释放 WASAPI 设备）后再真关。
    /// 少了这一步，最终位置只能靠 30 秒节流或"先暂停"才落盘，验收项⑤ 不成立。
    ///
    /// Phase 22 Task 5 拆成两个标志：收尾完成前每次用户关闭都继续取消（<c>_isClosing</c> 保持 true），
    /// 收尾完成后由自家 <c>Close()</c> 进入且 <c>_cleanupCompleted</c> 放行 —— 旧写法第二次进入就放行，
    /// 收尾期间的连点会绕过清理。
    /// </summary>
    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_cleanupCompleted) return;                 // our own Close() after cleanup -> allow
        args.Cancel = true;
        if (_isClosing) return;                        // second user request while cleanup runs -> keep waiting
        _isClosing = true;

        try { await SaveWindowGeometryAsync(); }
        catch (Exception ex) { AppLog.Write("保存窗口几何失败", ex); }

        try { await _vm.CleanupAsync(); }
        catch { /* 关闭流程不打扰用户 */ }

        _cleanupCompleted = true;
        Close();
    }

    // —— 窗口几何：启动恢复 + 关闭落盘 ——

    /// <summary>
    /// 从 settings.json 恢复窗口位置与尺寸（物理像素）。宽或高任一低于下限（800×500）时，
    /// 两者一起回落到 1200×800 —— 不做逐维兜底，否则首启的 800×450 默认值会拼出 800×800；
    /// 保存的中心点落在所有显示器的工作区之外时，在主显示器工作区居中。整段包在 try/catch 里：
    /// 几何问题永远不能挡住启动。
    /// </summary>
    private void RestoreWindowGeometry()
    {
        try
        {
            AppSettings saved;
            try { saved = _settings.LoadAsync().GetAwaiter().GetResult(); }
            catch { return; }

            // 单一判定（spec §4.5）：任一维低于下限就整对用默认值。逐维兜底会把首启的
            // AppSettings 默认 800×450 拼成 800×800。
            var useDefaults = saved.WindowWidth < MinWindowWidth || saved.WindowHeight < MinWindowHeight;
            var width = useDefaults ? DefaultWindowWidth : (int)Math.Round(saved.WindowWidth);
            var height = useDefaults ? DefaultWindowHeight : (int)Math.Round(saved.WindowHeight);

            var left = (int)Math.Round(saved.WindowLeft);
            var top = (int)Math.Round(saved.WindowTop);
            if (useDefaults || !IsWindowCenterVisible(left, top, width, height))
            {
                (left, top) = CenterOnPrimary(width, height);
            }

            AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
        }
        catch (Exception ex)
        {
            AppLog.Write("恢复窗口几何失败", ex);
        }
    }

    /// <summary>窗口中心是否落在任一显示器的工作区内（显示器拔插后旧坐标可能完全在屏外）。</summary>
    /// <remarks>
    /// 不用 brief 里的 <c>DisplayArea.FindAll().Any(...)</c>：本机 WinAppSDK 运行时枚举
    /// <c>IReadOnlyList&lt;DisplayArea&gt;</c> 时在 CsWinRT 的 <c>Make_IEnumerableObjRef</c> 里抛
    /// <c>InvalidCastException: ClassFactory 无法供应请求的类</c>（2026-10-23 实测，debug 日志见
    /// task-5-report.md），导致恢复整段被 catch。GetFromPoint 是同一语义的单点查询，不经过枚举。
    /// </remarks>
    private static bool IsWindowCenterVisible(int left, int top, int width, int height)
    {
        var cx = left + width / 2;
        var cy = top + height / 2;
        var area = DisplayArea.GetFromPoint(new PointInt32(cx, cy), DisplayAreaFallback.None);
        if (area is null) return false;
        var work = area.WorkArea;
        return cx >= work.X && cx < work.X + work.Width
            && cy >= work.Y && cy < work.Y + work.Height;
    }

    /// <summary>主显示器工作区居中坐标（工作区已排除任务栏）。</summary>
    private static (int Left, int Top) CenterOnPrimary(int width, int height)
    {
        var work = DisplayArea.Primary.WorkArea;
        return (work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2);
    }

    /// <summary>
    /// 关闭时落盘窗口几何。只在 OverlappedPresenter 处于 Restored 时写 —— 最大化/最小化/全屏下
    /// AppWindow.Position/Size 是"当前呈现"而不是用户记忆的普通几何，写进去会把下次启动摆成整屏。
    /// </summary>
    private async Task SaveWindowGeometryAsync()
    {
        if (AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return;

        var position = AppWindow.Position;
        var size = AppWindow.Size;
        await _settings.UpdateAsync(s => s with
        {
            WindowLeft = position.X,
            WindowTop = position.Y,
            WindowWidth = size.Width,
            WindowHeight = size.Height
        });
    }
}
