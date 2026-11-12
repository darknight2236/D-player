using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using MediaTrack = DPlayer.Models.Track;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;
using DPlayer.Views.Dialogs;

namespace DPlayer.Views.Controls;

internal sealed record LibrarySearchRow(string Group, object Item);

/// <summary>
/// Phase 24 音乐库主面板: 艺术家列表 / 专辑网格 / 艺术家详情 / 专辑详情 + 空态与状态行。
/// 结构由 LibraryViewModel.Page 驱动，本类只做手势（双击 / Enter）与封面惰性加载的转接。
/// </summary>
public partial class LibraryView : UserControl
{
    private readonly ObservableCollection<LibrarySearchRow> _searchRows = new();
    private LibraryViewModel? _vm;
    private MainViewModel? _shell;
    private PlayerViewModel? _player;
    private PlaylistsViewModel? _playlists;
    private bool _trackGeneratorHooked;
    private bool _isComposing;

    public ICollectionView SearchRowsView { get; }

    public LibraryView()
    {
        SearchRowsView = CollectionViewSource.GetDefaultView(_searchRows);
        SearchRowsView.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(LibrarySearchRow.Group)));

        InitializeComponent();

        TextCompositionManager.AddPreviewTextInputStartHandler(
            SearchBox, SearchBox_CompositionStart);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(
            SearchBox, SearchBox_CompositionUpdate);
        TextCompositionManager.AddPreviewTextInputHandler(
            SearchBox, SearchBox_CompositionCommit);
        SearchBox.LostKeyboardFocus += (_, _) => _isComposing = false;

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

        RebuildSearchRows();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachShell();
        if (!_trackGeneratorHooked)
        {
            AlbumTracksList.ItemContainerGenerator.StatusChanged += OnTrackContainersStatusChanged;
            SearchResultsList.ItemContainerGenerator.StatusChanged += OnTrackContainersStatusChanged;
            _trackGeneratorHooked = true;
        }
        RebuildSearchRows();
        RefreshTrackIndicator();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachShell();
        if (_trackGeneratorHooked)
        {
            AlbumTracksList.ItemContainerGenerator.StatusChanged -= OnTrackContainersStatusChanged;
            SearchResultsList.ItemContainerGenerator.StatusChanged -= OnTrackContainersStatusChanged;
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
        if (AlbumTracksList.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated ||
            SearchResultsList.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
        {
            RefreshTrackIndicator();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedAlbum) &&
            _vm?.SelectedAlbum is { } album)
        {
            _ = _vm.EnsureCoverAsync(album);
        }

        if (e.PropertyName == nameof(LibraryViewModel.SearchResults))
            RebuildSearchRows();

        if (e.PropertyName is nameof(LibraryViewModel.SelectedAlbum)
            or nameof(LibraryViewModel.SelectedArtist)
            or nameof(LibraryViewModel.SearchResults)
            or nameof(LibraryViewModel.Page))
        {
            Dispatcher.BeginInvoke(new Action(RefreshTrackIndicator), DispatcherPriority.Background);
        }
    }

    private void RebuildSearchRows()
    {
        _searchRows.Clear();
        if (_vm is null) return;

        foreach (var artist in _vm.SearchResults.Artists)
            _searchRows.Add(new LibrarySearchRow("艺术家", artist));
        foreach (var album in _vm.SearchResults.Albums)
            _searchRows.Add(new LibrarySearchRow("专辑", album));
        foreach (var track in _vm.SearchResults.Tracks)
            _searchRows.Add(new LibrarySearchRow("曲目", track));

        Dispatcher.BeginInvoke(new Action(RefreshTrackIndicator), DispatcherPriority.Background);
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

            var marker = VisualTreeHelpers.FindChildByName<Path>(container, "PART_Marker");
            if (marker is null) continue;

            var isCurrent = isLibraryActive
                && currentPath is not null
                && AlbumTracksList.Items[i] is MediaTrack track
                && string.Equals(track.FilePath, currentPath, StringComparison.OrdinalIgnoreCase);
            marker.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
        }

        SearchResultsList.UpdateLayout();
        foreach (var row in _searchRows)
        {
            if (row.Item is not MediaTrack track) continue;
            if (SearchResultsList.ItemContainerGenerator.ContainerFromItem(row)
                is not ListBoxItem container)
                continue;

            var marker = VisualTreeHelpers.FindChildByName<Path>(container, "PART_Marker");
            if (marker is null) continue;

            marker.Visibility = isLibraryActive && currentPath is not null &&
                string.Equals(track.FilePath, currentPath, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    /// <summary>点击列表空白处（不是项、也不是滚动条）时清除选中高亮。</summary>
    private void LibraryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;

        var source = e.OriginalSource as DependencyObject;
        if (VisualTreeHelpers.FindAncestor<ListBoxItem>(source) is not null) return;
        if (VisualTreeHelpers.FindAncestor<ScrollBar>(source) is not null) return;

        list.SelectedItem = null;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _vm?.Back();

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        _isComposing = false;
        SearchBox.Clear();
        _vm?.ClearSearch();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_vm is null || _isComposing) return;
        _ = SubmitSearchAsync();
    }

    private async Task SubmitSearchAsync()
    {
        if (_vm is null) return;
        var text = SearchBox.Text;
        if (string.Equals(text, _vm.SearchQuery, StringComparison.Ordinal)) return;
        try
        {
            await _vm.UpdateSearchAsync(text);
        }
        catch
        {
            // 搜索失败不应打断输入；结果保持上一份。
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _isComposing) return;
        _vm?.ClearSearch();
        e.Handled = true;
    }

    private void SearchBox_CompositionStart(object sender, TextCompositionEventArgs e)
        => _isComposing = true;

    private void SearchBox_CompositionUpdate(object sender, TextCompositionEventArgs e)
        => _isComposing = true;

    private void SearchBox_CompositionCommit(object sender, TextCompositionEventArgs e)
    {
        if (!_isComposing) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _isComposing = false;
            _ = SubmitSearchAsync();
        }), DispatcherPriority.Input);
    }

    private void SearchResults_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = VisualTreeHelpers.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not LibrarySearchRow row) return;

        switch (row.Item)
        {
            case LibraryArtist artist:
                _vm?.OpenSearchArtist(artist);
                e.Handled = true;
                break;
            case LibraryAlbum album:
                _vm?.OpenSearchAlbum(album);
                e.Handled = true;
                break;
        }
    }

    private async void SearchResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = VisualTreeHelpers.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is LibrarySearchRow { Item: MediaTrack track } &&
            _vm is not null)
        {
            await _vm.PlaySearchTrackAsync(track);
        }
    }

    private async void SearchResults_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (SearchResultsList.SelectedItem is not LibrarySearchRow row) return;

        switch (row.Item)
        {
            case LibraryArtist artist:
                _vm?.OpenSearchArtist(artist);
                e.Handled = true;
                break;
            case LibraryAlbum album:
                _vm?.OpenSearchAlbum(album);
                e.Handled = true;
                break;
            case MediaTrack track when _vm is not null:
                await _vm.PlaySearchTrackAsync(track);
                e.Handled = true;
                break;
        }
    }

    private void SearchAlbum_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryAlbum album } &&
            _vm is not null)
        {
            _ = _vm.EnsureCoverAsync(album);
        }
    }

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
        var item = VisualTreeHelpers.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
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

    private void AddAlbumToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var album = (sender as FrameworkElement)?.DataContext as LibraryAlbum
            ?? _vm?.SelectedAlbum;
        if (album is null) return;
        ShowAddToPlaylistDialog(album.Tracks);
    }

    private void AddTrackToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MediaTrack track) return;
        ShowAddToPlaylistDialog(new[] { track });
    }

    private void ShowAddToPlaylistDialog(IReadOnlyList<MediaTrack> tracks)
    {
        if (_vm is null || tracks.Count == 0) return;

        var snapshot = tracks.ToArray();
        var result = AddToPlaylistDialog.Show(
            Window.GetWindow(this), _vm.Playlists, snapshot.Length);
        if (result is null) return;

        if (result.NewPlaylistName is { } name)
            _vm.AddTracksToNewPlaylist(name, snapshot);
        else if (result.Target is { } target)
            _vm.AddTracksToPlaylist(target, snapshot);
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
        var item = VisualTreeHelpers.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        return item?.DataContext as T;
    }
}
