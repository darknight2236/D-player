namespace UmaPlayer.Services;

/// <summary>
/// [STA Thread Required] — OpenFileDialog must be called on STA thread.
/// Currently called from VM commands on UI thread. Will throw
/// InvalidOperationException if called from a background thread.
/// </summary>
public interface IFileDialogService
{
    IReadOnlyList<string> OpenFiles(string filter);
}
