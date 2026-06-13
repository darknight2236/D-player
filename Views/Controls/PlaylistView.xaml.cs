using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

    /// <summary>内部拖拽自定义 DataObject 格式名（用于区分外部 FileDrop）。</summary>
    private const string QueueItemsFormat = "UmaPlayer.QueueItems";

    /// <summary>PreviewMouseLeftButtonDown 时记录的起点；MouseMove 用于阈值判定。</summary>
    private Point? _dragStartPoint;

    /// <summary>当前 ListBox AdornerLayer 上的插入线 Adorner；同一时刻最多 1 个。</summary>
    private DropInsertionAdorner? _currentAdorner;

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

    // —— Phase 5：拖拽启动（PreviewMouseLeftButton* + MouseMove） ——

    private void QueueList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 仅记录起点；实际启动在 MouseMove 阈值后。
        // 不抢 ListBox 默认选中行为 —— 不 Handled。
        _dragStartPoint = e.GetPosition(QueueList);
    }

    private void QueueList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 鼠标抬起即清除起点，避免松开后再移动还会触发拖拽
        _dragStartPoint = null;
    }

    private void QueueList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm is null) return;
        if (_dragStartPoint is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _dragStartPoint = null; return; }

        var current = e.GetPosition(QueueList);
        var dx = System.Math.Abs(current.X - _dragStartPoint.Value.X);
        var dy = System.Math.Abs(current.Y - _dragStartPoint.Value.Y);
        if (dx < SystemParameters.MinimumHorizontalDragDistance &&
            dy < SystemParameters.MinimumVerticalDragDistance)
            return;

        // 只有当拖动起点落在某个 ListBoxItem 上时才启动（避免空白区拖出空选）
        var sourceItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (sourceItem is null) { _dragStartPoint = null; return; }

        // 按引用身份建立 selection 集合 —— Track 是 record（结构相等），用 IndexOf
        // 会让结构相等但引用不同的占位 Track 互相覆盖；扫一遍 Queue 按引用匹配既保证升序、
        // 又天然唯一（List.Add 插入顺序 = Queue 索引升序，无需额外 Distinct/OrderBy）
        var selected = new HashSet<Track>(
            QueueList.SelectedItems.Cast<Track>(),
            ReferenceEqualityComparer.Instance);
        var indices = new List<int>(selected.Count);
        for (int i = 0; i < _vm.Queue.Count; i++)
        {
            if (selected.Contains(_vm.Queue[i])) indices.Add(i);
        }
        if (indices.Count == 0) { _dragStartPoint = null; return; }

        _dragStartPoint = null; // 启动拖拽即消费起点
        var data = new DataObject(QueueItemsFormat, indices);
        // DoDragDrop 是同步 modal —— 期间 UI 线程被 OLE 阻塞，但 NAudio 在另一线程推流不停
        DragDrop.DoDragDrop(QueueList, data, DragDropEffects.Move);

        // 拖拽结束（无论 Drop / Esc / Leave）后清理 Adorner
        HideAdorner();
    }

    // —— Phase 5：DragOver / Drop（区分内部重排 vs 外部 FileDrop） ——

    private void QueueList_DragOver(object sender, DragEventArgs e)
    {
        if (_vm is null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        if (e.Data.GetDataPresent(QueueItemsFormat))
        {
            // 内部重排
            int insertIdx = ComputeInsertIndex(e.GetPosition(QueueList));
            ShowAdorner(insertIdx);
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // 外部文件
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            var audio = DragDropExtensions.FilterAudioPaths(paths);
            e.Effects = audio.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void QueueList_DragLeave(object sender, DragEventArgs e)
    {
        // 拖出 ListBox 边界即隐藏插入线（外部高亮由 Root_DragLeave 处理）
        HideAdorner();
    }

    private void QueueList_Drop(object sender, DragEventArgs e)
    {
        if (_vm is null) { e.Handled = true; return; }
        try
        {
            if (e.Data.GetDataPresent(QueueItemsFormat))
            {
                var sources = e.Data.GetData(QueueItemsFormat) as IReadOnlyList<int>;
                if (sources is null || sources.Count == 0) return;
                int target = ComputeInsertIndex(e.GetPosition(QueueList));
                _vm.MoveTracksCommand.Execute(new MoveTracksArgs(sources, target));
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                var audio = DragDropExtensions.FilterAudioPaths(paths);
                if (audio.Count > 0)
                {
                    _vm.DropExternalFilesCommand.Execute(audio);
                }
            }
        }
        finally
        {
            HideAdorner();
            e.Handled = true;
        }
    }

    // —— Phase 5：拖入高亮（外层 Border 承载 AllowDrop，但视觉高亮挂在 Row 1 的
    //    QueueListBorder 上，让用户只看到圆角列表框被框住，不连带工具栏）。
    //    仅外部文件拖入触发；内部重排走插入线 Adorner，不亮整框。 ——

    private void Root_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            !e.Data.GetDataPresent(QueueItemsFormat))
        {
            DragDropExtensions.SetIsDragOver(QueueListBorder, true);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        // DragEnter 已处理高亮；此处仅设 Effects 防止默认拒绝
        if (e.Data.GetDataPresent(QueueItemsFormat))
        {
            // 内部重排不在 Root 处理 Effects，让 ListBox 的 DragOver 决定
            return;
        }
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            var audio = DragDropExtensions.FilterAudioPaths(paths);
            e.Effects = audio.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        DragDropExtensions.SetIsDragOver(QueueListBorder, false);
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        // 1) 清列表框高亮（无论哪条路径）
        DragDropExtensions.SetIsDragOver(QueueListBorder, false);

        // 2) 内部重排：QueueList_Drop 已 Handled=true，此处不会到；保险起见早退
        if (e.Handled) return;
        if (e.Data.GetDataPresent(QueueItemsFormat)) return;

        // 3) 外部文件落在 Border 内但 ListBox 之外（如工具栏 gutter）—— 当作末尾入队
        //    避免用户看到 Copy 光标却无反应的假死
        if (_vm is null) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            var audio = DragDropExtensions.FilterAudioPaths(paths);
            if (audio.Count > 0)
            {
                _vm.DropExternalFilesCommand.Execute(audio);
            }
            e.Handled = true;
        }
    }

    // —— Phase 5：插入位置命中测试 ——

    /// <summary>
    /// 把 ListBox 坐标系下的鼠标点映射到插入索引 ∈ [0, Queue.Count]。
    /// 命中某项 → 鼠标在上半部 → 该项之前；下半部 → 该项之后。
    /// 无命中 → Queue.Count（末尾）。
    /// </summary>
    private int ComputeInsertIndex(Point posInListBox)
    {
        for (int i = 0; i < QueueList.Items.Count; i++)
        {
            if (QueueList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container) continue;
            var transform = container.TransformToAncestor(QueueList);
            var topLeft = transform.Transform(new Point(0, 0));
            var rect = new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight));
            if (posInListBox.Y >= rect.Top && posInListBox.Y < rect.Bottom)
            {
                return posInListBox.Y < rect.Top + rect.Height / 2 ? i : i + 1;
            }
        }
        return QueueList.Items.Count; // 鼠标在所有项之下 → 末尾
    }

    // —— Phase 5：Adorner 生命周期 ——

    private void ShowAdorner(int insertIndex)
    {
        var layer = AdornerLayer.GetAdornerLayer(QueueList);
        if (layer is null) return;
        if (_currentAdorner is null)
        {
            _currentAdorner = new DropInsertionAdorner(QueueList);
            layer.Add(_currentAdorner);
        }
        _currentAdorner.Update(insertIndex);
    }

    private void HideAdorner()
    {
        if (_currentAdorner is null) return;
        var layer = AdornerLayer.GetAdornerLayer(QueueList);
        layer?.Remove(_currentAdorner);
        _currentAdorner = null;
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
