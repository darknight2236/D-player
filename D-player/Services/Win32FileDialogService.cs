using Microsoft.Win32;

namespace DPlayer.Services;

/// <summary>
/// 基于 Microsoft.Win32.* 的同步对话框；异步方法只是把同步结果包成已完成的 Task。
/// 用户可见行为与 Phase 20/21 完全一致。
/// </summary>
public sealed class Win32FileDialogService : IFileDialogService
{
    public Task<IReadOnlyList<string>> OpenFilesAsync(string filter, bool multiselect = false)
        => Task.FromResult(OpenFiles(filter, multiselect));

    public Task<string?> OpenFolderAsync() => Task.FromResult(OpenFolder());

    public Task<string?> SaveFileAsync(string filter, string defaultFileName, string defaultExtension)
        => Task.FromResult(SaveFile(filter, defaultFileName, defaultExtension));

    private static IReadOnlyList<string> OpenFiles(string filter, bool multiselect)
    {
        var dialog = new OpenFileDialog { Filter = filter, Multiselect = multiselect };
        return dialog.ShowDialog() == true
            ? dialog.FileNames.ToList().AsReadOnly()
            : Array.Empty<string>();
    }

    private static string? OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "选择音乐文件夹" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string? SaveFile(string filter, string defaultFileName, string defaultExtension)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出播放列表",
            Filter = filter,
            FileName = defaultFileName,
            DefaultExt = defaultExtension,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
