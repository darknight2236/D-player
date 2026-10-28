using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;
using DPlayer.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DPlayer.WinUI.Views.Dialogs;

public sealed partial class EqualizerDialog : ContentDialog
{
    private readonly IPlaybackService _playbackService;
    private readonly ISettingsPersistence _persistence;
    private readonly PlayerViewModel _player;
    private EqualizerConfig _initialConfig = new();
    private bool _saved;
    private bool _suppress;
    private bool _ready;
    private bool _closing;

    public bool IsSaving { get; private set; }
    public event EventHandler? PreviewChanged;

    private readonly Slider[] _bandSliders = new Slider[EqualizerPresets.BandCount];
    private readonly TextBlock[] _bandLabels = new TextBlock[EqualizerPresets.BandCount];
    private Slider _preampSlider = null!;
    private TextBlock _preampLabel = null!;

    public EqualizerDialog(IPlaybackService playbackService, ISettingsPersistence persistence, PlayerViewModel player)
    {
        _playbackService = playbackService;
        _persistence = persistence;
        _player = player;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ready) return;
        // 实时链才是打开/取消的基准；复制频段，避免外部数组变化污染快照。
        _initialConfig = Snapshot(_playbackService.EqualizerConfig);
        BuildBands();
        try
        {
            _suppress = true;
            PresetCombo.Items.Clear();
            foreach (var name in EqualizerPresets.Names) PresetCombo.Items.Add(name);
            PresetCombo.Items.Add(EqualizerPresets.Custom);
        }
        finally { _suppress = false; }

        ApplyConfigToUi(_initialConfig);
        _ready = true;
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private static EqualizerConfig Snapshot(EqualizerConfig config)
        => config with { BandGainsDb = Array.AsReadOnly(config.BandGainsDb.ToArray()) };

    private void BuildBands()
    {
        BandsPanel.Children.Clear();
        BandsPanel.ColumnDefinitions.Clear();
        for (int b = 0; b <= EqualizerPresets.BandCount; b++)
            BandsPanel.ColumnDefinitions.Add(new ColumnDefinition());

        for (int b = 0; b < EqualizerPresets.BandCount; b++)
        {
            var (slider, label) = MakeSliderColumn(FreqText(EqualizerPresets.CenterFrequencies[b]), b);
            _bandSliders[b] = slider;
            _bandLabels[b] = label;
            slider.ValueChanged += BandSlider_ValueChanged;
        }

        var (preSlider, preLabel) = MakeSliderColumn("Pre", EqualizerPresets.BandCount);
        _preampSlider = preSlider;
        _preampLabel = preLabel;
        preSlider.ValueChanged += BandSlider_ValueChanged;
    }

    private (Slider Slider, TextBlock Label) MakeSliderColumn(string frequencyText, int column)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetRow(label, 0);

        var slider = new Slider
        {
            Minimum = -12,
            Maximum = 12,
            Orientation = Orientation.Vertical,
            Height = 170,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            slider, frequencyText == "Pre" ? "均衡器 Preamp" : $"均衡器 {frequencyText}Hz");
        Grid.SetRow(slider, 1);

        var frequency = new TextBlock
        {
            Text = frequencyText,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetRow(frequency, 2);

        grid.Children.Add(label);
        grid.Children.Add(slider);
        grid.Children.Add(frequency);
        Grid.SetColumn(grid, column);
        BandsPanel.Children.Add(grid);
        return (slider, label);
    }

    private static string FreqText(double hz) => hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";

