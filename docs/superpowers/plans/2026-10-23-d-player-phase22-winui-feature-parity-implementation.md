# Phase 22 WinUI Feature Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the WinUI 3 shell usable as a daily player: async real file pickers, playlist CRUD, import/export, full transport controls in the 64px bar, basic settings, persisted window geometry, and a local log outlet — without changing WPF's user-visible behavior.

**Architecture:** Contract-first. Task 1 changes `IFileDialogService` to async across Core/WPF/WinUI and promotes the WinUI build to the third `-Full` gate. Tasks 2–6 are independent shell slices that reuse the existing Core commands. Task 7 syncs docs and creates the acceptance checklist; Task 8 runs it.

**Tech Stack:** .NET 10, WPF shell (`D-player.csproj`), WinUI 3 / Windows App SDK 2.5.1 (`D-player.WinUI`, unpackaged + self-contained + x64), `D-player.Core` (WPF-free), xunit.v3 + Microsoft.Testing.Platform (180 tests).

**Spec:** `docs/superpowers/specs/2026-10-23-d-player-phase22-winui-feature-parity-design.md`

## Global Constraints

- Work directly on `master`; do not create branches.
- CRLF: every edited/created file must end up `i/lf    w/crlf` (`git ls-files --eol <path>`); repair with `unix2dos`; never use `sed -i`.
- `dotnet test` must never be given `--nologo` (it silently runs 0 tests and prints "成功: 0"); `--nologo` is only for `dotnet build`.
- Every task closes with: `dotnet build D-player.slnf -c Debug --nologo -v q` (0 warnings / 0 errors), `dotnet test D-player.slnf -c Debug -v q` (180 passed / 0 failed), `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` (0/0), `powershell -File tools/verify-gates.ps1 -Full` (all three gates).
- Never `git push` unless the user explicitly asks in a separate message.
- The agent must not read or write `%LOCALAPPDATA%\D-player\*` or `%LOCALAPPDATA%\D-player-winui\*`; acceptance items that need it are handed to the user.
- WPF user-visible behavior must not change. Core's public surface may change **only** for `IFileDialogService` in this phase.
- No new NuGet packages. Do not add WinUI to `D-player.slnf`.
- Do not modify Phase 20/21 plans or acceptance reports; only update capability docs (PHASE20 comparison, PROJECT, COUPLING, README) plus new Phase 22 docs.
- PowerShell scripts stay pure ASCII (PS 5.1 reads BOM-less files as GBK on this machine).

---

## File Structure

**Core**
- `D-player.Core/Services/IFileDialogService.cs` — async contract.
- `D-player.Core/ViewModels/PlaylistViewModel.cs` — four call sites become `await`.
- `D-player.Core/ViewModels/PlaylistsViewModel.cs` — two call sites + null stub.

**WPF shell**
- `D-player/Services/Win32FileDialogService.cs` — keeps synchronous dialogs behind `Task.FromResult`.

**WinUI shell**
- `D-player.WinUI/App.xaml.cs` — HWND holder, winui data path constant, log init/hooks.
- `D-player.WinUI/Services/WinUiFileDialogService.cs` — real pickers.
- `D-player.WinUI/Services/WinUiDialogService.cs` — prompt/confirm/info/error `ContentDialog` host.
- `D-player.WinUI/Services/WinUiAppPaths.cs` — data/log directory constants.
- `D-player.WinUI/Services/AppLog.cs` — file log + rotation.
- `D-player.WinUI/Views/NavRail.xaml(.cs)` — playlist toolbar, active marker, rename/delete.
- `D-player.WinUI/Views/TrackList.xaml(.cs)` — import/export toolbar, busy state, empty copy.
- `D-player.WinUI/Views/PlayerBar.xaml(.cs)` — transport/volume controls, responsive 64px bar.
- `D-player.WinUI/Views/Dialogs/SettingsDialog.xaml(.cs)` — basic settings.
- `D-player.WinUI/MainWindow.xaml.cs` — Space guard extension, window geometry, close guard.
- `D-player.WinUI/Views/Converters.cs` — `BoolToVisibilityConverter`.
- `D-player.WinUI/Theme/Tokens.xaml` — toolbar heights/button sizes.
- `D-player.WinUI/Theme/Styles.xaml` — only if a new shared style is actually needed.

**Tests / tools / docs**
- `Tests/ViewModels/PlaylistViewModelTests.cs`, `Tests/ViewModels/PlaylistsViewModelTests.cs`, `Tests/Extensions/AddDPlayerCoreTests.cs` — async stubs.
- `tools/verify-gates.ps1` — WinUI build becomes gate 3 in `-Full`.
- `docs/PHASE22-ACCEPTANCE.md`, `docs/PHASE20-COMPARISON.md`, `docs/PROJECT.md`, `docs/COUPLING.md`, `README.md`.

---

### Task 1: Async file dialog contract + WinUI gate promotion

**Files:**
- Modify: `D-player.Core/Services/IFileDialogService.cs` (whole file)
- Modify: `D-player.Core/ViewModels/PlaylistViewModel.cs` (call sites at ~341, ~357, ~413, ~440)
- Modify: `D-player.Core/ViewModels/PlaylistsViewModel.cs` (~336, ~384, ~602-607)
- Modify: `D-player/Services/Win32FileDialogService.cs` (whole file)
- Modify: `D-player.WinUI/Services/WinUiFileDialogService.cs` (whole file)
- Modify: `D-player.WinUI/App.xaml.cs` (~53-62)
- Modify: `Tests/ViewModels/PlaylistViewModelTests.cs`, `Tests/ViewModels/PlaylistsViewModelTests.cs`, `Tests/Extensions/AddDPlayerCoreTests.cs`
- Modify: `tools/verify-gates.ps1`

**Interfaces:**
- Produces:
  ```csharp
  Task<IReadOnlyList<string>> OpenFilesAsync(string filter, bool multiselect = false)
  Task<string?> OpenFolderAsync()
  Task<string?> SaveFileAsync(string filter, string defaultFileName, string defaultExtension)
  ```
- Consumes: existing `PlaylistFileFormats.OpenFilter` / `SaveFilter`; existing Core commands.

- [ ] **Step 1: Replace the interface**

`D-player.Core/Services/IFileDialogService.cs`:

```csharp
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
```

- [ ] **Step 2: Run the build and confirm the expected call-site failures**

Run: `dotnet build D-player.slnf -c Debug --nologo -v q`
Expected: fails with `CS0535` on both `IFileDialogService` implementations and `CS1061` at the six Core call sites. This is the failing step; do not fix tests yet.

- [ ] **Step 3: Update the six Core call sites**

In `D-player.Core/ViewModels/PlaylistViewModel.cs`:

```csharp
// AddToQueue
var files = await _fileDialog.OpenFilesAsync(
    "Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav",
    multiselect: true);

// ImportFolderToCurrent
var folder = await _fileDialog.OpenFolderAsync();

// ImportPlaylistFileAsync
var path = presetPath ?? (await _fileDialog.OpenFilesAsync(PlaylistFileFormats.OpenFilter)).FirstOrDefault();

// ExportPlaylistFileAsync
var dest = await _fileDialog.SaveFileAsync(
    PlaylistFileFormats.SaveFilter, SanitizeFileName(Name), ".m3u8");
```

In `D-player.Core/ViewModels/PlaylistsViewModel.cs`:

```csharp
// ImportFolderAsync
var folderPath = await _fileDialog.OpenFolderAsync();

// ImportPlaylistFileAsync
var path = presetPath ?? (await _fileDialog.OpenFilesAsync(PlaylistFileFormats.OpenFilter)).FirstOrDefault();
```

Update the nested `NullFileDialogService`:

```csharp
private sealed class NullFileDialogService : IFileDialogService
{
    public Task<IReadOnlyList<string>> OpenFilesAsync(string filter, bool multiselect = false)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    public Task<string?> OpenFolderAsync() => Task.FromResult<string?>(null);
    public Task<string?> SaveFileAsync(string filter, string defaultFileName, string defaultExtension)
        => Task.FromResult<string?>(null);
}
```

- [ ] **Step 4: Update the WPF implementation**

`D-player/Services/Win32FileDialogService.cs`:

```csharp
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
```

- [ ] **Step 5: Implement the WinUI pickers**

`D-player.WinUI/Services/WinUiFileDialogService.cs`:

```csharp
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
```

- [ ] **Step 6: Wire the HWND in `App.xaml.cs`**

Add `using WinRT.Interop;`, a field `private IntPtr _hwnd;`, and change the registration/window creation:

```csharp
services.AddSingleton<IFileDialogService>(_ => new WinUiFileDialogService(() => _hwnd));

var vm = _services.GetRequiredService<MainViewModel>();
var window = new MainWindow(vm);
_window = window;
_hwnd = WindowNative.GetWindowHandle(window);
window.Activate();
```

The service is constructed during `GetRequiredService<MainViewModel>()`, before `_hwnd` is written; it reads the field lazily on the first picker call, which happens only after user interaction.

- [ ] **Step 7: Update the test doubles and expectations**

`Tests/Extensions/AddDPlayerCoreTests.cs` — replace `StubFileDialogService` members:

