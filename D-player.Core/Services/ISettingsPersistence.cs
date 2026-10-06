using DPlayer.Configuration;

namespace DPlayer.Services;

/// <summary>
/// 应用配置的运行时持久化抽象。
///
/// 与 IOptions&lt;AppSettings&gt; 的分工：
///   - IOptions：启动只读快照（来自 appsettings.json，跟随程序分发）
///   - 本接口  ：运行时可变状态（窗口尺寸、音量等），写入用户数据目录
///
/// 并发模型：实现层用 SemaphoreSlim 把"读盘 → mutator → 写盘"封进同一临界区，
/// 调用方不再持有 AppSettings 的内存副本（避免"VM 写音量、Window 写尺寸时
/// VM 的旧副本把 Window 的尺寸覆盖回去"这类 race）。
/// </summary>
public interface ISettingsPersistence
{
    /// <summary>读取磁盘上的当前设置。文件不存在则返回 record 默认值。</summary>
    Task<AppSettings> LoadAsync();

    /// <summary>
    /// 原子读-改-写：实现内读最新磁盘版本 → 应用 mutator → 写回，
    /// 全程在同一 SemaphoreSlim 获取期内完成。
    ///
    /// 契约：
    ///   - mutator 必须是纯函数（无 IO 副作用）。它在锁内执行，副作用会阻塞其他更新
    ///   - 读盘失败（损坏 / 不存在 / 权限）时，把 new AppSettings() 作为 mutator 输入
    ///     ——与 LoadAsync 的失败回落语义一致。写盘失败仍会抛出
    /// </summary>
    Task UpdateAsync(Func<AppSettings, AppSettings> mutator);
}
