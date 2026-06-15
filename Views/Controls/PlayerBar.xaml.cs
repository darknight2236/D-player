using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;
using UmaPlayer.Views.Dialogs;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 播放栏 UserControl —— 唯一的可视化组件，包含封面、元数据、进度条、播放控制和音量。
///
/// Code-behind 只承担"WPF Slider 原生不支持的事件路由"：
///   1) 单击进度条空白处 → 跳转到该位置（PreviewMouseLeftButtonDown）
///   2) 拖动 thumb 开始/结束 → 触发 VM 的 SeekStarted/SeekCompleted
/// </summary>
public partial class PlayerBar : UserControl
{
    public PlayerBar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 单击跳转：从鼠标 X 坐标 / Track 宽度 计算归一化位置 [0,1]。
    /// 点击 Thumb 时不在此处理 —— 让 Thumb 自己触发 DragStarted/DragCompleted（更顺滑）。
    /// </summary>
    private void SeekBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 命中 Thumb 时跳过：避免单击 Thumb 也被当作"瞬移到鼠标位置"
        if (FindAncestor<Thumb>(e.OriginalSource as DependencyObject) != null) return;

        if (sender is not Slider slider) return;

        // PART_Track 是 Slider ControlTemplate 中的核心轨道元素（见 Themes/Controls.xaml）
        if (slider.Template.FindName("PART_Track", slider) is not Track track) return;

        var pos = e.GetPosition(track);
        var ratio = track.ActualWidth > 0 ? pos.X / track.ActualWidth : 0;
        ratio = Math.Clamp(ratio, 0, 1);

        (DataContext as PlayerViewModel)?.SeekCompletedCommand.Execute(ratio);
        e.Handled = true; // 阻止后续默认拖拽行为
    }

    /// <summary>沿可视化树向上找指定类型的祖先；用于检测点击是否命中 Thumb。</summary>
    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T match) return match;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
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
        SettingsDialog.Show(Window.GetWindow(this), persistence);
    }
}
