using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using UmaPlayer.ViewModels;
using UmaPlayer.Views.Dialogs;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// Phase 6 左侧歌单容器侧边栏。+ 新建、− 删选中、双击重命名;
/// "正在播放"行加 ▶ 前缀(模仿 PlaylistView.RefreshCurrentIndicator 的 ItemContainerGenerator 模式)。
/// Phase 9: 拖拽重排歌单顺序（复用 Phase 5 的 Adorner + 多选拖拽保护模式）。
/// </summary>
public partial class PlaylistsSidebarView : UserControl
{
    private PlaylistsViewModel? _vm;

    /// <summary>内部拖拽自定义 DataObject 格式名。</summary>
    private const string PlaylistItemsFormat = "UmaPlayer.PlaylistItems";

    /// <summary>PreviewMouseLeftButtonDown 时记录的起点；MouseMove 用于阈值判定。</summary>
    private Point? _dragStartPoint;

    /// <summary>当前 AdornerLayer 上的插入线 Adorner；同一时刻最多 1 个。</summary>
    private DropInsertionAdorner? _currentAdorner;

    public PlaylistsSidebarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            PlaylistList.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                if (PlaylistList.ItemContainerGenerator.Status ==
                    System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    RefreshActiveMarker();
            };
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Playlists.CollectionChanged -= OnPlaylistsChanged;
            UnhookAllPlaylistVms();
        }

        _vm = e.NewValue as PlaylistsViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Playlists.CollectionChanged += OnPlaylistsChanged;
            HookAllPlaylistVms();
            RefreshActiveMarker();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistsViewModel.CurrentPlaylistId))
            RefreshActiveMarker();
    }

    private void OnPlaylistsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 新增的 VM 需要 hook，被移除的 VM 需要 unhook
        if (e.OldItems != null)
        {
            foreach (PlaylistViewModel vm in e.OldItems)
                vm.PropertyChanged -= OnPlaylistVmPropertyChanged;
        }
        if (e.NewItems != null)
        {
            foreach (PlaylistViewModel vm in e.NewItems)
                vm.PropertyChanged += OnPlaylistVmPropertyChanged;
        }
        Dispatcher.BeginInvoke(new Action(RefreshActiveMarker),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnPlaylistVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistViewModel.IsActivePlaylist))
            RefreshActiveMarker();
    }

    private void HookAllPlaylistVms()
    {
        if (_vm is null) return;
        foreach (var vm in _vm.Playlists)
            vm.PropertyChanged += OnPlaylistVmPropertyChanged;
    }

    private void UnhookAllPlaylistVms()
    {
        if (_vm is null) return;
        foreach (var vm in _vm.Playlists)
            vm.PropertyChanged -= OnPlaylistVmPropertyChanged;
    }

    /// <summary>
    /// 模仿 PlaylistView.RefreshCurrentIndicator: 找每个 container 第 0 个 TextBlock(▶ 列),
    /// 按目标 VM.IsActivePlaylist 写 "▶" 或 ""。
    /// </summary>
    private void RefreshActiveMarker()
    {
        if (_vm is null) return;
        PlaylistList.UpdateLayout();
        for (int i = 0; i < PlaylistList.Items.Count; i++)
        {
            if (PlaylistList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
                continue;
            if (PlaylistList.Items[i] is not PlaylistViewModel vm) continue;

            var marker = FindChildByOrder<TextBlock>(container, 0);
            if (marker is null) continue;
            marker.Text = vm.IsActivePlaylist ? "▶" : string.Empty;
        }
    }

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

    // —— 事件处理 ——

    private void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is { } menu)
        {
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void AddPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var (ok, text) = PromptDialog.Show(Window.GetWindow(this), "新建歌单", "名称", "新歌单");
        if (!ok) return;
        _vm.AddPlaylistCommand.Execute(text);
    }

    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.ImportFolderCommand.Execute(null);
    }

    private void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var target = _vm.ViewedPlaylist;
        if (target is null) return;

        // 简单 yes/no 确认 —— Phase 6 用 MessageBox.OK/Cancel
        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"确定删除歌单 \"{target.Name}\" 吗?",
            "删除歌单",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        _vm.RemovePlaylistCommand.Execute(target);
    }

    private void PlaylistList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is null) return;
        if (item.DataContext is not PlaylistViewModel target) return;

        var (ok, text) = PromptDialog.Show(Window.GetWindow(this), "重命名歌单", "新名称", target.Name);
        if (!ok) return;
        _vm.RenamePlaylistCommand.Execute((target, text));
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

    // —— Phase 9: 拖拽重排（复用 Phase 5 PlaylistView 的模式） ——

    private void PlaylistList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(PlaylistList);
    }

    private void PlaylistList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = null;
    }

    private void PlaylistList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm is null) return;
        if (_dragStartPoint is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _dragStartPoint = null; return; }

        var current = e.GetPosition(PlaylistList);
        var dx = Math.Abs(current.X - _dragStartPoint.Value.X);
        var dy = Math.Abs(current.Y - _dragStartPoint.Value.Y);
        if (dx < SystemParameters.MinimumHorizontalDragDistance &&
            dy < SystemParameters.MinimumVerticalDragDistance)
            return;

        // 只有当拖动起点落在某个 ListBoxItem 上时才启动
        var sourceItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (sourceItem is null) { _dragStartPoint = null; return; }

        int srcIndex = PlaylistList.ItemContainerGenerator.IndexFromContainer(sourceItem);
        if (srcIndex < 0) { _dragStartPoint = null; return; }

        _dragStartPoint = null;
        var data = new DataObject(PlaylistItemsFormat, srcIndex);
        DragDrop.DoDragDrop(PlaylistList, data, DragDropEffects.Move);

        // 拖拽结束后清理 Adorner
        HideAdorner();
    }

    private void PlaylistList_DragOver(object sender, DragEventArgs e)
    {
        if (_vm is null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        if (e.Data.GetDataPresent(PlaylistItemsFormat))
        {
            int insertIdx = ComputeInsertIndex(e.GetPosition(PlaylistList));
            ShowAdorner(insertIdx);
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void PlaylistList_DragLeave(object sender, DragEventArgs e)
    {
        HideAdorner();
    }

    private void PlaylistList_Drop(object sender, DragEventArgs e)
    {
        if (_vm is null) { e.Handled = true; return; }
        try
        {
            if (e.Data.GetDataPresent(PlaylistItemsFormat))
            {
                int srcIndex = (int)e.Data.GetData(PlaylistItemsFormat)!;
                int tgtIndex = ComputeInsertIndex(e.GetPosition(PlaylistList));
                _vm.MovePlaylistCommand.Execute((srcIndex, tgtIndex));
            }
        }
        finally
        {
            HideAdorner();
            e.Handled = true;
        }
    }

    private int ComputeInsertIndex(Point posInListBox)
    {
        for (int i = 0; i < PlaylistList.Items.Count; i++)
        {
            if (PlaylistList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container) continue;
            var transform = container.TransformToAncestor(PlaylistList);
            var topLeft = transform.Transform(new Point(0, 0));
            var rect = new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight));
            if (posInListBox.Y >= rect.Top && posInListBox.Y < rect.Bottom)
            {
                return posInListBox.Y < rect.Top + rect.Height / 2 ? i : i + 1;
            }
        }
        return PlaylistList.Items.Count;
    }

    private void ShowAdorner(int insertIndex)
    {
        var layer = AdornerLayer.GetAdornerLayer(PlaylistList);
        if (layer is null) return;
        if (_currentAdorner is null)
        {
            _currentAdorner = new DropInsertionAdorner(PlaylistList);
            layer.Add(_currentAdorner);
        }
        _currentAdorner.Update(insertIndex);
    }

    private void HideAdorner()
    {
        if (_currentAdorner is null) return;
        var layer = AdornerLayer.GetAdornerLayer(PlaylistList);
        layer?.Remove(_currentAdorner);
        _currentAdorner = null;
    }
}
