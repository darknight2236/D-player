namespace DPlayer.Models;

/// <summary>
/// 文件夹绑定歌单的元数据缓存条目(Phase 10)。
/// 不含 AlbumArt 字节——按需加载, 保持缓存文件体积小。
/// 用于 library-cache.json 持久化。
/// </summary>
public sealed record LibraryCacheEntry(
    string FilePath,
    string Title,
    string? Artist,
    string? Album,
    string? Genre,
    int? Year,
    TimeSpan Duration,
    int? SampleRate,
    int? TrackNumber);
