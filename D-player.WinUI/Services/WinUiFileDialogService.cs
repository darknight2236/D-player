using DPlayer.Services;

namespace DPlayer.WinUI.Services;

/// <summary>
/// 切片阶段的最小实现：本阶段不暴露文件选择入口（导入文件夹 / 导入播放列表文件 / 导出
/// 都留待第二阶段），但 <see cref="IFileDialogService"/> 必须在容器里可解析，否则
/// PlaylistViewModel 工厂（ServiceCollectionExtensions 里从容器取本服务）在第一次构造歌单时抛。
/// 因此三个方法各自返回"用户取消"的语义值：空集合 / null / null。
///
/// 第二阶段接入真 picker 时的已知障碍（此处不解决，仅记录）：
/// 本接口是**同步**的，而 WinUI 的 FileOpenPicker / FolderPicker 只有异步 API
/// （PickSingleFileAsync 返回 IAsyncOperation）。要用同步接口驱动异步 picker 只能在 UI 线程上
/// 阻塞等待（.Result / GetAwaiter().GetResult()），而模态 picker 的消息又正需要这条 UI 线程——
/// 那就是死锁。所以真正的接法要么把 Core 侧接口改成异步（跨壳改动，本阶段禁止），
/// 要么在壳侧用异步入口 + 完成后回调，属于第二阶段工作。
/// </summary>
public sealed class WinUiFileDialogService : IFileDialogService
{
    /// <summary>切片期无入口：固定返回空集合（= 用户在对话框里点了取消）。</summary>
    public IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false)
        => Array.Empty<string>();

    /// <summary>切片期无入口：固定返回 null（= 用户取消）。</summary>
    public string? OpenFolder() => null;

    /// <summary>切片期无入口：固定返回 null（= 用户取消）。</summary>
    public string? SaveFile(string filter, string defaultFileName, string defaultExtension) => null;
}
