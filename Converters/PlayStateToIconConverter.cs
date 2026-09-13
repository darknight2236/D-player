using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DPlayer.Models;

namespace DPlayer.Converters;

/// <summary>
/// 将 PlayState 转换为播放按钮图标 Geometry（来自 Themes/Icons.xaml）：
///   - Playing → Icon.Pause（暂停图标，提示点击可暂停）
///   - 其他    → Icon.Play （播放图标）
/// 仅单向转换，ConvertBack 不被调用。
/// </summary>
[ValueConversion(typeof(PlayState), typeof(Geometry))]
public sealed class PlayStateToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is PlayState.Playing ? "Icon.Pause" : "Icon.Play";
        return Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
