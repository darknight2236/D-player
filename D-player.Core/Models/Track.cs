namespace DPlayer.Models;

/// <summary>
/// 表示一首音轨的所有元数据 —— 不可变 record，便于跨线程安全传递。
/// </summary>
/// <param name="FilePath">音频文件的绝对路径，唯一标识。</param>
/// <param name="Title">歌曲标题；若标签缺失则回落到文件名。</param>
/// <param name="Artist">艺术家（可空）。</param>
/// <param name="Album">专辑名（可空）。</param>
/// <param name="Genre">流派（可空）。</param>
/// <param name="Year">发行年份（可空，&lt;=0 视为无效）。</param>
/// <param name="SampleRate">采样率 Hz（可空）；加载完成后由播放服务回填。</param>
/// <param name="AlbumArt">内嵌封面原始字节；VM 中会转 BitmapImage 并缩放到 200px。</param>
/// <param name="Duration">总时长；构造时通常为 Zero，加载完成后由播放服务回填。</param>
public sealed record Track(
    string FilePath,
    string Title,
    string? Artist,
    string? Album,
    string? Genre,
    int? Year,
    int? SampleRate,
    byte[]? AlbumArt,
    TimeSpan Duration,
    int? TrackNumber);
