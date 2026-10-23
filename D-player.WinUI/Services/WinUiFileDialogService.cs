using System.Diagnostics;
using DPlayer.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DPlayer.WinUI.Services;

/// <summary>
/// 真正的 WinUI picker 实现。HWND 由 App 在窗口创建后写入，服务通过 Func&lt;IntPtr&gt; 懒读。
/// 所有失败（未初始化 HWND / COM 异常 / 空虚拟路径）都回落成"取消"语义并写调试输出；
/// Task 6 会把 Debug.WriteLine 换成 AppLog.Write。
/// </summary>
public sealed class WinUiFileDialogService : IFileDialogService
{
    private readonly Func<IntPtr> _hwndProvider;

    public WinUiFileDialogService(Func<IntPtr> hwndProvider) => _hwndProvider = hwndProvider;

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string filter, bool multiselect = false)
    {
        var hwnd = _hwndProvider();
        if (hwnd == IntPtr.Zero)
        {
            Debug.WriteLine("[WinUI 壳] FileOpenPicker 跳过：窗口 HWND 尚未就绪");
            return Array.Empty<string>();
        }

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
            foreach (var pattern in ParseOpenPatterns(filter)) picker.FileTypeFilter.Add(pattern);
            InitializeWithWindow.Initialize(picker, hwnd);

            if (multiselect)
            {
                var files = await picker.PickMultipleFilesAsync();
                return files is null
                    ? Array.Empty<string>()
                    : files.Select(f => f.Path).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            }

            var file = await picker.PickSingleFileAsync();
            return string.IsNullOrWhiteSpace(file?.Path) ? Array.Empty<string>() : new[] { file.Path };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WinUI 壳] FileOpenPicker 失败：{ex}");
            return Array.Empty<string>();
        }
    }

    public async Task<string?> OpenFolderAsync()
    {
        var hwnd = _hwndProvider();
        if (hwnd == IntPtr.Zero)
        {
            Debug.WriteLine("[WinUI 壳] FolderPicker 跳过：窗口 HWND 尚未就绪");
            return null;
        }

        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            return string.IsNullOrWhiteSpace(folder?.Path) ? null : folder.Path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WinUI 壳] FolderPicker 失败：{ex}");
            return null;
        }
    }

    public async Task<string?> SaveFileAsync(string filter, string defaultFileName, string defaultExtension)
    {
        var hwnd = _hwndProvider();
        if (hwnd == IntPtr.Zero)
        {
            Debug.WriteLine("[WinUI 壳] FileSavePicker 跳过：窗口 HWND 尚未就绪");
            return null;
        }

        var choice = ParseSaveChoice(filter);
        if (choice is null)
        {
            Debug.WriteLine($"[WinUI 壳] FileSavePicker 跳过：无法从过滤器解析扩展名 '{filter}'");
            return null;
        }

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = defaultFileName
            };
            picker.FileTypeChoices.Add(choice.Value.Description, choice.Value.Extensions.ToList());
            if (!string.IsNullOrWhiteSpace(defaultExtension) && defaultExtension.StartsWith('.'))
                picker.DefaultFileExtension = defaultExtension;

            InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSaveFileAsync();
            return string.IsNullOrWhiteSpace(file?.Path) ? null : file.Path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WinUI 壳] FileSavePicker 失败：{ex}");
            return null;
        }
    }

    /// <summary>"Audio Files|*.mp3;*.wma" -> [".mp3", ".wma"]（WinUI 的 FileTypeFilter 不接受通配符）。</summary>
    private static IReadOnlyList<string> ParseOpenPatterns(string filter)
    {
        var segment = filter.Split('|').Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(segment)) return new[] { "*" };

        var patterns = segment
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.StartsWith("*.", StringComparison.Ordinal) ? p[1..] : p)
            .Where(p => p == "*" || p.StartsWith('.'))
            .ToList();

        return patterns.Count == 0 ? new[] { "*" } : patterns;
    }

    /// <summary>"M3U8 播放列表|*.m3u8" -> ("M3U8 播放列表", [".m3u8"]).</summary>
    private static (string Description, IReadOnlyList<string> Extensions)? ParseSaveChoice(string filter)
    {
        var parts = filter.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0])) return null;

        var extensions = parts[1]
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.StartsWith("*.", StringComparison.Ordinal) ? p[1..] : p)
            .Where(p => p.StartsWith('.'))
            .ToList();

        return extensions.Count == 0 ? null : (parts[0], extensions);
    }
}
