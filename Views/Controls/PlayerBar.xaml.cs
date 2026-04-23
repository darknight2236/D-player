using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Views.Controls;

public partial class PlayerBar : UserControl
{
    public PlayerBar()
    {
        InitializeComponent();
    }

    private void SeekBar_DragStarted(object sender, DragStartedEventArgs e)
        => (DataContext as MainViewModel)?.SeekStartedCommand.Execute(null);

    private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
        => (DataContext as MainViewModel)?.SeekCompletedCommand.Execute(SeekBar.Value);
}
