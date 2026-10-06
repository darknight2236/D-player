using System.IO;
using DPlayer.Configuration;
using DPlayer.Extensions;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DPlayer.Tests.Extensions;

/// <summary>
/// DI 图回归保护。WinUI 工程刻意不进 D-player.slnf 门禁（本阶段它只是对比对象），
/// 所以两张壳共同依赖的这张图只能在 Core 侧测——这条测试就是那道便宜的围挡。
///
/// 要钉住的具体缺陷：歌单工厂（<c>Func&lt;Playlist, PlaylistViewModel&gt;</c>）从容器取
/// <see cref="IFileDialogService"/>，而该注册是**各壳自己**做的 —— 少注册时容器构建不报错，
/// 直到第一次构造 PlaylistViewModel 才炸（原来只有真机能看见）。这里把那条构造提前到门禁里。
/// 同理，任何新加到 MainViewModel / PlayerViewModel / PlaylistsViewModel / PlaylistViewModel
/// 构造上的依赖，忘了注册也会被这条拦下。
/// </summary>
public sealed class AddDPlayerCoreTests
{
    /// <summary>壳侧注册的替身：真实壳注册的是 WinUiFileDialogService（WPF 壳注册它的对应物）。</summary>
    private sealed class StubFileDialogService : IFileDialogService
    {
        public IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false) => Array.Empty<string>();
        public string? OpenFolder() => null;
        public string? SaveFile(string filter, string defaultFileName, string defaultExtension) => null;
    }

    /// <summary>
    /// 解析 MainViewModel（壳启动时那一次 GetRequiredService）并真的构造一个歌单 VM。
    /// 落盘目录固定在 %TEMP% 下的随机目录：三个持久化服务都会在构造时 CreateDirectory，
    /// 因此绝不能让它们用默认的 LocalApplicationData Root（那是用户数据，不是测试数据）。
    /// 断言只到"图能解析到底"——不调用 Play()、不加载任何曲目，所以不依赖本机有音频设备。
    /// </summary>
    [Fact]
    public async Task CoreGraph_ResolvesMainViewModel_AndConstructsFirstPlaylist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dplayer-di-{Guid.NewGuid():N}");
        try
        {
            var services = new ServiceCollection();
            services.AddDPlayerCore(
                new ConfigurationBuilder().Build(),
                new DPlayerDataPaths { Root = root, FolderName = "D-player-winui" });
            // UI 服务由壳注册，Core 不兜底——这行就是 WinUI 壳 App.xaml.cs 里的那一行
            services.AddSingleton<IFileDialogService, StubFileDialogService>();

            // await using：MainViewModel 只实现 IAsyncDisposable，容器要求异步释放
            // （同步 Dispose 会抛 "Use DisposeAsync to dispose the container"）。
            await using var provider = services.BuildServiceProvider();

            var vm = provider.GetRequiredService<MainViewModel>();
            Assert.NotNull(vm.Player);
            Assert.NotNull(vm.Playlists);

            // 第一次构造歌单：IFileDialogService 缺失时就是在这里抛
            var factory = provider.GetRequiredService<Func<Playlist, PlaylistViewModel>>();
            var playlist = factory(new Playlist("id1", "A", Array.Empty<string>(), -1, false, RepeatMode.Off));

            Assert.Equal("id1", playlist.Id);
            Assert.Empty(playlist.Queue);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { /* 临时目录清不掉不影响断言结论 */ }
        }
    }
}
