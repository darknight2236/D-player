using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace DPlayer.Converters;

/// <summary>
/// Phase 6 偿债 #1(部分): byte[] → 已 Freeze 的 BitmapImage。
/// CacheOption.OnLoad 让 BitmapImage 在 EndInit 前完全读完字节流(否则 BitmapImage 会持有
/// MemoryStream 直到第一次绘制 — 跨线程或源被释放时崩)。Freeze 让结果可以跨线程绑定(WPF UI),
/// 也免掉 INotifyPropertyChanged 的开销。
/// 输入为 null / 空 byte[] / 解码失败 → 返回 null(WPF Image 控件会显示为空)。
/// 仅 OneWay; ConvertBack 抛 NotSupportedException。
/// </summary>
public sealed class BytesToBitmapImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0)
            return null;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("BytesToBitmapImageConverter is OneWay.");
}
