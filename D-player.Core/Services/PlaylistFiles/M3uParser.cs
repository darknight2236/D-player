using System.Collections.Generic;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// M3U / M3U8 解析（Phase 18）：逐行取"非 # 开头的非空行"。
///
/// #EXTM3U / #EXTINF / 用户注释一律忽略 —— 设计稿 §5.1：标题与时长只信
/// ATL 从音频文件读到的结果，不用列表文件里的字符串覆盖。
/// </summary>
internal static class M3uParser
{
    public static IReadOnlyList<string> Parse(string text)
    {
        var entries = new List<string>();
        if (string.IsNullOrEmpty(text)) return entries;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line[0] == '#') continue;
            entries.Add(line);
        }
        return entries;
    }
}
