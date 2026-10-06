namespace DPlayer.ViewModels;

/// <summary>
/// 一次播放列表导入的结构化报告（Phase 18）。
///
/// 分层纪律：VM 只返回数据，中文文案由 View 层 PlaylistImportReportFormatter 组装。
/// 不要在 VM 里拼展示字符串——两个入口（侧边栏新建 / 工具栏追加）与拖拽聚合报告
/// 共用同一份文案规则，放 View 层才能 DRY。
/// </summary>
/// <param name="SourceFile">列表文件名（含后缀），用于文案里的「来源」。</param>
/// <param name="PlaylistName">新建时=新歌单名；追加时=目标歌单名；一条都没导入时=null。</param>
/// <param name="CreatedNewPlaylist">true=容器级新建歌单；false=追加到既有歌单。</param>
/// <param name="Imported">入列的曲目数：追加路径 = 通过服务层过滤的条目数，新建路径 = 元数据批量读取返回的曲目数。</param>
public sealed record PlaylistImportReport(
    string SourceFile,
    string? PlaylistName,
    bool CreatedNewPlaylist,
    int Imported,
    int SkippedMissing,
    int SkippedUnsupported,
    int TotalEntries)
{
    public int Skipped => SkippedMissing + SkippedUnsupported;

    public bool AnyImported => Imported > 0;
}
