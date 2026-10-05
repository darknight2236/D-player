using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DPlayer.Models;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// IPlaylistFileService 默认实现（Phase 18）：编排 编码探测 → 解析 → 归一化 → 过滤计数。
/// 无状态，注册为 Singleton。
/// </summary>
public sealed class PlaylistFileService : IPlaylistFileService
{
    private static readonly string[] UrlPrefixes = { "http://", "https://", "mms://", "rtsp://" };

    public async Task<PlaylistImportResult> ImportAsync(string playlistFilePath)
    {
        var suggested = SuggestName(playlistFilePath);

        try
        {
            if (string.IsNullOrWhiteSpace(playlistFilePath) || !File.Exists(playlistFilePath))
                return Empty(suggested);

            var bytes = await File.ReadAllBytesAsync(playlistFilePath).ConfigureAwait(false);
            var text = PlaylistFileEncoding.Decode(bytes);

            var raw = Path.GetExtension(playlistFilePath).Equals(".pls", StringComparison.OrdinalIgnoreCase)
                ? PlsParser.Parse(text)
                : M3uParser.Parse(text);

            var baseDir = Path.GetDirectoryName(Path.GetFullPath(playlistFilePath)) ?? string.Empty;
            return Classify(raw, baseDir, suggested);
        }
        // 读侧绝不抛（设计稿 §4）：文件被占用/无权限/路径含非法字符都退化为空结果，
        // 由 View 的"没有可导入的条目"报告兜住，用户仍能看到反馈而不是崩溃。
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                            or NotSupportedException or ArgumentException)
        {
            return Empty(suggested);
        }
    }

    public async Task ExportAsync(string destPath, IReadOnlyList<Track> tracks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);
        ArgumentNullException.ThrowIfNull(tracks);

        var text = M3u8Writer.Write(tracks);

        // 写侧必须让异常冒到 VM（设计稿 §4）：VM 捕获后转成错误文案，
        // View 用 ConfirmDialog.ShowError 弹出。不要在这里 try/catch 吞掉。
        await File.WriteAllTextAsync(destPath, text, M3u8Writer.Encoding).ConfigureAwait(false);
    }

    private static PlaylistImportResult Classify(IReadOnlyList<string> rawEntries, string baseDir, string suggested)
    {
        var accepted = new List<string>(rawEntries.Count);
        int missing = 0, unsupported = 0;

        foreach (var entry in rawEntries)
        {
            if (IsUrl(entry)) { unsupported++; continue; }

            string full;
            try
            {
                full = Path.IsPathRooted(entry)
                    ? Path.GetFullPath(entry)
                    : Path.GetFullPath(Path.Combine(baseDir, entry));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                unsupported++; continue;      // 非法字符 / 无效路径形态
            }

            var ext = Path.GetExtension(full);
            if (!AudioConstants.AudioExtensions.Any(a => string.Equals(a, ext, StringComparison.OrdinalIgnoreCase)))
            {
                unsupported++; continue;      // 后缀判定先于存在性判定
            }

            if (!File.Exists(full)) { missing++; continue; }

            accepted.Add(full);
        }

        return new PlaylistImportResult(suggested, accepted, rawEntries.Count, missing, unsupported);
    }

    private static bool IsUrl(string entry)
    {
        foreach (var prefix in UrlPrefixes)
        {
            if (entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string SuggestName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "导入的歌单";
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? "导入的歌单" : name;
    }

    private static PlaylistImportResult Empty(string suggested) =>
        new(suggested, Array.Empty<string>(), 0, 0, 0);
}
