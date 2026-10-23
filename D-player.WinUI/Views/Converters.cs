using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DPlayer.WinUI.Views;

/// <summary>
/// bool -> Visibility 的单向转换：左栏条目的「正在播放」▶ 标记按
/// <c>PlaylistViewModel.IsActivePlaylist</c> 显隐（NavRail.xaml 的 RailItemTemplate）。
/// 只读展示，ConvertBack 回写没有语义，直接抛。
/// </summary>
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}
