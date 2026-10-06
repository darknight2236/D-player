using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 多歌单持久化抽象(Phase 6, 替换 IQueuePersistence)。
/// 文件: %LocalAppData%\D-player\queue.json (沿用文件名)。
/// 契约: LoadAsync 绝不抛(失败回种子), SaveAsync IO 失败抛 IOException。
/// LoadAsync 内一次性迁移 v1 → v2; 迁移阶段 SaveAsync 失败吞掉, 内存仍是 v2, 下次启动重迁(幂等)。
/// </summary>
public interface IPlaylistService
{
    Task<QueueState> LoadAsync();
    Task SaveAsync(QueueState snapshot);
}
