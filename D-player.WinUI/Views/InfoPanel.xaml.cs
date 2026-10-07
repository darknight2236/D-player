using System.ComponentModel;
using DPlayer.ViewModels;
using DPlayer.WinUI.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace DPlayer.WinUI.Views;

/// <summary>
/// 右信息栏：当前曲目的封面 + 标题/艺术家/专辑/时长，无曲目时回落成「未在播放」+ 占位封面（spec §5）。
///
/// 内容走**代码后置赋值**而不是 <c>x:Bind</c>：<c>Player.CurrentTrack.Title</c> 是跨层链，
/// XamlCompiler 在 MarkupCompilePass1 会抛 WMC9999（TrackList.xaml 里有同一条实测记录），
/// 所以这里订阅 <see cref="PlayerViewModel.PropertyChanged"/> 自己刷（与 Task 1 的四个控件同纪律）。
///
/// 折叠：<see cref="IsCollapsed"/> 让宽度在 260 与 0 之间走 Storyboard（与 NavRail 同构，
/// 端点从 Tokens 读）；自动收起的阈值与"手动优先"在宿主 MainWindow。
/// </summary>
public sealed partial class InfoPanel : UserControl
{
    private const string OpacityPropertyPath = "Opacity";
    private const string WidthPropertyPath = "Width";

    private readonly double _expandedWidth = TokenResource.Double("InfoPanelWidth");
    private readonly double _collapsedWidth = 0;      // brief Step 3：右栏 260 &lt;-&gt; 0（不是留一条 48 的窄条）

    /// <summary>封面解码边长（与 XAML 里那个正方形同源）。</summary>
    private readonly int _coverDecodeSize = (int)TokenResource.Double("InfoPanelCoverSize");

    private Storyboard? _foldStoryboard;

    /// <summary>
    /// 切歌序号：CoverArtLoader 是异步的，连点两首播了两张封面时**后到的旧结果必须丢弃**，
    /// 否则慢的那张会盖掉快的（交叉淡入归 Task 5，但这里的乱序是 Task 2 引入的，就地兜住）。
    /// </summary>
    private int _coverSequence;

    /// <summary>信息面板的数据源（spec §3.1：每个控件持有它那一块的绑定源）。</summary>
    public PlayerViewModel Player { get; }

    /// <summary>当前是否折叠。宿主只读它，改状态走 <see cref="SetCollapsed"/>。</summary>
    public bool IsCollapsed { get; private set; }

