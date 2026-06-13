namespace UmaPlayer.Models;

/// <summary>
/// v2 队列持久化快照(Phase 6)。包含所有命名歌单 + "正在播放"指针。
/// 历史: v1 仅承载单一队列, 由 JsonPlaylistService.LoadAsync 一次性迁移到 v2。
/// SchemaVersion 永远写 2; ViewedPlaylistId 不持久化。
/// </summary>
public sealed record QueueState
{
    public int SchemaVersion { get; init; } = 2;
    public IReadOnlyList<Playlist> Playlists { get; init; } = Array.Empty<Playlist>();
    public string CurrentPlaylistId { get; init; } = string.Empty;
}
