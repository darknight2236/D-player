namespace UmaPlayer.Models;

/// <summary>
/// v3 队列持久化快照(Phase 10)。包含所有命名歌单 + "正在播放"指针。
/// 历史: v1 仅承载单一队列, 由 JsonPlaylistService.LoadAsync 一次性迁移到 v2;
/// v2→v3 新增 Playlist.SourceFolder, 无迁移(缺失字段反序列化为 null)。
/// SchemaVersion 永远写 3; ViewedPlaylistId 不持久化。
/// </summary>
public sealed record QueueState
{
    public int SchemaVersion { get; init; } = 3;
    public IReadOnlyList<Playlist> Playlists { get; init; } = Array.Empty<Playlist>();
    public string CurrentPlaylistId { get; init; } = string.Empty;
}
