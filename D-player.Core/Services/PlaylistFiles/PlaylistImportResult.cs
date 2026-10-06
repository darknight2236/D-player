using System.Collections.Generic;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// 一次导入的结构化结果（Phase 18）。
///
/// AcceptedPaths 已完成：相对路径归一化为绝对路径 + 后缀白名单过滤 + File.Exists 校验。
/// 三个计数互斥（一个条目只进一个桶），供 View 层拼"跳过 N 条"文案。
/// </summary>
/// <param name="SuggestedName">建议歌单名 = 列表文件名去后缀；无法取名时为 "导入的歌单"。</param>
/// <param name="AcceptedPaths">可用条目的绝对路径（保留原顺序与重复）。</param>
/// <param name="TotalEntries">列表文件里解析出的原始条目数。</param>
/// <param name="SkippedMissing">路径解析成功但文件不存在。</param>
/// <param name="SkippedUnsupported">网络流条目、后缀不在 AudioConstants 白名单、或路径本身非法。</param>
public sealed record PlaylistImportResult(
    string SuggestedName,
    IReadOnlyList<string> AcceptedPaths,
    int TotalEntries,
    int SkippedMissing,
    int SkippedUnsupported);
