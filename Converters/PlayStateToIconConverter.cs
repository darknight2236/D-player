using System.Globalization;
using System.Windows.Data;
using UmaPlayer.Models;

namespace UmaPlayer.Converters;

/// <summary>
/// 将 PlayState 转换为播放按钮图标：
///   - Playing → ⏸ (暂停图标，提示点击可暂停)
///   - 其他    → ▶ (播放图标)
/// 仅单向转换，ConvertBack 不被调用。
/// </summary>
[ValueConversion(typeof(PlayState), typeof(string))]
public sealed class PlayStateToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is PlayState.Playing ? "⏸" : "▶";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
