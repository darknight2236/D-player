using System.ComponentModel;
using DPlayer.Models;
using DPlayer.ViewModels;
using DPlayer.WinUI.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DPlayer.WinUI.Views;

/// <summary>
/// 底部播放器栏：▶/⏸ + 进度 + 时间。整块从 MainWindow 搬来，三条输入路径与投影逻辑未改写。
///
/// Task 5 动效：
///   ③ 按钮按下缩放 1→0.96（100ms）、抬起回弹（150ms）、悬停 1.04 —— ScaleTransform + Storyboard
///   ⑦ 播放/暂停图标交叉淡入 100ms —— PlayIcon/PauseIcon 两个 FontIcon 叠放，Opacity 互反
///   ⑧ 进度拇指拖动 ×1.15（100ms）、松手回 1.0（100ms）—— Thumb ScaleTransform + Storyboard
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

    // ③ 按钮缩放动画
    private Storyboard? _pressStoryboard;

    // ⑧ 进度拇指缩放动画
    private Storyboard? _thumbScaleStoryboard;
    private ScaleTransform? _thumbScale;

    // ⑦ 播放/暂停图标交叉淡入
    private Storyboard? _iconStoryboard;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerViewModel Player { get; }

    public PlaylistsViewModel Playlists { get; }

    /// <summary>
    /// Playlists 走构造参数，**不**走 <c>required</c> + 对象初始化器：初始化器在 ctor 体返回**之后**才执行，
    /// 而本 ctor 体里的订阅（下方最后一行）与 <c>InitializeComponent()</c> 里的 x:Bind 都要求它此刻已就位 ——
    /// 初始化器时代这里先抛 NullReferenceException，整窗起不来（Phase 21 收尾的起窗崩溃根因）。
    /// </summary>
    public PlayerBar(PlayerViewModel player, PlaylistsViewModel playlists)
    {
        // 顺序有承重：x:Bind（Player.PositionNormalized 等）在 InitializeComponent() 里就求值一遍，
        // 两个绑定源必须先赋值再 InitializeComponent() —— 与 NavRail.xaml.cs:49 同纪律。
        Player = player;
        Playlists = playlists;
        InitializeComponent();
        Loaded += (_, _) => HookSliderParts();
        player.PropertyChanged += Player_PropertyChanged;
        Playlists.PropertyChanged += (_, _) => RefreshView();
    }

    /// <summary>
    /// 给 Slider 的模板部件挂上进度条的两条用户输入路径：Thumb 的 DragStarted/DragCompleted（拖动）
    /// 与轨道元素的 PointerPressed（单击定位）。找不到部件时**静默跳过**。
    /// </summary>
    private void HookSliderParts()
    {
        if (_dragHooked && _railHooked) return;
        PositionSlider.ApplyTemplate();

        var thumb = _positionThumb
            ?? PositionSlider.FindName("HorizontalThumb") as Thumb
            ?? VisualTree.FindDescendant<Thumb>(PositionSlider);
        _positionThumb = thumb;

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
        if (thumb is null) return;
        thumb.DragStarted += Position_DragStarted;
        thumb.DragCompleted += Position_DragCompleted;
        _dragHooked = true;

        // ⑧ 给 Thumb 挂 ScaleTransform 用于拖动缩放动画
        HookThumbScale(thumb);
    }

    /// <summary>
    /// ⑧ 给进度条 Thumb 挂 ScaleTransform + 缩放动画。
    /// DragStarted → Scale ×1.15（100ms）；DragCompleted → Scale ×1.0（100ms）。
    /// CenterX/CenterY 延迟到 DragStarted 时计算（此时 Thumb 必定已布局完成）。
    /// </summary>
    private void HookThumbScale(Thumb thumb)
    {
        // Thumb 模板里已有一个 ScaleTransform（用于内部渲染），我们不能覆盖它。
        // 改为在 Thumb 的 RenderTransform 上包一层 CompositeTransform，或在找不到合适位置时静默跳过。
        // 实际上 Thumb 的 RenderTransform 默认是 null，我们可以直接设置。
        // CenterX/CenterY 不在这里计算——首次 Loaded 时 ActualWidth 可能仍为 0，
        // 推迟到 Position_DragStarted（用户拖动时 Thumb 必定已布局）。
        var scale = new ScaleTransform();
        thumb.RenderTransform = scale;
        _thumbScale = scale;
    }

    private void AnimateThumbScale(double targetScale)
    {
        if (_thumbScale is null) return;
        _thumbScaleStoryboard?.Stop();

        var sb = new Storyboard();
        var animX = new DoubleAnimation
        {
            To = targetScale,
            Duration = MotionTokens.D(MotionTokens.Fast),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(animX, _thumbScale);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        sb.Children.Add(animX);

        var animY = new DoubleAnimation
        {
            To = targetScale,
            Duration = MotionTokens.D(MotionTokens.Fast),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(animY, _thumbScale);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Children.Add(animY);

        _thumbScaleStoryboard = sb;
        sb.Begin();
    }

    private void Position_TrackPressed(object sender, PointerRoutedEventArgs e)
    {
        if (VisualTree.FindAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null) return;
        if (!TryGetTrackFraction(e, out var fraction)) return;
        e.Handled = true;
        var player = Player;
        if (player.Duration <= TimeSpan.Zero) return;
        player.SeekStartedCommand.Execute(null);
        PositionSlider.Value = fraction;
        player.SeekCompletedCommand.Execute(fraction);
    }

    private bool TryGetTrackFraction(PointerRoutedEventArgs e, out double fraction)
    {
        fraction = 0;
        var track = _positionTrack;
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
        if (Player.IsSeeking) return;
        if (Math.Abs(e.NewValue - Player.PositionNormalized) < 1e-9) return;
        if (Player.Duration <= TimeSpan.Zero) return;
        Player.SeekCompletedCommand.Execute(e.NewValue);
    }

    /// <summary>拖动开始：让 VM 进入 IsSeeking + ⑧ 拇指放大 ×1.15。</summary>
    private void Position_DragStarted(object sender, DragStartedEventArgs e)
    {
        Player.SeekStartedCommand.Execute(null);

        // ⑧ 延迟计算缩放中心：此时 Thumb 必定已布局完成，ActualWidth/Height 可靠
        if (_thumbScale is not null && _positionThumb is not null)
        {
            _thumbScale.CenterX = _positionThumb.ActualWidth / 2;
            _thumbScale.CenterY = _positionThumb.ActualHeight / 2;
        }

        AnimateThumbScale(1.15);
    }

    /// <summary>拖动结束：提交位置 + ⑧ 拇指缩回 ×1.0。</summary>
    private void Position_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        Player.SeekCompletedCommand.Execute(PositionSlider.Value);
        AnimateThumbScale(1.0);
    }

    public void TogglePlayPause()
    {
        if (Player.CurrentTrack is null)
            Playlists.ViewedPlaylist?.PlayCurrentCommand.Execute(null);
        else
            Player.PlayPauseCommand.Execute(null);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (Player.CurrentTrack is null)
            Playlists.ViewedPlaylist?.PlayCurrentCommand.Execute(null);
        else
            Player.PlayPauseCommand.Execute(null);
    }

    // —— ③ 按钮缩放动效 ——

    /// <summary>③ 悬停：Scale → 1.04（100ms）。</summary>
    private void PlayPauseButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        => AnimateButtonScale(1.04, MotionTokens.Fast);

    /// <summary>③ 离开：Scale → 1.0（150ms，回弹）。</summary>
    private void PlayPauseButton_PointerExited(object sender, PointerRoutedEventArgs e)
        => AnimateButtonScale(1.0, MotionTokens.Normal);

    /// <summary>③ 按下：Scale → 0.96（100ms）。</summary>
    private void PlayPauseButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        => AnimateButtonScale(0.96, MotionTokens.Fast);

    /// <summary>③ 松开（仍在按钮上）：回到悬停态 1.04（150ms）。</summary>
    private void PlayPauseButton_PointerReleased(object sender, PointerRoutedEventArgs e)
        => AnimateButtonScale(1.04, MotionTokens.Normal);

    private void AnimateButtonScale(double target, TimeSpan duration)
    {
        _pressStoryboard?.Stop();
        var sb = new Storyboard();

        var animX = new DoubleAnimation
        {
            To = target,
            Duration = MotionTokens.D(duration),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(animX, PlayPauseScale);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        sb.Children.Add(animX);

        var animY = new DoubleAnimation
        {
            To = target,
            Duration = MotionTokens.D(duration),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(animY, PlayPauseScale);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Children.Add(animY);

        _pressStoryboard = sb;
        sb.Begin();
    }

    // —— ⑦ 播放/暂停图标交叉淡入 ——

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshView();

    private static readonly string[] ViewProjections =
    [
        nameof(NowPlayingText), nameof(TimeText),
        nameof(HasPlayableSource), nameof(HasDuration),
    ];

    private void RefreshView()
    {
        foreach (var name in ViewProjections)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ⑦ 播放/暂停图标交叉淡入
        UpdatePlayPauseIcons();
    }

    /// <summary>
    /// ⑦ 更新播放/暂停图标的 Opacity：播放态 → PauseIcon 可见、PlayIcon 隐藏；暂停态反之。
    /// 交叉淡入 100ms 由 Storyboard 驱动。
    /// </summary>
    private void UpdatePlayPauseIcons()
    {
        var isPlaying = Player.PlayState == PlayState.Playing;
        var playTarget = isPlaying ? 0.0 : 1.0;
        var pauseTarget = isPlaying ? 1.0 : 0.0;

        _iconStoryboard?.Stop();

        var sb = new Storyboard();

        var playAnim = new DoubleAnimation
        {
            To = playTarget,
            Duration = MotionTokens.D(MotionTokens.Fast),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(playAnim, PlayIcon);
        Storyboard.SetTargetProperty(playAnim, "Opacity");
        sb.Children.Add(playAnim);

        var pauseAnim = new DoubleAnimation
        {
            To = pauseTarget,
            Duration = MotionTokens.D(MotionTokens.Fast),
            EasingFunction = MotionTokens.StandardEasing,
        };
        Storyboard.SetTarget(pauseAnim, PauseIcon);
        Storyboard.SetTargetProperty(pauseAnim, "Opacity");
        sb.Children.Add(pauseAnim);

        _iconStoryboard = sb;
        sb.Begin();
    }

    // —— x:Bind 只读投影 ——

    public string NowPlayingText => Player.CurrentTrack is { } t
        ? $"{t.Title} — {t.Artist}" : "未在播放";
    public string TimeText => $"{FormatClock(Player.Position)} / {FormatClock(Player.Duration)}";

    public bool HasPlayableSource => Player.CurrentTrack is not null
        || (Playlists.ViewedPlaylist?.Queue.Count ?? 0) > 0;

    public bool HasDuration => Player.Duration > TimeSpan.Zero;

    private static string FormatClock(TimeSpan t) => t.ToString(@"mm\:ss");
}
