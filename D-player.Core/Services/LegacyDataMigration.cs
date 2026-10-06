using System.IO;

namespace DPlayer.Services;

/// <summary>
/// 一次性数据目录迁移助手。
///
/// 项目从 "UmaPlayer" 更名为 "D-player" 后，用户数据目录也随之从
/// %LocalAppData%\UmaPlayer 改为 %LocalAppData%\D-player。为避免老用户升级后
/// 丢失已保存的设置/队列/元数据缓存，在应用启动最早期把旧目录内的文件搬到新目录。
///
/// 纪律：
///   - 幂等：只在旧目录存在时执行；逐个文件"新目录没有才搬"，重复调用无副作用。
///   - 绝不抛：迁移失败（权限/占用等）静默吞掉，旧数据保留供用户手动处理，不阻断启动。
///   - 必须在任何持久化服务（JsonSettingsPersistence / JsonPlaylistService /
///     JsonLibraryCache）被 DI 构造之前调用，见 App.OnStartup。
/// </summary>
public static class LegacyDataMigration
{
    private const string OldFolderName = "UmaPlayer";
    private const string NewFolderName = "D-player";

    /// <summary>把旧数据目录中的文件迁移到新目录（若新目录尚无同名文件）。</summary>
    public static void MigrateIfNeeded()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var oldDir = Path.Combine(appData, OldFolderName);
            var newDir = Path.Combine(appData, NewFolderName);

            if (!Directory.Exists(oldDir))
                return; // 全新安装或已迁移过，无事可做

            Directory.CreateDirectory(newDir);

            foreach (var sourceFile in Directory.EnumerateFiles(oldDir))
            {
                var destFile = Path.Combine(newDir, Path.GetFileName(sourceFile));
                if (!File.Exists(destFile))
                    File.Move(sourceFile, destFile);
            }
        }
        catch
        {
            // 迁移失败不阻断启动；旧目录数据原样保留
        }
    }
}
