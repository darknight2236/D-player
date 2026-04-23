using System.ComponentModel;
using System.Windows;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ISettingsPersistence _persistence;

    public MainWindow(MainViewModel vm, ISettingsPersistence persistence)
    {
        InitializeComponent();
        _vm = vm;
        _persistence = persistence;
        DataContext = _vm;

        try
        {
            var settings = _persistence.LoadAsync().GetAwaiter().GetResult();
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            EnsureVisible();
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            var settings = await _persistence.LoadAsync();
            settings = settings with
            {
                WindowLeft = Left, WindowTop = Top,
                WindowWidth = Width, WindowHeight = ActualHeight
            };
            await _persistence.SaveAsync(settings);
        }
        catch { }

        await _vm.CleanupAsync();
    }

    private void EnsureVisible()
    {
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        if (centerX < SystemParameters.VirtualScreenLeft ||
            centerX > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth ||
            centerY < SystemParameters.VirtualScreenTop ||
            centerY > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        }
    }
}
