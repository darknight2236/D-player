using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace DPlayer.WinUI.Views;

/// <summary>
/// Core 给的封面是**原始字节**（<c>Track.AlbumArt</c> → <c>PlayerViewModel.AlbumArtBytes</c>；
/// brief 里写的 <c>Track.AlbumArtBytes</c> 这个名字在 Core 里不存在），
/// 这里在壳内把它解码成 <see cref="BitmapImage"/>。
///
/// 按目标尺寸解码是承重的：<c>BitmapImage</c> 不设解码边长时按**原图分辨率**解码，
/// 内嵌封面常见 1200x1200 甚至更大，切歌时每帧都解一遍就是 spec §8 列的"封面大图导致切歌卡顿"。
/// 传进来的 decodeSize 由调用方从 Tokens 的 <c>InfoPanelCoverSize</c> 读，与显示边长同源。
/// </summary>
internal static class CoverArtLoader
{
    public static async Task<ImageSource?> LoadAsync(byte[]? bytes, int decodeSize)
    {
        if (bytes is null || bytes.Length == 0 || decodeSize <= 0) return null;

        using var stream = new InMemoryRandomAccessStream();

        // brief 给了两条写法，这里落的是第一条 `AsBuffer()`（System.Runtime.InteropServices.WindowsRuntime
        // 的 WindowsRuntimeBufferExtensions，本机 net10.0-windows10.0.19041 + WASDK 2.5.1 编译通过，
        // 所以没退回 DataWriter 那条）。实测记录见 task-2-report.md §1。
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);

        var image = new BitmapImage();
        // 只设 DecodePixelWidth、**不**同时设 DecodePixelHeight：两个都设时解码器按那个尺寸**拉伸**成
        // 目标矩形（WPF/UWP 同语义），非正方形封面会被压扁；而 XAML 侧的 Stretch=UniformToFill 本来就是
        // 等比放大后裁切，只需要宽度有界即可。（"两个都设会变形"这条是库语义判断，本轮没在真机上取到
        //  非正方形封面复现，报告 §7 标了 NOT VERIFIED。）
        image.DecodePixelWidth = decodeSize;
        await image.SetSourceAsync(stream);
        return image;
    }
}
