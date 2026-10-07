using System.ComponentModel;
using DPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DPlayer.WinUI.Views;

/// <summary>
/// 左导航栏：歌单列表。本控件从 MainWindow 搬来"整栏重建"那段逻辑（行为等价，未重写）。
///
/// 为什么是重建而不是声明式：Phase 20 的实测形状（见 <see cref="SyncPlaylistMenu"/> 的优先级注释），
/// Task 2 才换成 `ItemsSource` + `SelectedItem` 双向绑 <c>ViewedPlaylist</c>（spec §3.2 / G6）。
/// 拆分前后各 Task 只动一件事，回归可定位。
/// </summary>
public sealed partial class NavRail : UserControl
{
    /// <summary>
    /// 左栏重建重入守卫：本方法里设 Nav.SelectedItem 会同步回调 Nav_SelectionChanged，
    /// 那里回写 ViewedPlaylist 又触发 Playlists_PropertyChanged → 再次进入本方法。
    /// 第二次进来时菜单已经就是我们要的样子，跳过即可（深度锁在 1）。
    /// </summary>
    private bool _syncingMenu;

    /// <summary>导航的数据源：查看项与歌单集合都从这里读（<c>ViewedPlaylist</c> 是唯一真源）。</summary>
    public PlaylistsViewModel Playlists { get; }

    public NavRail(PlaylistsViewModel playlists)
    {
        Playlists = playlists;
        InitializeComponent();

        // 歌单是 InitializeAsync 里异步水化的，比 Loaded 晚 → 集合变化时必须重建左栏
        playlists.Playlists.CollectionChanged += (_, _) => SyncPlaylistMenu();
        playlists.PropertyChanged += Playlists_PropertyChanged;
        Loaded += (_, _) => SyncPlaylistMenu();
    }

    /// <summary>
    /// 左栏是 <see cref="PlaylistsViewModel.ViewedPlaylist"/> 的投影（WPF 侧的 sidebar 就是直接
    /// TwoWay 绑定它），所以选中项的优先级固定是：VM 的 ViewedPlaylist → 重建前已选项 → 第一项。
    /// 把"第一项"放在最前会在首次重建时把 Core 按持久化 CurrentPlaylistId 恢复出来的 ViewedPlaylist 顶掉。
    ///
    /// 条目实例用的是 <see cref="ListViewItem"/> 而不是 ListBoxItem：`ListView.IsItemItsOwnContainerOverride`
    /// 只认 ListViewItem，喂别的类型会被再生成一层 ListViewItem 包住，于是
    /// `ItemContainerStyle`（RailListViewItemStyle）落在外层容器上、手工条目只是它的内容 ——
    /// 2026-10-07 真机量到过这个：那时行高读回 19（MinHeight=36 没生效），六态也就没挂在我以为挂的地方。
    /// 这也和拆分前的写法同形（当年直接 new NavigationViewItem 塞进 MenuItems）。
    /// </summary>
    private void SyncPlaylistMenu()
    {
        if (_syncingMenu) return;      // 见字段注释：设 SelectedItem 会绕回这里
        _syncingMenu = true;
        try
        {
            var previous = (Nav.SelectedItem as ListViewItem)?.Tag as PlaylistViewModel;

            Nav.Items.Clear();
            foreach (var pl in Playlists.Playlists)
                Nav.Items.Add(new ListViewItem
                {
                    Content = pl.Name,
                    Tag = pl,
                    // 行的左右留白挂在条目自己的 Padding 上（模板的 ContentPresenter 用它做
                    // TemplateBinding）：这样左侧 3px 强调条仍贴在行最左，文字不贴边。
                    Padding = new Thickness(12, 0, 12, 0),
                });

            var items = Nav.Items.OfType<ListViewItem>().ToList();
            var target = items.FirstOrDefault(i => ReferenceEquals(i.Tag, Playlists.ViewedPlaylist))
                ?? items.FirstOrDefault(i => ReferenceEquals(i.Tag, previous))
                ?? items.FirstOrDefault();
            if (target is not null) Nav.SelectedItem = target;   // 触发 SelectionChanged → 换列表数据源
        }
        finally { _syncingMenu = false; }
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is ListViewItem { Tag: PlaylistViewModel pl })
        {
            // 已经是 ViewedPlaylist 时不回写：整栏重建也会走到这里，回写等于把导航当成权威。
            if (!ReferenceEquals(Playlists.ViewedPlaylist, pl))
                Playlists.ViewedPlaylist = pl;
            // 拆分前这里还调 ResyncView()（换中区数据源 + 刷底栏投影）。现在只回写上面那行 ViewedPlaylist 就够：
            // 中区订阅 Playlists.PropertyChanged 自刷；底栏的三个投影只依赖 Player，由 player.PropertyChanged
            // 驱动，歌单切换不改变它们的值。
        }
    }

    /// <summary>
    /// ViewedPlaylist 也可能由 Core 侧改（Hydrate 恢复 / Add / Remove / Move / 导入），
    /// 这些改动比 CollectionChanged 晚，必须回灌左栏，否则 pane 停在第一项而列表已是恢复出来的那单。
    /// </summary>
    private void Playlists_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistsViewModel.ViewedPlaylist)) SyncPlaylistMenu();
    }
}
