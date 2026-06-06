using Microsoft.Win32;

namespace UmaPlayer.Services;

/// <summary>
/// 基于 Microsoft.Win32.OpenFileDialog 的实现 —— WPF 推荐方式，不依赖 WinForms。
/// 当前限制为单选（Multiselect=false），未来支持播放列表后可放宽。
/// </summary>
public sealed class Win32FileDialogService : IFileDialogService
{
    public IReadOnlyList<string> OpenFiles(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Multiselect = false
        };

        // ShowDialog() == true 表示用户点击"打开"；其他情况（取消/关闭）返回空集合
        return dialog.ShowDialog() == true
            ? dialog.FileNames.ToList().AsReadOnly()
            : Array.Empty<string>();
    }
}
