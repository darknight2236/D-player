using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DPlayer.Converters;

/// <summary>
/// 将 IsMuted 转换为音量图标 Geometry：true→Icon.VolumeMuted，false→Icon.Volume。
/// （替代原 PlayerViewModel.VolumeIcon emoji 字符串，保持 VM 无 WPF/emoji 类型。）
/// </summary>
[ValueConversion(typeof(bool), typeof(Geometry))]
public sealed class BoolToVolumeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is true ? "Icon.VolumeMuted" : "Icon.Volume";
        return Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
