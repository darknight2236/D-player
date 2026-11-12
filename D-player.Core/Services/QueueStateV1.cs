using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// queue.json 的 v1 快照(仅迁移时反序列化, Phase 6/29)。
/// 顶层 internal 类型: System.Text.Json 源生成器无法为私有嵌套类型生成元数据。
/// </summary>
internal sealed record QueueStateV1
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<string>? Items { get; init; }
    public int CurrentIndex { get; init; } = -1;
    public bool ShuffleEnabled { get; init; }
    public RepeatMode RepeatMode { get; init; } = RepeatMode.Off;
}
