using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DPlayer.ViewModels;

namespace DPlayer.Views.Dialogs;

public sealed record AddToPlaylistResult(
    PlaylistViewModel? Target, string? NewPlaylistName);

/// <summary>
/// Phase 26 库 → 歌单选择对话框。只返回选择结果，不直接修改歌单。
/// </summary>
public partial class AddToPlaylistDialog : Window
{
    public AddToPlaylistResult? Result { get; private set; }

    public AddToPlaylistDialog(
        IReadOnlyList<PlaylistViewModel> playlists, int trackCount)
    {
        InitializeComponent();

        SubtitleText.Text = $"将加入 {trackCount} 首曲目";
        NewNameBox.Text = "新歌单";
        PlaylistList.ItemsSource = playlists;

        var firstSelectable = playlists.FirstOrDefault(p => !p.HasSourceFolder);
        PlaylistList.SelectedItem = firstSelectable;
        JoinButton.IsEnabled = firstSelectable is not null;
        EmptyText.Visibility =
            playlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            if (firstSelectable is not null)
            {
                PlaylistList.Focus();
                PlaylistList.ScrollIntoView(firstSelectable);
            }
            else
            {
                NewNameBox.Focus();
                NewNameBox.SelectAll();
            }
        };
    }

    public static AddToPlaylistResult? Show(
        Window? owner, IReadOnlyList<PlaylistViewModel> playlists, int trackCount)
    {
        var dlg = new AddToPlaylistDialog(playlists, trackCount) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    private void PlaylistList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
        => JoinButton.IsEnabled =
            PlaylistList.SelectedItem is PlaylistViewModel { HasSourceFolder: false };

    private void PlaylistList_MouseDoubleClick(
        object sender, MouseButtonEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistViewModel { HasSourceFolder: false })
            AcceptSelected();
    }

    private void PlaylistList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (PlaylistList.SelectedItem is not PlaylistViewModel { HasSourceFolder: false })
            return;
        AcceptSelected();
        e.Handled = true;
    }

    private void Join_Click(object sender, RoutedEventArgs e) => AcceptSelected();

    private void AcceptSelected()
    {
        if (PlaylistList.SelectedItem is not PlaylistViewModel { HasSourceFolder: false } target)
            return;
        Result = new AddToPlaylistResult(target, null);
        DialogResult = true;
    }

    private void Create_Click(object sender, RoutedEventArgs e) => AcceptNew();

    private void NewNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AcceptNew();
        e.Handled = true;
    }

    private void AcceptNew()
    {
        var name = string.IsNullOrWhiteSpace(NewNameBox.Text)
            ? "新歌单"
            : NewNameBox.Text.Trim();
        Result = new AddToPlaylistResult(null, name);
        DialogResult = true;
    }
}
