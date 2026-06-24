using System.IO;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 基于 z440.atl.core 的元数据读取实现。
///
/// 责任搬迁：从 MainViewModel 的 ReadTrackMetadataAsync / CreateFallbackTrack 整体迁来。
/// 行为不变；唯一差异是 ReadAsync 现在保证不抛（之前 catch 在调用方 PlayTrackAtAsync 兜底）。
/// </summary>
public sealed class AtlMetadataReader : ITrackMetadataReader
{
    /// <summary>
    /// 通过 z440.atl.core 读取音频标签（ID3、Vorbis Comment、APE 等）。
    /// 文件损坏或读不出有效音频时回落到仅含文件名的 fallback Track。
    /// 在后台线程执行，避免大文件首次解析卡 UI。
    /// </summary>
    public Task<Track> ReadAsync(string filePath)
    {
        return Task.Run<Track>(() =>
        {
            try
            {
                var atlTrack = new ATL.Track(filePath);

                // DurationMs == 0 通常意味着没有解析到有效音频数据 → 走 fallback
                if (atlTrack.DurationMs <= 0)
                    return CreateFallback(filePath);

                var title = !string.IsNullOrWhiteSpace(atlTrack.Title)
                    ? atlTrack.Title
                    : Path.GetFileNameWithoutExtension(filePath);

                // 只取第一张内嵌封面（多数情况下只有一张）
                var albumArt = atlTrack.EmbeddedPictures.Count > 0
                    ? atlTrack.EmbeddedPictures[0].PictureData
                    : null;

                return new Track(
                    FilePath: filePath,
                    Title: title,
                    Artist: atlTrack.Artist,
                    Album: atlTrack.Album,
                    Genre: atlTrack.Genre,
                    Year: atlTrack.Year > 0 ? atlTrack.Year : null,
                    SampleRate: atlTrack.SampleRate > 0 ? (int?)atlTrack.SampleRate : null,
                    AlbumArt: albumArt,
                    Duration: TimeSpan.FromMilliseconds(atlTrack.DurationMs),
                    TrackNumber: atlTrack.TrackNumber > 0 ? atlTrack.TrackNumber : null);
            }
            catch
            {
                return CreateFallback(filePath);
            }
        });
    }

    /// <summary>退化版 Track：仅含文件路径与文件名作为标题。同步、无 IO。</summary>
    public Track CreateFallback(string filePath)
    {
        return new Track(
            FilePath: filePath,
            Title: Path.GetFileNameWithoutExtension(filePath),
            Artist: null,
            Album: null,
            Genre: null,
            Year: null,
            SampleRate: null,
            AlbumArt: null,
            Duration: TimeSpan.Zero,
            TrackNumber: null);
    }
}