```csharp
public Task<IReadOnlyList<string>> OpenFilesAsync(string filter, bool multiselect = false)
    => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
public Task<string?> OpenFolderAsync() => Task.FromResult<string?>(null);
public Task<string?> SaveFileAsync(string filter, string defaultFileName, string defaultExtension)
    => Task.FromResult<string?>(null);
```

`Tests/ViewModels/PlaylistViewModelTests.cs` — replace every `OpenFiles` / `SaveFile` setup/received line:

```csharp
_fileDialog.OpenFilesAsync(Arg.Any<string>(), Arg.Any<bool>())
    .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
_fileDialog.OpenFilesAsync(Arg.Any<string>(), Arg.Any<bool>())
    .Returns(Task.FromResult<IReadOnlyList<string>>(new[] { @"D:\lists\rock.m3u8" }));
_fileDialog.OpenFilesAsync(Arg.Any<string>(), Arg.Any<bool>())
    .Returns(Task.FromResult<IReadOnlyList<string>>(new[] { @"D:\lists\old.pls" }));
_fileDialog.DidNotReceive().OpenFilesAsync(Arg.Any<string>(), Arg.Any<bool>());

_fileDialog.SaveFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
    .Returns(Task.FromResult<string?>(null));
_fileDialog.SaveFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
    .Returns(Task.FromResult<string?>(@"D:\out\a.m3u8"));
_fileDialog.Received(1).SaveFileAsync(PlaylistFileFormats.SaveFilter, "My_List_1_", ".m3u8");
_fileDialog.DidNotReceive().SaveFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
```

`Tests/ViewModels/PlaylistsViewModelTests.cs` — replace the four `OpenFiles` setups and the `DidNotReceive` with the same `OpenFilesAsync(...).Returns(Task.FromResult<IReadOnlyList<string>>(...))` shape.

`Tests/ViewModels/PlaybackResumeTests.cs` — no call sites; it only constructs the substitute, so it compiles unchanged. Leave it alone.

- [ ] **Step 8: Promote the WinUI build to gate 3 in `-Full`**

In `tools/verify-gates.ps1`:

- Update the `.DESCRIPTION` block so the vocabulary is:
  - `GATE 1 (every change)`: `.slnf` build 0 warnings.
  - `GATE 2 (every change)`: `.slnf` test 180 passed.
  - `GATE 3 (-Full only; every Phase 22 task)`: WinUI single-project build 0 warnings.
- Replace the mode line:
  ```powershell
  Write-Host ('mode: {0}' -f $(if ($withShell) { 'Full (3 gates: solution filter build + test + WinUI shell build)' } else { 'Fast (gates 1+2)' }))
  ```
- Replace the final label:
  ```powershell
  Invoke-BuildStep -Label 'gate 3/3: build the WinUI 3 project, 0 warnings required' `
                   -Arguments @('build', 'D-player.WinUI/D-player.WinUI.csproj', '-c', 'Debug', '--nologo', '-v', 'q')
  ```
- Keep the script pure ASCII.

- [ ] **Step 9: Run all gates**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test D-player.slnf -c Debug -v q
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
powershell -File tools/verify-gates.ps1 -Full
```

Expected: 0/0; 180 passed / 0 failed; 0/0; three passed steps ending with `all requested steps passed.`

- [ ] **Step 10: CRLF check and commit**

```bash
git ls-files --eol D-player.Core/Services/IFileDialogService.cs D-player.Core/ViewModels/PlaylistViewModel.cs D-player.Core/ViewModels/PlaylistsViewModel.cs D-player/Services/Win32FileDialogService.cs D-player.WinUI/Services/WinUiFileDialogService.cs D-player.WinUI/App.xaml.cs Tests/ViewModels/PlaylistViewModelTests.cs Tests/ViewModels/PlaylistsViewModelTests.cs Tests/Extensions/AddDPlayerCoreTests.cs tools/verify-gates.ps1
git add D-player.Core/Services/IFileDialogService.cs D-player.Core/ViewModels/PlaylistViewModel.cs D-player.Core/ViewModels/PlaylistsViewModel.cs D-player/Services/Win32FileDialogService.cs D-player.WinUI/Services/WinUiFileDialogService.cs D-player.WinUI/App.xaml.cs Tests/ViewModels/PlaylistViewModelTests.cs Tests/ViewModels/PlaylistsViewModelTests.cs Tests/Extensions/AddDPlayerCoreTests.cs tools/verify-gates.ps1
git commit -m "feat(core): make file dialogs async across both shells"
```

---

### Task 2: Dialog host + NavRail playlist management + Space guard

**Files:**
- Create: `D-player.WinUI/Services/WinUiDialogService.cs`
- Create: `D-player.WinUI/Views/Converters.cs`
- Modify: `D-player.WinUI/Views/NavRail.xaml(.cs)`
- Modify: `D-player.WinUI/MainWindow.xaml.cs` (Space guard)
- Modify: `D-player.WinUI/Theme/Tokens.xaml` only if a new size is needed (none required; reuse `FoldButtonSize`, `SpacingXs`)

**Interfaces:**
- Produces:
  ```csharp
  internal static class WinUiDialogService
  {
      public static bool IsOpen { get; }
      public static Task<string?> ShowPromptAsync(FrameworkElement owner, string title, string label, string initial);
      public static Task<bool> ShowConfirmAsync(FrameworkElement owner, string title, string message, string confirmText = "确定");
      public static Task ShowErrorAsync(FrameworkElement owner, string title, string message);
      public static Task ShowInfoAsync(FrameworkElement owner, string title, string message);
  }
  internal sealed class BoolToVisibilityConverter : IValueConverter
  ```
- Consumes: `PlaylistsViewModel.AddPlaylistCommand`, `RenamePlaylistCommand`, `RemovePlaylistCommand`, `ImportPlaylistFileAsync`, `PlaylistImportReportFormatter`.

- [ ] **Step 1: Create the converter**

`D-player.WinUI/Views/Converters.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DPlayer.WinUI.Views;

internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Create the dialog host**

`D-player.WinUI/Services/WinUiDialogService.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DPlayer.WinUI.Services;

/// <summary>
/// 壳内唯一的 ContentDialog 宿主。所有对话框都从这里走，
/// 这样 MainWindow 的空格守卫只需要看一个 IsOpen 标志，不必猜弹层的可视化树。
/// </summary>
internal static class WinUiDialogService
{
    public static bool IsOpen { get; private set; }

    public static async Task<string?> ShowPromptAsync(
        FrameworkElement owner, string title, string label, string initial)
    {
        if (owner.XamlRoot is null) return null;

        var box = new TextBox { Text = initial, Header = label };
        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Title = title,
            Content = box,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        IsOpen = true;
        try
        {
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? box.Text : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            IsOpen = false;
        }
    }

    public static async Task<bool> ShowConfirmAsync(
        FrameworkElement owner, string title, string message, string confirmText = "确定")
    {
        if (owner.XamlRoot is null) return false;

        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        IsOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            return false;
        }
        finally
        {
            IsOpen = false;
        }
    }

    public static Task ShowErrorAsync(FrameworkElement owner, string title, string message)
        => ShowMessageAsync(owner, title, message);

    public static Task ShowInfoAsync(FrameworkElement owner, string title, string message)
        => ShowMessageAsync(owner, title, message);

    private static async Task ShowMessageAsync(FrameworkElement owner, string title, string message)
    {
        if (owner.XamlRoot is null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
        };

        IsOpen = true;
        try
        {
            await dialog.ShowAsync();
        }
        catch
        {
            // A dialog that cannot open must not crash the shell.
        }
        finally
        {
            IsOpen = false;
        }
    }
}
```

- [ ] **Step 3: Extend the Space guard in `MainWindow.xaml.cs`**

Add `using System.Linq;`, `using Microsoft.UI.Xaml.Controls.Primitives;`, `using Microsoft.UI.Xaml.Media;`, `using DPlayer.WinUI.Services;`.

Inside `RootGrid_KeyDown`, after the `Space` check and after resolving `focused`:

```csharp
if (WinUiDialogService.IsOpen || IsContentDialogOpen()) return;
if (IsTextInputFocused(focused)) return;
```

Add the two helpers:

```csharp
private static bool IsTextInputFocused(DependencyObject focused)
    => focused is TextBox or PasswordBox or RichEditBox or ComboBox
       || VisualTree.FindAncestor<TextBox>(focused) is not null
       || VisualTree.FindAncestor<PasswordBox>(focused) is not null
       || VisualTree.FindAncestor<RichEditBox>(focused) is not null
       || VisualTree.FindAncestor<ComboBox>(focused) is not null;

private bool IsContentDialogOpen()
    => RootGrid.XamlRoot is { } root
       && VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
           .Any(p => p.Child is not null && VisualTree.FindDescendant<ContentDialog>(p.Child) is not null);
