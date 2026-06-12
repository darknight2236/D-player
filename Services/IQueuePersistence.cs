using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 队列持久化抽象（Phase 4）。文件路径：%LocalAppData%\UmaPlayer\queue.json。
///
/// 与 ISettingsPersistence 的设计基线相同：
///   - 实现层用 SemaphoreSlim 串行化读写
///   - 单实例 Singleton；DI 容器懒构造
///   - 启动时若文件不存在/损坏，LoadAsync 返回 default(QueueState)；写盘失败抛
///
/// 与 ISettingsPersistence 的差异：
///   - 这里不需要 read-modify-write —— 队列状态由 PlaylistViewModel 整体快照后写入，
///     仅有一个写者（MainWindow.Window_Closing），不存在合并竞态。
///   - 因此用 SaveAsync(QueueState snapshot) 比 UpdateAsync(Func&lt;&gt;) 更直接。
/// </summary>
public interface IQueuePersistence
{
    /// <summary>
    /// 读取磁盘上的队列快照。
    /// 文件不存在 / 损坏 / SchemaVersion 不匹配 时静默返回空 QueueState（绝不抛）。
    /// </summary>
    Task<QueueState> LoadAsync();

    /// <summary>
    /// 把快照写入 queue.json。
    /// 写盘失败会抛（IO/权限），由 MainWindow.Window_Closing 顶层 catch 静默处理。
    /// </summary>
    Task SaveAsync(QueueState snapshot);
}
