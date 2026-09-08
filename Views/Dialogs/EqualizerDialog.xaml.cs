using System.Windows;
using System.Windows.Controls;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;

namespace DPlayer.Views.Dialogs;

/// <summary>
/// Phase 14 均衡器对话框。复用 SettingsDialog 模式：静态 Show 工厂 + modal ShowDialog()。
/// 拖动滑块实时下发 IPlaybackService.EqualizerConfig（即时听感）；保存写 settings.json 并同步 PlayerViewModel。
/// 11 根竖直滑块（10 段 + preamp）由 code-behind 动态构建到 BandsPanel（UniformGrid）。
/// </summary>
public partial class EqualizerDialog : Window
{
    private readonly ISettingsPersistence _persistence;
    private readonly IPlaybackService _playbackService;
    private readonly PlayerViewModel? _playerViewModel;

    private EqualizerConfig _initialConfig = new();  // 进入时快照，取消时回滚
    private bool _saved;                              // Save 成功标志
    private bool _suppress;                           // 程序化设置滑块/下拉时抑制 ValueChanged 回推

    private readonly Slider[] _bandSliders = new Slider[EqualizerPresets.BandCount];
    private readonly TextBlock[] _bandLabels = new TextBlock[EqualizerPresets.BandCount];
    private Slider _preampSlider = null!;
    private TextBlock _preampLabel = null!;

    public EqualizerDialog(ISettingsPersistence persistence, IPlaybackService playbackService, PlayerViewModel? playerViewModel = null)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _playerViewModel = playerViewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>模态显示均衡器对话框。返回 true = 用户保存。</summary>
    public static bool Show(Window? owner, ISettingsPersistence persistence, IPlaybackService playbackService, PlayerViewModel? playerViewModel = null)
    {
        var dlg = new EqualizerDialog(persistence, playbackService, playerViewModel) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _persistence.LoadAsync().ConfigureAwait(true);
            _initialConfig = EqualizerConfig.Create(
                settings.EqualizerEnabled, settings.EqualizerPreamp,
                settings.EqualizerBands, settings.EqualizerPreset);
        }
        catch
        {
            _initialConfig = new EqualizerConfig();
        }

        BuildBands();

        PresetCombo.Items.Clear();
        foreach (var n in EqualizerPresets.Names) PresetCombo.Items.Add(n);
        PresetCombo.Items.Add(EqualizerPresets.Custom);

        // 链上已是本配置（PlayerViewModel 启动时已应用），仅同步 UI，无需再下发
        ApplyConfigToUi(_initialConfig);
    }

    /// <summary>构建 11 列（10 段 + preamp），每列：dB 值标签 / 竖直滑块 / 频率标签。</summary>
    private void BuildBands()
    {
        BandsPanel.Children.Clear();
        var sliderStyle = (Style)FindResource("EqBandSlider");

        for (int b = 0; b < EqualizerPresets.BandCount; b++)
        {
            var (slider, valueLabel, _) = MakeSliderColumn(sliderStyle, FreqText(EqualizerPresets.CenterFrequencies[b]));
            _bandSliders[b] = slider;
            _bandLabels[b] = valueLabel;
            slider.ValueChanged += BandSlider_ValueChanged;
        }

        var (preSlider, preValue, _) = MakeSliderColumn(sliderStyle, "Pre");
        _preampSlider = preSlider;
        _preampLabel = preValue;
        preSlider.ValueChanged += BandSlider_ValueChanged;
    }

    private (Slider slider, TextBlock value, TextBlock freq) MakeSliderColumn(Style sliderStyle, string freqText)
    {
        var grid = new Grid { Margin = new Thickness(2, 0, 2, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var value = new TextBlock
        {
            Text = "0",
            Style = (Style)FindResource("CaptionText"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(value, 0);

        var slider = new Slider
        {
            Style = sliderStyle,
            Value = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(slider, 1);

        var freq = new TextBlock
        {
            Text = freqText,
            Style = (Style)FindResource("CaptionText"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(freq, 2);

        grid.Children.Add(value);
        grid.Children.Add(slider);
        grid.Children.Add(freq);
        BandsPanel.Children.Add(grid);
        return (slider, value, freq);
    }

    private static string FreqText(double hz) => hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";

    private double[] CurrentGains()
    {
        var g = new double[EqualizerPresets.BandCount];
        for (int b = 0; b < g.Length; b++) g[b] = _bandSliders[b].Value;
        return g;
    }

    private void RefreshLabels()
    {
        for (int b = 0; b < EqualizerPresets.BandCount; b++)
            _bandLabels[b].Text = $"{_bandSliders[b].Value:0}";
        _preampLabel.Text = $"{_preampSlider.Value:0}";
    }

    private void PushToService(string preset)
    {
        _playbackService.EqualizerConfig = EqualizerConfig.Create(
            EnableCheckBox.IsChecked == true, _preampSlider.Value, CurrentGains(), preset);
    }

    private void ApplyConfigToUi(EqualizerConfig config)
    {
        _suppress = true;
        EnableCheckBox.IsChecked = config.Enabled;
        for (int b = 0; b < EqualizerPresets.BandCount; b++)
            _bandSliders[b].Value = config.BandGainsDb[b];
        _preampSlider.Value = config.PreampDb;
        PresetCombo.SelectedItem = config.Preset;
        _suppress = false;
        RefreshLabels();
    }

    private void BandSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        RefreshLabels();
        var preset = EqualizerPresets.Match(CurrentGains());
        _suppress = true;
        PresetCombo.SelectedItem = preset;
        _suppress = false;
        PushToService(preset);
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (PresetCombo.SelectedItem is not string name || name == EqualizerPresets.Custom) return;
        if (!EqualizerPresets.TryGet(name, out var gains)) return;
        _suppress = true;
        for (int b = 0; b < EqualizerPresets.BandCount; b++) _bandSliders[b].Value = gains[b];
        _suppress = false;
        RefreshLabels();
        PushToService(name);
    }

    private void Enable_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        PushToService(PresetCombo.SelectedItem as string ?? EqualizerPresets.Flat);
    }

    private void RestoreFlat_Click(object sender, RoutedEventArgs e)
    {
        if (!EqualizerPresets.TryGet(EqualizerPresets.Flat, out var gains)) return;
        _suppress = true;
        for (int b = 0; b < EqualizerPresets.BandCount; b++) _bandSliders[b].Value = gains[b];
        _preampSlider.Value = 0;
        PresetCombo.SelectedItem = EqualizerPresets.Flat;
        _suppress = false;
        RefreshLabels();
        PushToService(EqualizerPresets.Flat);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = PresetCombo.SelectedItem as string ?? EqualizerPresets.Flat;
            var enabled = EnableCheckBox.IsChecked == true;
            var preamp = _preampSlider.Value;
            var gains = CurrentGains();

            await _persistence.UpdateAsync(s => s with
            {
                EqualizerEnabled = enabled,
                EqualizerPreamp = preamp,
                EqualizerBands = gains,
                EqualizerPreset = preset
            });

            // 确保链上是最终配置（拖动已实时下发，这里再确认一次）
            _playbackService.EqualizerConfig = EqualizerConfig.Create(enabled, preamp, gains, preset);

            // 同步 PlayerViewModel（按钮高亮即时更新）—— 故意不 ConfigureAwait(false)，留在 UI 线程
            if (_playerViewModel != null)
                _playerViewModel.EqualizerEnabled = enabled;

            _saved = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存 EQ 设置失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_saved)
            _playbackService.EqualizerConfig = _initialConfig; // 取消/关闭 → 撤销实时预览
    }
}
