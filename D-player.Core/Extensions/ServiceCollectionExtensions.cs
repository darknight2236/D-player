using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DPlayer.Configuration;
using DPlayer.Services;
using DPlayer.Services.PlaylistFiles;
using DPlayer.ViewModels;

namespace DPlayer.Extensions;

/// <summary>
/// DI 容器注册中心 —— 集中维护共享服务的生命周期与实现绑定，
/// 让 App.OnStartup 只需一行 `services.AddDPlayerCore(configuration, dataPaths)`。
/// UI 相关服务（文件对话框等）由各壳自行注册；用户数据目录（<see cref="DPlayerDataPaths"/>）
/// 由各壳传入，两个壳各写各的目录。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDPlayerCore(
        this IServiceCollection services,
        IConfiguration configuration,
        DPlayerDataPaths dataPaths)
    {
        // 用户数据目录 —— 三个持久化服务都依赖它，先注册再谈其他
        services.AddSingleton(dataPaths);

        // 配置 —— 仅绑定 "Player" 子节，避免与其他节冲突
        services.Configure<AppSettings>(configuration.GetSection("Player"));

        // 业务服务（Singleton —— 持有音频设备/文件句柄等长期资源）
        services.AddSingleton<IPlaybackService, NAudioPlaybackService>();
        services.AddSingleton<ISettingsPersistence, JsonSettingsPersistence>();

        // Phase 6: 多歌单持久化 (替换 IQueuePersistence)
        services.AddSingleton<IPlaylistService, JsonPlaylistService>();

        // 预留服务 —— 注册 Stub 以便未来替换不需要改 DI
        services.AddSingleton<IAudioDeviceManager, StubAudioDeviceManager>();

        // 输出工厂（Transient —— 每次调用都新建一个 IWavePlayer，
        // 由 IPlaybackService 负责释放生命周期）
        services.AddTransient<IAudioOutputFactory, StubAudioOutputFactory>();

        // 元数据读取（Singleton —— 无状态、纯函数式接口）
        services.AddSingleton<ITrackMetadataReader, AtlMetadataReader>();

        // Phase 10: 库扫描 + 元数据缓存
        services.AddSingleton<ILibraryScannerService, LibraryScannerService>();
        services.AddSingleton<ILibraryCache, JsonLibraryCache>();

        // Phase 18: 播放列表文件导入导出（无状态、纯文件 IO → Singleton）
        services.AddSingleton<IPlaylistFileService, PlaylistFileService>();

        // ViewModel
        services.AddTransient<PlayerViewModel>();
        // PlaylistViewModel 由 PlaylistsViewModel 通过工厂创建; 工厂封装依赖, seed 是动态参数。
        services.AddTransient<Func<Models.Playlist, PlaylistViewModel>>(sp => seed =>
            new PlaylistViewModel(
                seed,
                sp.GetRequiredService<IPlaybackService>(),
                sp.GetRequiredService<IFileDialogService>(),
                sp.GetRequiredService<ITrackMetadataReader>(),
                sp.GetRequiredService<IPlaylistFileService>()));
        services.AddSingleton<PlaylistsViewModel>();
        services.AddTransient<MainViewModel>();

        return services;
    }
}
