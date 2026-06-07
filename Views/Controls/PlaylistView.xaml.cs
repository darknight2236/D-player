using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UmaPlayer.Models;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 播放队列 UserControl 的 code-behind。
///
/// 职责：
///   1) 双击 ListBox 项 → PlayTrackAtCommand(index)
///   2) Delete 键 → RemoveTrackCommand(SelectedIndex)
///   3) × 按钮 → RemoveTrackCommand(对应行 index)
///   4) 监听 VM.CurrentIndex 变化，刷新行首 ▶ 标记与文字颜色
/// </summary>
public partial class PlaylistView : UserControl
{
    private PlaylistViewModel? _vm;

    public PlaylistView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // ListBox 虚拟化下, 滚动会重建 container; 当生成器完成一批新容器时重画 ▶
        Loaded += (_, _) =>
        {
            QueueList.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                if (QueueList.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    RefreshCurrentIndicator();
            };
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 取消旧订阅
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Queue.CollectionChanged -= OnQueueChanged;
        }

        _vm = e.NewValue as PlaylistViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Queue.CollectionChanged += OnQueueChanged;
            // 初次绑定时刷新一次
            RefreshCurrentIndicator();
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistViewModel.CurrentIndex))
            RefreshCurrentIndicator();
    }

    /// <summary>
    /// 队列变化（增删/替换）后刷新 ▶ 标记。
    /// 必要原因：PlayTrackAtAsync 会做 Queue[index]=meta 触发 Replace, ListBox 重建该容器,
    /// 默认 Foreground/marker 文字会回到 ForegroundPrimary/空, 必须重画。
    /// </summary>
    private void OnQueueChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // 延迟到布局完成后刷新, 避免在 container 尚未生成时读取 ItemContainerGenerator
        Dispatcher.BeginInvoke(new Action(RefreshCurrentIndicator), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 遍历 ListBox 所有可见项，根据 VM.CurrentIndex 设置 ▶ 标记与文字颜色。
    /// 通过 ItemContainerGenerator + VisualTree 直接修改，避开 MultiBinding/Converter 复杂度。
    /// </summary>
    private void RefreshCurrentIndicator()
    {
        if (_vm == null) return;

        QueueList.UpdateLayout(); // 确保 container 已生成
        for (int i = 0; i < QueueList.Items.Count; i++)
        {
            var container = QueueList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            if (container == null) continue;

            var marker = FindChildByOrder<TextBlock>(container, 0); // ▶ 列
            var title  = FindChildByOrder<TextBlock>(container, 1); // 文件名列
            if (marker == null || title == null) continue;

            bool isCurrent = (i == _vm.CurrentIndex);
            marker.Text = isCurrent ? "▶" : ""; // ▶
            title.Foreground = isCurrent
                ? (Brush)Application.Current.FindResource("AccentPrimary")
                : (Brush)Application.Current.FindResource("ForegroundPrimary");
        }
    }

    /// <summary>
    /// 按"出现顺序"在 VisualTree 中找第 N 个 T 类型的子元素。
    /// 0 = ▶ 列, 1 = 文件名列（与 XAML 中 DataTemplate 的 TextBlock 顺序对应）。
    /// </summary>
    private static T? FindChildByOrder<T>(DependencyObject parent, int n) where T : DependencyObject
    {
        int count = 0;
        return Walk(parent);

        T? Walk(DependencyObject p)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(p); i++)
            {
                var c = VisualTreeHelper.GetChild(p, i);
                if (c is T match)
                {
                    if (count == n) return match;
                    count++;
                }
                var deeper = Walk(c);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }

    // —— 事件转发 ——

    /// <summary>双击列表项 → 播放该项。空白区双击不触发。</summary>
    private void QueueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;

        int index = QueueList.ItemContainerGenerator.IndexFromContainer(item);
        if (index < 0) return;

        _vm.PlayTrackAtCommand.Execute(index);
        e.Handled = true;
    }

    /// <summary>Delete 键 → 删除选中项。</summary>
    private void QueueList_KeyDown(object sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        if (e.Key != Key.Delete) return;
        if (QueueList.SelectedIndex < 0) return;

        _vm.RemoveTrackCommand.Execute(QueueList.SelectedIndex);
        e.Handled = true;
    }

    /// <summary>× 按钮 → 删除对应行。Tag 已绑定 DataContext (Track)。</summary>
    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not Track track) return;

        int index = _vm.Queue.IndexOf(track);
        if (index < 0) return;

        _vm.RemoveTrackCommand.Execute(index);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T match) return match;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
