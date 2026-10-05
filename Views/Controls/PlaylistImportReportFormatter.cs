using System.Collections.Generic;
using System.Text;
using DPlayer.ViewModels;

namespace DPlayer.Views.Controls;

/// <summary>
/// 把 VM 返回的结构化导入报告拼成中文提示文案（Phase 18）。
///
/// 放 View 层的理由：侧边栏按钮、侧边栏拖拽、歌单工具栏按钮、列表区拖拽四个入口
/// 共用同一套文案规则；VM 只提供数据（分层纪律：VM 不拼展示字符串）。
/// 纯字符串函数，无 WPF 依赖，因此可单测。
/// </summary>
public static class PlaylistImportReportFormatter
{
    /// <summary>对话框标题（所有入口统一）。</summary>
    public const string DialogTitle = "导入播放列表";

    public static string Format(IReadOnlyList<PlaylistImportReport> reports)
    {
        if (reports is null || reports.Count == 0) return string.Empty;
        return reports.Count == 1 ? FormatSingle(reports[0]) : FormatMany(reports);
    }

    private static string FormatSingle(PlaylistImportReport r)
    {
        if (!r.AnyImported)
        {
            return r.Skipped == 0
                ? $"「{r.SourceFile}」没有可导入的条目"
                : $"「{r.SourceFile}」没有可导入的条目\n{SkippedLine(r)}";
        }

        var target = r.CreatedNewPlaylist ? $"新歌单「{r.PlaylistName}」" : $"歌单「{r.PlaylistName}」";
        var second = r.Skipped == 0 ? $"{r.Imported} 首入列" : $"{r.Imported} 首入列，{SkippedLine(r)}";

        return $"已导入「{r.SourceFile}」→ {target}\n{second}";
    }

    private static string FormatMany(IReadOnlyList<PlaylistImportReport> reports)
    {
        var sb = new StringBuilder();
        sb.Append("已导入 ").Append(reports.Count).Append(" 个播放列表");

        foreach (var r in reports)
        {
            sb.Append('\n').Append("· ").Append(r.SourceFile).Append(" → ");

            if (!r.AnyImported)
            {
                sb.Append("没有可导入的条目");
            }
            else
            {
                sb.Append(r.CreatedNewPlaylist ? "新歌单「" : "歌单「")
                  .Append(r.PlaylistName).Append("」：").Append(r.Imported).Append(" 首");
            }

            if (r.Skipped > 0) sb.Append("（跳过 ").Append(r.Skipped).Append("）");
        }

        return sb.ToString();
    }

    /// <summary>"跳过 N 条（文件缺失 X / 格式不支持 Y）"——调用方决定前面接换行还是逗号。</summary>
    private static string SkippedLine(PlaylistImportReport r) =>
        $"跳过 {r.Skipped} 条（文件缺失 {r.SkippedMissing} / 格式不支持 {r.SkippedUnsupported}）";
}