```

- [ ] **Step 4: Add the NavRail toolbar and active marker**

In `NavRail.xaml` resources add:

```xml
<views:BoolToVisibilityConverter x:Key="BoolToVisibility" />
```

Header row (replace the existing two-column header Grid):

```xml
<Grid Grid.Row="0" Padding="{StaticResource RailHeaderPadding}"
      ColumnSpacing="{StaticResource SpacingXs}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>

    <Button x:Name="FoldButton" Width="{StaticResource FoldButtonSize}" Height="{StaticResource FoldButtonSize}"
            Style="{StaticResource IconButtonStyle}" Click="FoldButton_Click"
            AutomationProperties.Name="折叠或展开歌单栏" ToolTipService.ToolTip="折叠或展开歌单栏">
        <FontIcon x:Name="FoldButtonGlyph" Glyph="&#xE76B;" FontSize="{StaticResource FontSizeCaption}" />
    </Button>

    <TextBlock x:Name="HeaderLabel" Grid.Column="1" Text="歌单" Tag="Fade"
               FontSize="{StaticResource FontSizeCaption}"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}"
               VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />

    <StackPanel x:Name="RailActions" Grid.Column="2" Orientation="Horizontal"
                Spacing="{StaticResource SpacingXs}" VerticalAlignment="Center">
        <Button x:Name="AddPlaylistButton" Width="{StaticResource FoldButtonSize}" Height="{StaticResource FoldButtonSize}"
                Style="{StaticResource IconButtonStyle}" Click="AddPlaylistButton_Click"
                AutomationProperties.Name="新建歌单" ToolTipService.ToolTip="新建歌单">
            <FontIcon Glyph="&#xE710;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>
        <Button x:Name="ImportPlaylistButton" Width="{StaticResource FoldButtonSize}" Height="{StaticResource FoldButtonSize}"
                Style="{StaticResource IconButtonStyle}" Click="ImportPlaylistButton_Click"
                AutomationProperties.Name="导入播放列表" ToolTipService.ToolTip="导入播放列表（M3U / M3U8 / PLS）">
            <FontIcon Glyph="&#xE8E5;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>
        <Button x:Name="DeletePlaylistButton" Width="{StaticResource FoldButtonSize}" Height="{StaticResource FoldButtonSize}"
                Style="{StaticResource IconButtonStyle}" Click="DeletePlaylistButton_Click"
                AutomationProperties.Name="删除当前歌单" ToolTipService.ToolTip="删除当前歌单">
            <FontIcon Glyph="&#xE74D;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>
    </StackPanel>
</Grid>
```

Add `DoubleTapped="PlaylistList_DoubleTapped"` to the `PlaylistList` element.

In `RailItemTemplate`, add the marker as the first child of the root Grid (it overlays the 12px left padding, no layout cost):

```xml
<FontIcon Glyph="&#xE768;" FontSize="8" Width="8" Margin="-8,0,0,0"
          HorizontalAlignment="Left" VerticalAlignment="Center"
          Foreground="{ThemeResource AccentFillColorDefaultBrush}" IsHitTestVisible="False"
          Visibility="{x:Bind IsActivePlaylist, Converter={StaticResource BoolToVisibility}, Mode=OneWay}" />
```

- [ ] **Step 5: Wire NavRail behavior**

In `NavRail.xaml.cs` add `using DPlayer.WinUI.Services;`, `using Microsoft.UI.Xaml.Input;`, and:

- In `SetCollapsed`, before collecting fade targets:

```csharp
if (collapsed)
{
    RailActions.Visibility = Visibility.Collapsed;
    RailActions.IsHitTestVisible = false;
}
else
{
    RailActions.Visibility = Visibility.Visible;
    RailActions.IsHitTestVisible = true;
}
```

- In `ApplyFoldEndState`, after setting `Rail.Width`:

```csharp
RailActions.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
RailActions.IsHitTestVisible = !collapsed;
```

- Add handlers:

```csharp
private async void AddPlaylistButton_Click(object sender, RoutedEventArgs e)
{
    var name = await WinUiDialogService.ShowPromptAsync(this, "新建歌单", "名称", "新歌单");
    if (name is null) return;
    Playlists.AddPlaylistCommand.Execute(name);
}

private async void ImportPlaylistButton_Click(object sender, RoutedEventArgs e)
{
    try
    {
        var report = await Playlists.ImportPlaylistFileAsync();
        if (report is null) return;
        await WinUiDialogService.ShowInfoAsync(this, PlaylistImportReportFormatter.DialogTitle,
            PlaylistImportReportFormatter.Format(new[] { report }));
    }
    catch (Exception ex)
    {
        await WinUiDialogService.ShowErrorAsync(this, "导入失败", ex.Message);
    }
}

private async void DeletePlaylistButton_Click(object sender, RoutedEventArgs e)
{
    var target = Playlists.ViewedPlaylist;
    if (target is null) return;
    if (!await WinUiDialogService.ShowConfirmAsync(this, "删除歌单",
            $"确定删除歌单「{target.Name}」吗？此操作不可撤销。", "删除")) return;
    Playlists.RemovePlaylistCommand.Execute(target);
}

private async void PlaylistList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
{
    if (e.OriginalSource is not FrameworkElement source) return;
    if (VisualTree.FindAncestor<ListViewItem>(source)?.Content is not PlaylistViewModel target) return;
    e.Handled = true;

    var name = await WinUiDialogService.ShowPromptAsync(this, "重命名歌单", "新名称", target.Name);
    if (name is null) return;
    Playlists.RenamePlaylistCommand.Execute((target, name));
}
```

- [ ] **Step 6: Build and smoke-test**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test D-player.slnf -c Debug -v q
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
powershell -File tools/verify-gates.ps1 -Full
```

Manual smoke (launch `dotnet run --project D-player.WinUI/D-player.WinUI.csproj -c Debug`):
1. `+` opens the prompt; create `测试歌单`.
2. Double-click the row, rename to `测试歌单 2`; confirm the name updates and the list keeps the selection.
3. `−` confirms and deletes; the default playlist remains.
4. With focus in the rename TextBox, type a space; playback must **not** toggle.
If no GUI automation is available, boot the shell, verify it stays alive, and report the four interactions as NOT VERIFIED for the user.

- [ ] **Step 7: CRLF check and commit**

```bash
git ls-files --eol D-player.WinUI/Services/WinUiDialogService.cs D-player.WinUI/Views/Converters.cs D-player.WinUI/Views/NavRail.xaml D-player.WinUI/Views/NavRail.xaml.cs D-player.WinUI/MainWindow.xaml.cs
git add D-player.WinUI/Services/WinUiDialogService.cs D-player.WinUI/Views/Converters.cs D-player.WinUI/Views/NavRail.xaml D-player.WinUI/Views/NavRail.xaml.cs D-player.WinUI/MainWindow.xaml.cs
git commit -m "feat(winui): add playlist management and dialog host"
```

---

### Task 3: TrackList import/export toolbar

**Files:**
- Modify: `D-player.WinUI/Views/TrackList.xaml(.cs)`
- Modify: `D-player.WinUI/Theme/Tokens.xaml` (`TrackToolbarHeight=40`, `ToolbarButtonSize=28`)

**Interfaces:**
- Consumes: `PlaylistViewModel.AddToQueueCommand`, `ImportFolderToCurrentCommand`, `ImportPlaylistFileAsync()`, `ExportPlaylistFileAsync()`, `ClearQueueCommand`; `WinUiDialogService`; `PlaylistImportReportFormatter`.
- Produces: a 40px toolbar row above the header; busy state; updated empty-state copy.

- [ ] **Step 1: Add the tokens**

In `Theme/Tokens.xaml`:

```xml
<x:Double x:Key="TrackToolbarHeight">40</x:Double>
<x:Double x:Key="ToolbarButtonSize">28</x:Double>
```

- [ ] **Step 2: Add the toolbar row**

In `TrackList.xaml`, change the root Grid to three rows:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
</Grid.RowDefinitions>
```

Insert the toolbar at row 0. Then change the existing elements' row assignments exactly:

```xml
<!-- existing Header -->
<Grid x:Name="Header" Grid.Row="1" ...>
<!-- existing ListView -->
<ListView x:Name="TrackItems" Grid.Row="2" ...>
<!-- existing EmptyHint -->
<StackPanel x:Name="EmptyHint" Grid.Row="2" ...>
```

Toolbar markup:

```xml
<Grid Grid.Row="0" Height="{StaticResource TrackToolbarHeight}"
      Padding="{StaticResource TrackListHeaderPadding}"
      ColumnSpacing="{StaticResource SpacingSm}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>

    <StackPanel Orientation="Horizontal" Spacing="{StaticResource SpacingXs}">
        <Button x:Name="AddFilesButton" Width="{StaticResource ToolbarButtonSize}" Height="{StaticResource ToolbarButtonSize}"
                Style="{StaticResource IconButtonStyle}" Click="AddFilesButton_Click"
                AutomationProperties.Name="添加文件" ToolTipService.ToolTip="添加文件（多选）">
            <FontIcon Glyph="&#xE710;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>
        <Button x:Name="ImportFolderButton" Width="{StaticResource ToolbarButtonSize}" Height="{StaticResource ToolbarButtonSize}"
                Style="{StaticResource IconButtonStyle}" Click="ImportFolderButton_Click"
                AutomationProperties.Name="导入文件夹" ToolTipService.ToolTip="导入文件夹（递归扫描，追加到当前歌单）">
            <FontIcon Glyph="&#xE8B7;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>
        <Button x:Name="ImportListButton" Width="{StaticResource ToolbarButtonSize}" Height="{StaticResource ToolbarButtonSize}"
                Style="{StaticResource IconButtonStyle}" Click="ImportListButton_Click"
                AutomationProperties.Name="导入播放列表" ToolTipService.ToolTip="导入播放列表（M3U / M3U8 / PLS，追加）">
            <FontIcon Glyph="&#xE8E5;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>
    </StackPanel>

    <ProgressRing x:Name="ImportProgress" Grid.Column="1" Width="16" Height="16"
                  IsActive="False" HorizontalAlignment="Center" VerticalAlignment="Center" />

    <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="{StaticResource SpacingXs}">
        <Button x:Name="ExportButton" Width="{StaticResource ToolbarButtonSize}" Height="{StaticResource ToolbarButtonSize}"
                Style="{StaticResource IconButtonStyle}" Click="ExportButton_Click"
                AutomationProperties.Name="导出播放列表" ToolTipService.ToolTip="导出为 M3U8">
            <FontIcon Glyph="&#xE74E;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>
        <Button x:Name="ClearButton" Width="{StaticResource ToolbarButtonSize}" Height="{StaticResource ToolbarButtonSize}"
                Style="{StaticResource IconButtonStyle}" Click="ClearButton_Click"
                AutomationProperties.Name="清空当前歌单" ToolTipService.ToolTip="清空当前歌单">
            <FontIcon Glyph="&#xE74D;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>
    </StackPanel>
