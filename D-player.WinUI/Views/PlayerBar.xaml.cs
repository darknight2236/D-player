using System.ComponentModel;
using DPlayer.Models;
using DPlayer.ViewModels;
using DPlayer.WinUI.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DPlayer.WinUI.Views;

/// <summary>
/// 底部播放器栏：▶/⏸ + 进度 + 时间。整块从 MainWindow 搬来，三条输入路径与投影逻辑未改写。
///
/// 本控件自己实现 INPC 并重发三个呈现投影（PlayPauseGlyph / NowPlayingText / TimeText）：
/// 播放器每次 PropertyChanged 就重发这三个名字。进度条不在此列——它直接绑
/// Core 的 <c>PlayerViewModel.PositionNormalized</c>（WPF 的 PlayerBar.xaml 绑的也是它，
/// 壳侧再写一份同公式就是影子实现）。
/// （不给本类加 INPC 的话 XamlCompiler 会报 WMC1506 "OneWay bindings require at least one of
/// their steps to support raising notifications"，且值不会自动刷新。）
/// </summary>
public sealed partial class PlayerBar : UserControl, INotifyPropertyChanged
{
    /// <summary>Slider 模板 Thumb 只挂一次（<c>Loaded</c> 可能重复触发）。</summary>
    private bool _dragHooked;

    /// <summary>轨道单击的挂接同样只做一次。</summary>
    private bool _railHooked;

    /// <summary>挂好接的拖动滑块：单击定位要按它的宽度算出滑块中心的可行程（见 TryGetTrackFraction）。</summary>
    private Thumb? _positionThumb;

    /// <summary>模板里的轨道元素（HorizontalTemplate）：单击定位以它自己的边界为准，而不是 Slider 的整体宽度。</summary>
    private FrameworkElement? _positionTrack;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// x:Bind 的源：本控件自己的播放状态。
    /// 进度条绑的是 <c>Player.PositionNormalized</c>：Core 的属性，带自己的
    /// <c>[NotifyPropertyChangedFor]</c>（<c>PlayerViewModel.cs:34</c> + <c>:95</c>），
    /// WPF 的 <c>PlayerBar.xaml:44</c> 绑的也是同一个成员。壳侧不再抄一份同公式的影子实现。
    /// </summary>
    public PlayerViewModel Player { get; }

    /// <summary>
    /// ▶/⏸ 的"当前没有曲目"那条分支走的是歌单（<c>ViewedPlaylist.PlayCurrentCommand</c>），
    /// 不是播放器，所以本控件需要第二个数据源。构造签名按 spec/计划的契约保持只收
    /// <see cref="PlayerViewModel"/>，这条用 required 属性由宿主（MainWindow）在装配时补上：
    /// 忘了写 <c>{ Playlists = … }</c> 编译不过，不会退化成"按钮点了没反应"。
    /// </summary>
    public required PlaylistsViewModel Playlists { get; set; }

    public PlayerBar(PlayerViewModel player)
    {
        // 顺序有承重：x:Bind 在 InitializeComponent() 里就把源读一遍（PositionNormalized / 三个投影），
        // Player 必须在它之前赋值。
        Player = player;
        InitializeComponent();

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
        // 拆分前这段挂在 RootGrid.Loaded 上（Window 本身没有 Loaded 事件，那是 FrameworkElement 的）；
        // 本控件是 FrameworkElement，直接挂自己的 Loaded。
        Loaded += (_, _) => HookSliderParts();

        player.PropertyChanged += Player_PropertyChanged;
        // B3：播放钮的 IsEnabled 取决于是否有可播内容（CurrentTrack 或歌单有曲目）。
        // Playlists.ViewedPlaylist 变化时也要刷新，所以挂一条 PropertyChanged。
        // Playlists 是 required 属性，构造时由宿主赋值，不会为 null（CS8602 是分析器的保守警告）。
        Playlists!.PropertyChanged += (_, _) => RefreshView();
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
            ?? VisualTree.FindDescendant<Thumb>(PositionSlider);
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
        if (VisualTree.FindAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null) return;
        if (!TryGetTrackFraction(e, out var fraction)) return;   // 几何读不到就别动，让原生那半（已量化到千分之一程）接手
        e.Handled = true;                 // 必须在提交前吃掉：否则 Slider 的类处理器会把 Value 改到端点
        var player = Player;
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
        if (Player.IsSeeking) return;
        if (Math.Abs(e.NewValue - Player.PositionNormalized) < 1e-9) return;
        if (Player.Duration <= TimeSpan.Zero) return;
        Player.SeekCompletedCommand.Execute(e.NewValue);   // 归一化 [0,1] → 服务
    }

    /// <summary>拖动开始：让 VM 进入 IsSeeking，位置回写自此被抑制（与 WPF 的 Thumb.DragStarted 同纪律）。</summary>
    private void Position_DragStarted(object sender, DragStartedEventArgs e)
        => Player.SeekStartedCommand.Execute(null);

    /// <summary>拖动结束：一次性提交最终位置（与 WPF 的 Thumb.DragCompleted 同纪律，拖动中途不 Seek）。</summary>
    private void Position_DragCompleted(object sender, DragCompletedEventArgs e)
        => Player.SeekCompletedCommand.Execute(PositionSlider.Value);

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (Player.CurrentTrack is null)
            Playlists.ViewedPlaylist?.PlayCurrentCommand.Execute(null);
        else
            Player.PlayPauseCommand.Execute(null);
    }

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshView();

    /// <summary>
    /// 将播放器状态重发到本壳自有的三个呈现投影（进度条位置不在此列：它绑的是 Core 的
    /// <see cref="PlayerViewModel.PositionNormalized"/>，由 Core 自己发通知）。
    /// 同时刷新 B3 的两个禁用态投影（<see cref="HasPlayableSource"/> / <see cref="HasDuration"/>）。
    /// </summary>
    private static readonly string[] ViewProjections =
    [
        nameof(PlayPauseGlyph), nameof(NowPlayingText), nameof(TimeText),
        nameof(HasPlayableSource), nameof(HasDuration),
    ];

    private void RefreshView()
    {
        foreach (var name in ViewProjections)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // —— x:Bind 只读投影 ——

    public string PlayPauseGlyph => Player.PlayState == PlayState.Playing ? "\uE769" : "\uE768";
    public string NowPlayingText => Player.CurrentTrack is { } t
        ? $"{t.Title} — {t.Artist}" : "未在播放";
    public string TimeText => $"{FormatClock(Player.Position)} / {FormatClock(Player.Duration)}";

    /// <summary>
    /// B3：播放钮是否有可播内容。当前有曲目 或 查看歌单有曲目 → 启用；否则禁用（灰化，Opacity 0.4）。
    /// </summary>
    public bool HasPlayableSource => Player.CurrentTrack is not null
        || (Playlists.ViewedPlaylist?.Queue.Count ?? 0) > 0;

    /// <summary>
    /// B3：进度条是否可用。Duration > 0 表示有载入曲目；空队列时禁用。
    /// </summary>
    public bool HasDuration => Player.Duration > TimeSpan.Zero;

    /// <summary>
    /// mm:ss 时钟文本。TimeSpan 自定义格式里的 ":" 必须写成 "\:"，而 brief 给的
    /// <c>$"{ts:mm\:ss}"</c> 内插写法过不了编译器（反斜杠在普通字符串字面量里是非法转义 → CS1009），
    /// 故改成逐字字符串。
    /// </summary>
    private static string FormatClock(TimeSpan t) => t.ToString(@"mm\:ss");
}
