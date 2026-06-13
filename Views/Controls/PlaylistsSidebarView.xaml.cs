using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UmaPlayer.ViewModels;
using UmaPlayer.Views.Dialogs;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// Phase 6 左侧歌单容器侧边栏。+ 新建、− 删选中、双击重命名;
/// "正在播放"行加 ▶ 前缀(模仿 PlaylistView.RefreshCurrentIndicator 的 ItemContainerGenerator 模式)。
/// </summary>
public partial class PlaylistsSidebarView : UserControl
{
    private PlaylistsViewModel? _vm;

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
        if (_vm is null) return;
        var (ok, text) = PromptDialog.Show(Window.GetWindow(this), "新建歌单", "名称", "新歌单");
        if (!ok) return;
        _vm.AddPlaylistCommand.Execute(text);
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
}
