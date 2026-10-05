using System;
using System.Collections.Generic;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// PLS（INI 风格）解析（Phase 18）：只取 [playlist] 段内 File&lt;N&gt;= 的值。
///
/// 按**出现顺序**返回而非按序号排序 —— 现实中不少工具序号不连续或乱序。
/// Title&lt;N&gt; / Length&lt;N&gt; / NumberOfEntries / Version 一律忽略（同 M3uParser 的理由）。
/// </summary>
internal static class PlsParser
{
    public static IReadOnlyList<string> Parse(string text)
    {
        var entries = new List<string>();
        if (string.IsNullOrEmpty(text)) return entries;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line[0] == '[') continue;                     // 段头 [playlist]
            if (line[0] == '#' || line[0] == ';') continue;   // INI 注释

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;                            // 无 '=' 或以 '=' 开头

            var key = line.Substring(0, eq).Trim();
            if (!IsFileKey(key)) continue;

            var value = line.Substring(eq + 1).Trim();
            if (value.Length == 0) continue;

            entries.Add(value);
        }
        return entries;
    }

    /// <summary>key 形如 File + 至少一位数字（大小写不敏感）。</summary>
    private static bool IsFileKey(string key)
    {
        if (key.Length < 5) return false;                     // 最短合法 key 是 "File1"
        if (!key.StartsWith("File", StringComparison.OrdinalIgnoreCase)) return false;

        for (int i = 4; i < key.Length; i++)
        {
            if (!char.IsDigit(key[i])) return false;
        }
        return true;
    }
}
