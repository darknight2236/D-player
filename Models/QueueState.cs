namespace UmaPlayer.Models;

/// <summary>
/// 队列持久化的不可变快照（Phase 4）。仅包含路径与队列态——不携带 Track 元数据或封面。
///
/// 启动时 PlaylistViewModel 同步读盘 → 把 Items 填到 ObservableCollection&lt;Track&gt;
/// 时为每条创建占位 Track（与 OpenAndPlay 的"占位 → 完整元数据"流程相同）。
///
/// 使用 record 不可变 + with 表达式风格，与 AppSettings 一致。
/// </summary>
public sealed record QueueState
{
    /// <summary>Schema 版本号；加载时不匹配则 fallback 空队列。当前版本：1。</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>队列中每首曲的文件路径，按队列顺序排列。</summary>
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();

    /// <summary>当前播放/选中索引；空队列或未选时为 -1。</summary>
    public int CurrentIndex { get; init; } = -1;

    /// <summary>是否启用 Shuffle 模式。</summary>
    public bool ShuffleEnabled { get; init; }

    /// <summary>循环模式（Off / List / One）。</summary>
    public RepeatMode RepeatMode { get; init; } = RepeatMode.Off;
}
