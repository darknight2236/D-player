using System.Globalization;
using System.Windows.Data;
using UmaPlayer.Models;

namespace UmaPlayer.Converters;

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
