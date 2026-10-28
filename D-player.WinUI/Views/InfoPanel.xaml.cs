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
/// Task 5 动效：
///   ⑥ 切歌封面交叉淡入 150ms（CoverA/CoverB 轮换）+ 标题/艺术家上移 4px 淡入 100ms
///   ⑨ 占位封面 ↔ 真封面交叉淡入 150ms（与⑥共用 CoverA/CoverB 机制）
///
/// 折叠钮住在栏头（与左栏同形）：收起终值 48px，钮在 12 + 24 + 12 里居中，收起后仍点得到。
/// 宿主 MainWindow 只负责自动收起/恢复；用户手动点钮时本控件发 <see cref="ToggledManually"/>，
/// 宿主据此置"手动优先"。
/// </summary>
public sealed partial class InfoPanel : UserControl
{
    private const string OpacityPropertyPath = "Opacity";
    private const string WidthPropertyPath = "Width";

    // 折叠钮的 Segoe MDL2 码位：展开态 E76C（chevron 向右 = 收起），收起态 E76B（向左 = 展开）。
    // 用 ((char)0x…).ToString() 生成而不是 \u 转义/字面私有区字符：后两种写法在本轮编辑链路上被吞过
    // （MainWindow 旧实现同样注释），码位由编译器保证。
    private static readonly string CollapseGlyph = ((char)0xE76C).ToString();
    private static readonly string ExpandGlyph = ((char)0xE76B).ToString();

    private readonly double _expandedWidth = TokenResource.Double("InfoPanelWidth");
    private readonly double _collapsedWidth = TokenResource.Double("InfoPanelCollapsedWidth");

    private readonly int _coverDecodeSize = (int)TokenResource.Double("InfoPanelCoverSize");

    private Storyboard? _foldStoryboard;
    private Storyboard? _coverStoryboard;
    private Storyboard? _textStoryboard;

    /// <summary>
    /// 切歌序号：CoverArtLoader 是异步的，连点两首播了两张封面时**后到的旧结果必须丢弃**。
    /// </summary>
    private int _coverSequence;

    /// <summary>
    /// ⑥ 当前活跃的封面 Image（A 或 B）。新图加载到非活跃那张上，然后交叉淡入。
    /// </summary>
    private bool _coverAIsActive = true;

    public PlayerViewModel Player { get; }

    public bool IsCollapsed { get; private set; }

    /// <summary>
    /// 用户点了栏头折叠钮（区别于宿主按窗口宽度触发的自动收起）。
    /// 宿主据此置"手动优先"，否则下一次 SizeChanged 会立刻把面板改回去。
    /// </summary>
    public event EventHandler? ToggledManually;

