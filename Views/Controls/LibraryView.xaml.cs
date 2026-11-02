using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    public LibraryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = e.NewValue as LibraryViewModel;

        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.SelectedAlbum) &&
            _vm?.SelectedAlbum is { } album)
        {
            _ = _vm.EnsureCoverAsync(album);
        }
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
            _ = _vm?.RefreshAsync();
    }

    private static T? FindItemDataContext<T>(MouseButtonEventArgs e) where T : class
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        return item?.DataContext as T;
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
