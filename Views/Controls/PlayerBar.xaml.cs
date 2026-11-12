using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DPlayer.Services;
using DPlayer.ViewModels;
using DPlayer.Views.Dialogs;

namespace DPlayer.Views.Controls;

/// <summary>
/// 播放栏 UserControl —— 唯一的可视化组件，包含封面、元数据、进度条、播放控制和音量。
///
/// Code-behind 只承担"WPF Slider 原生不支持的事件路由"：
///   1) 单击进度条空白处 → 跳转到该位置（PreviewMouseLeftButtonDown）
///   2) 拖动 thumb 开始/结束 → 触发 VM 的 SeekStarted/SeekCompleted
/// </summary>
public partial class PlayerBar : UserControl
{
    // 窄于该宽度时，居中的传输按钮组会与右对齐的音量组相撞（600 宽时实测重叠）。
    private const double NarrowLayoutThreshold = 740;
    private const double WideVolumeWidth = 100;
    private const double NarrowVolumeWidth = 48;

    public PlayerBar()
    {
        InitializeComponent();
        SizeChanged += PlayerBar_SizeChanged;

        // 单击跳转必须用 handledEventsToo:true 挂接 —— 隐式 Slider 样式开启了
        // IsMoveToPointEnabled（Phase 13），Slider 的类处理器 OnPreviewMouseLeftButtonDown
        // 会在实例处理器之前把 e.Handled 置 true；XAML 附加的实例处理器（不接收已处理事件）
        // 会被静默跳过，导致点击进度条整体失效（音频不受只改本地 Value 的 move-to-point 影响：
        // Value 是 OneWay 绑定，被 PositionNormalized 的轮询覆盖且不通知 VM）。
        SeekBar.AddHandler(PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(SeekBar_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
    }

    /// <summary>窄窗收缩音量区：传输组居中固定 272px，600 宽时右侧音量组必须让出空间。</summary>
    private void PlayerBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < NarrowLayoutThreshold;
        VolumeSlider.Width = narrow ? NarrowVolumeWidth : WideVolumeWidth;
        var gap = narrow ? new Thickness(0) : new Thickness(8, 0, 0, 0);
        EqualizerButton.Margin = gap;
        SettingsButton.Margin = gap;
    }

    /// <summary>
    /// 单击跳转：从鼠标 X 坐标 / Track 宽度 计算归一化位置 [0,1]。
    /// 点击 Thumb 时不在此处理 —— 让 Thumb 自己触发 DragStarted/DragCompleted（更顺滑）。
    /// </summary>
    private void SeekBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 命中 Thumb 时跳过：避免单击 Thumb 也被当作"瞬移到鼠标位置"
        if (VisualTreeHelpers.FindAncestor<Thumb>(e.OriginalSource as DependencyObject) != null) return;

        if (sender is not Slider slider) return;

        // PART_Track 是 Slider ControlTemplate 中的核心轨道元素（见 Themes/Controls.xaml）
        if (slider.Template.FindName("PART_Track", slider) is not Track track) return;

        var pos = e.GetPosition(track);
        var ratio = track.ActualWidth > 0 ? pos.X / track.ActualWidth : 0;
        ratio = Math.Clamp(ratio, 0, 1);

        (DataContext as PlayerViewModel)?.SeekCompletedCommand.Execute(ratio);
        e.Handled = true; // 阻止后续默认拖拽行为
    }

    // —— Thumb 拖拽：开始时让 VM 进入 IsSeeking 状态，结束时提交最终位置 ——

    private void SeekBar_DragStarted(object sender, DragStartedEventArgs e)
        => (DataContext as PlayerViewModel)?.SeekStartedCommand.Execute(null);

    private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
        => (DataContext as PlayerViewModel)?.SeekCompletedCommand.Execute(SeekBar.Value);

    /// <summary>点击 ⚙ 按钮打开设置对话框。</summary>
    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var persistence = App.GetService<ISettingsPersistence>();
        var playbackService = App.GetService<IPlaybackService>();
        var fileDialog = App.GetService<IFileDialogService>();
        var playerViewModel = DataContext as PlayerViewModel;
        if (SettingsDialog.Show(
                Window.GetWindow(this), persistence, playbackService,
                playerViewModel, fileDialog))
        {
            // 只有库根目录真的变了才重扫；频谱/音量保存不应触发整库刷新
            if (Window.GetWindow(this)?.DataContext is MainViewModel main)
                _ = main.Library.RefreshIfRootsChangedAsync();
        }
    }

    /// <summary>点击 🎚 按钮打开均衡器对话框（Phase 14）。</summary>
    private void EqualizerBtn_Click(object sender, RoutedEventArgs e)
    {
        var persistence = App.GetService<ISettingsPersistence>();
        var playbackService = App.GetService<IPlaybackService>();
        var playerViewModel = DataContext as PlayerViewModel;
        EqualizerDialog.Show(Window.GetWindow(this), persistence, playbackService, playerViewModel);
    }
}
