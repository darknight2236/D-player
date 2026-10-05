using System.Windows;
using System.Windows.Input;

namespace DPlayer.Views.Dialogs;

/// <summary>
/// 主题化对话框（深色无边框 chrome + TitleBar），替代系统 MessageBox（浅色、与深紫主题割裂）。
/// 模式：Show = 确认（取消/确定，返回 true=确认）；ShowError = 错误提示（仅确定按钮，Esc 可关闭）；
/// ShowInfo = 结果告知（仅确定按钮，Esc 可关闭）。
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>模态显示确认框。owner 用于居中；title 同时用于窗口标题与自绘标题栏。</summary>
    public static bool Show(Window? owner, string title, string message)
        => ShowCore(owner, title, message, showCancel: true);

    /// <summary>模态显示错误提示（仅确定按钮；Esc 可关闭）。</summary>
    public static void ShowError(Window? owner, string title, string message)
        => ShowCore(owner, title, message, showCancel: false);

    /// <summary>模态显示信息提示（仅确定按钮；Esc 可关闭）。与 ShowError 同形状，语义为"结果告知"而非错误。</summary>
    public static void ShowInfo(Window? owner, string title, string message)
        => ShowCore(owner, title, message, showCancel: false);

    private static bool ShowCore(Window? owner, string title, string message, bool showCancel)
    {
        var dlg = new ConfirmDialog
        {
            Owner = owner,
            Title = title,
        };
        dlg.DialogTitleBar.Title = title;
        dlg.MessageText.Text = message;

        if (!showCancel)
        {
            dlg.CancelButton.Visibility = Visibility.Collapsed;
            // 单按钮模式下 Esc 仍可关闭（对齐系统 MessageBox 的关闭习惯）
            dlg.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    dlg.Close();
                    e.Handled = true;
                }
            };
        }

        return dlg.ShowDialog() == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
