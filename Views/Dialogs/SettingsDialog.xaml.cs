using System.Collections.ObjectModel;
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
    private readonly IFileDialogService _fileDialog;
    private readonly ObservableCollection<string> _libraryFoldersDraft = new();
    private bool _libraryFoldersLoaded;

    public SettingsDialog(
        ISettingsPersistence persistence,
        IPlaybackService playbackService,
        PlayerViewModel? playerViewModel,
        IFileDialogService fileDialog)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _playerViewModel = playerViewModel;
        _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        InitializeComponent();
        // 草稿集合在构造期就绑定：设置加载失败时列表仍可用（只是为空）。
        LibraryFoldersList.ItemsSource = _libraryFoldersDraft;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// 模态显示设置对话框。owner 用于居中。
    /// 返回 true = 用户保存，false = 取消/关闭。
    /// </summary>
    public static bool Show(
        Window? owner,
        ISettingsPersistence persistence,
        IPlaybackService playbackService,
        PlayerViewModel? playerViewModel,
        IFileDialogService fileDialog)
    {
        var dlg = new SettingsDialog(persistence, playbackService, playerViewModel, fileDialog)
        {
            Owner = owner,
        };
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

            // Phase 24: 音乐库根目录回显（草稿集合，取消即丢弃）
            _libraryFoldersDraft.Clear();
            foreach (var root in settings.LibraryFolders)
                _libraryFoldersDraft.Add(root);
            _libraryFoldersLoaded = true;
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
                SpectrumSmoothing = spectrumSmoothing,

                // Phase 24: 保存音乐库根目录
                // 加载失败时草稿是空的，不能用它覆盖已配置的根目录。
                LibraryFolders = _libraryFoldersLoaded
                    ? _libraryFoldersDraft.ToArray()
                    : s.LibraryFolders,
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
            ConfirmDialog.ShowError(this, "错误", $"保存设置失败：{ex.Message}");
        }
    }

    /// <summary>选择音乐库根目录并加入草稿列表（规范化 + 大小写不敏感去重）。</summary>
    private async void AddLibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = await _fileDialog.OpenFolderAsync();
        var normalized = LibraryPathNormalizer.Normalize(folder);
        if (normalized is null) return;
        if (_libraryFoldersDraft.Contains(normalized, StringComparer.OrdinalIgnoreCase)) return;
        _libraryFoldersDraft.Add(normalized);
    }

    /// <summary>从草稿列表移除选中根目录（仅改内存，保存才落盘）。</summary>
    private void RemoveLibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryFoldersList.SelectedItem is string selected)
            _libraryFoldersDraft.Remove(selected);
    }
}