    public InfoPanel(PlayerViewModel player)
    {
        // 顺序有承重：与 PlayerBar/NavRail 同一条 x:Bind 纪律（虽然本控件的内容是代码赋值，
        // XAML 里的 {StaticResource} 令牌也要求控件已构造完）。
        Player = player;
        InitializeComponent();

        // Width=0 时 WinUI 不裁剪子元素（FrameworkElement 没有 WPF 的 IsClippedToBounds），
        // 228px 的封面会照画到中列上方 —— 那正是 A3 的"折叠后仍有残留"。Clip 跟着尺寸走就行，
        // 它不参与布局，不会自激。
        Panel.SizeChanged += (_, _) => Panel.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, Panel.ActualWidth, Panel.ActualHeight),
        };

        player.PropertyChanged += Player_PropertyChanged;
        Loaded += (_, _) => RefreshView();
    }

    /// <summary>
    /// 播放器心跳（Position 每 33ms 一次）也会走到这里，所以 <see cref="RefreshView"/> 里只碰
    /// 变了的东西：文本按值比对、封面只在 CurrentTrack/AlbumArtBytes 变化时重解码。
    /// </summary>
    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (name == nameof(PlayerViewModel.CurrentTrack) || name == nameof(PlayerViewModel.AlbumArtBytes))
        {
            RefreshTexts();
            RefreshCover();
            return;
        }
        // Duration 是 Core 的 [ObservableProperty]，切歌时随曲目一起变；时长文本跟着它。
        if (name == nameof(PlayerViewModel.Duration)) RefreshDuration();
    }

    private void RefreshView()
    {
        RefreshTexts();
        RefreshCover();
    }

    private void RefreshTexts()
    {
        var track = Player.CurrentTrack;

        // 空态（spec §5）：整栏「未在播放」+ 占位封面；艺术家/专辑/时长三行直接不占位，
        // 免得 StackPanel 的 Spacing 在空串上也留出三道空隙。
        TitleText.Text = track is null ? "未在播放" : track.Title;
        SetOptionalLine(ArtistText, track?.Artist);
        SetOptionalLine(AlbumText, track?.Album);
        RefreshDuration();
    }

    private static void SetOptionalLine(TextBlock line, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            line.Visibility = Visibility.Collapsed;
            return;
        }
        line.Visibility = Visibility.Visible;
        if (line.Text != text) line.Text = text;
    }

    private void RefreshDuration()
    {
        var duration = Player.Duration;
        DurationText.Visibility = Player.CurrentTrack is null ? Visibility.Collapsed : Visibility.Visible;
        var text = FormatClock(duration);
        if (DurationText.Text != text) DurationText.Text = text;
    }

    /// <summary>
    /// mm:ss，满一小时才带小时位。与 PlayerBar.xaml.cs 的 FormatClock 是同一类一行规则，
    /// 那条只出 mm:ss（底栏时间窗就那么大）；两处各一行、不为了 DRY 造第三个文件，
    /// 真要在 Task 3/5 统一时也应统一进 Core 侧的呈现层而不是壳里互相抄。
    /// TimeSpan 自定义格式里的 ":" 必须写成 "\:"，且逐字字符串才写得出来（Task 1 实测：
    /// 内插写法 <c>$"{ts:mm\:ss}"</c> 会撞 CS1009）。
    /// </summary>
    private static string FormatClock(TimeSpan t)
        => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    private async void RefreshCover()
    {
        var sequence = ++_coverSequence;
        var bytes = Player.AlbumArtBytes;
        ImageSource? source = null;

        if (bytes is { Length: > 0 })
        {
            try
            {
                source = await CoverArtLoader.LoadAsync(bytes, _coverDecodeSize);
            }
            catch (Exception)
            {
                // 坏图（截断的 JPEG、非图像字节）不该把壳带崩：SetSourceAsync 对这些抛的是
                // 普通异常，栈里也没有我们的判断可依。退化方向 = 占位封面，等价于"无封面"。
                source = null;
            }
        }

        if (sequence != _coverSequence) return;      // 已被更新的切歌取代：丢掉迟到的结果

        Cover.Source = source;
        CoverPlaceholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // —— 折叠 ——

    /// <summary>
    /// 折叠/展开右栏。与 <see cref="NavRail.SetCollapsed"/> 同构（同一条 100ms 淡出 + 200ms 收宽的形状），
    /// 区别只在端点：这里收到 0，且要淡出的是整块内容而不是逐行的名称标签。
    /// 重复调用以最后一次为准（先读当前动画中的值当起点，再撤上一个 Storyboard）。
    /// </summary>
    public void SetCollapsed(bool collapsed, bool animate)
    {
        if (collapsed == IsCollapsed && _foldStoryboard is null) return;

        IsCollapsed = collapsed;

        var currentWidth = Panel.ActualWidth;         // 必须在 Stop() 之前读
        var currentOpacity = ContentHost.Opacity;

        _foldStoryboard?.Stop();
        _foldStoryboard = null;

        if (!animate)
        {
            ApplyFoldEndState(collapsed);
            return;
        }

        var sb = new Storyboard { FillBehavior = FillBehavior.HoldEnd };

        var fade = new DoubleAnimation
        {
            From = currentOpacity,
            To = collapsed ? 0 : 1,
            Duration = MotionTokens.D(MotionTokens.Fast),
            EasingFunction = MotionTokens.StandardEasing,
            BeginTime = collapsed ? TimeSpan.Zero : MotionTokens.Slow,
        };
        Storyboard.SetTarget(fade, ContentHost);
        Storyboard.SetTargetProperty(fade, OpacityPropertyPath);
        sb.Children.Add(fade);

        var width = new DoubleAnimation
        {
            From = currentWidth,
            To = collapsed ? _collapsedWidth : _expandedWidth,
            Duration = MotionTokens.D(MotionTokens.Slow),
            EasingFunction = MotionTokens.StandardEasing,
            BeginTime = collapsed ? MotionTokens.Fast : TimeSpan.Zero,
        };
        Storyboard.SetTarget(width, Panel);
        Storyboard.SetTargetProperty(width, WidthPropertyPath);
        sb.Children.Add(width);

        sb.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_foldStoryboard, sb)) return;
            _foldStoryboard = null;
            ApplyFoldEndState(collapsed);
        };

        _foldStoryboard = sb;
        sb.Begin();
    }

    /// <summary>把折叠终态写成本地值（HoldEnd 会一直压着 Width/Opacity，不落地就成两处真相）。</summary>
    private void ApplyFoldEndState(bool collapsed)
    {
        Panel.Width = collapsed ? _collapsedWidth : _expandedWidth;
        ContentHost.Opacity = collapsed ? 0 : 1;
    }
}