</Grid>
```

- [ ] **Step 3: Implement the toolbar state and handlers**

Add `using DPlayer.WinUI.Services;` and `using DPlayer.ViewModels;` (already present) to `TrackList.xaml.cs`.

Add the field and methods:

```csharp
private bool _isImporting;

private void UpdateToolbarState()
{
    var pl = Playlists.ViewedPlaylist;
    var hasView = pl is not null;
    var hasTracks = pl is { Queue.Count: > 0 };

    AddFilesButton.IsEnabled = ImportFolderButton.IsEnabled = ImportListButton.IsEnabled
        = hasView && !_isImporting;
    ExportButton.IsEnabled = ClearButton.IsEnabled = hasTracks && !_isImporting;
}

private async Task RunImportAsync(Func<Task> action)
{
    if (_isImporting) return;
    _isImporting = true;
    ImportProgress.IsActive = true;
    UpdateToolbarState();
    try
    {
        await action();
    }
    catch (Exception ex)
    {
        await WinUiDialogService.ShowErrorAsync(this, "导入失败", ex.Message);
    }
    finally
    {
        _isImporting = false;
        ImportProgress.IsActive = false;
        UpdateToolbarState();
    }
}

private async void AddFilesButton_Click(object sender, RoutedEventArgs e)
{
    if (Playlists.ViewedPlaylist is not { } pl) return;
    await RunImportAsync(() => pl.AddToQueueCommand.ExecuteAsync(null));
}

private async void ImportFolderButton_Click(object sender, RoutedEventArgs e)
{
    if (Playlists.ViewedPlaylist is not { } pl) return;
    await RunImportAsync(() => pl.ImportFolderToCurrentCommand.ExecuteAsync(null));
}

private async void ImportListButton_Click(object sender, RoutedEventArgs e)
{
    if (Playlists.ViewedPlaylist is not { } pl) return;
    await RunImportAsync(async () =>
    {
        var report = await pl.ImportPlaylistFileAsync();
        if (report is not null)
            await WinUiDialogService.ShowInfoAsync(this, PlaylistImportReportFormatter.DialogTitle,
                PlaylistImportReportFormatter.Format(new[] { report }));
    });
}

private async void ExportButton_Click(object sender, RoutedEventArgs e)
{
    if (Playlists.ViewedPlaylist is not { } pl || pl.Queue.Count == 0) return;
    try
    {
        var error = await pl.ExportPlaylistFileAsync();
        if (error is not null)
            await WinUiDialogService.ShowErrorAsync(this, "导出失败", error);
    }
    catch (Exception ex)
    {
        await WinUiDialogService.ShowErrorAsync(this, "导出失败", ex.Message);
    }
}

private async void ClearButton_Click(object sender, RoutedEventArgs e)
{
    if (Playlists.ViewedPlaylist is not { } pl || pl.Queue.Count == 0) return;
    if (!await WinUiDialogService.ShowConfirmAsync(this, "清空歌单",
            $"确定清空歌单「{pl.Name}」吗？此操作不可撤销。", "清空")) return;
    pl.ClearQueueCommand.Execute(null);
}
```

Call `UpdateToolbarState()` from `ResyncView()` (after `UpdateEmptyHint()`) and from `OnViewedQueueChanged()`:

```csharp
private void OnViewedQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    UpdateEmptyHint();
    UpdateToolbarState();
}
```

- [ ] **Step 4: Update the empty-state copy**

Replace the second empty-state line:

```xml
<TextBlock Text="点击上方「添加文件」或「导入文件夹」开始" HorizontalAlignment="Center" MaxWidth="360"
           TextWrapping="Wrap"
           Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
