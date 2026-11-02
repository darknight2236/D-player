using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DPlayer.Converters;

/// <summary>
/// 反向布尔→可见性：true → Collapsed，false → Visible。
/// 用于主面板的队列视图（IsLibraryViewActive 为 true 时让位给库视图）。
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(
        object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(
        object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
