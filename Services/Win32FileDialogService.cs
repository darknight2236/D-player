using Microsoft.Win32;

namespace DPlayer.Services;

/// <summary>
/// 基于 Microsoft.Win32.OpenFileDialog 的实现 —— WPF 推荐方式，不依赖 WinForms。
/// 默认单选；调用方可通过 multiselect=true 启用多选（用于播放列表入队）。
/// </summary>
public sealed class Win32FileDialogService : IFileDialogService
{
    public IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Multiselect = multiselect
        };

        // ShowDialog() == true 表示用户点击"打开"；其他情况（取消/关闭）返回空集合
        return dialog.ShowDialog() == true
            ? dialog.FileNames.ToList().AsReadOnly()
            : Array.Empty<string>();
    }

    public string? OpenFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择音乐文件夹"
        };

        return dialog.ShowDialog() == true
            ? dialog.FolderName
            : null;
    }
}