```

- [ ] **Step 5: Build, boot and smoke-test**

Run the four gate commands from Global Constraints. Then launch the shell and verify:
1. Toolbar buttons are visible, have tooltips, and `导出`/`清空` are disabled on an empty playlist.
2. `添加文件` opens the real Windows picker; cancel returns without changing the queue.
3. `导入文件夹` opens the real folder picker; cancel is a no-op.
4. `导出` on a non-empty playlist opens the save picker.
If no GUI automation is available, boot the shell, verify it stays alive, and report the picker interactions as NOT VERIFIED for the user.

- [ ] **Step 6: CRLF check and commit**

```bash
git ls-files --eol D-player.WinUI/Views/TrackList.xaml D-player.WinUI/Views/TrackList.xaml.cs D-player.WinUI/Theme/Tokens.xaml
git add D-player.WinUI/Views/TrackList.xaml D-player.WinUI/Views/TrackList.xaml.cs D-player.WinUI/Theme/Tokens.xaml
git commit -m "feat(winui): add import and export toolbar"
```

---

### Task 4: PlayerBar transport controls (64px single row)

**Files:**
- Modify: `D-player.WinUI/Views/PlayerBar.xaml` (whole layout, keep the progress slider block and play-button animations)
- Modify: `D-player.WinUI/Views/PlayerBar.xaml.cs`
- Modify: `D-player.WinUI/Theme/Tokens.xaml` only if a new size is needed (none required)

**Interfaces:**
- Consumes: `Playlists.ViewedPlaylist.PrevTrackCommand` / `NextTrackCommand`, `Playlists.ToggleShuffleCommand`, `Playlists.CycleRepeatCommand`, `Playlists.ShuffleEnabled`, `Playlists.RepeatMode`, `Player.ToggleMuteCommand`, `Player.Volume`, `Player.IsMuted`.
- Produces: `PrevButton`, `PlayPauseButton`, `NextButton`, `ShuffleButton`, `RepeatButton`, `MuteButton`, `VolumeSlider`; `SyncTransportVisuals()`.

- [ ] **Step 1: Replace the PlayerBar XAML layout**

Keep the existing `PositionSlider` attributes/handlers and the `PlayPauseButton` style/animations exactly as they are. The new structure is:

```xml
<Grid Height="{StaticResource PlayerBarHeight}" Padding="12,6"
      ColumnSpacing="{StaticResource SpacingSm}"
      Background="{ThemeResource LayerFillColorDefaultBrush}">
    <VisualStateManager.VisualStateGroups>
        <VisualStateGroup x:Name="BarWidthStates">
            <VisualState x:Name="Wide">
                <VisualState.StateTriggers>
                    <AdaptiveTrigger MinWindowWidth="1100" />
                </VisualState.StateTriggers>
            </VisualState>
            <VisualState x:Name="Medium">
                <VisualState.StateTriggers>
                    <AdaptiveTrigger MinWindowWidth="860" />
                </VisualState.StateTriggers>
                <VisualState.Setters>
                    <Setter Target="VolumeSlider.Visibility" Value="Collapsed" />
                </VisualState.Setters>
            </VisualState>
            <VisualState x:Name="Narrow">
                <VisualState.StateTriggers>
                    <AdaptiveTrigger MinWindowWidth="0" />
                </VisualState.StateTriggers>
                <VisualState.Setters>
                    <Setter Target="VolumeSlider.Visibility" Value="Collapsed" />
                    <Setter Target="NowPlayingLabel.Visibility" Value="Collapsed" />
                </VisualState.Setters>
            </VisualState>
        </VisualStateGroup>
    </VisualStateManager.VisualStateGroups>

    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>

    <TextBlock x:Name="NowPlayingLabel" Grid.Column="0" Width="180"
               Text="{x:Bind NowPlayingText, Mode=OneWay}"
               TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />

    <StackPanel Grid.Column="1" Orientation="Horizontal"
                Spacing="{StaticResource SpacingXs}" VerticalAlignment="Center">
        <Button x:Name="PrevButton" Width="32" Height="32" Style="{StaticResource IconButtonStyle}"
                Click="PrevButton_Click" AutomationProperties.Name="上一首" ToolTipService.ToolTip="上一首">
            <FontIcon Glyph="&#xE892;" FontSize="{StaticResource FontSizeBody}" />
        </Button>

        <Button x:Name="PlayPauseButton" Click="PlayPause_Click"
                Style="{StaticResource PlayPauseButtonStyle}"
                IsEnabled="{x:Bind HasPlayableSource, Mode=OneWay}"
                VerticalAlignment="Center"
                RenderTransformOrigin="0.5,0.5"
                PointerEntered="PlayPauseButton_PointerEntered"
                PointerExited="PlayPauseButton_PointerExited"
                PointerPressed="PlayPauseButton_PointerPressed"
                PointerReleased="PlayPauseButton_PointerReleased"
                AutomationProperties.Name="播放/暂停">
            <Button.RenderTransform>
                <ScaleTransform x:Name="PlayPauseScale" ScaleX="1" ScaleY="1" />
            </Button.RenderTransform>
            <Grid>
                <FontIcon x:Name="PlayIcon" Glyph="&#xE768;" Foreground="{ThemeResource TextOnAccentFillColorPrimaryBrush}" Opacity="1" />
                <FontIcon x:Name="PauseIcon" Glyph="&#xE769;" Foreground="{ThemeResource TextOnAccentFillColorPrimaryBrush}" Opacity="0" />
            </Grid>
        </Button>

        <Button x:Name="NextButton" Width="32" Height="32" Style="{StaticResource IconButtonStyle}"
                Click="NextButton_Click" AutomationProperties.Name="下一首" ToolTipService.ToolTip="下一首">
            <FontIcon Glyph="&#xE893;" FontSize="{StaticResource FontSizeBody}" />
        </Button>

        <Button x:Name="ShuffleButton" Width="32" Height="32" Style="{StaticResource IconButtonStyle}"
                Click="ShuffleButton_Click" AutomationProperties.Name="随机播放：关" ToolTipService.ToolTip="随机播放：关">
            <FontIcon x:Name="ShuffleIcon" Glyph="&#xE8B1;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>

        <Button x:Name="RepeatButton" Width="32" Height="32" Style="{StaticResource IconButtonStyle}"
                Click="RepeatButton_Click" AutomationProperties.Name="循环：关" ToolTipService.ToolTip="循环：关">
            <FontIcon x:Name="RepeatIcon" Glyph="&#xE8EE;" FontSize="{StaticResource FontSizeCaption}" />
        </Button>
    </StackPanel>

    <Grid Grid.Column="2" ColumnSpacing="{StaticResource SpacingSm}" VerticalAlignment="Center">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>

        <!-- 保留 Phase 21 的全部属性与事件：StepFrequency、拖动/单击两条路径、Value 绑定、HelpText -->
        <Slider x:Name="PositionSlider" Minimum="0" Maximum="1" StepFrequency="0.001"
                SmallChange="0.005" LargeChange="0.05"
                Style="{StaticResource ProgressSliderStyle}"
                IsEnabled="{x:Bind HasDuration, Mode=OneWay}"
                Value="{x:Bind Player.PositionNormalized, Mode=OneWay}"
                ValueChanged="Position_Changed"
                AutomationProperties.Name="播放进度"
                AutomationProperties.HelpText="方向键微调约 0.5%，PageUp/PageDown 约 5%" />

        <TextBlock Grid.Column="1" Text="{x:Bind TimeText, Mode=OneWay}" VerticalAlignment="Center" />
    </Grid>

    <StackPanel Grid.Column="3" Orientation="Horizontal"
                Spacing="{StaticResource SpacingXs}" VerticalAlignment="Center">
        <Button x:Name="MuteButton" Width="32" Height="32" Style="{StaticResource IconButtonStyle}"
                Click="MuteButton_Click" AutomationProperties.Name="静音" ToolTipService.ToolTip="静音">
            <FontIcon x:Name="MuteIcon" Glyph="&#xE767;" FontSize="{StaticResource FontSizeBody}" />
        </Button>

        <Slider x:Name="VolumeSlider" Width="88" Minimum="0" Maximum="1" StepFrequency="0.01"
                ValueChanged="VolumeSlider_ValueChanged"
                AutomationProperties.Name="音量" VerticalAlignment="Center" />
    </StackPanel>
</Grid>
```

- [ ] **Step 2: Add the transport state sync**

In `PlayerBar.xaml.cs` add `using System.Globalization;`, `using DPlayer.Models;`, `using Microsoft.UI.Xaml.Automation;`, `using Microsoft.UI.Xaml.Media;`, and:

```csharp
private bool _syncingVolume;
private string _lastTransportSignature = string.Empty;

private static Brush ThemeBrush(string key, Brush fallback)
{
    try { return Application.Current.Resources[key] as Brush ?? fallback; }
    catch { return fallback; }
}

private readonly Brush _accentBrush =
    ThemeBrush("AccentFillColorDefaultBrush", new SolidColorBrush(Microsoft.UI.Colors.White));
private readonly Brush _secondaryBrush =
    ThemeBrush("TextFillColorSecondaryBrush", new SolidColorBrush(Microsoft.UI.Colors.Gray));

private void SyncTransportVisuals()
{
    var viewed = Playlists.ViewedPlaylist;
    var signature =
        $"{Playlists.ShuffleEnabled}|{Playlists.RepeatMode}|{Player.IsMuted}|" +
        $"{Player.Volume.ToString("F3", CultureInfo.InvariantCulture)}|" +
        $"{viewed?.Id ?? "-"}|{viewed?.HasCurrentTrack ?? false}";
    if (signature == _lastTransportSignature) return;
    _lastTransportSignature = signature;

    var hasTrack = viewed?.HasCurrentTrack == true;
    PrevButton.IsEnabled = hasTrack;
    NextButton.IsEnabled = hasTrack;

    ShuffleIcon.Foreground = Playlists.ShuffleEnabled ? _accentBrush : _secondaryBrush;
    var shuffleText = Playlists.ShuffleEnabled ? "随机播放：开" : "随机播放：关";
    ToolTipService.SetToolTip(ShuffleButton, shuffleText);
    AutomationProperties.SetName(ShuffleButton, shuffleText);

    var repeat = Playlists.RepeatMode;
    RepeatIcon.Glyph = ((char)(repeat == RepeatMode.One ? 0xE8ED : 0xE8EE)).ToString();
    RepeatIcon.Foreground = repeat == RepeatMode.Off ? _secondaryBrush : _accentBrush;
    var repeatText = repeat switch
    {
        RepeatMode.Off => "循环：关",
        RepeatMode.List => "循环：列表",
        RepeatMode.One => "循环：单曲",
        _ => "循环：关",
    };
    ToolTipService.SetToolTip(RepeatButton, repeatText);
    AutomationProperties.SetName(RepeatButton, repeatText);

    var muteText = Player.IsMuted ? "取消静音" : "静音";
    MuteIcon.Glyph = ((char)(Player.IsMuted ? 0xE74F : 0xE767)).ToString();
    ToolTipService.SetToolTip(MuteButton, muteText);
    AutomationProperties.SetName(MuteButton, muteText);

    _syncingVolume = true;
    VolumeSlider.Value = Player.Volume;
    _syncingVolume = false;
}
```

In the `PlayerBar` constructor, after the existing subscriptions, add the state subscription and the initial sync:

```csharp
player.PropertyChanged += Player_PropertyChanged;
Playlists.PropertyChanged += (_, _) => RefreshView();
// Queue/state changes do not raise Playlists.PropertyChanged; StateChanged does.
Playlists.StateChanged += (_, _) => RefreshView();
SyncTransportVisuals();
```

Then call `SyncTransportVisuals()` at the end of `RefreshView()`.

- [ ] **Step 3: Add the click and volume handlers**

```csharp
private void PrevButton_Click(object sender, RoutedEventArgs e)
    => Playlists.ViewedPlaylist?.PrevTrackCommand.Execute(null);

private void NextButton_Click(object sender, RoutedEventArgs e)
    => Playlists.ViewedPlaylist?.NextTrackCommand.Execute(null);

private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    => Playlists.ToggleShuffleCommand.Execute(null);

private void RepeatButton_Click(object sender, RoutedEventArgs e)
    => Playlists.CycleRepeatCommand.Execute(null);

private void MuteButton_Click(object sender, RoutedEventArgs e)
    => Player.ToggleMuteCommand.Execute(null);

