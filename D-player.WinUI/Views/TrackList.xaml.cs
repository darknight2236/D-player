using System.Collections.Specialized;
using System.ComponentModel;
using DPlayer.Models;
using DPlayer.ViewModels;
using DPlayer.WinUI.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DPlayer.WinUI.Views;

/// <summary>
/// 中区曲目列表：数据源、空状态、双击播放。全部从 MainWindow 搬来，逻辑未改写。
///
/// 数据源走代码后置赋值并自己订阅 <c>ViewedPlaylist</c> 的变化（拆分前是 MainWindow 的
/// ResyncView 顺带做的，现在由本控件负责）——跨层 x:Bind 链会让 XamlCompiler 抛 WMC9999，
/// 见 TrackList.xaml 里的注释。
/// </summary>
public sealed partial class TrackList : UserControl
{
    /// <summary>当前挂了 Queue.CollectionChanged 的歌单，换查看项时解绑重挂。</summary>
    private PlaylistViewModel? _hookedPlaylist;

    /// <summary>列表要跟着看的两个量：查看项（ViewedPlaylist）与它自己的 Queue。</summary>
    public PlaylistsViewModel Playlists { get; }

    public TrackList(PlaylistsViewModel playlists)
    {
        Playlists = playlists;
        InitializeComponent();

        // 拆分前 MainWindow 的 Playlists_PropertyChanged 里是无条件调 ResyncView 的，这里同口径。
        playlists.PropertyChanged += Playlists_PropertyChanged;
        Loaded += (_, _) => ResyncView();
    }

    private void Playlists_PropertyChanged(object? sender, PropertyChangedEventArgs e) => ResyncView();

    private void ResyncView()
    {
        var pl = Playlists.ViewedPlaylist;
        if (!ReferenceEquals(_hookedPlaylist, pl))
        {
            if (_hookedPlaylist is not null) _hookedPlaylist.Queue.CollectionChanged -= OnViewedQueueChanged;
            _hookedPlaylist = pl;
            if (pl is not null) pl.Queue.CollectionChanged += OnViewedQueueChanged;
        }

        // 换查看项时才换列表数据源；同一集合实例重复赋值会被 ItemsControl 自己吃掉，
        // 但显式判等可以避免每 33ms 的播放器心跳都去碰一次 ItemsSource。
        var queue = pl?.Queue;
        if (!ReferenceEquals(TrackItems.ItemsSource, queue)) TrackItems.ItemsSource = queue;

        UpdateEmptyHint();
    }

    private void OnViewedQueueChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyHint();

    private void UpdateEmptyHint()
    {
        var pl = Playlists.ViewedPlaylist;
        EmptyHint.Visibility = pl is null || pl.Queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

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
        // 见 Theme/VisualTree.cs）。
        if (e.OriginalSource is not FrameworkElement source) return;
        if (VisualTree.FindAncestor<ListViewItem>(source) is not { } container) return;
        if (container.Content is not Track track) return;

        var pl = Playlists.ViewedPlaylist;
        if (pl is null) return;

        var index = IndexOfByReference(pl.Queue, track);
        if (index < 0) return;

        // 与 WPF 壳共用同一条入口（Views/Controls/PlaylistView.xaml.cs → PlaylistsViewModel.HandleDoubleClickPlay）：
        // 它内部先切 CurrentPlaylistId，再走 PlayTrackAtCommand —— 而 PlayTrackAt 的第一步是
        // _shuffleHistory.Clear()（"视为新会话"）。两壳的双击因此语义一致；自己拼一半必然漂移。
        await Playlists.HandleDoubleClickPlay(pl, index);
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
}
