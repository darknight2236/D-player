using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UmaPlayer.Converters;

/// <summary>
/// 将 bool 映射为画刷资源：
///   true  → AccentPrimary（强调色，提示「已激活」）
///   false → ForegroundSecondary（次要色，提示「未激活」）
/// 用于 Shuffle 按钮的 on/off 颜色切换；循环按钮也可复用（RepeatMode != Off → true）。
/// </summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public sealed class BoolToAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isActive = value is bool b && b;
        var key = isActive ? "AccentHover" : "ForegroundPrimary";
        return (Brush)Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
