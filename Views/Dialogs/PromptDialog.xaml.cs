using System.Windows;
using System.Windows.Input;

namespace DPlayer.Views.Dialogs;

/// <summary>
/// Phase 6 共享单输入框对话框。AddPlaylist / RenamePlaylist 都用它。
/// 静态 Show(...) 返回 (Confirmed, Text)。Confirmed=false → 用户取消, Text 不可信。
/// </summary>
public partial class PromptDialog : Window
{
    public PromptDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    /// <summary>
    /// 模态显示。owner 用于居中; title/label/defaultValue 三段都可空。
    /// 返回 (确认?, 输入文本)。
    /// </summary>
    public static (bool Confirmed, string Text) Show(
        Window? owner,
        string title,
        string label,
        string defaultValue = "")
    {
        var dlg = new PromptDialog
        {
            Owner = owner,
            Title = title,
        };
        dlg.LabelText.Text = label;
        dlg.InputBox.Text = defaultValue ?? string.Empty;

        var ok = dlg.ShowDialog() == true;
        return (ok, dlg.InputBox.Text);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter 由 IsDefault Button 自动处理; Esc 由 IsCancel 处理。本方法预留扩展点(占位)。
    }
}