    private double[] CurrentGains()
    {
        var gains = new double[EqualizerPresets.BandCount];
        for (int b = 0; b < gains.Length; b++) gains[b] = _bandSliders[b].Value;
        return gains;
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
        // VM 的启用 setter 会写盘；预览只能通知壳刷新高亮。
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyConfigToUi(EqualizerConfig config)
    {
        try
        {
            _suppress = true;
            EnableCheckBox.IsChecked = config.Enabled;
            for (int b = 0; b < EqualizerPresets.BandCount; b++)
                _bandSliders[b].Value = config.BandGainsDb[b];
            _preampSlider.Value = config.PreampDb;
            PresetCombo.SelectedItem = EqualizerPresets.Names.Contains(config.Preset)
                ? config.Preset : EqualizerPresets.Custom;
        }
        finally { _suppress = false; }
        RefreshLabels();
    }

    private void BandSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _suppress || IsSaving || _closing) return;
        RefreshLabels();
        var preset = EqualizerPresets.Match(CurrentGains());
        try { _suppress = true; PresetCombo.SelectedItem = preset; }
        finally { _suppress = false; }
        PushToService(preset);
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suppress || IsSaving || _closing) return;
        if (PresetCombo.SelectedItem is not string name) return;
        if (name == EqualizerPresets.Custom)
        {
            PushToService(name); // 仅更新预设标记，保留当前曲线，确保保存后重开一致。
            return;
        }
        if (!EqualizerPresets.TryGet(name, out var gains)) return;
        ApplyGains(gains, name);
    }

    private void Enable_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _suppress || IsSaving || _closing) return;
        PushToService(PresetCombo.SelectedItem as string ?? EqualizerPresets.Flat);
    }

    private void RestoreFlat_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || IsSaving || _closing) return;
        if (!EqualizerPresets.TryGet(EqualizerPresets.Flat, out var gains)) return;
        ApplyGains(gains, EqualizerPresets.Flat, preamp: 0);
    }

    private void ApplyGains(double[] gains, string preset, double? preamp = null)
    {
        try
        {
            _suppress = true;
            for (int b = 0; b < EqualizerPresets.BandCount; b++) _bandSliders[b].Value = gains[b];
            if (preamp.HasValue) _preampSlider.Value = preamp.Value;
            PresetCombo.SelectedItem = preset;
        }
        finally { _suppress = false; }
        RefreshLabels();
        PushToService(preset);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || IsSaving || _closing) return;
        IsSaving = true;
        IsEnabled = false;
        SaveButton.Content = "保存中…";
        SaveError.Visibility = Visibility.Collapsed;
        try
        {
            // await 前冻结完整值；锁内 mutator 不访问 UI，也不共享可变频段数组。
            var config = Snapshot(EqualizerConfig.Create(
                EnableCheckBox.IsChecked == true, _preampSlider.Value, CurrentGains(),
                PresetCombo.SelectedItem as string ?? EqualizerPresets.Custom));
            await _persistence.UpdateAsync(s => s with
            {
                EqualizerEnabled = config.Enabled,
                EqualizerPreamp = config.PreampDb,
                EqualizerBands = config.BandGainsDb.ToArray(),
                EqualizerPreset = config.Preset,
            });
            // 仅成功落盘后同步 VM；预览/取消绝不触碰其持久化 setter。
            _player.EqualizerEnabled = config.Enabled;
            _saved = true;
        }
        catch (Exception ex)
        {
            AppLog.Write("[WinUI 壳] 保存 EQ 设置失败", ex);
            SaveError.Text = "保存失败，请重试，或取消以恢复打开前的配置。";
            SaveError.Visibility = Visibility.Visible;
        }
        finally
        {
            IsSaving = false;
            IsEnabled = true;
            SaveButton.Content = "保存";
        }
        if (_saved) Hide();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => TryCancel();

    // 主窗口关闭也走同一守卫，避免保存未完成就开始清理播放服务。
    public bool TryCancel()
    {
        if (IsSaving) return false;
        if (!_closing) Hide();
        return true;
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (IsSaving)
        {
            args.Cancel = true;
            return;
        }
        _closing = true;
        IsEnabled = false;
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        if (!_saved && _ready)
            _playbackService.EqualizerConfig = _initialConfig;
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }
}
