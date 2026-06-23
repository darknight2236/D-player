using System.Windows.Controls;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 当前播放曲目信息面板 —— 封面 + 标题/艺术家/专辑/采样率。
/// DataContext = PlayerViewModel（由 MainWindow.xaml 注入）。
/// </summary>
public partial class TrackInfoView : UserControl
{
    public TrackInfoView()
    {
        InitializeComponent();
    }
}
