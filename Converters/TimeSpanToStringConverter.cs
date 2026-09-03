using System.Globalization;
using System.Windows.Data;

namespace DPlayer.Converters;

/// <summary>
/// 将 TimeSpan 格式化为播放时间显示：
///   - &gt;=1 小时：h:mm:ss   (如 "1:02:03")
///   - &lt; 1 小时：m:ss      (如 "3:45")
/// 默认/异常值显示 "0:00"。
/// </summary>
[ValueConversion(typeof(TimeSpan), typeof(string))]
public sealed class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        return "0:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
