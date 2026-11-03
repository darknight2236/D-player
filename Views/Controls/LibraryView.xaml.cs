using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MediaTrack = DPlayer.Models.Track;
using DPlayer.Services;
using DPlayer.ViewModels;
using DPlayer.Views.Dialogs;

namespace DPlayer.Views.Controls;

/// <summary>
/// Phase 24 音乐库主面板: 艺术家列表 / 专辑网格 / 艺术家详情 / 专辑详情 + 空态与状态行。
/// 结构由 LibraryViewModel.Page 驱动，本类只做手势（双击 / Enter）与封面惰性加载的转接。
/// </summary>
public partial class LibraryView : UserControl
{
    private LibraryViewModel? _vm;
    private MainViewModel? _shell;
    private PlayerViewModel? _player;
    private PlaylistsViewModel? _playlists;
    private bool _trackGeneratorHooked;

    public LibraryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = e.NewValue as LibraryViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachShell();
        if (!_trackGeneratorHooked)
        {
            AlbumTracksList.ItemContainerGenerator.StatusChanged += OnTrackContainersStatusChanged;
            _trackGeneratorHooked = true;
        }
        RefreshTrackIndicator();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachShell();
        if (_trackGeneratorHooked)
        {
            AlbumTracksList.ItemContainerGenerator.StatusChanged -= OnTrackContainersStatusChanged;
            _trackGeneratorHooked = false;
        }
    }

    private void AttachShell()
    {
        var shell = Window.GetWindow(this)?.DataContext as MainViewModel;
        if (ReferenceEquals(_shell, shell)) return;

        DetachShell();
        _shell = shell;
        if (_shell is null) return;

        _player = _shell.Player;
        _playlists = _shell.Playlists;
        _player.PropertyChanged += OnPlayerPropertyChanged;
        _playlists.PropertyChanged += OnPlaylistsPropertyChanged;
        RefreshTrackIndicator();
    }

    private void DetachShell()
    {
        if (_player is not null)
            _player.PropertyChanged -= OnPlayerPropertyChanged;
        if (_playlists is not null)
            _playlists.PropertyChanged -= OnPlaylistsPropertyChanged;
        _player = null;
        _playlists = null;
        _shell = null;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.CurrentTrack))
            RefreshTrackIndicator();
    }

    private void OnPlaylistsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlaylistsViewModel.IsLibraryPlaybackActive)
            or nameof(PlaylistsViewModel.CurrentPlaylistId))
        {
            RefreshTrackIndicator();
        }
    }

    private void OnTrackContainersStatusChanged(object? sender, EventArgs e)
    {
        if (AlbumTracksList.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            RefreshTrackIndicator();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedAlbum) &&
            _vm?.SelectedAlbum is { } album)
        {
            _ = _vm.EnsureCoverAsync(album);
        }

        if (e.PropertyName is nameof(LibraryViewModel.SelectedAlbum)
            or nameof(LibraryViewModel.SelectedArtist)
            or nameof(LibraryViewModel.Page))
        {
            Dispatcher.BeginInvoke(new Action(RefreshTrackIndicator), DispatcherPriority.Background);
        }
    }

    private void RefreshTrackIndicator()
    {
        if (_player is null || _playlists is null) return;

        var currentPath = _player.CurrentTrack?.FilePath;
        var isLibraryActive = _playlists.IsLibraryPlaybackActive;
        AlbumTracksList.UpdateLayout();

        for (var i = 0; i < AlbumTracksList.Items.Count; i++)
        {
            if (AlbumTracksList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
                continue;

            var marker = FindChildByName<Path>(container, "PART_Marker");
            if (marker is null) continue;

            var isCurrent = isLibraryActive
                && currentPath is not null
                && AlbumTracksList.Items[i] is MediaTrack track
                && string.Equals(track.FilePath, currentPath, StringComparison.OrdinalIgnoreCase);
            marker.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>点击列表空白处（不是项、也不是滚动条）时清除选中高亮。</summary>
    private void LibraryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;

        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<ListBoxItem>(source) is not null) return;
        if (FindAncestor<ScrollBar>(source) is not null) return;

        list.SelectedItem = null;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _vm?.Back();

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _ = _vm.RefreshAsync();
    }

    private void ArtistsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => OpenArtist(FindItemDataContext<LibraryArtist>(e));

    private void ArtistsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OpenArtist(ArtistsList.SelectedItem as LibraryArtist);
    }

    private void OpenArtist(LibraryArtist? artist)
    {
        if (artist is not null) _vm?.OpenArtist(artist);
    }

    private void AlbumsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => OpenAlbum(FindItemDataContext<LibraryAlbum>(e), fromArtist: false);

    private void AlbumsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var list = (ListBox)sender;
        OpenAlbum(list.SelectedItem as LibraryAlbum, fromArtist: false);
    }

    private void ArtistAlbumsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => OpenAlbum(FindItemDataContext<LibraryAlbum>(e), fromArtist: true);

    private void ArtistAlbumsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var list = (ListBox)sender;
        OpenAlbum(list.SelectedItem as LibraryAlbum, fromArtist: true);
    }

    private void OpenAlbum(LibraryAlbum? album, bool fromArtist)
    {
        if (album is not null) _vm?.OpenAlbum(album, fromArtist);
    }

    private async void AlbumTracksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.SelectedAlbum is not { } album) return;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item is null) return;
        var index = AlbumTracksList.ItemContainerGenerator.IndexFromContainer(item);
        if (index >= 0)
            await _vm.PlayAlbumTrackAsync(album, index);
    }

    private async void AlbumTracksList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_vm?.SelectedAlbum is not { } album) return;
        var index = AlbumTracksList.SelectedIndex;
        if (index >= 0)
            await _vm.PlayAlbumTrackAsync(album, index);
    }

    private void AlbumTile_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryAlbum album } && _vm is not null)
            _ = _vm.EnsureCoverAsync(album);
    }

    private void AlbumDetailCover_Loaded(object sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedAlbum is { } album)
            _ = _vm.EnsureCoverAsync(album);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is null) return;

        var persistence = App.GetService<ISettingsPersistence>();
        var playback = App.GetService<IPlaybackService>();
        var fileDialog = App.GetService<IFileDialogService>();
        var player = (window.DataContext as MainViewModel)?.Player;

        if (SettingsDialog.Show(window, persistence, playback, player, fileDialog))
            _ = _vm?.RefreshIfRootsChangedAsync();
    }

    private static T? FindItemDataContext<T>(MouseButtonEventArgs e) where T : class
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        return item?.DataContext as T;
    }

    private static T? FindChildByName<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        if (parent is T match && match.Name == name)
            return match;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (FindChildByName<T>(VisualTreeHelper.GetChild(parent, i), name) is { } found)
                return found;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj is not null)
        {
            if (obj is T match) return match;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