private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
{
    if (_syncingVolume) return;
    Player.Volume = (float)e.NewValue;
}
```

- [ ] **Step 4: Build and inspect the bar**

Run the four gate commands. Then launch the shell and verify:
1. Bar is 64px (pixel/UIA measurement).
2. Prev/Next disabled with no current track; enabled after playing.
3. Shuffle toggles color + tooltip; repeat cycles Off → List → One with the correct icon; mute toggles icon and volume.
4. Narrow the window below 1100 and 860: volume slider hides, then the now-playing text hides; no overlap or horizontal scroll.
If no GUI automation is available, boot the shell and report the visual checks as NOT VERIFIED for the user.

- [ ] **Step 5: CRLF check and commit**

```bash
git ls-files --eol D-player.WinUI/Views/PlayerBar.xaml D-player.WinUI/Views/PlayerBar.xaml.cs
git add D-player.WinUI/Views/PlayerBar.xaml D-player.WinUI/Views/PlayerBar.xaml.cs
git commit -m "feat(winui): complete the 64px transport bar"
```

---

### Task 5: Settings dialog + window geometry

**Files:**
- Create: `D-player.WinUI/Services/WinUiAppPaths.cs`
- Create: `D-player.WinUI/Views/Dialogs/SettingsDialog.xaml(.cs)`
- Modify: `D-player.WinUI/Views/PlayerBar.xaml(.cs)` (settings button)
- Modify: `D-player.WinUI/MainWindow.xaml.cs` (constructor + geometry + close guard)
- Modify: `D-player.WinUI/App.xaml.cs` (pass `ISettingsPersistence`; use `WinUiAppPaths`)

**Interfaces:**
- Produces:
  ```csharp
  internal static class WinUiAppPaths
  {
      public const string DataFolderName = "D-player-winui";
      public static string DataDirectory { get; }
      public static string LogDirectory { get; }
  }
  public sealed partial class SettingsDialog : ContentDialog
  {
      public SettingsDialog(PlayerViewModel player);
  }
  public MainWindow(MainViewModel vm, ISettingsPersistence settings);
  ```
- Consumes: `ISettingsPersistence.LoadAsync/UpdateAsync`, `AppSettings.WindowLeft/Top/Width/Height`, `PlayerViewModel.Volume`.

- [ ] **Step 1: Add the path constants**

`D-player.WinUI/Services/WinUiAppPaths.cs`:

```csharp
using System.IO;

namespace DPlayer.WinUI.Services;

internal static class WinUiAppPaths
{
    public const string DataFolderName = "D-player-winui";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
}
```

In `App.xaml.cs`, replace `FolderName = "D-player-winui"` with `FolderName = WinUiAppPaths.DataFolderName`.

- [ ] **Step 2: Create the settings dialog**

`D-player.WinUI/Views/Dialogs/SettingsDialog.xaml`:

```xml
<ContentDialog
    x:Class="DPlayer.WinUI.Views.Dialogs.SettingsDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="设置"
    CloseButtonText="完成"
    DefaultButton="Close">

    <StackPanel Spacing="{StaticResource SpacingLg}" Width="360">
        <StackPanel Spacing="{StaticResource SpacingSm}">
            <TextBlock Text="音量" Style="{StaticResource BodyStrongTextBlockStyle}" />
            <Grid ColumnSpacing="{StaticResource SpacingSm}">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <Slider x:Name="VolumeSlider" Minimum="0" Maximum="1" StepFrequency="0.01"
                        ValueChanged="VolumeSlider_ValueChanged" AutomationProperties.Name="音量" />
                <TextBlock x:Name="VolumePercent" Grid.Column="1" VerticalAlignment="Center" />
            </Grid>
        </StackPanel>

        <StackPanel Spacing="{StaticResource SpacingSm}">
            <TextBlock Text="音频输出" Style="{StaticResource BodyStrongTextBlockStyle}" />
            <TextBlock Text="系统默认 — WASAPI 共享" Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
            <TextBlock Text="输出设备选择将在后续版本开放"
                       FontSize="{StaticResource FontSizeCaption}"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
        </StackPanel>

        <StackPanel Spacing="{StaticResource SpacingSm}">
            <TextBlock Text="日志" Style="{StaticResource BodyStrongTextBlockStyle}" />
            <TextBlock x:Name="LogPathText" TextWrapping="Wrap"
                       FontSize="{StaticResource FontSizeCaption}"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
            <Button Content="打开日志文件夹" Click="OpenLogFolder_Click" />
        </StackPanel>
    </StackPanel>
</ContentDialog>
```

`D-player.WinUI/Views/Dialogs/SettingsDialog.xaml.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using DPlayer.ViewModels;
using DPlayer.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DPlayer.WinUI.Views.Dialogs;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly PlayerViewModel _player;
    private bool _ready;

    public SettingsDialog(PlayerViewModel player)
    {
        _player = player;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        VolumeSlider.Value = _player.Volume;
        UpdatePercent();
        LogPathText.Text = WinUiAppPaths.LogDirectory;
        _ready = true;
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePercent();
        if (!_ready) return;
        _player.Volume = (float)e.NewValue;   // 既有 setter 负责同步播放服务与落盘
    }

    private void UpdatePercent()
        => VolumePercent.Text = $"{VolumeSlider.Value:P0}";

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(WinUiAppPaths.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = WinUiAppPaths.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WinUI 壳] 打开日志文件夹失败：{ex}");
        }
    }
}
```

- [ ] **Step 3: Add the settings button to PlayerBar**

In the right-side StackPanel of `PlayerBar.xaml`, after `VolumeSlider`:

```xml
<Button x:Name="SettingsButton" Width="32" Height="32" Style="{StaticResource IconButtonStyle}"
        Click="SettingsButton_Click" AutomationProperties.Name="设置" ToolTipService.ToolTip="设置">
    <FontIcon Glyph="&#xE713;" FontSize="{StaticResource FontSizeBody}" />
</Button>
```

In `PlayerBar.xaml.cs` add `using System.Diagnostics;` and `using DPlayer.WinUI.Views.Dialogs;` and:

```csharp
private async void SettingsButton_Click(object sender, RoutedEventArgs e)
{
    var dialog = new SettingsDialog(Player) { XamlRoot = XamlRoot };
    if (dialog.XamlRoot is null) return;
    try
    {
        await dialog.ShowAsync();
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[WinUI 壳] 打开设置失败：{ex}");
    }
}
```

- [ ] **Step 4: Restore/save window geometry**

In `MainWindow.xaml.cs` add `using System.Diagnostics;`, `using System.Linq;`, `using DPlayer.Configuration;`, `using DPlayer.Services;`, `using DPlayer.WinUI.Services;`, `using Microsoft.UI.Windowing;`, `using Windows.Graphics;`.

Change the constructor signature and store the service:

```csharp
private readonly ISettingsPersistence _settings;

private const int DefaultWindowWidth = 1200;
private const int DefaultWindowHeight = 800;
private const int MinWindowWidth = 800;
private const int MinWindowHeight = 500;

private bool _cleanupCompleted;

public MainWindow(MainViewModel vm, ISettingsPersistence settings)
{
    _vm = vm;
    _settings = settings;
    InitializeComponent();
    // ... existing Title / Mica / control assembly / event wiring ...
    RestoreWindowGeometry();
}
```

Add:

```csharp
private void RestoreWindowGeometry()
{
    try
    {
        AppSettings saved;
        try { saved = _settings.LoadAsync().GetAwaiter().GetResult(); }
        catch { return; }

        var width = saved.WindowWidth >= MinWindowWidth ? (int)Math.Round(saved.WindowWidth) : DefaultWindowWidth;
        var height = saved.WindowHeight >= MinWindowHeight ? (int)Math.Round(saved.WindowHeight) : DefaultWindowHeight;

        var left = (int)Math.Round(saved.WindowLeft);
        var top = (int)Math.Round(saved.WindowTop);
        if (saved.WindowWidth < MinWindowWidth || saved.WindowHeight < MinWindowHeight
            || !IsWindowCenterVisible(left, top, width, height))
        {
            (left, top) = CenterOnPrimary(width, height);
        }

        AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
    }
    catch (Exception ex)
    {
        // Geometry failures must never prevent startup; Task 6 routes this to AppLog.
        Debug.WriteLine($"[WinUI 壳] 恢复窗口几何失败：{ex}");
    }
}

private static bool IsWindowCenterVisible(int left, int top, int width, int height)
{
    var cx = left + width / 2;
    var cy = top + height / 2;
    return DisplayArea.FindAll().Any(area =>
    {
        var work = area.WorkArea;
        return cx >= work.X && cx < work.X + work.Width
            && cy >= work.Y && cy < work.Y + work.Height;
    });
}

private static (int Left, int Top) CenterOnPrimary(int width, int height)
{
    var work = DisplayArea.Primary.WorkArea;
    return (work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2);
}

