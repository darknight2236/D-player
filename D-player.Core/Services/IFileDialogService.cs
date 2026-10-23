namespace DPlayer.Services;

/// <summary>
/// 文件选择对话框抽象，便于单元测试以 Mock 替换。
///
/// 三个方法都从 UI 线程发起。WPF 实现仍在 UI 线程同步弹模态对话框
/// （返回已完成的 Task）；WinUI 实现走 FileOpenPicker / FolderPicker / FileSavePicker，
/// 需要窗口 HWND。
///
/// 返回值语义：取消 / 关闭 / picker 失败 / 虚拟路径为空 -> 空集合或 null，绝不抛。
/// VM 侧 await 之后一律 ConfigureAwait(true) 再改 ObservableCollection。
/// </summary>
public interface IFileDialogService
{
    Task<IReadOnlyList<string>> OpenFilesAsync(string filter, bool multiselect = false);
    Task<string?> OpenFolderAsync();
    Task<string?> SaveFileAsync(string filter, string defaultFileName, string defaultExtension);
}
