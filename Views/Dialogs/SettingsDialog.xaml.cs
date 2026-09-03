using System.Windows;
using DPlayer.Services;
using DPlayer.ViewModels;

namespace DPlayer.Views.Dialogs;

/// <summary>
/// Phase 11 设置对话框。模态显示，默认音量滑块可编辑，音频输出灰色占位。
/// 与 PromptDialog 模式一致：静态 Show() 工厂 + modal ShowDialog()。
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly ISettingsPersistence _persistence;
    private readonly IPlaybackService _playbackService;
    private readonly PlayerViewModel? _playerViewModel;

    public SettingsDialog(ISettingsPersistence persistence, IPlaybackService playbackService, PlayerViewModel? playerViewModel = null)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _playerViewModel = playerViewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// 模态显示设置对话框。owner 用于居中。
    /// 返回 true = 用户保存，false = 取消/关闭。
    /// </summary>
    public static bool Show(Window? owner, ISettingsPersistence persistence, IPlaybackService playbackService, PlayerViewModel? playerViewModel = null)
    {
        var dlg = new SettingsDialog(persistence, playbackService, playerViewModel) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _persistence.LoadAsync().ConfigureAwait(true);
            VolumeSlider.Value = settings.DefaultVolume;
            VolumePercent.Text = $"{settings.DefaultVolume:P0}";

            // Phase 13: 加载频谱设置
            SpectrumEnabledCheckBox.IsChecked = settings.SpectrumEnabled;
            SensitivitySlider.Value = settings.SpectrumSensitivity;
            ColorThemeComboBox.SelectedIndex = settings.SpectrumColorTheme;
            SmoothingSlider.Value = settings.SpectrumSmoothing;
            SensitivityValue.Text = settings.SpectrumSensitivity.ToString("F1");
            SmoothingValue.Text = settings.SpectrumSmoothing.ToString("F2");
        }
        catch
        {
            // settings.json missing/corrupt → slider stays at XAML default (0), acceptable
        }

        // 绑定滑块值变化事件（避免设计时触发）
        SensitivitySlider.ValueChanged += (_, args) =>
            SensitivityValue.Text = args.NewValue.ToString("F1");
        SmoothingSlider.ValueChanged += (_, args) =>
            SmoothingValue.Text = args.NewValue.ToString("F2");
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumePercent != null)
            VolumePercent.Text = $"{e.NewValue:P0}";

        // 实时调整播放音量
        _playbackService.Volume = (float)e.NewValue;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var volume = (float)VolumeSlider.Value;
            var spectrumEnabled = SpectrumEnabledCheckBox.IsChecked ?? true;
            var spectrumSensitivity = SensitivitySlider.Value;
            var spectrumColorTheme = ColorThemeComboBox.SelectedIndex;
            var spectrumSmoothing = SmoothingSlider.Value;

            await _persistence.UpdateAsync(s => s with
            {
                DefaultVolume = volume,

                // Phase 13: 保存频谱设置
                SpectrumEnabled = spectrumEnabled,
                SpectrumSensitivity = spectrumSensitivity,
                SpectrumColorTheme = spectrumColorTheme,
                SpectrumSmoothing = spectrumSmoothing
            });

            // 同步更新 PlayerViewModel 属性，使 UI 立即反映新设置
            if (_playerViewModel != null)
            {
                _playerViewModel.Volume = volume;

                // Phase 13: 同步频谱设置，避免需重启才生效
                _playerViewModel.SpectrumEnabled = spectrumEnabled;
                _playerViewModel.SpectrumSensitivity = spectrumSensitivity;
                _playerViewModel.SpectrumColorTheme = spectrumColorTheme;
                _playerViewModel.SpectrumSmoothing = spectrumSmoothing;
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存设置失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
