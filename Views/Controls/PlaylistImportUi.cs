using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using DPlayer.ViewModels;
using DPlayer.Views.Dialogs;

namespace DPlayer.Views.Controls;

/// <summary>
/// 播放列表导入的 View 层共用执行器（Phase 18）。
///
/// 四个入口（侧边栏按钮 / 侧边栏拖拽 / 工具栏按钮 / 列表区拖拽）共用同一套
/// "跑导入 → 聚合报告 → 弹信息框 → 兜异常" 流程。异常必须在这里兜住：
/// 调用方是 async void 事件处理器，未观察异常会直接崩进程。
/// </summary>
public static class PlaylistImportUi
{
    /// <summary>对话框入口：importOne 传 null 表示让 VM 自己弹文件对话框。</summary>
    public static async Task RunDialogAsync(
        Func<string?, Task<PlaylistImportReport?>> importOne, Window? owner)
    {
        try
        {
            var report = await importOne(null).ConfigureAwait(true);
            if (report is null) return;                 // 用户取消 → 不弹任何框

            ConfirmDialog.ShowInfo(owner, PlaylistImportReportFormatter.DialogTitle,
                PlaylistImportReportFormatter.Format(new[] { report }));
        }
        catch (Exception ex)
        {
            ConfirmDialog.ShowError(owner, PlaylistImportReportFormatter.DialogTitle, $"导入失败：{ex.Message}");
        }
    }

    /// <summary>拖拽入口：逐个导入（每个文件一个歌单/一次追加），聚合成一份报告。</summary>
    public static async Task RunForDroppedFilesAsync(
        Func<string?, Task<PlaylistImportReport?>> importOne, Window? owner, IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return;

        try
        {
            var reports = new List<PlaylistImportReport>(paths.Count);
            foreach (var path in paths)
            {
                var report = await importOne(path).ConfigureAwait(true);
                if (report is not null) reports.Add(report);
            }
            if (reports.Count == 0) return;

            ConfirmDialog.ShowInfo(owner, PlaylistImportReportFormatter.DialogTitle,
                PlaylistImportReportFormatter.Format(reports));
        }
        catch (Exception ex)
        {
            ConfirmDialog.ShowError(owner, PlaylistImportReportFormatter.DialogTitle, $"导入失败：{ex.Message}");
        }
    }
}