private async Task SaveWindowGeometryAsync()
{
    if (AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return;

    var position = AppWindow.Position;
    var size = AppWindow.Size;
    await _settings.UpdateAsync(s => s with
    {
        WindowLeft = position.X,
        WindowTop = position.Y,
        WindowWidth = size.Width,
        WindowHeight = size.Height
    });
}
```

Replace `AppWindow_Closing` with:

```csharp
private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
{
    if (_cleanupCompleted) return;                 // our own Close() after cleanup -> allow
    args.Cancel = true;
    if (_isClosing) return;                        // second user request while cleanup runs -> keep waiting
    _isClosing = true;

    try { await SaveWindowGeometryAsync(); }
    catch (Exception ex) { Debug.WriteLine($"[WinUI 壳] 保存窗口几何失败：{ex}"); }

    try { await _vm.CleanupAsync(); }
    catch { /* 关闭流程不打扰用户 */ }

    _cleanupCompleted = true;
    Close();
}
```

- [ ] **Step 5: Update `App.xaml.cs` window construction**

```csharp
var settings = _services.GetRequiredService<ISettingsPersistence>();
var window = new MainWindow(vm, settings);
_window = window;
_hwnd = WindowNative.GetWindowHandle(window);
window.Activate();
```

- [ ] **Step 6: Build and verify geometry**

Run the four gate commands. Then launch the shell:
1. Move/resize the window; close; reopen; verify the same position/size (UIA `GetWindowRect` or a screenshot).
2. Maximize; close; reopen; verify it restores the previous normal size, not the maximized size.
3. Open settings; change volume; verify playback volume changes immediately; reopen the app and verify the volume persists.
4. Log folder button opens Explorer at the log directory (the agent must not inspect the files).
If no GUI automation is available, boot the shell and report the interactions as NOT VERIFIED for the user.

- [ ] **Step 7: CRLF check and commit**

```bash
git ls-files --eol D-player.WinUI/Services/WinUiAppPaths.cs D-player.WinUI/Views/Dialogs/SettingsDialog.xaml D-player.WinUI/Views/Dialogs/SettingsDialog.xaml.cs D-player.WinUI/Views/PlayerBar.xaml D-player.WinUI/Views/PlayerBar.xaml.cs D-player.WinUI/MainWindow.xaml.cs D-player.WinUI/App.xaml.cs
git add D-player.WinUI/Services/WinUiAppPaths.cs D-player.WinUI/Views/Dialogs/SettingsDialog.xaml D-player.WinUI/Views/Dialogs/SettingsDialog.xaml.cs D-player.WinUI/Views/PlayerBar.xaml D-player.WinUI/Views/PlayerBar.xaml.cs D-player.WinUI/MainWindow.xaml.cs D-player.WinUI/App.xaml.cs
git commit -m "feat(winui): add settings and persist window geometry"
```

---

### Task 6: Local log outlet

**Files:**
- Create: `D-player.WinUI/Services/AppLog.cs`
- Modify: `D-player.WinUI/App.xaml.cs`
- Modify: `D-player.WinUI/Services/WinUiFileDialogService.cs`
- Modify: `D-player.WinUI/MainWindow.xaml.cs`

**Interfaces:**
- Produces:
  ```csharp
  internal static class AppLog
  {
      public static void Initialize(string directory);
      public static void Write(string message, Exception? ex = null);
  }
  ```
- Consumes: `WinUiAppPaths.LogDirectory`, `IPlaybackService.PlaybackError`.

- [ ] **Step 1: Create `AppLog`**

`D-player.WinUI/Services/AppLog.cs`:

```csharp
using System.Globalization;
using System.IO;
using System.Text;

namespace DPlayer.WinUI.Services;

/// <summary>
/// 最小本地日志：追加到 app.log，超过 1 MiB 轮转到 app.log.1。
/// 任何 IO 失败都静默吞掉——日志不能成为新的崩溃源。
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "app.log");
        }
        catch
        {
            _path = null;
        }
    }

    public static void Write(string message, Exception? ex = null)
    {
        var path = _path;
        if (path is null) return;

        try
        {
            lock (Gate)
            {
                RotateIfNeeded(path);
                var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} " +
                           $"[T{Environment.CurrentManagedThreadId}] {message}" +
                           (ex is null ? string.Empty : Environment.NewLine + ex);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不改变应用行为
        }
    }

    private static void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes) return;

        var backup = path + ".1";
        if (File.Exists(backup)) File.Delete(backup);
        File.Move(path, backup);
    }
}
```

- [ ] **Step 2: Hook the app**

In `App.xaml.cs` constructor, after `InitializeComponent()`:

```csharp
AppLog.Initialize(WinUiAppPaths.LogDirectory);

UnhandledException += (_, e) => AppLog.Write("UnhandledException", e.Exception);
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    AppLog.Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);
TaskScheduler.UnobservedTaskException += (_, e) =>
    AppLog.Write("UnobservedTaskException", e.Exception);
```

Do **not** set `Handled` on the application event; a logged crash should still be visible.

In `OnLaunched`, after building the provider:

```csharp
_services.GetRequiredService<IPlaybackService>().PlaybackError += error =>
    AppLog.Write($"PlaybackError: {error}");
```

In `StartAsync`, replace the two `Debug.WriteLine` calls:

```csharp
if (restoreWarning is not null)
    AppLog.Write($"断点续播被跳过：{restoreWarning}");
...
catch (Exception ex)
{
    AppLog.Write("InitializeAsync 抛出", ex);
}
```

Add `AppLog.Write("WinUI shell starting")` right after `AppLog.Initialize(...)`.

- [ ] **Step 3: Route picker and geometry failures to the log**

In `WinUiFileDialogService.cs`, replace each `Debug.WriteLine(...)` with:

```csharp
AppLog.Write("FileOpenPicker 失败", ex);
AppLog.Write("FolderPicker 失败", ex);
AppLog.Write("FileSavePicker 失败", ex);
AppLog.Write("picker 跳过：窗口 HWND 尚未就绪");
AppLog.Write($"FileSavePicker 跳过：无法从过滤器解析扩展名 '{filter}'");
```

Remove the now-unused `using System.Diagnostics;`.

In `MainWindow.xaml.cs`, replace the geometry-save `Debug.WriteLine` with:

```csharp
AppLog.Write("保存窗口几何失败", ex);
```

- [ ] **Step 4: Build and verify the log path structurally**

Run the four gate commands. Launch the shell once. The agent must **not** read the log file (the data directory is off-limits by policy); instead verify:
1. The app starts and stays alive.
2. Settings → 打开日志文件夹 opens Explorer at the expected directory (visual check).
3. The log file content and `PlaybackError` record are handed to the user in the acceptance checklist.

- [ ] **Step 5: CRLF check and commit**

```bash
git ls-files --eol D-player.WinUI/Services/AppLog.cs D-player.WinUI/App.xaml.cs D-player.WinUI/Services/WinUiFileDialogService.cs D-player.WinUI/MainWindow.xaml.cs
git add D-player.WinUI/Services/AppLog.cs D-player.WinUI/App.xaml.cs D-player.WinUI/Services/WinUiFileDialogService.cs D-player.WinUI/MainWindow.xaml.cs
git commit -m "feat(winui): add a file log outlet"
```

---

### Task 7: Acceptance checklist + documentation sync

**Files:**
- Create: `docs/PHASE22-ACCEPTANCE.md`
- Modify: `docs/PHASE20-COMPARISON.md`
- Modify: `docs/PROJECT.md`
- Modify: `docs/COUPLING.md`
- Modify: `README.md`

**Interfaces:** none (documentation only).

- [ ] **Step 1: Create the acceptance checklist**

Create `docs/PHASE22-ACCEPTANCE.md` with the same format as `PHASE21-ACCEPTANCE.md`:

```markdown
# Phase 22 WinUI 功能补齐验收清单

执行者：acceptance agent（自动化部分）+ 用户（手测部分）。
规则：不修改任何产品文件；只追加本验收记录。证据截图/UIA dump 位于 `%TEMP%\p22-acc\`。
格式：`项 | PASS/FAIL/NOT VERIFIED | 证据方式 + 实测读数 | 原始证据路径`。

## A 导入与歌单管理
- A1 添加文件（多选）→ 曲目/时长/序号正确显示。
- A2 导入文件夹（递归）→ 只收 5 种音频后缀，追加到当前歌单。
- A3 导入 M3U/M3U8/PLS 到当前歌单 → 追加 + 报告含导入/跳过计数。
- A4 左栏导入播放列表 → 新建歌单并切换查看。
- A5 导出当前歌单 M3U8 → 文件存在，`#EXTINF` 与绝对路径正确。
- A6 新建/重命名/删除歌单；删最后一个自动重建默认歌单；删当前播放歌单后播放器停止。
- A7 ▶ 活跃标记随播放歌单切换；折叠态仍可见。

## B 播放控制
- B1 上一首/下一首在当前曲目为空时禁用，有曲目时切歌正确。
- B2 随机开/关：图标强调态、ToolTip、实际随机行为。
- B3 循环三态：Off/List/One 图标与 ToolTip 正确；单曲循环播完重播。
- B4 音量滑块与静音：即时生效、图标状态正确、重启后恢复。
- B5 底栏 64px 实测；宽/中/窄三档无重叠、无横向滚动。
- B6 设置对话框：音量立即生效；音频输出只读；日志入口可打开日志目录。

## C 持久化与窗口
- C1 移动/缩放窗口 → 关闭 → 重开：位置与尺寸恢复。
- C2 最大化关闭 → 重开：回到上次的普通几何。
- C3 断点续播回归：播放中关窗 → 重开按 ▶ 从断点继续。
- C4 屏外自愈（用户手测）：窗口中心不在任何显示器内 → 主屏居中。

## D 日志与门禁
- D1 启动后日志文件存在且含启动记录；损坏音频/缺失断点文件触发 `PlaybackError` 记录；日志 IO 失败不影响应用。
- D2 `powershell -File tools/verify-gates.ps1 -Full` → 三道门禁全绿。
- D3 WPF 壳的导入/导出/文件夹对话框行为不变（用户手测）。

