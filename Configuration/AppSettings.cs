namespace UmaPlayer.Configuration;

/// <summary>
/// 应用全局配置 —— 同时承担两种角色：
/// 1) 启动默认值快照：通过 IOptions&lt;AppSettings&gt; 绑定 appsettings.json 的 "Player" 节；
/// 2) 运行时持久化载体：由 JsonSettingsPersistence 读写 %LocalAppData%\UmaPlayer\settings.json。
///
/// 使用 record + init 属性，通过 `with` 表达式不可变更新，避免并发写入时的数据竞争。
/// </summary>
public sealed record AppSettings
{
    /// <summary>默认音量 0.0~1.0，VM 启动时套用并随用户拖动音量条持久化。</summary>
    public float DefaultVolume { get; init; } = 0.8f;

    /// <summary>输出模式 (预留)：WasapiShared / WasapiExclusive / Asio 等。</summary>
    public string OutputMode { get; init; } = "WasapiShared";

    /// <summary>用户首选输出设备 ID (预留)。</summary>
    public string? PreferredDeviceId { get; init; }

    /// <summary>上次播放的文件路径 (预留：用于"上次播放"恢复)。</summary>
    public string? LastPlayedPath { get; init; }

    // 窗口几何 —— 关闭时由 MainWindow.Window_Closing 写入，启动时读取
    public double WindowLeft { get; init; }
    public double WindowTop { get; init; }
    public double WindowWidth { get; init; } = 800;
    public double WindowHeight { get; init; } = 450;
}
