using DPlayer.ViewModels;
using DPlayer.WinUI.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Animation;

namespace DPlayer.WinUI.Views;

/// <summary>
/// 左导航栏：歌单列表。**Task 2 起是声明式的**（spec §3.2 / G6）——
/// <c>ItemsSource</c> 绑 <see cref="PlaylistsViewModel.Playlists"/>、条目内容走 DataTemplate，
/// 不再手工增删条目、不再每次点击整栏重建（Phase 20 遗留的 SyncPlaylistMenu 已删）。
///
/// 折叠（spec §5：200 与 48 之间往返）：宽度用 WinUI 自带 Storyboard（<c>MotionTokens</c> 的 Slow=200ms + 标准缓动），
/// 栏内文字先淡出 Fast=100ms 再收，避免挤压（§6 第 4 项的形状；toolkit 动画归 Task 5，这里不引）。
/// 折叠状态**不持久化**——那要往 settings.json 加字段、动 Core，本阶段明确不做。
/// </summary>
public sealed partial class NavRail : UserControl
{
    /// <summary>折叠时要淡出的文字的哨兵（XAML 里写 Tag="Fade"，见 NavRail.xaml 的注释）。</summary>
    private const string FadeTag = "Fade";

    /// <summary>Storyboard 的目标属性路径是字符串，写错不报错、只会静默不动 —— 集中成常量。</summary>
    private const string OpacityPropertyPath = "Opacity";
    private const string WidthPropertyPath = "Width";

    // 折叠钮的两个 Segoe MDL2 码位：展开态 U+E76B（chevron 向左 = 收起），折叠态 U+E700（汉堡 = 打开）。
    // 文件里存的是**字面的私有区字符**而不是 \u 转义——转义在本轮编辑链路上被吃掉、落盘成空串，
    // 码位已用 od 逐字节复核过。渲染成什么样由真机截图判定（报告 §4）。
    private const string CollapseGlyph = "";
    private const string ExpandGlyph = "";

    /// <summary>端点宽度从 Tokens.xaml 读，别在这儿重复写 200/48：改了令牌若只有静态布局跟着变、动画端点留在旧值，就是两处真相。</summary>
    private readonly double _expandedWidth = TokenResource.Double("NavRailWidth");
    private readonly double _collapsedWidth = TokenResource.Double("NavRailCollapsedWidth");

    /// <summary>进行中的折叠动画。重复点折叠钮时先 Stop 再从头算 —— 以最后一次为准（spec §8 的抖动缓解）。</summary>
    private Storyboard? _foldStoryboard;

    /// <summary>当前是否折叠。宿主只读它，改状态走 <see cref="SetCollapsed"/>（brief 的 Produces 契约）。</summary>
    public bool IsCollapsed { get; private set; }

    /// <summary>导航的数据源：查看项与歌单集合都从这里读（<c>ViewedPlaylist</c> 是唯一真源）。</summary>
    public PlaylistsViewModel Playlists { get; }

    public NavRail(PlaylistsViewModel playlists)
    {
        // 顺序有承重：x:Bind 在 InitializeComponent() 里就把源读一遍（见 PlayerBar.xaml.cs 同纪律）。
        Playlists = playlists;
        InitializeComponent();

        // 歌单是 InitializeAsync 里异步水化的（比 Loaded 晚）。水化那一趟的顺序是「先 Add 条目、
        // 后置 ViewedPlaylist」（PlaylistsViewModel.cs:104 与 :114），所以正常启动路径靠 SelectedItem
        // 的 OneWay 绑定就够；这里补的是另一类：条目集合变了、而 ViewedPlaylist 这个源**没再发通知**
        // （例如删掉的不是当前查看项，或 ViewedPlaylist 被重指成同一个引用）。
        playlists.Playlists.CollectionChanged += (_, _) => SyncSelectionToViewedPlaylist();
    }