## E 回归
- E1 `.slnf` 180 通过 / 0 失败；WinUI 单壳 0 警告 0 错误。
- E2 Phase 21 的折叠、Tab 顺序、空格播放/暂停、方向键步进、9 项动效抽样不回退。
- E3 Core 除 `IFileDialogService` 外无公开面变化；WPF 用户可见行为零变化。
```

- [ ] **Step 2: Update `docs/PHASE20-COMPARISON.md` §3**

Add a note under the §3 heading: “Phase 22 已闭合下列行；频谱/EQ/拖拽/排序/文件夹绑定管理仍留给后续阶段。” Then update these rows:

| 能力 | WPF | WinUI（Phase 22 后） | 备注 |
|---|---|---|---|
| 文件/文件夹对话框 | ✅ | ✅ | `IFileDialogService` 已异步；WinUI 用 FileOpenPicker/FolderPicker/FileSavePicker，WPF 保留同步对话框 |
| 播放列表文件导入/导出 | ✅ | ✅ | 追加到当前歌单 / 新建歌单 / 导出 M3U8 |
| 音量与静音 | ✅ | ✅ | 64px 底栏内，音量滑块在窄窗收起 |
| 上一首/下一首、随机、循环三态 | ✅ | ✅ | 底栏单行 |
| 设置对话框 | ✅ | ✅（基础版） | 默认音量 + 音频输出只读 + 日志入口；频谱参数留 Phase 23 |
| 歌单增删/重命名 | ✅ | ✅ | 左栏工具栏 + 双击重命名；拖拽重排仍缺 |
| 窗口几何持久化 + 可见性自愈 | ✅ | ✅ | 物理像素；最大化不覆盖普通几何 |
| 曲目信息面板 | ✅ | 🟡 | 封面/标题/艺术家/专辑/时长已有；采样率与频谱仍缺 |
| 频谱 / EQ / 拖拽 / 排序 / 文件夹绑定管理 | ✅ | ❌ | Phase 23 / 24 |

- [ ] **Step 3: Update `docs/COUPLING.md`**

Add a Phase 22 block:

```markdown
| **Phase 22 新增（WinUI 功能层契约）** | | |
| 异步文件对话框 | `IFileDialogService` 三个方法返回 `Task`；WPF 实现同步弹框后 `Task.FromResult`，WinUI 用 picker + `InitializeWithWindow`。取消/失败一律空集合或 null | 同步接口驱动 WinUI 异步 picker 会在 UI 线程死锁；Core 不得出现 WinUI 类型 |
| 窗口几何单位 | WinUI 的 `AppSettings.WindowLeft/Top/Width/Height` 存**物理像素**；只在 `OverlappedPresenterState.Restored` 时保存；中心点不在任何 `DisplayArea` 内则主屏居中 | 与 WPF 的 DIP 语义不同，但两壳数据目录分开，不共享 settings.json |
| 日志出口 | `%LOCALAPPDATA%\D-player-winui\logs\app.log`，1 MiB 轮转到 `app.log.1`；IO 失败静默 | 日志是排障出口，不是用户提示；代理验收不得读写该目录 |
| 二次关闭 | WinUI 用 `_cleanupCompleted`：清理完成前二次关闭继续取消；清理完成后自己的 `Close()` 放行 | WPF 同形缺陷未修，两壳在此处不对称，单独拍板 |
| 对话框宿主 | 所有 `ContentDialog` 经壳内 `WinUiDialogService`；`IsOpen` 供空格守卫判断 | 不让 Core VM 驱动 UI 提示 |
```

- [ ] **Step 4: Update `docs/PROJECT.md`**

Add a Phase 22 row after Phase 21:

```markdown
| 22 | WinUI 功能补齐（日常可用闭环）：异步文件对话框 + 真实 picker、导入/导出、歌单增删改与 ▶ 标记、上一首/下一首/随机/循环/音量/静音、设置基础版、窗口几何持久化、本地日志；WinUI 构建纳入 `verify-gates -Full` 第三道门禁。频谱/EQ/拖拽/排序/文件夹绑定管理留 Phase 23/24 |
```

Update the gate vocabulary in §4/§5 (wherever “壳侧检查（不是门禁）” appears) to:

```markdown
- 门禁 1/2（每次改动）：`.slnf` 构建 0 警告 + 测试 180 通过（`-Fast`）。
- 门禁 3（`-Full`，每个 Phase 22 任务必跑）：WinUI 单工程构建 0 警告。
```

- [ ] **Step 5: Update `README.md`**

- Features section: change the WinUI paragraph from “仍是功能切片” to “已具备导入/导出、歌单管理、完整播放控制、设置、窗口几何与日志；仍缺频谱/EQ/拖拽/排序/文件夹绑定管理”.
- “构建与运行” gate section: replace “壳侧检查 = WinUI 单工程构建。它不是门禁” with the three-gate vocabulary; `-Fast` = gates 1+2, `-Full` = gates 1+2+3.
- “已知限制” WinUI bullet: remove the no-import/no-controls claims; keep spectrum/EQ/drag/sort/folder-bound gaps and the `-Full`-only protection note.
- Phase table: add Phase 22.

- [ ] **Step 6: Final gates + commit**

```bash
powershell -File tools/verify-gates.ps1 -Full
git ls-files --eol docs/PHASE22-ACCEPTANCE.md docs/PHASE20-COMPARISON.md docs/PROJECT.md docs/COUPLING.md README.md
git add docs/PHASE22-ACCEPTANCE.md docs/PHASE20-COMPARISON.md docs/PROJECT.md docs/COUPLING.md README.md
git commit -m "docs(phase22): sync capabilities, gates and acceptance checklist"
```

Expected: `all requested steps passed.`

---

### Task 8: Acceptance walkthrough

**Files:**
- Modify: `docs/PHASE22-ACCEPTANCE.md` (fill the result table)
- Evidence: `%TEMP%\p22-acc\` (not committed)

**Interfaces:** none.

- [ ] **Step 1: Run the automated gates and record the exact output**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test D-player.slnf -c Debug -v q
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
powershell -File tools/verify-gates.ps1 -Full
```

Expected: 0/0; 180 passed / 0 failed; 0/0; three gates passed. Save the console log under `%TEMP%\p22-acc\gates.log`.

- [ ] **Step 2: Launch the shell and walk A/B/C/D/E**

Launch `dotnet run --project D-player.WinUI/D-player.WinUI.csproj -c Debug`. For each item in `docs/PHASE22-ACCEPTANCE.md`:
- Use the UI (real mouse/keyboard) wherever possible.
- For measurements (64px bar, window geometry), use UIA rectangles/screenshots under `%TEMP%\p22-acc\`.
- Never read/write `%LOCALAPPDATA%\D-player-winui\*`; items that need it (log content, off-screen self-heal) are marked NOT VERIFIED and handed to the user with exact steps.
- Do not modify product files during the walkthrough; the acceptance record is the only tracked file you append to.

- [ ] **Step 3: Fill the results and hand off the user-only items**

Append per-item rows in the standard format. At the end, add a “交给用户” section listing:
- D1 log file content.
- C4 off-screen self-heal.
- B5/B6 visual feel if no GUI automation was available.
- D3 WPF dialog behavior.

- [ ] **Step 4: Commit the acceptance record**

```bash
git ls-files --eol docs/PHASE22-ACCEPTANCE.md
git add docs/PHASE22-ACCEPTANCE.md
git commit -m "docs(phase22): record the acceptance results"
```

Expected: the checklist is fully filled; no PASS without evidence.

---

## Self-Review

**Spec coverage**
- Async file dialog contract: Task 1.
- Gate promotion: Task 1.
- NavRail CRUD + active marker + Space guard: Task 2.
- TrackList import/export toolbar + empty copy + busy state: Task 3.
- PlayerBar controls + responsive 64px layout: Task 4.
- Settings dialog + volume + log entry: Task 5.
- Window geometry restore/save/self-heal/maximized guard/second close: Task 5.
- Log outlet + hooks: Task 6.
- Docs + acceptance: Tasks 7–8.
- Out-of-scope items (spectrum/EQ/drag/sort/folder-bound management/sample rate/Mica retest) are absent from every task, matching spec §2.2.

**Type consistency**
- `OpenFilesAsync` / `OpenFolderAsync` / `SaveFileAsync` are used identically in Core, WPF, WinUI, and tests.
- `WinUiDialogService` method names/signatures match between Task 2 and Tasks 2/3.
- `WinUiAppPaths.DataFolderName` / `LogDirectory` are defined in Task 5 and used in Tasks 5/6/App.
- `SettingsDialog(PlayerViewModel)` matches the PlayerBar call in Task 5.
- `MainWindow(MainViewModel, ISettingsPersistence)` matches the App call in Task 5.
- `TrackToolbarHeight` / `ToolbarButtonSize` are defined in Task 3 and used only there.
- `SyncTransportVisuals` is defined and called in Task 4; no later task references a different name.

**Placeholder scan**
- No `TBD`, `TODO`, “implement later”, “similar to Task N”, or unspecified error handling.
- Every code step includes the actual code; every command step includes the exact command and expected result.
