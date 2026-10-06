using System.Collections.Specialized;
using System.ComponentModel;
using DPlayer.Models;
using DPlayer.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DPlayer.WinUI;

/// <summary>
/// Phase 20 切片主窗口：Fluent 深色 + 自绘标题栏 + 三区布局（左歌单 / 中曲目 / 下播放器栏）。
/// MicaBackdrop 按 brief 挂上，但本机 unpackaged 形态下材质未挂载（实测见 MainWindow.xaml 注释）。
/// 只消费 Core 的公开成员；关闭路径复刻 WPF 壳的 cancel-and-close（见 AppWindow_Closing）。
///
/// 底部栏的四个投影属性（PlayPauseGlyph / NowPlayingText / TimeText / PositionFraction）
/// 走 x:Bind OneWay + 本类自己实现 INPC：播放器每次 PropertyChanged 就重发这四个名字。
/// （不给本类加 INPC 的话 XamlCompiler 会报 WMC1506 "OneWay bindings require at least one of
/// their steps to support raising notifications"，且值不会自动刷新。）
/// </summary>
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _vm;

    /// <summary>抑制"程序改滑块值"反过来触发一次 Seek（刷新投影会写 Slider.Value）。</summary>
    private bool _suppressSeek;

    /// <summary>cancel-and-close 守卫：首次 Closing 拦下、做完异步收尾再放行。</summary>
    private bool _isClosing;

    /// <summary>当前挂了 Queue.CollectionChanged 的歌单，换查看项时解绑重挂。</summary>
    private PlaylistViewModel? _hookedPlaylist;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel ViewModel => _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        Title = "D-player";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // 材质在本机 unpackaged 形态下实测未挂载（见 MainWindow.xaml 顶部注释），仍按 brief 接上。
        SystemBackdrop = new MicaBackdrop();

        // Window 本身没有 Loaded 事件（那是 FrameworkElement 的）——挂根 Grid
        RootGrid.Loaded += (_, _) => SyncPlaylistMenu();
        // 歌单是 InitializeAsync 里异步水化的，比 Loaded 晚 → 集合变化时必须重建左栏
        _vm.Playlists.Playlists.CollectionChanged += (_, _) => SyncPlaylistMenu();
        _vm.Playlists.PropertyChanged += (_, _) => ResyncView();
        _vm.Player.PropertyChanged += Player_PropertyChanged;
        AppWindow.Closing += AppWindow_Closing;
    }

    // —— 左栏：歌单导航 ——

    private void SyncPlaylistMenu()
    {
        var previous = (Nav.SelectedItem as NavigationViewItem)?.Tag as PlaylistViewModel;

        Nav.MenuItems.Clear();
        foreach (var pl in _vm.Playlists.Playlists)
            Nav.MenuItems.Add(new NavigationViewItem { Content = pl.Name, Tag = pl });

        // 尽量保住用户原来选中的那一项（集合变化会整栏重建）
        var target = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, previous))
            ?? Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        if (target is not null) Nav.SelectedItem = target;   // 触发 SelectionChanged → 绑定列表
    }

    private void Nav_SelectionChanged(object sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: PlaylistViewModel pl })
        {
            _vm.Playlists.ViewedPlaylist = pl;
            ResyncView();   // 换列表数据源 + 重挂 Queue 事件 + 刷新空状态
        }
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
        if (TrackList.SelectedItem is not Track track) return;
        var pl = _vm.Playlists.ViewedPlaylist;
        if (pl is null) return;

        var index = IndexOfByReference(pl.Queue, track);
        if (index < 0) return;

        // 与 WPF 的 PlaylistsViewModel.HandleDoubleClickPlay 同语义：先切"正在播放"指针再出声，
        // 否则队列侧的 CurrentPlaylistId / IsActivePlaylist 会与真正在响的歌单脱钩。
        _vm.Playlists.CurrentPlaylistId = pl.Id;
        await pl.PlayIndexAsync(index);
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

    // —— 底部：播放器栏 ——

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Player.CurrentTrack is null)
            _vm.Playlists.ViewedPlaylist?.PlayCurrentCommand.Execute(null);
        else
            _vm.Player.PlayPauseCommand.Execute(null);
    }

    private void Position_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSeek || _vm.Player.Duration <= TimeSpan.Zero) return;
        // PlayerViewModel 的 Seek 方法本体是 private（只通过生成的命令暴露），故走命令。
        // 切片期已知差异：WPF 只在拖动结束时 Seek，这里每次 ValueChanged 都完成一次（拖拽中会连续定位）。
        _vm.Player.SeekStartedCommand.Execute(null);
        _vm.Player.SeekCompletedCommand.Execute(e.NewValue);   // 归一化 [0,1] → 服务
    }

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshView();

    /// <summary>将播放器状态重发到四个 x:Bind 投影；期间抑制滑块回写的 Seek。</summary>
    private static readonly string[] ViewProjections =
    [
        nameof(PlayPauseGlyph), nameof(NowPlayingText), nameof(TimeText), nameof(PositionFraction),
    ];

    private void RefreshView()
    {
        _suppressSeek = true;
        try
        {
            foreach (var name in ViewProjections)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        finally { _suppressSeek = false; }
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

    public string PlayPauseGlyph => _vm.Player.PlayState == PlayState.Playing ? "\uE769" : "\uE768";
    public string NowPlayingText => _vm.Player.CurrentTrack is { } t
        ? $"{t.Title} — {t.Artist}" : "未在播放";
    public string TimeText => $"{FormatClock(_vm.Player.Position)} / {FormatClock(_vm.Player.Duration)}";
    public double PositionFraction => _vm.Player.Duration.TotalSeconds <= 0 ? 0
        : _vm.Player.Position.TotalSeconds / _vm.Player.Duration.TotalSeconds;

    /// <summary>
    /// mm:ss 时钟文本。TimeSpan 自定义格式里的 ":" 必须写成 "\:"，而 brief 给的
    /// <c>$"{ts:mm\:ss}"</c> 内插写法过不了编译器（反斜杠在普通字符串字面量里是非法转义 → CS1009），
    /// 故改成逐字字符串。
    /// </summary>
    private static string FormatClock(TimeSpan t) => t.ToString(@"mm\:ss");
}
