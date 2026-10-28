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
        SpectrumEnabledCheckBox.IsChecked = _player.SpectrumEnabled;
        SensitivitySlider.Maximum = 2;
        SensitivitySlider.Value = _player.SpectrumSensitivity;
        SensitivitySlider.Minimum = 0.5;
        ThemeComboBox.ItemsSource = _player.SpectrumColorThemes;
        ThemeComboBox.SelectedIndex = _player.SpectrumColorTheme;
        SmoothingSlider.Value = _player.SpectrumSmoothing;
        UpdateSpectrumValueLabels();
        _ready = true;
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdatePercent();
        if (!_ready) return;
        _player.Volume = (float)e.NewValue;   // 既有 setter 负责同步播放服务与落盘
    }

    private void SpectrumEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _player.SpectrumEnabled = SpectrumEnabledCheckBox.IsChecked == true;
    }

    private void SensitivitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateSpectrumValueLabels();
        if (!_ready) return;
        _player.SpectrumSensitivity = SensitivitySlider.Value;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _player.SpectrumColorTheme = ThemeComboBox.SelectedIndex;
    }

    private void SmoothingSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateSpectrumValueLabels();
        if (!_ready) return;
        _player.SpectrumSmoothing = SmoothingSlider.Value;
    }

    private void UpdateSpectrumValueLabels()
    {
        SensitivityValue.Text = $"{SensitivitySlider.Value:0.00}";
        SmoothingValue.Text = $"{SmoothingSlider.Value:0.00}";
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
            AppLog.Write("[WinUI 壳] 打开日志文件夹失败", ex);
        }
    }
}
