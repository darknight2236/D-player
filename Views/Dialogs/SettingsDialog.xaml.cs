using System.Windows;
using UmaPlayer.Services;

namespace UmaPlayer.Views.Dialogs;

/// <summary>
/// Phase 11 设置对话框。模态显示，默认音量滑块可编辑，音频输出灰色占位。
/// 与 PromptDialog 模式一致：静态 Show() 工厂 + modal ShowDialog()。
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly ISettingsPersistence _persistence;

    public SettingsDialog(ISettingsPersistence persistence)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// 模态显示设置对话框。owner 用于居中。
    /// 返回 true = 用户保存，false = 取消/关闭。
    /// </summary>
    public static bool Show(Window? owner, ISettingsPersistence persistence)
    {
        var dlg = new SettingsDialog(persistence) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _persistence.LoadAsync().ConfigureAwait(true);
            VolumeSlider.Value = settings.DefaultVolume;
            VolumePercent.Text = $"{settings.DefaultVolume:P0}";
        }
        catch
        {
            // settings.json missing/corrupt → slider stays at XAML default (0), acceptable
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumePercent != null)
            VolumePercent.Text = $"{e.NewValue:P0}";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var volume = (float)VolumeSlider.Value;
            await _persistence.UpdateAsync(s => s with { DefaultVolume = volume }).ConfigureAwait(true);
            DialogResult = true;
        }
        catch
        {
            MessageBox.Show(this, "Failed to save settings.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
