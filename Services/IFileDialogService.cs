namespace DPlayer.Services;

/// <summary>
/// 文件选择对话框抽象，便于单元测试以 Mock 替换。
///
/// [STA Thread Required] —— Win32 OpenFileDialog 必须在 STA 线程调用。
/// 当前由 VM 的 RelayCommand 在 UI 线程触发，符合要求；
/// 若从后台线程调用会抛 InvalidOperationException。
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// 弹出文件选择对话框。
    /// </summary>
    /// <param name="filter">WPF 格式过滤器，如 "Audio Files|*.mp3;*.wav"。</param>
    /// <param name="multiselect">是否允许多选；默认 false 保持原行为。</param>
    /// <returns>用户选中的文件路径列表；取消则返回空集合。</returns>
    IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false);

    /// <summary>
    /// 弹出文件夹选择对话框(Phase 10)。
    /// </summary>
    /// <returns>用户选中的文件夹路径；取消则返回 null。</returns>
    string? OpenFolder();
}
