using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 文件夹绑定歌单的元数据缓存抽象(Phase 10)。
/// 所有文件夹绑定歌单共用一个缓存文件, 按 SourceFolder 路径分组。
/// 契约: LoadAsync 绝不抛(失败回空列表), SaveAsync IO 失败抛 IOException。
/// </summary>
public interface ILibraryCache
{
    Task<IReadOnlyList<LibraryCacheEntry>> LoadAsync(string folderPath);
    Task SaveAsync(string folderPath, IReadOnlyList<LibraryCacheEntry> entries);
}
