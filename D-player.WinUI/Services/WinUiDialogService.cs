using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DPlayer.WinUI.Services;

/// <summary>
/// 壳内唯一的 ContentDialog 宿主。所有对话框都从这里走，
/// 这样 MainWindow 的空格守卫只需要看一个 IsOpen 标志，不必猜弹层的可视化树。
///
/// IsOpen 是 static：ContentDialog 同一时刻只能开一个（ShowAsync 会抛），
/// 本壳也确实只有这一处入口，所以一个壳级布尔量就够，不需要每窗口一个实例。
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
            // 弹不出来（例如已有同 XamlRoot 的对话框）按"取消"处理，别把壳炸掉。
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
            // 删除是不可逆的：默认焦点落在「取消」，回车不误删。
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
