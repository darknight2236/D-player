using UmaPlayer.Configuration;

namespace UmaPlayer.Services;

/// <summary>
/// 应用配置的运行时持久化抽象。
///
/// 与 IOptions&lt;AppSettings&gt; 的分工：
///   - IOptions：启动只读快照（来自 appsettings.json，跟随程序分发）
///   - 本接口  ：运行时可变状态（窗口尺寸、音量等），写入用户数据目录
/// </summary>
public interface ISettingsPersistence
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}
