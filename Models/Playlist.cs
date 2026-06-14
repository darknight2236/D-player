namespace UmaPlayer.Models;

/// <summary>
/// 一个命名歌单(Phase 6)。Id 是创建时生成的 GUID, 主键, 不可变;
/// Name 仅展示, 可重复可重命名。ShuffleEnabled / RepeatMode / CurrentIndex
/// 下沉到歌单级别 —— 各歌单独立, 不再共享顶层状态。
/// </summary>
public sealed record Playlist(
    string Id,
    string Name,
    IReadOnlyList<string> Items,
    int CurrentIndex,
    bool ShuffleEnabled,
    RepeatMode RepeatMode,
    string? SourceFolder = null);
