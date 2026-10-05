using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DPlayer.Models;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// extended M3U8 写出（Phase 18）：UTF-8 无 BOM、CRLF 行尾、绝对路径。
///
/// 格式：
///   #EXTM3U
///   #EXTINF:{秒},{Artist - Title}
///   {绝对路径}
/// 秒 = 四舍五入的 Duration.TotalSeconds；未知（&lt;= 0）写 -1。
/// #EXTINF 的显示名里出现逗号无需转义——该字段只按第一个逗号切分。
/// </summary>
internal static class M3u8Writer
{
    public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string Write(IReadOnlyList<Track> tracks)
    {
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\r\n");

        if (tracks is null) return sb.ToString();

        foreach (var track in tracks)
        {
            var seconds = track.Duration > TimeSpan.Zero
                ? (int)Math.Round(track.Duration.TotalSeconds)
                : -1;

            sb.Append("#EXTINF:").Append(seconds).Append(',').Append(DisplayName(track)).Append("\r\n");
            sb.Append(track.FilePath).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string DisplayName(Track track)
    {
        var title = string.IsNullOrWhiteSpace(track.Title)
            ? Path.GetFileName(track.FilePath)
            : track.Title;

        return string.IsNullOrWhiteSpace(track.Artist)
            ? title
            : track.Artist + " - " + title;
    }
}
