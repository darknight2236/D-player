using System.Collections.Specialized;
using System.ComponentModel;
using DPlayer.Models;
using DPlayer.ViewModels;
using DPlayer.WinUI.Services;
using DPlayer.WinUI.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

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

    /// <summary>⑨ 空态是否已经动画过（首次可见时才播放淡入+上移）。</summary>
    private bool _emptyAnimated;

    /// <summary>
    /// 导入进行中（Phase 22 Task 3）。三个导入入口共用：期间转 16px 忙碌环、禁掉工具栏五颗钮，
    /// 并拦住重入（RunImportAsync 的第一道 if）。
    /// </summary>
    private bool _isImporting;

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
        UpdateToolbarState();
    }

    private void OnViewedQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyHint();
        UpdateToolbarState();
    }

    private void UpdateEmptyHint()
    {
        var pl = Playlists.ViewedPlaylist;
        var isEmpty = pl is null || pl.Queue.Count == 0;
        var wasVisible = EmptyHint.Visibility == Visibility.Visible;
        EmptyHint.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

        // ⑨ 首次从 Collapsed→Visible 时播放淡入+上移动画（150ms + 8px）
        if (isEmpty && !wasVisible && !_emptyAnimated)
        {
            _emptyAnimated = true;
            AnimateEmptyStateIn();
        }
        // 如果有曲目了，重置动画标记，下次变空时再播一次
        if (!isEmpty) _emptyAnimated = false;
    }

    /// <summary>
    /// ⑨ 空态淡入+上移：Opacity 0→1（150ms）+ TranslateTransform.Y 8→0（150ms）。
    /// 仅首次显示时播放，避免每次切到空歌单都重复。
    /// </summary>
    private void AnimateEmptyStateIn()
    {
        var sb = new Storyboard();

        var opacityAnim = new DoubleAnimation
        {
            From = 0.0, To = 1.0,
            Duration = MotionTokens.D(MotionTokens.Normal),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(opacityAnim, EmptyHint);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");
        sb.Children.Add(opacityAnim);

        var translateAnim = new DoubleAnimation
        {
            From = 8.0, To = 0.0,
            Duration = MotionTokens.D(MotionTokens.Normal),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(translateAnim, EmptyHintTranslate);
        Storyboard.SetTargetProperty(translateAnim, "Y");
        sb.Children.Add(translateAnim);

        sb.Begin();
    }

    // —— Phase 22 Task 3：导入/导出工具栏 ——

    /// <summary>
    /// 工具栏五颗钮的启用规则（唯一真源）：
    ///   · 左组（添加文件 / 导入文件夹 / 导入播放列表）—— 需要有查看中的歌单且不在导入中；
    ///   · 右组（导出 / 清空）—— 还要求队列非空。
    /// 三个调用点：ResyncView（换歌单/初始化）、OnViewedQueueChanged（队列增删）、
    /// RunImportAsync 的进出两处（忙碌态）。只靠 PropertyChanged 不够 —— 队列变化不会让
    /// PlaylistsViewModel 发 PropertyChanged，所以 OnViewedQueueChanged 必须自己调一次。
    /// </summary>
    private void UpdateToolbarState()
    {
        var pl = Playlists.ViewedPlaylist;
        var hasView = pl is not null;
        var hasTracks = pl is { Queue.Count: > 0 };

        AddFilesButton.IsEnabled = ImportFolderButton.IsEnabled = ImportListButton.IsEnabled
            = hasView && !_isImporting;
        ExportButton.IsEnabled = ClearButton.IsEnabled = hasTracks && !_isImporting;
    }

    /// <summary>
    /// 三个导入入口的共同外壳：重入拦截、忙碌环、整组禁用、异常转错误对话框，finally 里复位。
    /// 注意禁用发生在 await 之前 —— 对话框弹出的整个期间工具栏都是禁的，用户不能叠加第二次导入。
    /// </summary>
    private async Task RunImportAsync(Func<Task> action)
    {
        if (_isImporting) return;
        _isImporting = true;
        ImportProgress.IsActive = true;
        UpdateToolbarState();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await WinUiDialogService.ShowErrorAsync(this, "导入失败", ex.Message);
        }
        finally
        {
            _isImporting = false;
            ImportProgress.IsActive = false;
            UpdateToolbarState();
        }
    }

    /// <summary>添加文件（多选）→ 追加到当前歌单。取消 = 队列不变。</summary>
    private async void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (Playlists.ViewedPlaylist is not { } pl) return;
        await RunImportAsync(() => pl.AddToQueueCommand.ExecuteAsync(null));
    }

    /// <summary>导入文件夹（递归扫描）→ 追加到当前歌单。取消 = 无操作。</summary>
    private async void ImportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Playlists.ViewedPlaylist is not { } pl) return;
        await RunImportAsync(() => pl.ImportFolderToCurrentCommand.ExecuteAsync(null));
    }

    /// <summary>
    /// 导入播放列表文件 → 追加。VM 返回 null 表示用户取消（不弹任何框）；
    /// 返回报告则用 Core 的 PlaylistImportReportFormatter 拼文案（与 WPF 壳同一套规则）。
    /// </summary>
    private async void ImportListButton_Click(object sender, RoutedEventArgs e)
    {
        if (Playlists.ViewedPlaylist is not { } pl) return;
        await RunImportAsync(async () =>
        {
            var report = await pl.ImportPlaylistFileAsync();
            if (report is not null)
                await WinUiDialogService.ShowInfoAsync(this, PlaylistImportReportFormatter.DialogTitle,
                    PlaylistImportReportFormatter.Format(new[] { report }));
        });
    }

    /// <summary>
    /// 导出为 M3U8。不走 RunImportAsync（导出不该转"导入中"的忙碌环），
    /// 自己兜异常：VM 用返回字符串表达可展示的错误，异常则取 Message。
    /// </summary>
    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (Playlists.ViewedPlaylist is not { } pl || pl.Queue.Count == 0) return;
        try
        {
            var error = await pl.ExportPlaylistFileAsync();
            if (error is not null)
                await WinUiDialogService.ShowErrorAsync(this, "导出失败", error);
        }
        catch (Exception ex)
        {
            await WinUiDialogService.ShowErrorAsync(this, "导出失败", ex.Message);
        }
    }

    /// <summary>清空当前歌单：先确认（默认焦点在「取消」），确认后走 VM 的 ClearQueueCommand。</summary>
    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (Playlists.ViewedPlaylist is not { } pl || pl.Queue.Count == 0) return;
        if (!await WinUiDialogService.ShowConfirmAsync(this, "清空歌单",
                $"确定清空歌单「{pl.Name}」吗？此操作不可撤销。", "清空")) return;
        pl.ClearQueueCommand.Execute(null);
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
    /// C5：为每行设置 UIA Name，使屏幕阅读器读出"曲名，艺术家，时长"。
    /// 走 ContainerContentChanging 而不是 x:Bind 静态函数绑定：WinUI 3 的 XamlCompiler
    /// 不支持 {x:Bind local:TrackRowLabels.Build(...)} 这种静态方法调用（报 WMC 错误），
    /// 实测不可用。ContainerContentChanging 在每次容器回收/重用时都触发，保证 UIA Name
    /// 与当前数据同步。
    /// </summary>
    private void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is Track t)
            AutomationProperties.SetName(args.ItemContainer, TrackRowLabels.Build(t.Title, t.Artist, t.Duration));
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

/// <summary>
/// <c>int?</c> → 显示文本：null 时回落成 "-"（表头序号列）。
/// x:Bind 的 FallbackValue 只覆盖绑定路径抛异常的情形，不覆盖值为 null；
/// 所以这里用 Converter 显式判空。
/// </summary>
internal sealed class NullableIntToDashConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
        => value is int n ? n.ToString() : "-";

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// <see cref="TimeSpan"/> → "mm:ss" 文本（等宽数字列）。
/// 与 PlayerBar.FormatClock 同格式（<c>mm\:ss</c>），但走 IValueConverter 以便 XAML 侧 {x:Bind}。
/// </summary>
internal sealed class TimeSpanToMmSsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
        => value is TimeSpan ts ? ts.ToString(@"mm\:ss") : "-";

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}
