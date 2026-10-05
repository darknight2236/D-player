using System;
using System.Text;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// 播放列表文件的字节 → 文本解码（Phase 18）。
///
/// 探测顺序：BOM（UTF-8 / UTF-16LE / UTF-16BE）→ 无 BOM 时用"非法字节即抛"的严格 UTF-8
/// 试解码 → 失败回退 GBK(936)。传统 .m3u 在中文 Windows 上多为 ANSI/GBK，
/// 只按 UTF-8 读会让整表中文路径变成 U+FFFD，进而被"文件缺失"过滤掉。
///
/// GBK 依赖 CodePagesEncodingProvider：在静态构造函数里注册，保证早于本类任何解码调用，
/// 不依赖 App 启动顺序（RegisterProvider 幂等）。
/// </summary>
internal static class PlaylistFileEncoding
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static PlaylistFileEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string Decode(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }
}