    /// <summary>
    /// 选中项回灌自 <see cref="PlaylistsViewModel.ViewedPlaylist"/>，幂等：引用相同就不碰 SelectedItem。
    ///
    /// 刻意**不**回落成第一项（拆分前那条 <c>?? items.FirstOrDefault()</c> 兜底不保留）：那是整栏重建时代的产物，
    /// 声明式之后 ViewedPlaylist 才是权威，替用户"挑第一项"会把 Core 按持久化 CurrentPlaylistId 恢复出来的
    /// 查看项顶掉（Task 1 的承重教训）。ViewedPlaylist 为 null 就让左栏空选，要不要给默认值由 Core 决定
    /// （Hydrate 里已经有「指向不存在的歌单则修正到第一个」，PlaylistsViewModel.cs:108-114）。
    /// </summary>
    private void SyncSelectionToViewedPlaylist()
    {
        var viewed = Playlists.ViewedPlaylist;
        if (viewed is not null && !ReferenceEquals(PlaylistList.SelectedItem, viewed))
            PlaylistList.SelectedItem = viewed;
    }

    /// <summary>
    /// 用户点选/键盘选中一行 -> 回写 ViewedPlaylist（唯一真源，与 WPF 侧栏同纪律）。
    /// 已经是它就什么都不做：Core 的 [ObservableProperty] setter 自身也有相等判断，这里显式写着便于审计。
    ///
    /// 为什么不走 brief Step 1 的 <c>SelectedItem="{x:Bind …, Mode=TwoWay}"</c>：
    /// TwoWay 会在**集合变化把选中项挤出去**时把 null 也写回 ViewedPlaylist。RemovePlaylist 的
    /// 「被删的是当前查看项就切到相邻项」靠的正是当时那个 ViewedPlaylist（PlaylistsViewModel.cs:224-228），
    /// 而 Remove 的通知链里 Selector 清空 SelectedItem 比 Core 那句判断更早 —— 于是 ViewedPlaylist 先被写成
    /// null、Core 的 ReferenceEquals 落空、左栏和列表一起空选。OneWay + 本处理器只在"用户真选了东西"时写，
    /// null 永远不回流。（本壳没有删歌单入口，这是防"下阶段接上 UI 就炸"的地雷，不是当前可复现的缺陷；
    ///  本轮未真机复现，见报告 §7 的 NOT VERIFIED。）
    /// </summary>
    private void PlaylistList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistViewModel pl && !ReferenceEquals(Playlists.ViewedPlaylist, pl))
            Playlists.ViewedPlaylist = pl;
    }

    // —— 折叠 ——

    /// <summary>
    /// 折叠/展开左栏。<paramref name="animate"/> 为假时直接落终值（宿主恢复状态、以及不想要动画的路径用）。
    /// 重复调用以最后一次为准：先读**当前动画中的值**当起点，再撤掉上一个 Storyboard 从头算。
    /// </summary>
    public void SetCollapsed(bool collapsed, bool animate)
    {
        // 已经在目标态且没有进行中的动画 -> 什么都不做（连 Width 赋值都不碰，省一次无谓布局）。
        if (collapsed == IsCollapsed && _foldStoryboard is null) return;

        IsCollapsed = collapsed;
        FoldButtonGlyph.Glyph = collapsed ? ExpandGlyph : CollapseGlyph;

        // 先收集再动画：条目里的名称标签在 DataTemplate 里、每行一份，只能按 Tag="Fade" 从可视化树收。
        var labels = CollectFadeTargets();
        var currentWidth = Rail.ActualWidth;          // 必须在 Stop() 之前读：Stop 把动画值撤成旧本地值
        var currentOpacities = labels.Select(l => l.Opacity).ToList();

        _foldStoryboard?.Stop();
        _foldStoryboard = null;

        if (!animate)
        {
            ApplyFoldEndState(collapsed, labels);
            return;
        }

        var sb = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
        var targetWidth = collapsed ? _collapsedWidth : _expandedWidth;
        // 折叠：文字先淡出 100ms，宽度等它淡完再收 200ms；展开：宽度先撑开 200ms，文字最后淡入 100ms。
        var labelDelay = collapsed ? TimeSpan.Zero : MotionTokens.Slow;
        var widthDelay = collapsed ? MotionTokens.Fast : TimeSpan.Zero;

        for (int i = 0; i < labels.Count; i++)
        {
            var fade = new DoubleAnimation
            {
                From = currentOpacities[i],           // 从当前（可能正在动画中）的值起，中途反转不跳变
                To = collapsed ? 0 : 1,
                Duration = MotionTokens.D(MotionTokens.Fast),
                EasingFunction = MotionTokens.StandardEasing,
                BeginTime = labelDelay,
            };
            // SetTarget 收运行时元素引用：DataTemplate 里的元素没有 namescope 可按名字寻。
            Storyboard.SetTarget(fade, labels[i]);
            Storyboard.SetTargetProperty(fade, OpacityPropertyPath);
            sb.Children.Add(fade);
        }

        var width = new DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = MotionTokens.D(MotionTokens.Slow),
            EasingFunction = MotionTokens.StandardEasing,
            BeginTime = widthDelay,
        };
        Storyboard.SetTarget(width, Rail);
        Storyboard.SetTargetProperty(width, WidthPropertyPath);
        sb.Children.Add(width);

        sb.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_foldStoryboard, sb)) return;   // 被后来的折叠顶掉：别动状态
            _foldStoryboard = null;
            ApplyFoldEndState(collapsed, CollectFadeTargets());
        };

        _foldStoryboard = sb;
        sb.Begin();
    }

    private void FoldButton_Click(object sender, RoutedEventArgs e) => SetCollapsed(!IsCollapsed, animate: true);

    /// <summary>
    /// 把折叠终态写成本地值：HoldEnd 会一直压着 Width/Opacity，不撤动画的话下一次折叠读到的本地起点是旧值、
    /// 而 XAML 侧改宽度也改不动（动画优先级高于本地值）。
    /// 名称标签在折叠态本来也会被 48px 的几何挤成 0 宽（12 + 徽标 24 + 间距 8 + 12 > 48），
    /// 这里显式打回 0/1 是为了让"动画中途被打断"后不留半透明的文字。
    /// </summary>
    private void ApplyFoldEndState(bool collapsed, IReadOnlyList<FrameworkElement> labels)
    {
        Rail.Width = collapsed ? _collapsedWidth : _expandedWidth;
        foreach (var label in labels) label.Opacity = collapsed ? 0 : 1;
    }

    /// <summary>
    /// 要淡出的文字：表头「歌单」+ 每一行的名称标签（都是 Tag="Fade"）。徽标不在其中——
    /// 折叠态只留图标（spec §5）。用可视化树遍历而不是声明式绑定，是因为 WinUI 3 的 RelativeSource
    /// 没有 FindAncestor、ElementName 又出不了 DataTemplate 的 namescope（实测依据见报告 §0.1）。
    /// </summary>
    private IReadOnlyList<FrameworkElement> CollectFadeTargets()
        => VisualTree.FindDescendants(Rail)
            .OfType<FrameworkElement>()
            .Where(e => e.Tag as string == FadeTag)
            .ToList();
}

/// <summary>
/// 徽标 = 歌单名首字（spec §5「图标 + 名称」；brief Step 3 给的「歌单首字或符号」里选了首字，
/// 这样折叠态各歌单仍彼此可辨）。
///
/// 为什么不直接在 XAML 里写 <c>{x:Bind Name.Substring(0, 1)}</c>：空歌单名会让 Substring 越界抛，
/// 而异常发生在**渲染期**（条目 realized 的那一刻），栈里连我们的代码都没有。
/// Core 侧重命名时"名字全空白"会回落成旧值（PlaylistsViewModel.cs:232），所以空串不是不可能状态。
/// 这里用 '?' 兜住。
/// </summary>
internal sealed class PlaylistInitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
        => value is string s && s.Length > 0 ? s[..1] : "?";

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();   // 只读展示：徽标不回写名称
}
