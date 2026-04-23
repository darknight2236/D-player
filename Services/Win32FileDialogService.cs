using Microsoft.Win32;

namespace UmaPlayer.Services;

public sealed class Win32FileDialogService : IFileDialogService
{
    public IReadOnlyList<string> OpenFiles(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Multiselect = false
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames.ToList().AsReadOnly()
            : Array.Empty<string>();
    }
}
