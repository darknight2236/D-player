using DPlayer.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace DPlayer.WinUI.Views;

/// <summary>
/// 右信息栏的骨架。本 Task 只有占位文本（spec §5 的内容与自动收起在 Task 2 实现），
/// 但构造签名先按最终形状钉住：由 MainWindow 传入 <see cref="PlayerViewModel"/>，
/// 控件自己持有它那一块的绑定源。
/// </summary>
public sealed partial class InfoPanel : UserControl
{
    /// <summary>信息面板的数据源（Task 2 起用：封面 + 标题/艺术家/专辑/时长）。</summary>
    public PlayerViewModel Player { get; }

    public InfoPanel(PlayerViewModel player)
    {
        Player = player;
        InitializeComponent();
    }
}