    public InfoPanel(PlayerViewModel player)
    {
        Player = player;
        InitializeComponent();

        ContentHost.Children.Add(new SpectrumView(player)
        {
            Height = 120,
            Margin = new Thickness(0, 8, 0, 0),
        });

        Panel.SizeChanged += (_, _) => Panel.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, Panel.ActualWidth, Panel.ActualHeight),
        };

        player.PropertyChanged += Player_PropertyChanged;
        Loaded += (_, _) => RefreshView();
    }

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (name == nameof(PlayerViewModel.CurrentTrack) || name == nameof(PlayerViewModel.AlbumArtBytes))
        {
            RefreshTexts();
            RefreshCover();
            return;
        }
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

        TitleText.Text = track is null ? "未在播放" : track.Title;
        SetOptionalLine(ArtistText, track?.Artist);
        SetOptionalLine(AlbumText, track?.Album);
        SetOptionalLine(SampleRateTextBlock, Player.SampleRateText);
        RefreshDuration();

        // ⑥ 文本上移动画：TranslateTransform.Y 4→0 + Opacity 0→1（100ms）
        AnimateTextSlideUp();
    }

    /// <summary>
    /// ⑥ 标题/艺术家文本上移淡入：TranslateTransform.Y 从 4→0 + Opacity 0→1，100ms。
    /// 每次切歌时触发，给新文本一个"从下方滑入"的入场感。
    /// </summary>
    private void AnimateTextSlideUp()
    {
        _textStoryboard?.Stop();

        var sb = new Storyboard();

        // Title slide-up
        var titleTransAnim = new DoubleAnimation
        {
            From = 4.0, To = 0.0,
            Duration = MotionTokens.D(MotionTokens.Fast),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(titleTransAnim, TitleTranslate);
        Storyboard.SetTargetProperty(titleTransAnim, "Y");
        sb.Children.Add(titleTransAnim);

        var titleOpacityAnim = new DoubleAnimation
        {
            From = 0.0, To = 1.0,
            Duration = MotionTokens.D(MotionTokens.Fast),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(titleOpacityAnim, TitleText);
        Storyboard.SetTargetProperty(titleOpacityAnim, "Opacity");
        sb.Children.Add(titleOpacityAnim);

        // Artist slide-up
        var artistTransAnim = new DoubleAnimation
        {
            From = 4.0, To = 0.0,
            Duration = MotionTokens.D(MotionTokens.Fast),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(artistTransAnim, ArtistTranslate);
        Storyboard.SetTargetProperty(artistTransAnim, "Y");
        sb.Children.Add(artistTransAnim);

        var artistOpacityAnim = new DoubleAnimation
        {
            From = 0.0, To = 1.0,
            Duration = MotionTokens.D(MotionTokens.Fast),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(artistOpacityAnim, ArtistText);
        Storyboard.SetTargetProperty(artistOpacityAnim, "Opacity");
        sb.Children.Add(artistOpacityAnim);

        _textStoryboard = sb;
        sb.Begin();
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
                source = null;
            }
        }

        if (sequence != _coverSequence) return;

        // ⑥⑨ 交叉淡入：新图加载到非活跃的那张 Image 上，然后交换 Opacity
        CrossFadeCover(source);
    }

    /// <summary>
    /// ⑥⑨ 封面交叉淡入 150ms。将新 Source 赋给非活跃的 CoverA/CoverB，
    /// 然后动画交换两者的 Opacity（新图 0→1，旧图 1→0），避免闪白。
    /// 当 source 为 null 时（无封面/占位），也走交叉淡入——新图设为透明，
    /// 让 CoverPlaceholder 露出来。
    /// </summary>
    private void CrossFadeCover(ImageSource? source)
    {
        var hasSource = source is not null;

        // 更新占位可见性（占位在没有真图时可见）
        CoverPlaceholder.Visibility = hasSource ? Visibility.Collapsed : Visibility.Visible;

        // 将新图加载到非活跃的那张上
        var incomingImage = _coverAIsActive ? CoverB : CoverA;
        var outgoingImage = _coverAIsActive ? CoverA : CoverB;

        // 先设置新图的 Source（此时 Opacity 还是 0，不会闪）
        incomingImage.Source = source;

        // 防叠加：停止上一个封面动画
        _coverStoryboard?.Stop();

        // 交叉淡入：incoming 0→1, outgoing 1→0
        var sb = new Storyboard();

        var fadeIn = new DoubleAnimation
        {
            From = outgoingImage.Opacity, To = 0.0,
            Duration = MotionTokens.D(MotionTokens.Normal),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(fadeIn, outgoingImage);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        sb.Children.Add(fadeIn);

        var fadeOut = new DoubleAnimation
        {
            From = incomingImage.Opacity, To = 1.0,
            Duration = MotionTokens.D(MotionTokens.Normal),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(fadeOut, incomingImage);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        sb.Children.Add(fadeOut);

        sb.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_coverStoryboard, sb)) return;
            // 切换活跃封面
            _coverAIsActive = !_coverAIsActive;
            // 清除旧图的 Source 释放内存
            outgoingImage.Source = null;
        };

        _coverStoryboard = sb;
        sb.Begin();
    }

    private static string FormatClock(TimeSpan t)
        => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    // —— 折叠 ——

    private void FoldButton_Click(object sender, RoutedEventArgs e)
    {
        ToggledManually?.Invoke(this, EventArgs.Empty);
        SetCollapsed(!IsCollapsed, animate: true);
    }

    public void SetCollapsed(bool collapsed, bool animate)
    {
        if (collapsed == IsCollapsed && _foldStoryboard is null) return;

        IsCollapsed = collapsed;
        FoldButtonGlyph.Glyph = collapsed ? ExpandGlyph : CollapseGlyph;

        var currentWidth = Panel.ActualWidth;
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

    private void ApplyFoldEndState(bool collapsed)
    {
        Panel.Width = collapsed ? _collapsedWidth : _expandedWidth;
        ContentHost.Opacity = collapsed ? 0 : 1;
    }
}
