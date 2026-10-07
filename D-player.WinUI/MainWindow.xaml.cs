using System.Collections.Specialized;
using System.ComponentModel;
using DPlayer.Models;
using DPlayer.ViewModels;
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

namespace DPlayer.WinUI;

/// <summary>
/// Phase 20 切片主窗口：Fluent 深色 + Mica + 自绘标题栏 + 三区布局（左歌单 / 中曲目 / 下播放器栏）。
/// Mica 走框架的 Window.SystemBackdrop（不是手动 MicaController），前提是根 Grid 背景为 Transparent，
/// 实测数据与判读见 MainWindow.xaml 顶部注释。
/// 只消费 Core 的公开成员；关闭路径复刻 WPF 壳的 cancel-and-close（见 AppWindow_Closing）。
///
/// 底部栏的三个呈现投影（PlayPauseGlyph / NowPlayingText / TimeText）走 x:Bind OneWay + 本类
/// 自己实现 INPC：播放器每次 PropertyChanged 就重发这三个名字。进度条不在此列——它直接绑
/// Core 的 <c>PlayerViewModel.PositionNormalized</c>（WPF 的 PlayerBar.xaml 绑的也是它，
/// 壳侧再写一份同公式就是影子实现）。
/// （不给本类加 INPC 的话 XamlCompiler 会报 WMC1506 "OneWay bindings require at least one of
/// their steps to support raising notifications"，且值不会自动刷新。）
/// </summary>
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _vm;

    /// <summary>cancel-and-close 守卫：首次 Closing 拦下、做完异步收尾再放行。</summary>
    private bool _isClosing;

    /// <summary>当前挂了 Queue.CollectionChanged 的歌单，换查看项时解绑重挂。</summary>
    private PlaylistViewModel? _hookedPlaylist;

    /// <summary>
    /// 左栏重建重入守卫：本方法里设 Nav.SelectedItem 会同步回调 Nav_SelectionChanged，
    /// 那里回写 ViewedPlaylist 又触发 Playlists_PropertyChanged → 再次进入本方法。
    /// 第二次进来时菜单已经就是我们要的样子，跳过即可（深度锁在 1）。
    /// </summary>
    private bool _syncingMenu;

    /// <summary>Slider 模板 Thumb 只挂一次（<c>Loaded</c> 可能重复触发）。</summary>
    private bool _dragHooked;

    /// <summary>轨道单击的挂接同样只做一次。</summary>
    private bool _railHooked;

    /// <summary>挂好接的拖动滑块：单击定位要按它的宽度算出滑块中心的可行程（见 TryGetTrackFraction）。</summary>
    private Thumb? _positionThumb;

    /// <summary>模板里的轨道元素（HorizontalTemplate）：单击定位以它自己的边界为准，而不是 Slider 的整体宽度。</summary>
    private FrameworkElement? _positionTrack;

    public event PropertyChangedEventHandler? PropertyChanged;

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

        // 进度条的两条用户输入路径都在模板部件上，所以都在代码后置里挂（HookSliderParts）：
        // 拖动进度条：WinUI 3 的 Slider **没有** DragStarted/DragCompleted —— 实测三条路都走不通：
        //   1) XAML `DragStarted="…"`            → XamlCompiler WMC0011 "Unknown member"
        //   2) XAML `prim:Thumb.DragStarted="…"`  → XamlCompiler WMC0010 "Unknown attachable member"
        //   3) C# `PositionSlider.DragStarted += …` → CS1061（WinUI 3 的元数据里没有 SliderBase，
        //      这对事件只存在于模板内部 Thumb 上）
        // 所以取 Thumb 模板部件（名字来自 Windows App SDK 自带 Themes/generic.xaml 的 Slider 模板：
        // HorizontalThumb / VerticalThumb）订阅 DragStarted/DragCompleted —— 这正是 WPF 的
        // Views/Controls/PlayerBar.xaml.cs:70-74 订阅的那一对事件的同一个来源，语义一致：
        // 拖动期间 Core 的 IsSeeking 为真 → 30 Hz 位置回写不会把滑块拽回（PlayerViewModel.cs:180-186），
        // 拖动结束时才提交一次 Seek。单击定位挂在模板轨道元素上（Position_TrackPressed）：原生处理是
        // Slider 自己的类处理器，只有比它更深的元素能在它把 Value 量化到端点之前拿到这次按下。
        // Window 本身没有 Loaded 事件（那是 FrameworkElement 的）——挂根 Grid
        RootGrid.Loaded += (_, _) => { SyncPlaylistMenu(); HookSliderParts(); };
        // 歌单是 InitializeAsync 里异步水化的，比 Loaded 晚 → 集合变化时必须重建左栏
        _vm.Playlists.Playlists.CollectionChanged += (_, _) => SyncPlaylistMenu();
        _vm.Playlists.PropertyChanged += Playlists_PropertyChanged;
        _vm.Player.PropertyChanged += Player_PropertyChanged;
        AppWindow.Closing += AppWindow_Closing;
    }

    // —— 左栏：歌单导航 ——

    /// <summary>
    /// 左栏是 <see cref="PlaylistsViewModel.ViewedPlaylist"/> 的投影（WPF 侧的 sidebar 就是直接
    /// TwoWay 绑定它），所以选中项的优先级固定是：VM 的 ViewedPlaylist → 重建前已选项 → 第一项。
    /// 把"第一项"放在最前会在首次重建时把 Core 按持久化 CurrentPlaylistId 恢复出来的 ViewedPlaylist 顶掉。
    /// </summary>
    private void SyncPlaylistMenu()
    {
        if (_syncingMenu) return;      // 见字段注释：设 SelectedItem 会绕回这里
        _syncingMenu = true;
        try
        {
            var previous = (Nav.SelectedItem as NavigationViewItem)?.Tag as PlaylistViewModel;

            Nav.MenuItems.Clear();
            foreach (var pl in _vm.Playlists.Playlists)
                Nav.MenuItems.Add(new NavigationViewItem { Content = pl.Name, Tag = pl });

            var items = Nav.MenuItems.OfType<NavigationViewItem>().ToList();
            var target = items.FirstOrDefault(i => ReferenceEquals(i.Tag, _vm.Playlists.ViewedPlaylist))
                ?? items.FirstOrDefault(i => ReferenceEquals(i.Tag, previous))
                ?? items.FirstOrDefault();
            if (target is not null) Nav.SelectedItem = target;   // 触发 SelectionChanged → 换列表数据源
        }
        finally { _syncingMenu = false; }
    }

    private void Nav_SelectionChanged(object sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: PlaylistViewModel pl })
        {
            // 已经是 ViewedPlaylist 时不回写：整栏重建也会走到这里，回写等于把导航当成权威。
            if (!ReferenceEquals(_vm.Playlists.ViewedPlaylist, pl))
                _vm.Playlists.ViewedPlaylist = pl;
            ResyncView();   // 换列表数据源 + 重挂 Queue 事件 + 刷新空状态
        }
    }

    /// <summary>
    /// ViewedPlaylist 也可能由 Core 侧改（Hydrate 恢复 / Add / Remove / Move / 导入），
    /// 这些改动比 CollectionChanged 晚，必须回灌左栏，否则 pane 停在第一项而列表已是恢复出来的那单。
    /// </summary>
    private void Playlists_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistsViewModel.ViewedPlaylist)) SyncPlaylistMenu();
        ResyncView();
    }

    private void ResyncView()
    {
        var pl = _vm.Playlists.ViewedPlaylist;
        if (!ReferenceEquals(_hookedPlaylist, pl))
        {
            if (_hookedPlaylist is not null) _hookedPlaylist.Queue.CollectionChanged -= OnViewedQueueChanged;
            _hookedPlaylist = pl;
            if (pl is not null) pl.Queue.CollectionChanged += OnViewedQueueChanged;
        }

        // 换查看项时才换列表数据源；同一集合实例重复赋值会被 ItemsControl 自己吃掉，
        // 但显式判等可以避免每 33ms 的播放器心跳都去碰一次 ItemsSource。
        var queue = pl?.Queue;
        if (!ReferenceEquals(TrackList.ItemsSource, queue)) TrackList.ItemsSource = queue;

        UpdateEmptyHint();
        RefreshView();
    }

    private void OnViewedQueueChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyHint();

    private void UpdateEmptyHint()
    {
        var pl = _vm.Playlists.ViewedPlaylist;
        EmptyHint.Visibility = pl is null || pl.Queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // —— 中区：曲目列表 ——

    private async void TrackList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // 命中测试优先于 SelectedItem：DoubleTapped 挂在 ListView 本体上，列表下方的空白区双击
        // 同样会走到这里，读 SelectedItem 就会把"上一次选中的那首"再播一遍。WPF 侧
        // （Views/Controls/PlaylistView.xaml.cs:162-166）是 FindAncestor<ListBoxItem>(e.OriginalSource)
        // 并写着"空白区双击不触发"，本壳同纪律：**从被点中的元素向上解析 ListViewItem 容器**，
        // 解析不出来（列表下方的空白区）就什么都不做 —— 绝不回读 SelectedItem。
        // 用 e.OriginalSource 转 FrameworkElement 起步：非可视节点（如 Run）转不过来说明没点在行上。
        // 实测：`ListView.ContainerFromPoint` / `ContainerFromElement` 在 WinUI 3 的托管投影里不存在
        // （CS1061），所以容器只能自己沿可视化树向上找（VisualTreeHelper.GetParent 收 DependencyObject，
        // 见文件末尾的 FindAncestor）。
        if (e.OriginalSource is not FrameworkElement source) return;
        if (FindAncestor<ListViewItem>(source) is not { } container) return;
        if (container.Content is not Track track) return;

        var pl = _vm.Playlists.ViewedPlaylist;
        if (pl is null) return;

        var index = IndexOfByReference(pl.Queue, track);
        if (index < 0) return;

        // 与 WPF 壳共用同一条入口（Views/Controls/PlaylistView.xaml.cs → PlaylistsViewModel.HandleDoubleClickPlay）：
        // 它内部先切 CurrentPlaylistId，再走 PlayTrackAtCommand —— 而 PlayTrackAt 的第一步是
        // _shuffleHistory.Clear()（"视为新会话"）。两壳的双击因此语义一致；自己拼一半必然漂移。
        await _vm.Playlists.HandleDoubleClickPlay(pl, index);
    }

    /// <summary>
    /// 按引用身份回找索引。Track 是 record（结构相等），Queue.IndexOf 会把
    /// "同一文件入队两次"的两个占位 Track 认成同一个（COUPLING.md §5 同纪律）。
    /// </summary>
    private static int IndexOfByReference(IReadOnlyList<Track> queue, Track track)
    {
        for (int i = 0; i < queue.Count; i++)
        {
            if (ReferenceEquals(queue[i], track)) return i;
        }
        return -1;
    }

    /// <summary>
    /// 给 Slider 的模板部件挂上进度条的两条用户输入路径：Thumb 的 DragStarted/DragCompleted（拖动）
    /// 与轨道元素的 PointerPressed（单击定位）。先显式 <c>ApplyTemplate()</c> 再找部件，使这条查找不依赖
    /// "模板在本方法跑过时已经应用完"这个没人保证的前提；找不到部件时**静默跳过**，各自的退化方向写在
    /// 下面的注释里。Loaded 可能多次触发，故用 <see cref="_dragHooked"/> / <see cref="_railHooked"/> 各挂一次。
    /// </summary>
    private void HookSliderParts()
    {
        if (_dragHooked && _railHooked) return;
        PositionSlider.ApplyTemplate();

        // 首选模板部件名 HorizontalThumb（来源：Windows App SDK 自带 Themes/generic.xaml 的 Slider 模板），
        // 退路是向下找第一个后代 Thumb（不依赖名字；SliderInnerThumb 是 Thumb 模板里的 Ellipse，不是 Thumb）。
        // 2026-10-07 实测：本壳上 `PositionSlider.FindName("HorizontalThumb")` 取不到东西（同一个方法里
        // FindName("HorizontalTemplate") 读出的是 null），命中的是向下找那条退路 —— 原生控件的模板名字
        // 不在托管 namescope 里。所以下面找轨道也不单靠名字。
        // 挂没挂上曾经只有"空队列（Duration=0）下拖动不崩"这种**不区分**的证据（那种情形 Position_Changed
        // 在下面的 Duration 守卫处就返回了，挂上挂不上 observable 上没区别）；2026-10-07 用真机注入鼠标
        // 量到了区分得出来的读数：按住滑块拖到行程 30% → IsSeeking 翻一次 True→False、松手后 Value 停在
        // 0.3 → 部件确实被找到并挂上了（见 .superpowers/sdd/…/click-to-position-report.md §3）。
        var thumb = _positionThumb
            ?? PositionSlider.FindName("HorizontalThumb") as Thumb
            ?? FindDescendant<Thumb>(PositionSlider);
        _positionThumb = thumb;

        // 轨道元素：单击定位挂在它上面（见 Position_TrackPressed），几何量也从它读 —— 轨道边界与
        // Slider 边界在本模板里恰好相同（实测 x=0 / w=904），但那是量出来的，不是假设出来的。
        // 先按部件名找，找不到就用"挂好接的滑块的可视化父节点"：实测模板树里 HorizontalThumb 的直接
        // 父节点就是 HorizontalTemplate（两条 Rail Rect 与三根 TickBar 也挂在它下面）。
        // 两处都落空时**不挂**：此时单击退回控件自己的 move-to-point（StepFrequency 已在 XAML 里调到
        // 千分之一程，实测落点仍在 0.248 / 0.499 / 0.749 这种点上，只是精度约一个像素），
        // 不会退回"饱和到端点"。
        if (!_railHooked)
        {
            _positionTrack = PositionSlider.FindName("HorizontalTemplate") as FrameworkElement
                ?? (thumb is null ? null : VisualTreeHelper.GetParent(thumb) as FrameworkElement);
            if (_positionTrack is not null)
            {
                _positionTrack.AddHandler(UIElement.PointerPressedEvent,
                    new PointerEventHandler(Position_TrackPressed), handledEventsToo: true);
                _railHooked = true;
            }
        }

        if (_dragHooked) return;
        if (thumb is null) return;          // 两条路都没命中：保持未挂状态，拖动退回"连续 Seek"的旧行为
        thumb.DragStarted += Position_DragStarted;
        thumb.DragCompleted += Position_DragCompleted;
        _dragHooked = true;
    }

    /// <summary>沿可视化树向上找指定类型的祖先（WPF 侧 FindAncestor 的 WinUI 版）；走到顶或传入非可视节点时返回 null。</summary>
    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj is not null)
        {
            if (obj is T match) return match;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    /// <summary>沿可视化树深度优先找第一个指定类型的后代。</summary>
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    // —— 底部：播放器栏 ——

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Player.CurrentTrack is null)
            _vm.Playlists.ViewedPlaylist?.PlayCurrentCommand.Execute(null);
        else
            _vm.Player.PlayPauseCommand.Execute(null);
    }

    /// <summary>
    /// 轨道上按下 = 单击定位。挂在模板轨道元素（HorizontalTemplate）的 PointerPressed 上，
    /// 而且必须挂在**比 Slider 更深的元素**上：原生处理是 Slider 自己的类处理器，事件冒泡到它时
    /// 已经晚了 —— 实测在 Slider 上观察时 <c>Value</c> 已经被改成端点值，在轨道元素上观察时还是旧值。
    /// 原生那条路做的事：把按下点换算成分数后按 <c>StepFrequency</c>（默认 1）量化，
    /// 在 0..1 的量程上只剩 {0,1} 两个落点，所以点哪儿都饱和到端点（2026-10-07 用户实测到的缺陷，
    /// 根因见 .superpowers/sdd/…/click-to-position-report.md §1，`LargeChange` 那条解释已被量掉）。
    /// 这里把事件吃掉、自己按测出来的几何算落点，并且照拖动那条路的纪律提交：按住 IsSeeking，一次提交。
    /// </summary>
    private void Position_TrackPressed(object sender, PointerRoutedEventArgs e)
    {
        // 按在滑块上：交给 DragStarted / DragCompleted（既不在这里重复提交，也不跳位）
        if (FindAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null) return;
        if (!TryGetTrackFraction(e, out var fraction)) return;   // 几何读不到就别动，让原生那半（已量化到千分之一程）接手
        e.Handled = true;                 // 必须在提交前吃掉：否则 Slider 的类处理器会把 Value 改到端点
        var player = _vm.Player;
        if (player.Duration <= TimeSpan.Zero) return;   // 没有载入曲目：什么都不提交
        player.SeekStartedCommand.Execute(null);        // 与拖动同纪律：提交期间抑制 30 Hz 位置回写
        PositionSlider.Value = fraction;               // 程序写入不参与 StepFrequency 量化（实测）
        player.SeekCompletedCommand.Execute(fraction); // 归一化 [0,1] → 服务，唯一一次提交
    }

    /// <summary>
    /// 按下点在轨道上的分数：以**轨道元素自己的边界**为准（不是 Slider 的整体宽度，也不是模板的
    /// 视觉根）。滑块中心只在 [thumbWidth/2, trackWidth - thumbWidth/2] 之间移动 —— 本机实测轨道
    /// 904 px、滑块 18 px → 可行程 9..895，与 Value=0.1637 时实测的滑块中心 154.1 吻合。
    /// </summary>
    private bool TryGetTrackFraction(PointerRoutedEventArgs e, out double fraction)
    {
        fraction = 0;
        var track = _positionTrack;         // 就是本方法所属的那次挂接（HookSliderParts）找到的轨道元素
        if (track is null) return false;
        var thumbWidth = _positionThumb?.ActualWidth ?? 0;
        var travel = track.ActualWidth - thumbWidth;
        if (travel <= 0) return false;
        var x = e.GetCurrentPoint(track).Position.X;
        fraction = Math.Clamp((x - thumbWidth / 2) / travel, 0, 1);
        return true;
    }

    private void Position_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        // 进度条写回 Value 的三条路里，只有"用户改的"才该提交 Seek：
        //   1) 拖动 —— Slider 模板 Thumb 的 DragStarted / DragCompleted（见下面两个 handler）：
        //      拖动期间 Core 的 `IsSeeking` 为真，位置回写被 PlayerViewModel.HandlePositionChanged 抑制，
        //      松手才提交一次；
        //   2) 单击轨道 —— 由 Position_TrackPressed 自己提交，它按住 IsSeeking 走上面同一条抑制路径，
        //      所以到这里会被第一道守卫挡下（2026-10-07 之前这里就是缺陷现场：原生按 StepFrequency=1
        //      把落点量化成端点，然后被这里提交出去）；
        //   3) 30 Hz 位置回写与键盘步进 —— 回写这条路的新值就是 PositionNormalized，用第二道守卫跳过。
        if (_vm.Player.IsSeeking) return;
        if (Math.Abs(e.NewValue - _vm.Player.PositionNormalized) < 1e-9) return;
        if (_vm.Player.Duration <= TimeSpan.Zero) return;
        _vm.Player.SeekCompletedCommand.Execute(e.NewValue);   // 归一化 [0,1] → 服务
    }

    /// <summary>拖动开始：让 VM 进入 IsSeeking，位置回写自此被抑制（与 WPF 的 Thumb.DragStarted 同纪律）。</summary>
    private void Position_DragStarted(object sender, DragStartedEventArgs e)
        => _vm.Player.SeekStartedCommand.Execute(null);

    /// <summary>拖动结束：一次性提交最终位置（与 WPF 的 Thumb.DragCompleted 同纪律，拖动中途不 Seek）。</summary>
    private void Position_DragCompleted(object sender, DragCompletedEventArgs e)
        => _vm.Player.SeekCompletedCommand.Execute(PositionSlider.Value);

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshView();

    /// <summary>
    /// 将播放器状态重发到本壳自有的三个呈现投影（进度条位置不在此列：它绑的是 Core 的
    /// <see cref="PlayerViewModel.PositionNormalized"/>，由 Core 自己发通知）。
    /// </summary>
    private static readonly string[] ViewProjections =
    [
        nameof(PlayPauseGlyph), nameof(NowPlayingText), nameof(TimeText),
    ];

    private void RefreshView()
    {
        foreach (var name in ViewProjections)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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

    // —— x:Bind 只读投影 ——

    /// <summary>
    /// 进度条绑的是 <c>Player.PositionNormalized</c>：Core 的属性，带自己的
    /// <c>[NotifyPropertyChangedFor]</c>（<c>PlayerViewModel.cs:34</c> + <c>:95</c>），
    /// WPF 的 <c>PlayerBar.xaml:44</c> 绑的也是同一个成员。壳侧不再抄一份同公式的影子实现。
    /// </summary>
    public PlayerViewModel Player => _vm.Player;

    public string PlayPauseGlyph => _vm.Player.PlayState == PlayState.Playing ? "\uE769" : "\uE768";
    public string NowPlayingText => _vm.Player.CurrentTrack is { } t
        ? $"{t.Title} — {t.Artist}" : "未在播放";
    public string TimeText => $"{FormatClock(_vm.Player.Position)} / {FormatClock(_vm.Player.Duration)}";

    /// <summary>
    /// mm:ss 时钟文本。TimeSpan 自定义格式里的 ":" 必须写成 "\:"，而 brief 给的
    /// <c>$"{ts:mm\:ss}"</c> 内插写法过不了编译器（反斜杠在普通字符串字面量里是非法转义 → CS1009），
    /// 故改成逐字字符串。
    /// </summary>
    private static string FormatClock(TimeSpan t) => t.ToString(@"mm\:ss");
}
