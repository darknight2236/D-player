using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;
using DPlayer.WinUI.Services;
using DPlayer.WinUI.Theme;
using DPlayer.WinUI.Views.Dialogs;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.System;

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

    /// <summary>PageUp/PageDown 的键盘补挂同样只做一次（见 <see cref="Position_KeyDown"/>）。</summary>
    private bool _keysHooked;

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

    /// <summary>最近一次已应用到图标的 PlayState；位置心跳（~30 Hz PropertyChanged）不再重启动画。</summary>
    private PlayState? _lastIconState;

    // —— Phase 22：运输控件状态同步 ——

    /// <summary>SyncTransportVisuals 写 VolumeSlider.Value 时的重入 guard（ValueChanged 不再回写 Player.Volume）。</summary>
    private bool _syncingVolume;

    /// <summary>悬停音量弹层的驱动定时器：按真实鼠标位置开/关，不依赖 PointerExited。</summary>
    private readonly DispatcherTimer _volumeHoverTimer;

    private bool _volumePopupHover;
    private IntPtr _hwnd;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint lpPoint);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    /// <summary>最近一次已应用的运输控件状态签名；位置心跳（~30 Hz）上早退，不刷画笔/滑块。</summary>
    private string _lastTransportSignature = string.Empty;

    private static Brush ThemeBrush(string key, Brush fallback)
    {
        try { return Application.Current.Resources[key] as Brush ?? fallback; }
        catch { return fallback; }
    }

    private readonly Brush _accentBrush =
        ThemeBrush("AccentFillColorDefaultBrush", new SolidColorBrush(Microsoft.UI.Colors.White));
    private readonly Brush _secondaryBrush =
        ThemeBrush("TextFillColorSecondaryBrush", new SolidColorBrush(Microsoft.UI.Colors.Gray));

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerViewModel Player { get; }

    public PlaylistsViewModel Playlists { get; }

    private readonly IPlaybackService _playbackService;
    private readonly ISettingsPersistence _settings;
    private EqualizerDialog? _equalizerDialog;

    /// <summary>
    /// Playlists 走构造参数，**不**走 <c>required</c> + 对象初始化器：初始化器在 ctor 体返回**之后**才执行，
    /// 而本 ctor 体里的订阅（下方最后一行）与 <c>InitializeComponent()</c> 里的 x:Bind 都要求它此刻已就位 ——
    /// 初始化器时代这里先抛 NullReferenceException，整窗起不来（Phase 21 收尾的起窗崩溃根因）。
    /// </summary>
    public PlayerBar(PlayerViewModel player, PlaylistsViewModel playlists,
        IPlaybackService playbackService, ISettingsPersistence settings)
    {
        // 顺序有承重：x:Bind（Player.PositionNormalized 等）在 InitializeComponent() 里就求值一遍，
        // 两个绑定源必须先赋值再 InitializeComponent() —— 与 NavRail.xaml.cs:49 同纪律。
        Player = player;
        Playlists = playlists;
        _playbackService = playbackService;
        _settings = settings;
        InitializeComponent();
        // 内置 Thumb tooltip 直接显示滑块的原始 Value（0..1 小数）；换成时间 / 百分比。
        PositionSlider.ThumbToolTipValueConverter = new PositionThumbToolTipConverter(player);
        VolumeSlider.ThumbToolTipValueConverter = new VolumeThumbToolTipConverter();
        VolumePopupSlider.ThumbToolTipValueConverter = new VolumeThumbToolTipConverter();

        _volumeHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _volumeHoverTimer.Tick += (_, _) => UpdateVolumeFlyout();
        VolumeFlyout.Closed += (_, _) => _volumePopupHover = false;

        Loaded += (_, _) => HookSliderParts();
        player.PropertyChanged += Player_PropertyChanged;
        Playlists.PropertyChanged += (_, _) => RefreshView();
        // Queue/state changes do not raise Playlists.PropertyChanged; StateChanged does.
        Playlists.StateChanged += (_, _) => RefreshView();
        SyncTransportVisuals();
    }

    /// <summary>
    /// 给 Slider 挂上进度条的用户输入路径：模板部件 Thumb 的 DragStarted/DragCompleted（拖动）
    /// 与轨道元素的 PointerPressed（单击定位），外加控件本身的 KeyDown（PageUp/PageDown，见
    /// <see cref="Position_KeyDown"/>）。找不到部件时**静默跳过**。
    /// </summary>
    private void HookSliderParts()
    {
        if (_dragHooked && _railHooked && _keysHooked) return;
        PositionSlider.ApplyTemplate();

        if (!_keysHooked)
        {
            // PageUp/PageDown 补挂（见 Position_KeyDown）。handledEventsToo=true 与 MainWindow 的
            // 空格守卫同款：即使基础控件先标记 handled，事件仍会送到本处理器。
            PositionSlider.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Position_KeyDown), handledEventsToo: true);
            _keysHooked = true;
        }

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

    /// <summary>
    /// PageUp/PageDown → ±<c>LargeChange</c>（spec G7「PageUp 5% 步进」）。WinUI 3 基础 Slider 的
    /// 键盘路径只接方向键/Home/End，不把 PageUp/PageDown 映射到 LargeChange——Phase 21 验收 C2
    /// 实测步进为 0，而 <c>AutomationProperties.HelpText</c> 已向用户承诺「约 5%」。这里补上：
    /// 写回的仍是 <c>PositionSlider.Value</c>，与方向键/单击/拖动共用 <see cref="Position_Changed"/>
    /// 这唯一提交点（不新增第二条 seek 路径）；结果 clamp 到 [Minimum, Maximum]，端点处重复按不再提交。
    /// </summary>
    private void Position_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.PageUp or VirtualKey.PageDown)) return;
        e.Handled = true;   // 无论是否步进都由本处理器消费，避免被其它键盘路径接走
        if (Player.Duration <= TimeSpan.Zero) return;

        var delta = e.Key == VirtualKey.PageUp ? PositionSlider.LargeChange : -PositionSlider.LargeChange;
        var target = Math.Clamp(PositionSlider.Value + delta, PositionSlider.Minimum, PositionSlider.Maximum);
        if (Math.Abs(target - PositionSlider.Value) < 1e-9) return;   // 已在端点：无提交
        PositionSlider.Value = target;
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
        nameof(TimeText),
        nameof(HasPlayableSource), nameof(HasDuration),
    ];

    private void RefreshView()
    {
        foreach (var name in ViewProjections)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ⑦ 播放/暂停图标交叉淡入（内部只认 PlayState 真变化；位置心跳 tick 上早退）
        UpdatePlayPauseIcons();

        SyncTransportVisuals();
    }

    /// <summary>
    /// Phase 22 运输控件状态同步：上一首/下一首可用性、随机/循环/静音图标与提示、音量滑块。
    /// 按签名早退 —— RefreshView 由每次 Player.PropertyChanged 触发（含 ~30 Hz 位置心跳），
    /// 无变化时不再重设画笔与滑块值；写 VolumeSlider.Value 时用 <see cref="_syncingVolume"/> 挡住回写。
    /// </summary>
    private void SyncTransportVisuals()
    {
        // 传输按钮作用于"正在播放"的歌单，不跟随查看项；查看项只决定中间列表显示什么。
        var current = Playlists.CurrentPlaylist;
        var eqEnabled = _equalizerDialog is null
            ? Player.EqualizerEnabled : _playbackService.EqualizerConfig.Enabled;
        var signature =
            $"{Playlists.ShuffleEnabled}|{Playlists.RepeatMode}|{Player.IsMuted}|" +
            $"{Player.Volume.ToString("F3", CultureInfo.InvariantCulture)}|" +
            $"{eqEnabled}|" +
            $"{current?.Id ?? "-"}|{current?.HasCurrentTrack ?? false}";
        if (signature == _lastTransportSignature) return;
        _lastTransportSignature = signature;

        var hasTrack = current?.HasCurrentTrack == true;
        PrevButton.IsEnabled = hasTrack;
        NextButton.IsEnabled = hasTrack;

        ShuffleIcon.Foreground = Playlists.ShuffleEnabled ? _accentBrush : _secondaryBrush;
        var shuffleText = Playlists.ShuffleEnabled ? "随机播放：开" : "随机播放：关";
        ToolTipService.SetToolTip(ShuffleButton, shuffleText);
        AutomationProperties.SetName(ShuffleButton, shuffleText);

        var repeat = Playlists.RepeatMode;
        RepeatIcon.Glyph = ((char)(repeat == RepeatMode.One ? 0xE8ED : 0xE8EE)).ToString();
        RepeatIcon.Foreground = repeat == RepeatMode.Off ? _secondaryBrush : _accentBrush;
        var repeatText = repeat switch
        {
            RepeatMode.Off => "循环：关",
            RepeatMode.List => "循环：列表",
            RepeatMode.One => "循环：单曲",
            _ => "循环：关",
        };
        ToolTipService.SetToolTip(RepeatButton, repeatText);
        AutomationProperties.SetName(RepeatButton, repeatText);

        var muteText = Player.IsMuted ? "取消静音" : "静音";
        MuteIcon.Glyph = ((char)(Player.IsMuted ? 0xE74F : 0xE767)).ToString();
        ToolTipService.SetToolTip(MuteButton, muteText);
        AutomationProperties.SetName(MuteButton, muteText);

        EqIcon.Stroke = eqEnabled ? _accentBrush : _secondaryBrush;

        _syncingVolume = true;
        VolumeSlider.Value = Player.Volume;
        VolumePopupSlider.Value = Player.Volume;
        _syncingVolume = false;
    }

    /// <summary>
    /// ⑦ 更新播放/暂停图标的 Opacity：播放态 → PauseIcon 可见、PlayIcon 隐藏；暂停态反之。
    /// 交叉淡入 100ms 由 Storyboard 驱动。**只在 <see cref="PlayState"/> 真变化时**重播——RefreshView
    /// 由每次 Player.PropertyChanged 触发（含 ~30 Hz 位置心跳），无条件重播会让图标永远停在
    /// 交叉淡入的混合帧（Phase 21 验收 E1 实测）；心跳 tick 上直接早退，让上一条动画自行落定。
    /// 真切换时保留既有反叠机制：先 Stop() 旧 Storyboard 再 Begin() 新的。
    /// </summary>
    private void UpdatePlayPauseIcons()
    {
        if (_lastIconState == Player.PlayState) return;
        _lastIconState = Player.PlayState;

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

    // —— Phase 22：运输控件与音量 ——

    private void PrevButton_Click(object sender, RoutedEventArgs e)
        => Playlists.CurrentPlaylist?.PrevTrackCommand.Execute(null);

    private void NextButton_Click(object sender, RoutedEventArgs e)
        => Playlists.CurrentPlaylist?.NextTrackCommand.Execute(null);

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        => Playlists.ToggleShuffleCommand.Execute(null);

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
        => Playlists.CycleRepeatCommand.Execute(null);

    private void MuteButton_Click(object sender, RoutedEventArgs e)
        => Player.ToggleMuteCommand.Execute(null);

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingVolume) return;
        Player.Volume = (float)e.NewValue;
    }

    private void VolumePopupSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingVolume) return;
        Player.Volume = (float)e.NewValue;
    }

    // —— 窄窗的悬停音量弹层 ——

    private void MuteButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        => _volumeHoverTimer.Start();

    private void VolumeFlyoutPanel_PointerEntered(object sender, PointerRoutedEventArgs e)
        => _volumePopupHover = true;

    private void VolumeFlyoutPanel_PointerExited(object sender, PointerRoutedEventArgs e)
        => _volumePopupHover = false;

    /// <summary>
    /// 弹层状态机：横向音量条收起且指针在静音钮上 → 打开；指针既不在钮也不在弹层 → 关闭。
    /// 不用 PointerExited 判断"离开"：ShowAt 之后 WinUI 会立刻给锚点补一发 PointerExited
    /// （实测 hover-debug.log），按它关会把弹层在 250ms 内收掉。改为每 tick 读 GetCursorPos，
    /// 按真实坐标判断是否仍在钮上；弹层内部仍用 PointerEntered/Exited 维护 _volumePopupHover。
    /// </summary>
    private void UpdateVolumeFlyout()
    {
        if (VolumeSlider.Visibility != Visibility.Collapsed)
        {
            if (VolumeFlyout.IsOpen) VolumeFlyout.Hide();
            _volumeHoverTimer.Stop();
            return;
        }

        var overButton = IsPointerOver(MuteButton);
        if (overButton && !VolumeFlyout.IsOpen)
        {
            VolumeFlyout.ShowAt(MuteButton);
            return;
        }

        if (overButton || _volumePopupHover) return;

        if (VolumeFlyout.IsOpen) VolumeFlyout.Hide();
        _volumeHoverTimer.Stop();
    }

    private bool IsPointerOver(FrameworkElement element)
    {
        if (!GetCursorPos(out var cursor)) return false;
        var scale = element.XamlRoot.RasterizationScale;
        var origin = element.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var topLeft = new NativePoint { X = (int)(origin.X * scale), Y = (int)(origin.Y * scale) };
        ClientToScreen(GetWindowHandle(), ref topLeft);
        var width = element.ActualWidth * scale;
        var height = element.ActualHeight * scale;
        return cursor.X >= topLeft.X && cursor.X < topLeft.X + width
            && cursor.Y >= topLeft.Y && cursor.Y < topLeft.Y + height;
    }

    private IntPtr GetWindowHandle()
    {
        if (_hwnd == IntPtr.Zero)
            _hwnd = Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        return _hwnd;
    }

    private async void EqButton_Click(object sender, RoutedEventArgs e)
    {
        if (_equalizerDialog is not null || XamlRoot is null) return;
        try
        {
            _equalizerDialog = new EqualizerDialog(_playbackService, _settings, Player) { XamlRoot = XamlRoot };
            _equalizerDialog.PreviewChanged += Equalizer_PreviewChanged;
            await _equalizerDialog.ShowAsync();
        }
        catch (Exception ex) { AppLog.Write("[WinUI 壳] 打开均衡器失败", ex); }
        finally
        {
            if (_equalizerDialog is not null)
                _equalizerDialog.PreviewChanged -= Equalizer_PreviewChanged;
            _equalizerDialog = null;
            SyncTransportVisuals();
        }
    }

    private void Equalizer_PreviewChanged(object? sender, EventArgs e) => SyncTransportVisuals();

    public bool TryPrepareForWindowClose()
    {
        if (_equalizerDialog?.TryCancel() == false) return false;
        // 关闭清理的 await 期间不再允许打开 EQ 发起保存。
        EqButton.IsEnabled = false;
        return true;
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(Player) { XamlRoot = XamlRoot };
        if (dialog.XamlRoot is null) return;
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write("[WinUI 壳] 打开设置失败", ex);
        }
    }

    // —— x:Bind 只读投影 ——

    public string TimeText => $"{FormatClock(Player.Position)} / {FormatClock(Player.Duration)}";

    public bool HasPlayableSource => Player.CurrentTrack is not null
        || (Playlists.ViewedPlaylist?.Queue.Count ?? 0) > 0;

    public bool HasDuration => Player.Duration > TimeSpan.Zero;

    private static string FormatClock(TimeSpan t) => t.ToString(@"mm\:ss");
}

/// <summary>
/// 进度条拇指提示：内置提示显示原始 0..1 小数，这里换成时间（短曲 mm:ss，超过 1 小时用 h:mm:ss）。
/// </summary>
internal sealed class PositionThumbToolTipConverter : IValueConverter
{
    private readonly PlayerViewModel _player;

    public PositionThumbToolTipConverter(PlayerViewModel player) => _player = player;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var fraction = value is double d ? Math.Clamp(d, 0, 1) : 0;
        return FormatClock(_player.Duration * fraction);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    private static string FormatClock(TimeSpan t)
        => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
}

/// <summary>音量拇指提示：0..1 小数换成整数百分比（如 75%）。</summary>
internal sealed class VolumeThumbToolTipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var fraction = value is double d ? Math.Clamp(d, 0, 1) : 0;
        return $"{Math.Round(fraction * 100):0}%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
