using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace UmaPlayer.Converters;

/// <summary>
/// MultiBinding 转换器：从 ListBox + 当前项 计算 1-based 行号。
/// 用于曲目列表的 # 列。
/// </summary>
[ValueConversion(typeof(object), typeof(int))]
public sealed class TrackIndexConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 0;
        if (values[0] is not ListBox listBox) return 0;
        if (values[1] is null) return 0;

        var index = listBox.Items.IndexOf(values[1]);
        return index >= 0 ? index + 1 : 0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
