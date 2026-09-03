using System.Globalization;
using System.Windows.Data;
using DPlayer.Models;

namespace DPlayer.Converters;

/// <summary>
/// 将 RepeatMode 转换为循环按钮图标：
///   Off  → ⇄  (不循环)
///   List → 🔁 (列表循环)
///   One  → 🔂 (单曲循环)
/// </summary>
[ValueConversion(typeof(RepeatMode), typeof(string))]
public sealed class RepeatModeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            RepeatMode.List => "\U0001F501", // 🔁
            RepeatMode.One  => "\U0001F502", // 🔂
            _               => "⇄",     // ⇄
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
