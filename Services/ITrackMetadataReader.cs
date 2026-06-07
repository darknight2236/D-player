using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 音频元数据读取抽象 —— 把"从文件读 Title/Artist/封面"的关注点从 ViewModel 解耦出来。
///
/// 设计契约：
///   - ReadAsync 永远不抛：文件损坏、读不出有效音频 → 返回 CreateFallback 的结果
///   - CreateFallback 同步、无 IO：仅 FilePath + Title（来自文件名），其他字段为 null/空
///     用于队列入队时立刻占位，等用户实际播放时再调 ReadAsync 回填完整元数据
/// </summary>
public interface ITrackMetadataReader
{
    /// <summary>异步读元数据。读失败返回 fallback Track（绝不抛）。</summary>
    Task<Track> ReadAsync(string filePath);

    /// <summary>同步 fallback：仅文件名作为 Title，其它字段为空。用于入队时占位。</summary>
    Track CreateFallback(string filePath);
}
