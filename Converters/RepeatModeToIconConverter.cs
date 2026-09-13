using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DPlayer.Models;

namespace DPlayer.Converters;

/// <summary>
/// 将 RepeatMode 转换为循环按钮图标 Geometry（来自 Themes/Icons.xaml）：
///   Off  → Icon.RepeatOff
///   List → Icon.RepeatList
///   One  → Icon.RepeatOne
/// </summary>
[ValueConversion(typeof(RepeatMode), typeof(Geometry))]
public sealed class RepeatModeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            RepeatMode.List => "Icon.RepeatList",
            RepeatMode.One  => "Icon.RepeatOne",
            _               => "Icon.RepeatOff",
        };
        return Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
